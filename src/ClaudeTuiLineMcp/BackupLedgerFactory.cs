namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-12.6-mcp-tools.md §9.7: the ONLY place the real
/// <c>~/.claude/claude-tui-line/backups/</c> and <c>~/.claude/settings.json</c> paths are ever
/// constructed. <see cref="Program"/> registers the result as the DI-injected <see cref="BackupLedger"/>
/// singleton for the running server; every test constructs <see cref="BackupLedger"/> directly
/// with temp-directory paths and never calls this factory, so the default root can never leak into
/// a test by omission.
/// </summary>
internal static class BackupLedgerFactory
{
    public static BackupLedger CreateDefault()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? throw new InvalidOperationException("HOME is not set; the backup root cannot be resolved.");

        var backupRoot = BackupLedger.DefaultBackupRoot(home);
        var settingsPath = Path.Combine(home, ".claude", "settings.json");
        return new BackupLedger(backupRoot, settingsPath);
    }
}
