using System.Diagnostics;
using System.Globalization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4/§5: resolves one <c>command</c> item's value against
/// <see cref="ItemCache"/>. A fresh cache entry (within <see cref="PaneItem.TtlSeconds"/>, default
/// 30) is returned without spawning anything. A miss or expired entry spawns the command —
/// argv by default, or <c>sh -c "&lt;string&gt;"</c> when <see cref="PaneItem.Shell"/> — bounded by
/// <see cref="PaneItem.TimeoutMs"/> (default 150) and killed with its whole process tree on
/// timeout, mirroring <see cref="GitBranch"/>. Any failure to produce a fresh value — timeout,
/// nonzero exit, a missing binary — falls back to the last cached value even if expired
/// ("stale-on-failure"); only a command that has never once succeeded resolves to null
/// (suppressed). A clean run with empty output is a legitimate value (empty), cached like any
/// other, so a command that is quiet by design does not respawn every render.
/// </summary>
public static class CommandProvider
{
    internal const int DefaultTtlSeconds = 30;
    internal const int DefaultTimeoutMs = 150;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.11.2: <see cref="Value"/> as <see cref="ResolveAsync"/> always
    /// returned, plus <see cref="Unavailable"/> — true only when this render's fresh spawn attempt
    /// failed (timed out, exited nonzero, or could not start) and there was no cached value to fall
    /// back on, i.e. exactly the case <see cref="Value"/> is null for a reason other than "the
    /// command legitimately printed nothing." A TTL-fresh cache hit and a stale-on-failure fallback
    /// to a cached value are both <see cref="Unavailable"/> = false, since both answered.
    /// </summary>
    public readonly record struct CommandResolution(string? Value, bool Unavailable);

    public static async Task<CommandResolution> ResolveAsync(
        PaneItem item, string? rawStdinJson, string? cwd, string cacheDir, string widthsDir, int? surfaceWidth, bool paneWidthEligible,
        IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string> unavailableIds, RenderNoteCollector notes)
    {
        if (item.Id is not { Length: > 0 } id || item.Command is not { Count: > 0 } command)
        {
            return new CommandResolution(null, Unavailable: false);
        }

        var ttl = TimeSpan.FromSeconds(item.TtlSeconds is > 0 ? item.TtlSeconds.Value : DefaultTtlSeconds);
        var timeout = TimeSpan.FromMilliseconds(item.TimeoutMs is > 0 ? item.TimeoutMs.Value : DefaultTimeoutMs);

        // §5.0.1: the item's placement identity (unsubstituted argv + cwd) plus this render's
        // resolved surface width, used to look up the width the item's pane last resolved to on a
        // render at the same surface width — see ItemCache.WidthKeyFor's doc comment.
        var widthKey = ItemCache.WidthKeyFor(id, command, cwd, surfaceWidth);
        var previousPaneWidth = paneWidthEligible ? ItemCache.TryReadWidth(widthsDir, widthKey) : null;

        var expansion = ArgvPlaceholders.Expand(command, item.Shell, values);
        var valueKey = ItemCache.KeyFor(id, expansion.Argv, cwd, previousPaneWidth, expansion.ExportedEnv);

        var cached = ItemCache.TryRead(cacheDir, valueKey);
        if (cached is { } fresh && DateTimeOffset.UtcNow - fresh.CapturedAt < ttl)
        {
            return new CommandResolution(fresh.Value, Unavailable: false);
        }

        // §4.2.2: a placeholder naming a source that did not answer this render makes the whole
        // item unavailable rather than substituting "" for "we don't know" — same stale-on-failure
        // fallback as any other spawn failure, and it declines to pay for a subprocess whose input
        // is already known to be wrong.
        if (expansion.ReferencedIds.Any(unavailableIds.Contains))
        {
            return new CommandResolution(cached?.Value, Unavailable: cached?.Value is null);
        }

        var spawned = await RunAsync(item, expansion, rawStdinJson, cwd, timeout, previousPaneWidth, notes).ConfigureAwait(false);
        if (spawned is not { } result)
        {
            return new CommandResolution(cached?.Value, Unavailable: cached?.Value is null);
        }

        ItemCache.Write(cacheDir, valueKey, new CacheEntry(result.Value, DateTimeOffset.UtcNow, result.ExitCode));
        return new CommandResolution(result.Value, Unavailable: false);
    }

    private readonly record struct SpawnResult(string? Value, int ExitCode);

    private static async Task<SpawnResult?> RunAsync(
        PaneItem item, ArgvPlaceholders.Expansion expansion, string? rawStdinJson, string? cwd, TimeSpan timeout, int? previousPaneWidth,
        RenderNoteCollector notes)
    {
        // §4.1: shell:true only ever forwards argv[0] to `sh -c` — an argv of more than one
        // element under shell:true would silently discard every element after the first and run
        // the wrong command with no signal at all. Suppress instead, the same as any other
        // command that cannot produce a value, so §7/--check can explain why.
        if (item.Shell && expansion.Argv.Count > 1)
        {
            return null;
        }

        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        if (!string.IsNullOrEmpty(cwd))
        {
            psi.WorkingDirectory = cwd;
        }

        if (item.Shell)
        {
            psi.FileName = "sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(expansion.Argv[0]);
        }
        else
        {
            psi.FileName = expansion.Argv[0];
            foreach (var arg in expansion.Argv.Skip(1))
            {
                psi.ArgumentList.Add(arg);
            }
        }

        psi.Environment["CLAUDE_TUI_LINE_ITEM_ID"] = item.Id;
        if (previousPaneWidth is { } width)
        {
            psi.Environment["CLAUDE_TUI_LINE_PANE_WIDTH"] = width.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var (name, value) in expansion.ExportedEnv)
        {
            psi.Environment[name] = value;
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("command did not start");
        }
        catch
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(timeout);

            await process.StandardInput.WriteAsync(rawStdinJson ?? "").ConfigureAwait(false);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            // §4.0.1: no default cap — a command item's output is unbounded unless the config
            // opts into maxLines, at which point excess lines are dropped here, before §3.1's
            // block-packing ever sees the value, and the drop is surfaced as a render note (§9.8.1)
            // since this path has no other channel to report a silent truncation.
            var lines = PaneAssembler.SplitBlockLines(stdout);
            var kept = lines;
            if (item.MaxLines is int cap && cap > 0 && lines.Count > cap)
            {
                kept = lines.Take(cap).ToList();
                notes.Add($"item '{item.Id}' emitted {lines.Count} lines; {kept.Count} kept (maxLines)");
            }

            var value = string.Join("\n", kept);
            return new SpawnResult(value.Length == 0 ? null : value, process.ExitCode);
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
