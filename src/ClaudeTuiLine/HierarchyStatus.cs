using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>The status-file timeline entry that is current right now: its tone, full text and short text.</summary>
public sealed record HierarchyEntry(string Tone, string Text, string Short);

internal sealed class HierarchyStatusDoc
{
    [JsonPropertyName("schema")]
    public int? Schema { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("member_sessions")]
    public List<string?>? MemberSessions { get; set; }

    [JsonPropertyName("timeline")]
    public List<HierarchyTimelineEntry?>? Timeline { get; set; }
}

/// <summary>
/// Only <c>at</c> is typed: the other four fields are validated on the picked entry alone, so a
/// wrong type in an entry that is not current must not fail deserialization.
/// </summary>
internal sealed class HierarchyTimelineEntry
{
    [JsonPropertyName("at")]
    public DateTimeOffset? At { get; set; }

    [JsonPropertyName("visible")]
    public JsonElement Visible { get; set; }

    [JsonPropertyName("tone")]
    public JsonElement Tone { get; set; }

    [JsonPropertyName("text")]
    public JsonElement Text { get; set; }

    [JsonPropertyName("short")]
    public JsonElement Short { get; set; }
}

[JsonSerializable(typeof(HierarchyStatusDoc))]
internal partial class HierarchyStatusJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Read-only consumer of agent-hierarchy's precomputed <c>.claude/hierarchy/status.json</c>. It
/// never computes anything: it locates the file, validates it, and returns the timeline entry that
/// is current at <c>now</c>. It never throws — a sidecar file must not break the statusline.
/// </summary>
internal static class HierarchyStatus
{
    internal const int MaxBytes = 262_144;

    public static HierarchyEntry? Probe(string? cwd, string? sessionId, DateTimeOffset now) =>
        ReadAndSelect(Locate(cwd), sessionId, now);

    /// <summary>
    /// The status file of the first enclosing directory that has a <c>.git</c> entry (directory or
    /// file — a linked worktree is its own pool). Existence checks only; null when there is none.
    /// </summary>
    internal static string? Locate(string? cwd)
    {
        try
        {
            if (string.IsNullOrEmpty(cwd) || !Path.IsPathRooted(cwd))
            {
                return null;
            }

            for (var dir = cwd; dir is not null; dir = Path.GetDirectoryName(dir))
            {
                var git = Path.Combine(dir, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return Path.Combine(dir, ".claude", "hierarchy", "status.json");
                }
            }
        }
        catch
        {
        }

        return null;
    }

    internal static HierarchyEntry? ReadAndSelect(string? path, string? sessionId, DateTimeOffset now)
    {
        try
        {
            return ReadAndSelectCore(path, sessionId, now);
        }
        catch
        {
            return null;
        }
    }

    private static HierarchyEntry? ReadAndSelectCore(string? path, string? sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var buffer = new byte[MaxBytes + 1];
        var length = 0;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int n;
            while (length < buffer.Length && (n = stream.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += n;
            }
        }

        if (length > MaxBytes)
        {
            return null;
        }

        var doc = JsonSerializer.Deserialize(buffer.AsSpan(0, length), HierarchyStatusJsonContext.Default.HierarchyStatusDoc);
        if (doc is null
            || doc.Schema != 1
            || doc.ExpiresAt is not { } expiresAt || now >= expiresAt
            || doc.MemberSessions is not { } members || members.Contains(null)
            || doc.Timeline is not { Count: > 0 } timeline)
        {
            return null;
        }

        foreach (var e in timeline)
        {
            if (e?.At is null)
            {
                return null;
            }
        }

        var picked = timeline[0]!;
        foreach (var e in timeline)
        {
            if (e!.At <= now)
            {
                picked = e;
            }
        }

        if (picked.Visible.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || picked.Tone.ValueKind != JsonValueKind.String
            || picked.Text.ValueKind != JsonValueKind.String
            || picked.Short.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (picked.Visible.ValueKind == JsonValueKind.False
            || (!string.IsNullOrEmpty(sessionId) && members.Contains(sessionId, StringComparer.Ordinal)))
        {
            return null;
        }

        return new HierarchyEntry(picked.Tone.GetString()!, picked.Text.GetString()!, picked.Short.GetString()!);
    }
}
