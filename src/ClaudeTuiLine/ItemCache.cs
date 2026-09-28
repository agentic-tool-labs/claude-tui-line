using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5: one cache entry for a <c>command</c> item — the value the provider
/// spawn produced, when, and its exit code. The pane width the item last rendered into is a
/// separate concern, kept in its own <see cref="WidthEntry"/> store (§5.0.1's <c>widths/</c>
/// directory) — a long-TTL value entry must not drag a stale width along with it.
/// </summary>
public sealed record CacheEntry(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("capturedAt")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("exitCode")] int ExitCode);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CacheEntry))]
internal partial class CacheEntryJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5.0.1: one entry in the <c>widths/</c> store — the inner width a
/// command item's pane resolved to on the render that last stamped it. Keyed by
/// <see cref="ItemCache.WidthKeyFor"/>, which folds in the render's resolved surface width, so a
/// <c>--preview</c> at one surface width and a live render at another never read or write the
/// same entry (§9.3.4).
/// </summary>
public sealed record WidthEntry([property: JsonPropertyName("paneWidth")] int PaneWidth);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(WidthEntry))]
internal partial class WidthEntryJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5: the per-item command-provider cache. One file per cache key — not one
/// shared map — because two sessions refreshing two different items would otherwise
/// read-modify-write the same file and last-write-wins would silently discard whichever refresh
/// lost the race, leaving that item stale until its own next respawn. Per-key files make
/// last-write-wins correct at the granularity the value actually has. A torn or unparsable cache
/// file is treated as empty (a miss), never an error, per §7.
/// </summary>
public static class ItemCache
{
    public static string ResolveCacheDir(string? cacheOverride, string? xdgCacheHome, string? home)
    {
        if (!string.IsNullOrEmpty(cacheOverride))
        {
            return Path.Combine(cacheOverride, "items");
        }

        var baseDir = !string.IsNullOrEmpty(xdgCacheHome)
            ? xdgCacheHome
            : string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".cache");

        return baseDir is null
            ? Path.Combine(Path.GetTempPath(), "claude-tui-line", "items")
            : Path.Combine(baseDir, "claude-tui-line", "items");
    }

    public static string ResolveCacheDir() =>
        ResolveCacheDir(
            Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CACHE"),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            Environment.GetEnvironmentVariable("HOME"));

    public static string ResolveWidthsCacheDir(string? cacheOverride, string? xdgCacheHome, string? home)
    {
        if (!string.IsNullOrEmpty(cacheOverride))
        {
            return Path.Combine(cacheOverride, "widths");
        }

        var baseDir = !string.IsNullOrEmpty(xdgCacheHome)
            ? xdgCacheHome
            : string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".cache");

        return baseDir is null
            ? Path.Combine(Path.GetTempPath(), "claude-tui-line", "widths")
            : Path.Combine(baseDir, "claude-tui-line", "widths");
    }

    public static string ResolveWidthsCacheDir() =>
        ResolveWidthsCacheDir(
            Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CACHE"),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            Environment.GetEnvironmentVariable("HOME"));

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §5.0.1: the <c>widths/</c> store's key — <c>id</c> + hash of the
    /// item's declared (unsubstituted) argv + <c>cwd</c> + <paramref name="surfaceWidth"/>,
    /// independent of the item's own resolved pane width or any exported env var. Program.cs's
    /// post-sizing pass writes here and <see cref="CommandProvider"/> reads it before that width
    /// is itself known, without the two depending on each other; folding in
    /// <paramref name="surfaceWidth"/> is what keeps a <c>--preview --columns 60</c> and a live
    /// render at 120 from reading or writing each other's entry (§9.3.4). <c>cwd</c> is part of
    /// the key because a command like <c>git status --short</c> means different things in
    /// different sessions, and this cache is shared by every session on the machine.
    /// </summary>
    public static string WidthKeyFor(string id, IReadOnlyList<string> argv, string? cwd, int? surfaceWidth) =>
        KeyFor(id, string.Join('', argv) + '' + (cwd ?? "") + '' + (surfaceWidth?.ToString(CultureInfo.InvariantCulture) ?? ""));

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4.2.3: the resolved-value cache key, covering every input the child
    /// process can see — the resolved argv (placeholders already substituted), <c>cwd</c>,
    /// <paramref name="paneWidth"/> (<see cref="CommandProvider"/>'s
    /// <c>CLAUDE_TUI_LINE_PANE_WIDTH</c>), and every exported <c>CLAUDE_TUI_LINE_VAL_*</c>
    /// (<paramref name="env"/>, shell-mode only) — stated as a property, not a channel list, so a
    /// future input is covered by construction rather than needing to be remembered. Distinct from
    /// <see cref="WidthKeyFor"/>: this key changes whenever anything the spawned process could
    /// observe changes, which is deliberately not true of the widths-store key.
    /// </summary>
    public static string KeyFor(string id, IReadOnlyList<string> resolvedArgv, string? cwd, int? paneWidth, IReadOnlyDictionary<string, string> env)
    {
        var envJoined = string.Join('', env.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        var joined = string.Join('', resolvedArgv) + '' + (cwd ?? "") + '' + (paneWidth?.ToString(CultureInfo.InvariantCulture) ?? "") + '' + envJoined;
        return KeyFor(id, joined);
    }

    private static string KeyFor(string id, string joined)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        return $"{SanitizeId(id)}-{hash[..16]}";
    }

    private static string SanitizeId(string id)
    {
        var chars = id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return chars.Length == 0 ? "_" : new string(chars);
    }

    public static CacheEntry? TryRead(string cacheDir, string key)
    {
        try
        {
            var path = Path.Combine(cacheDir, key + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize(text, CacheEntryJsonContext.Default.CacheEntry);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Atomic: temp file in the same directory, then rename — never a partial read of a concurrent write.</summary>
    public static void Write(string cacheDir, string key, CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var path = Path.Combine(cacheDir, key + ".json");
            var tempPath = Path.Combine(cacheDir, $".{key}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, CacheEntryJsonContext.Default.CacheEntry));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // §7: a cache write failure just means the next render re-spawns; never an error.
        }
    }

    public static int? TryReadWidth(string widthsDir, string key)
    {
        try
        {
            var path = Path.Combine(widthsDir, key + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize(text, WidthEntryJsonContext.Default.WidthEntry)?.PaneWidth;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Atomic: temp file in the same directory, then rename — never a partial read of a concurrent write.</summary>
    public static void WriteWidth(string widthsDir, string key, int paneWidth)
    {
        try
        {
            Directory.CreateDirectory(widthsDir);
            var path = Path.Combine(widthsDir, key + ".json");
            var tempPath = Path.Combine(widthsDir, $".{key}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new WidthEntry(paneWidth), WidthEntryJsonContext.Default.WidthEntry));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // §7: a cache write failure just means the next render re-spawns; never an error.
        }
    }
}
