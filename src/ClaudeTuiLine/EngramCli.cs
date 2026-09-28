using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>SPEC-104 §D.1: derived engram reachability state, first-match-wins per §D.2.</summary>
public enum EngramState
{
    Active,
    Idle,
    Unreachable,
    Unavailable,
}

public sealed record EngramStatusInfo(
    bool Initialised,
    string? Server,
    int? Pid,
    int? Port,
    string? Version,
    long? UptimeSeconds);

public sealed record EngramActivityInfo(
    string? LastKind,
    long? LastAgeSeconds,
    long? WindowSeconds,
    long? WindowCount,
    IReadOnlyDictionary<string, long> Kinds);

/// <summary>SPEC-104 §E.1: the engram item's full probe result. Deliberately replaces the plain
/// <see cref="EngramResult"/> everywhere it used to flow, so every call site is forced to update.</summary>
public sealed record EngramProbeResult(
    EngramResult? Telemetry,
    EngramStatusInfo? Status,
    EngramActivityInfo? Activity,
    EngramState State,
    DateTimeOffset? LastEventAt);

internal sealed record EngramStatusCliJson(
    [property: JsonPropertyName("Initialised")] bool? Initialised,
    [property: JsonPropertyName("Server")] string? Server,
    [property: JsonPropertyName("Pid")] int? Pid,
    [property: JsonPropertyName("Port")] int? Port,
    [property: JsonPropertyName("Version")] string? Version,
    [property: JsonPropertyName("UptimeSeconds")] long? UptimeSeconds);

internal sealed record EngramActivityCliJson(
    [property: JsonPropertyName("LastKind")] string? LastKind,
    [property: JsonPropertyName("LastAgeSeconds")] long? LastAgeSeconds,
    [property: JsonPropertyName("WindowSeconds")] long? WindowSeconds,
    [property: JsonPropertyName("WindowCount")] long? WindowCount,
    [property: JsonPropertyName("Kinds")] Dictionary<string, long>? Kinds);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(EngramStatusCliJson))]
[JsonSerializable(typeof(EngramActivityCliJson))]
internal partial class EngramCliJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-104 §E.2: spawns <c>engram status --json</c> / <c>engram activity --since &lt;window&gt; --json</c>
/// via <see cref="ItemCache"/>, mirroring <see cref="CommandProvider"/>'s timeout/kill-tree/stale-on-failure
/// pattern. Deliberately does NOT replicate <see cref="CommandProvider"/>'s early-return on a nonzero exit
/// code (§D.4): stdout is parsed first, and a JSON shape that resolves real <c>Server</c>/<c>Pid</c> values
/// is trusted regardless of exit code, since <c>engram status</c> can legitimately exit nonzero for a
/// cleanly-stopped server.
/// </summary>
internal static class EngramCli
{
    internal static async Task<EngramStatusInfo?> ProbeStatusAsync(string? binary, string cacheDir, int ttlSeconds, int timeoutMs)
    {
        if (string.IsNullOrEmpty(binary))
        {
            return null;
        }

        var raw = await RunCachedAsync("engram:status", new[] { binary, "status", "--json" }, cacheDir, ttlSeconds, timeoutMs).ConfigureAwait(false);
        return raw is null ? null : ParseStatus(raw);
    }

    internal static async Task<EngramActivityInfo?> ProbeActivityAsync(string? binary, string window, string cacheDir, int ttlSeconds, int timeoutMs)
    {
        if (string.IsNullOrEmpty(binary))
        {
            return null;
        }

        var raw = await RunCachedAsync("engram:activity", new[] { binary, "activity", "--since", window, "--json" }, cacheDir, ttlSeconds, timeoutMs).ConfigureAwait(false);
        return raw is null ? null : ParseActivity(raw);
    }

    /// <summary>SPEC-104 §E.3: "45s" / "12m" / "3h" / "2d" — no existing helper covers this.</summary>
    internal static string HumanizeDuration(long totalSeconds)
    {
        if (totalSeconds < 60)
        {
            return totalSeconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        var minutes = totalSeconds / 60;
        if (minutes < 60)
        {
            return minutes.ToString(CultureInfo.InvariantCulture) + "m";
        }

        var hours = minutes / 60;
        if (hours < 24)
        {
            return hours.ToString(CultureInfo.InvariantCulture) + "h";
        }

        var days = hours / 24;
        return days.ToString(CultureInfo.InvariantCulture) + "d";
    }

    private static EngramStatusInfo? ParseStatus(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize(json, EngramCliJsonContext.Default.EngramStatusCliJson);
            if (parsed is null || (parsed.Server is null && parsed.Pid is null))
            {
                return null;
            }

            return new EngramStatusInfo(parsed.Initialised ?? false, parsed.Server, parsed.Pid, parsed.Port, parsed.Version, parsed.UptimeSeconds);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EngramActivityInfo? ParseActivity(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize(json, EngramCliJsonContext.Default.EngramActivityCliJson);
            if (parsed is null || (parsed.LastKind is null && parsed.WindowCount is null))
            {
                return null;
            }

            IReadOnlyDictionary<string, long> kinds = parsed.Kinds ?? new Dictionary<string, long>();
            return new EngramActivityInfo(parsed.LastKind, parsed.LastAgeSeconds, parsed.WindowSeconds, parsed.WindowCount, kinds);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyEnv = new Dictionary<string, string>();

    private static async Task<string?> RunCachedAsync(string cacheId, IReadOnlyList<string> argv, string cacheDir, int ttlSeconds, int timeoutMs)
    {
        var key = ItemCache.KeyFor(cacheId, argv, cwd: null, paneWidth: null, env: EmptyEnv);
        var cached = ItemCache.TryRead(cacheDir, key);
        if (cached is { } fresh && DateTimeOffset.UtcNow - fresh.CapturedAt < TimeSpan.FromSeconds(ttlSeconds))
        {
            return fresh.Value;
        }

        var spawned = await SpawnAsync(argv, TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
        if (spawned is null)
        {
            return cached?.Value;
        }

        ItemCache.Write(cacheDir, key, new CacheEntry(spawned, DateTimeOffset.UtcNow, 0));
        return spawned;
    }

    /// <summary>§C.8.3: argv array, <c>UseShellExecute = false</c> — no shell, no string interpolation.</summary>
    private static async Task<string?> SpawnAsync(IReadOnlyList<string> argv, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = argv[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var arg in argv.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("engram probe did not start");
        }
        catch
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            return string.IsNullOrEmpty(stdout) ? null : stdout;
        }
        catch
        {
            TryKill(process);
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}

/// <summary>
/// SPEC-104 §E.5: the single build point for <see cref="EngramProbeResult"/>, shared by both
/// Program.cs call sites — owns binary resolution (§C.8) and state derivation (§D.2) so neither
/// is duplicated.
/// </summary>
public static class EngramProbe
{
    private const int DefaultIdleAfterSeconds = 300;
    private const string DefaultActivityWindow = "1h";

    private static readonly char[] PathSeparators = { '/', '\\' };

    private static readonly HashSet<string> Tier2FieldNames = new(StringComparer.Ordinal)
    {
        "server", "version", "uptime", "port", "pid",
    };

    private static readonly HashSet<string> Tier3FieldNames = new(StringComparer.Ordinal)
    {
        "lastKind", "lastAge", "windowCount",
    };

    internal static bool IsTier2Field(string field) => Tier2FieldNames.Contains(field);

    internal static bool IsTier3Field(string field) => Tier3FieldNames.Contains(field) || field.StartsWith("kind:", StringComparison.Ordinal);

    internal static bool IsKnownField(string field) =>
        field is "facts" or "verb" or "lastEventAge" || IsTier2Field(field) || IsTier3Field(field);

    /// <summary>
    /// Fire-early/await-late, matching <see cref="GitBranch.ProbeAsync"/>: callers should start
    /// this immediately once <paramref name="sessionId"/>/cwd are known and await it just before
    /// constructing <see cref="ItemContext"/>.
    /// </summary>
    public static async Task<EngramProbeResult?> BuildAsync(string? sessionId, DateTimeOffset now, EngramItemSettings? settings, string cacheDir)
    {
        var (telemetry, telemetryTimestamp) = EngramTelemetry.BuildWithTimestamp(sessionId, now);

        var fields = settings?.Fields;
        var needsTier2 = fields is { Count: > 0 } && fields.Any(f => f.Field is { Length: > 0 } fn && IsTier2Field(fn));
        var hasStateColorOverride = settings?.StateColors is { } sc &&
            (sc.Active is not null || sc.Idle is not null || sc.Unreachable is not null || sc.Unavailable is not null);
        var needsTier3 = (fields is { Count: > 0 } && fields.Any(f => f.Field is { Length: > 0 } fn && IsTier3Field(fn))) || hasStateColorOverride;

        var idleAfterSeconds = settings?.IdleAfterSeconds is > 0 ? settings.IdleAfterSeconds.Value : DefaultIdleAfterSeconds;

        if (!needsTier2 && !needsTier3)
        {
            var state = DeriveState(telemetry, telemetryTimestamp, status: null, activity: null, now, idleAfterSeconds);
            return new EngramProbeResult(telemetry, null, null, state, telemetryTimestamp);
        }

        var binary = ResolveBinary(settings?.BinaryPath);
        var ttlSeconds = settings?.TtlSeconds is > 0 ? settings.TtlSeconds.Value : CommandProvider.DefaultTtlSeconds;
        var timeoutMs = settings?.TimeoutMs is > 0 ? settings.TimeoutMs.Value : CommandProvider.DefaultTimeoutMs;
        var window = settings?.ActivityWindow is { Length: > 0 } w ? w : DefaultActivityWindow;

        var statusTask = needsTier2 ? EngramCli.ProbeStatusAsync(binary, cacheDir, ttlSeconds, timeoutMs) : Task.FromResult<EngramStatusInfo?>(null);
        var activityTask = needsTier3 ? EngramCli.ProbeActivityAsync(binary, window, cacheDir, ttlSeconds, timeoutMs) : Task.FromResult<EngramActivityInfo?>(null);

        var status = await statusTask.ConfigureAwait(false);
        var activity = await activityTask.ConfigureAwait(false);

        var derivedState = DeriveState(telemetry, telemetryTimestamp, status, activity, now, idleAfterSeconds);
        return new EngramProbeResult(telemetry, status, activity, derivedState, telemetryTimestamp);
    }

    private static EngramState DeriveState(EngramResult? telemetry, DateTimeOffset? telemetryTimestamp, EngramStatusInfo? status, EngramActivityInfo? activity, DateTimeOffset now, int idleAfterSeconds)
    {
        if (telemetry is null && (status is null || !status.Initialised))
        {
            return EngramState.Unavailable;
        }

        if (status is not null && (status.Server != "Running" || !status.Initialised || status.Pid is null))
        {
            return EngramState.Unreachable;
        }

        long? ageSeconds = activity?.LastAgeSeconds;
        if (ageSeconds is null && telemetryTimestamp is { } ts)
        {
            ageSeconds = (long)Math.Max(0, (now - ts).TotalSeconds);
        }

        return ageSeconds is { } age && age > idleAfterSeconds ? EngramState.Idle : EngramState.Active;
    }

    /// <summary>
    /// SPEC-104 §C.8.2/§C.8.3: first hit wins, no fallthrough once <paramref name="binaryPath"/> is
    /// set (§G.9). A separator-bearing value must be rooted — never resolved relative to cwd. A
    /// bare name is a PATH lookup only. The fallback list is hardcoded absolutes with
    /// <c>$HOME</c> from the environment, never from cwd/config (§G.11).
    /// </summary>
    internal static string? ResolveBinary(string? binaryPath)
    {
        if (!string.IsNullOrEmpty(binaryPath))
        {
            return ResolveExplicitBinaryPath(binaryPath);
        }

        if (TryFindOnPath("engram") is { } onPath)
        {
            return onPath;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Add(Path.Combine(home, ".local", "bin", "engram"));
        }

        candidates.Add("/usr/local/bin/engram");
        candidates.Add("/opt/homebrew/bin/engram");

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ResolveExplicitBinaryPath(string binaryPath)
    {
        if (binaryPath.IndexOfAny(PathSeparators) < 0)
        {
            return TryFindOnPath(binaryPath);
        }

        return Path.IsPathRooted(binaryPath) ? binaryPath : null;
    }

    internal static string? TryFindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
