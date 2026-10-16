namespace ClaudeTuiLine;

/// <summary>
/// Adds or removes a guarded <c>FORCE_HYPERLINK=1</c> export in a POSIX shell profile.
/// Claude Code decides whether to pass a statusline's OSC 8 links through before this binary
/// runs, so the only lever is the environment <c>claude</c> starts in. The export is guarded by
/// the variables that mark herdr, tmux, screen and SSH, so plain terminals are left alone.
/// </summary>
/// <remarks>
/// The profile is the user's own file, so every write is preceded by a timestamped backup next to
/// it, goes through a temp file and a rename so a crash cannot leave it half-written, follows a
/// symlinked profile to its real file, keeps the file's mode, and is read back before success is
/// reported. A dry run performs none of the writes and reports what they would have been.
/// </remarks>
internal static class HyperlinksCommand
{
    internal const string Marker = "# claude-tui-line: make Claude Code pass statusline links through here";

    internal const string ExportLine =
        "[ -n \"$HERDR_ENV$TMUX$STY$SSH_CONNECTION$SSH_TTY\" ] && export FORCE_HYPERLINK=1";

    /// <summary>The profile that applies to <paramref name="shell"/>, or null when it is not zsh or bash.</summary>
    internal static string? ProfilePath(string? shell, string home)
    {
        var name = Path.GetFileName(shell ?? string.Empty);
        return name switch
        {
            "zsh" => Path.Combine(home, ".zshenv"),
            "bash" => Path.Combine(home, ".bashrc"),
            _ => null,
        };
    }

    /// <summary>Appends the guarded export unless it is already there. Returns a report.</summary>
    internal static string Enable(string profile, bool dryRun, DateTimeOffset now)
    {
        var target = ResolveTarget(profile);
        var text = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
        if (text.Contains(ExportLine, StringComparison.Ordinal))
        {
            return $"already enabled in {profile}; nothing changed";
        }

        var lead = text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n";
        var updated = $"{text}{lead}{Marker}\n{ExportLine}\n";
        var change = $"  + {Marker}\n  + {ExportLine}";
        return Apply(profile, target, text, updated, dryRun, now, "add to", change);
    }

    /// <summary>Removes the marker and export lines. Returns a report.</summary>
    internal static string Disable(string profile, bool dryRun, DateTimeOffset now)
    {
        var target = ResolveTarget(profile);
        if (!File.Exists(target))
        {
            return $"nothing to remove: {profile} does not exist";
        }

        var text = File.ReadAllText(target);
        var lines = text.Split('\n');
        var kept = lines.Where(l => l != Marker && l != ExportLine).ToArray();
        if (kept.Length == lines.Length)
        {
            return $"nothing to remove in {profile}";
        }

        var change = string.Join("\n", lines.Where(l => l == Marker || l == ExportLine).Select(l => $"  - {l}"));
        return Apply(profile, target, text, string.Join("\n", kept), dryRun, now, "remove from", change);
    }

    private static string ResolveTarget(string profile)
    {
        var full = Path.GetFullPath(profile);
        var info = new FileInfo(full);
        return info.LinkTarget is null ? full : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
    }

    private static string Apply(
        string profile, string target, string before, string after, bool dryRun, DateTimeOffset now, string verb, string change)
    {
        var existed = File.Exists(target);
        var stem = $"{target}.bak-claude-tui-line-{now.ToUniversalTime():yyyyMMddTHHmmssZ}";
        var backup = stem;
        for (var n = 2; File.Exists(backup); n++)
        {
            backup = $"{stem}-{n}";
        }

        var backupNote = existed ? $"backup: {backup}" : "no backup needed: the file does not exist yet";

        if (dryRun)
        {
            return $"dry run, nothing written: would {verb} {profile}\n{change}\n{backupNote}";
        }

        if (existed)
        {
            File.Copy(target, backup, overwrite: false);
        }

        var temp = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.claude-tui-line.tmp");
        try
        {
            File.WriteAllText(temp, after);
            if (existed && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(target));
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }

        if (File.ReadAllText(target) != after)
        {
            if (existed)
            {
                File.Copy(backup, target, overwrite: true);
            }

            throw new IOException($"{target} did not read back as written; the original was restored from {backup}");
        }

        return $"done: {verb} {profile}\n{change}\n{backupNote}\nnew shells and new claude sessions pick it up";
    }
}
