namespace ClaudeTuiLineShared;

/// <summary>
/// SPEC-101-calibrate-chrome-reserve.md §6.3: the atomic writer used to live only in
/// ClaudeTuiLineMcp, unreachable from the CLI. Moved here since ClaudeTuiLineMcp already
/// references ClaudeTuiLineShared, so both callers share one implementation.
/// </summary>
public static class ConfigWriter
{
    /// <summary>
    /// docs/backup-ledger.md "Writing settings.json" rule 2 / SPEC-V2-FRAMEWORK.md §12.2 rule 3:
    /// atomic temp-file-then-rename, so an interrupted write cannot leave a torn config on disk.
    /// This is the opposite discipline from the ledger append (§9.4) — the two must not be
    /// unified.
    /// </summary>
    public static void WriteAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(dir))
        {
            throw new InvalidOperationException($"could not determine a directory for '{path}'");
        }

        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, path, overwrite: true);
    }
}
