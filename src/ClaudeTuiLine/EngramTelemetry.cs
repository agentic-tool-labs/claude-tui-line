using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClaudeTuiLine;

public sealed record EngramResult(long? Facts, string? Verb);

/// <summary>
/// Reads the last 64KB of Engram's shared telemetry.jsonl and derives the fact count and
/// activity verb for segment 13, per CAPTURE.md's "Segment 13" section.
/// </summary>
public static class EngramTelemetry
{
    private const int TailBytes = 65536;
    private const int InstantFreshSeconds = 10;
    private const int OngoingMaxAgeSeconds = 900;
    private const string PlaceholderSessionId = "__no_session__";

    private static readonly HashSet<string> SharedKinds = new(StringComparer.Ordinal)
    {
        "recall", "remember", "browse", "expand", "digest", "revise",
        "session-open", "index", "embedding", "server-start", "server-stop",
    };

    private static readonly Dictionary<string, string> InstantVerbs = new(StringComparer.Ordinal)
    {
        ["user-prompt"] = "✱ captured",
        ["remember"] = "✱ saved",
        ["recall"] = "◉ recalled",
        ["browse"] = "◉ recalled",
        ["expand"] = "◉ recalled",
        ["digest"] = "◈ digested",
        ["revise"] = "◈ digested",
        ["session-start"] = "▸ primed",
        ["subagent-start"] = "▸ primed",
        ["server-start"] = "● up",
        ["server-stop"] = "○ down",
    };

    public static EngramResult? Build(string? sessionId, DateTimeOffset now, string? telemetryPath = null) =>
        BuildWithTimestamp(sessionId, now, telemetryPath).Result;

    /// <summary>
    /// SPEC-104 §D.2(3)/§E.3: same derivation as <see cref="Build"/>, plus the newest eligible
    /// telemetry record's parsed timestamp, needed for the Idle-age fallback and the
    /// <c>lastEventAge</c> field when no Tier 3 probe ran.
    /// </summary>
    internal static (EngramResult? Result, DateTimeOffset? NewestTimestamp) BuildWithTimestamp(string? sessionId, DateTimeOffset now, string? telemetryPath = null)
    {
        var path = telemetryPath ?? ResolveTelemetryPath();
        if (path is null)
        {
            return (null, null);
        }

        string[] lines;
        try
        {
            lines = ReadTailLines(path);
        }
        catch
        {
            return (null, null);
        }

        if (lines.Length == 0)
        {
            return (null, null);
        }

        var effectiveSessionId = string.IsNullOrEmpty(sessionId) ? PlaceholderSessionId : sessionId;

        JsonDocument? newestEligible = null;
        try
        {
            foreach (var line in lines)
            {
                if (!TryParseObject(line, out var doc))
                {
                    continue;
                }

                if (!IsEligible(doc.RootElement, effectiveSessionId))
                {
                    doc.Dispose();
                    continue;
                }

                newestEligible?.Dispose();
                newestEligible = doc;
            }

            if (newestEligible is null)
            {
                return (null, null);
            }

            var facts = FindNewestFactCount(lines);
            var (runningIndex, runningEmbedding) = FindRunning(lines);

            var runningParts = new List<string>();
            if (IsWithinAge(runningIndex, now, OngoingMaxAgeSeconds))
            {
                runningParts.Add("✎ indexing");
            }

            if (IsWithinAge(runningEmbedding, now, OngoingMaxAgeSeconds))
            {
                runningParts.Add("∿ embedding");
            }

            string? instantVerb = TryGetInstantVerb(newestEligible.RootElement, now);

            var verbParts = new List<string>(runningParts);
            if (instantVerb is not null)
            {
                verbParts.Add(instantVerb);
            }

            var verb = verbParts.Count > 0 ? string.Join(' ', verbParts) : null;

            if (facts is null && verb is null)
            {
                return (null, null);
            }

            DateTimeOffset? newestTimestamp = TryGetString(newestEligible.RootElement, "timestamp", out var ts) &&
                DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedTs)
                    ? parsedTs
                    : null;

            return (new EngramResult(facts, verb), newestTimestamp);
        }
        finally
        {
            newestEligible?.Dispose();
        }
    }

    private static string? ResolveTelemetryPath()
    {
        var home = Environment.GetEnvironmentVariable("ENGRAM_HOME");
        if (string.IsNullOrEmpty(home))
        {
            var userHome = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(userHome))
            {
                return null;
            }

            home = Path.Combine(userHome, ".engram");
        }

        return Path.Combine(home, "telemetry.jsonl");
    }

    private static string[] ReadTailLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var seekOffset = Math.Max(0, length - TailBytes);
        var torn = seekOffset > 0;

        stream.Seek(seekOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var tail = reader.ReadToEnd();

        var lines = tail.Split('\n');
        if (torn && lines.Length > 0)
        {
            lines = lines[1..];
        }

        return lines;
    }

    private static bool TryParseObject(string line, out JsonDocument doc)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            doc = null!;
            return false;
        }

        try
        {
            var parsed = JsonDocument.Parse(trimmed);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                doc = null!;
                return false;
            }

            doc = parsed;
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            return false;
        }
    }

    private static bool IsEligible(JsonElement record, string sessionId)
    {
        if (TryGetString(record, "session_id", out var recordSessionId) &&
            string.Equals(recordSessionId, sessionId, StringComparison.Ordinal))
        {
            return true;
        }

        return TryGetString(record, "kind", out var kind) && SharedKinds.Contains(kind);
    }

    private static long? FindNewestFactCount(string[] lines)
    {
        long? facts = null;
        foreach (var line in lines)
        {
            if (!TryParseObject(line, out var doc))
            {
                continue;
            }

            using (doc)
            {
                if (!TryGetString(doc.RootElement, "kind", out var kind) ||
                    (kind != "session-start" && kind != "subagent-start"))
                {
                    continue;
                }

                if (doc.RootElement.TryGetProperty("long_term_fact_count", out var value) &&
                    value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt64(out var count))
                {
                    facts = count;
                }
            }
        }

        return facts;
    }

    private static (string? Index, string? Embedding) FindRunning(string[] lines)
    {
        string? runningIndex = null;
        string? runningEmbedding = null;

        foreach (var line in lines)
        {
            if (!TryParseObject(line, out var doc))
            {
                continue;
            }

            using (doc)
            {
                if (!TryGetString(doc.RootElement, "kind", out var kind) ||
                    (kind != "index" && kind != "embedding"))
                {
                    continue;
                }

                var phase = TryGetString(doc.RootElement, "phase", out var p) ? p : null;
                var timestamp = TryGetString(doc.RootElement, "timestamp", out var t) ? t : null;
                var started = phase == "started" ? timestamp : null;

                if (kind == "index")
                {
                    runningIndex = started;
                }
                else
                {
                    runningEmbedding = started;
                }
            }
        }

        return (runningIndex, runningEmbedding);
    }

    private static string? TryGetInstantVerb(JsonElement record, DateTimeOffset now)
    {
        if (!TryGetString(record, "timestamp", out var timestamp) || !IsWithinAge(timestamp, now, InstantFreshSeconds))
        {
            return null;
        }

        if (!TryGetString(record, "kind", out var kind))
        {
            return null;
        }

        if (kind == "file-touched")
        {
            var path = TryGetString(record, "path", out var p) ? p : null;
            return string.IsNullOrEmpty(path) ? "✎ edit" : "✎ " + PathBasename(path);
        }

        return InstantVerbs.TryGetValue(kind, out var verb) ? verb : null;
    }

    private static string PathBasename(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    private static bool TryGetString(JsonElement record, string property, out string value)
    {
        if (record.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsWithinAge(string? timestamp, DateTimeOffset now, int maxAgeSeconds)
    {
        if (string.IsNullOrEmpty(timestamp))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return false;
        }

        var age = (now - parsed).TotalSeconds;
        return age >= 0 && age <= maxAgeSeconds;
    }
}
