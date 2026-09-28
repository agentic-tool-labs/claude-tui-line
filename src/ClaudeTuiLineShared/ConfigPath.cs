namespace ClaudeTuiLineShared;

/// <summary>
/// SPEC-83: config path resolution lives here, not in <c>ClaudeTuiLine.ConfigLoader</c>, so that
/// both the AOT/self-contained CLI and the framework-dependent MCP server can share one
/// implementation without the MCP publish tripping NETSDK1151. SPEC-12.6-mcp-tools.md §7.1's
/// argument stands unchanged: a second copy of this rule would let the server validate one file
/// and write another, with every tool reporting success.
/// </summary>
public static class ConfigPath
{
    /// <summary>
    /// SPEC.md §6b "Config path resolution": <paramref name="configPathOverride"/>, when set and
    /// non-empty, is the config file path. Otherwise <c>$HOME/.claude/claude-tui-line.json</c>
    /// (or null if <paramref name="home"/> is itself unset/empty).
    /// </summary>
    public static string? ResolveConfigPath(string? configPathOverride, string? home)
    {
        if (!string.IsNullOrEmpty(configPathOverride))
        {
            return configPathOverride;
        }

        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".claude", "claude-tui-line.json");
    }

    public static string? ResolveConfigPath() =>
        ResolveConfigPath(
            Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG"),
            Environment.GetEnvironmentVariable("HOME"));

    /// <summary>
    /// SPEC-101-calibrate-chrome-reserve.md §6.1: transient calibration state (30-minute expiry),
    /// beside the config, derived from the env-aware no-override form so the hook (no args, no
    /// <c>--config</c>) and the CLI always agree on where it lives — §6.2 rejects <c>--config</c>
    /// with <c>--calibrate</c> specifically to keep this path un-overridable.
    /// </summary>
    public static string? ResolveCalibrationStatePath()
    {
        var configPath = ResolveConfigPath();
        return configPath is null ? null : Path.Combine(Path.GetDirectoryName(configPath)!, "claude-tui-line.calibration.json");
    }

    /// <summary>
    /// SPEC-101-calibrate-chrome-reserve.md §12.1/§12.2: the durable first-run/version-nudge
    /// record. Deliberately a separate file from the transient state above — a corrupt state file
    /// should cost only a re-run of `--calibrate`, while losing this record resurrects the
    /// first-run prompt and forgets any dismissal. Lives beside the config, NOT under
    /// ItemCache.ResolveCacheDir(), which is disposable by design (falls back to $TMPDIR with no
    /// HOME) — clearing a cache directory must not re-nag the user.
    /// </summary>
    public static string? ResolveCalibrationRecordPath()
    {
        var configPath = ResolveConfigPath();
        return configPath is null ? null : Path.Combine(Path.GetDirectoryName(configPath)!, "claude-tui-line.calibration-record.json");
    }
}
