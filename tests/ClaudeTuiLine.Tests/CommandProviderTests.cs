using System.Runtime.CompilerServices;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4.1 (defect 14): <c>shell: true</c> only ever forwards
/// <c>command[0]</c> to <c>sh -c</c>, so an argv of more than one element under
/// <c>shell: true</c> would silently drop every element after the first and run the wrong
/// command. <see cref="CommandProvider"/> suppresses that item instead of spawning it, while
/// a single-element argv under <c>shell: true</c> — what the string form of <c>command</c>
/// normalizes to — and any argv under <c>shell: false</c> resolve normally.
/// </summary>
public class CommandProviderTests
{
    // ItemCache.Write persists one file per cache key under cacheDir/widthsDir, so a test that
    // shares Path.GetTempPath() with earlier runs of itself can read back a value or note-emitting
    // decision left over from a previous process — not the fresh, live-execution result the test
    // body assumes. A fresh, per-call directory rules that out.
    private static string IsolatedTempDir([CallerMemberName] string? testName = null) =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ctl-tests", $"{testName}-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task ShellTrueWithMultiElementCommand_ResolvesToNull()
    {
        var item = new PaneItem(null, null, null, null, Id: "defect14-shell-multi", Command: new[] { "echo", "hi" }, Shell: true);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Null(value.Value);
    }

    [Fact]
    public async Task ShellTrueWithSingleElementCommand_ResolvesNormally()
    {
        var item = new PaneItem(null, null, null, null, Id: "defect14-shell-single", Command: new[] { "echo hi" }, Shell: true);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("hi", value.Value);
    }

    [Fact]
    public async Task ShellFalseWithMultiElementCommand_ResolvesNormally()
    {
        var item = new PaneItem(null, null, null, null, Id: "defect14-noshell-multi", Command: new[] { "echo", "hi" }, Shell: false);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("hi", value.Value);
    }

    // ---- SPEC-V2-FRAMEWORK.md §4.2/§4.2.2/§4.2.3: argv placeholders end to end ----

    [Fact]
    public async Task NonShell_SpawnedProcessSeesSubstitutedArgv()
    {
        var item = new PaneItem(null, null, null, null, Id: "argv-echo-substituted", Command: new[] { "echo", "{val}" }, Shell: false);
        var values = new Dictionary<string, string?> { ["val"] = "hello" };

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values, unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("hello", value.Value);
    }

    [Fact]
    public async Task Shell_SpawnedProcessSeesReferencedValueAsEnvVarNotSubstitutedIntoCommandString()
    {
        // The trailing "# {val}" is a shell comment — it references "val" so ArgvPlaceholders
        // detects it and exports the env var, without the shell ever seeing "{val}" as text to run.
        var item = new PaneItem(null, null, null, null, Id: "argv-shell-env-export",
            Command: new[] { "echo \"$CLAUDE_TUI_LINE_VAL_VAL\" # {val}" }, Shell: true);
        var values = new Dictionary<string, string?> { ["val"] = "hello" };

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values, unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("hello", value.Value);
    }

    [Fact]
    public async Task UnavailableReferencedSource_SuppressesTheSpawnAndReportsUnavailable()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"argv-unavailable-marker-{Guid.NewGuid():N}.txt");
        var item = new PaneItem(null, null, null, null, Id: "argv-unavailable-source",
            Command: new[] { "sh", "-c", $"touch {marker}", "{other-id}" }, Shell: false);
        var values = new Dictionary<string, string?> { ["other-id"] = "irrelevant" };

        try
        {
            var value = await CommandProvider.ResolveAsync(
                item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
                values, unavailableIds: new[] { "other-id" }, notes: new RenderNoteCollector());

            Assert.True(value.Unavailable);
            Assert.Null(value.Value);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task UnavailableReferencedSource_WithStaleCachedValue_FallsBackToStaleValueAndIsAvailable()
    {
        const string id = "argv-unavailable-stale-fallback";
        var command = new[] { "echo hi {other-id}" };
        var item = new PaneItem(null, null, null, null, Id: id, Command: command, Shell: true);
        var values = new Dictionary<string, string?> { ["other-id"] = "x" };
        var cacheDir = Path.GetTempPath();

        var expansion = ArgvPlaceholders.Expand(command, shell: true, values);
        var valueKey = ItemCache.KeyFor(id, expansion.Argv, cwd: null, paneWidth: null, expansion.ExportedEnv);
        ItemCache.Write(cacheDir, valueKey, new CacheEntry("stale-value", DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5), ExitCode: 0));

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values, unavailableIds: new[] { "other-id" }, notes: new RenderNoteCollector());

        Assert.Equal("stale-value", value.Value);
        Assert.False(value.Unavailable);
    }

    [Fact]
    public async Task ResolvedValueCacheKey_ChangesWhenAReferencedValueChanges_SoTheSecondCallIsNotAStaleHit()
    {
        var item = new PaneItem(null, null, null, null, Id: "argv-cache-key-env",
            Command: new[] { "echo \"$CLAUDE_TUI_LINE_VAL_VAL\" # {val}" }, Shell: true);
        var cacheDir = Path.GetTempPath();

        var first = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            new Dictionary<string, string?> { ["val"] = "one" }, unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());
        var second = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            new Dictionary<string, string?> { ["val"] = "two" }, unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("one", first.Value);
        Assert.Equal("two", second.Value);
    }

    [Fact]
    public async Task ResolvedValueCacheKey_ChangesWhenStampedPaneWidthChanges_SoTheSecondCallIsNotAStaleHit()
    {
        const string id = "argv-cache-key-width";
        var command = new[] { "sh", "-c", "echo $CLAUDE_TUI_LINE_PANE_WIDTH" };
        var item = new PaneItem(null, null, null, null, Id: id, Command: command, Shell: false);
        var cacheDir = Path.GetTempPath();
        var widthsDir = Path.GetTempPath();
        var widthKey = ItemCache.WidthKeyFor(id, command, cwd: null, surfaceWidth: 120);

        ItemCache.WriteWidth(widthsDir, widthKey, paneWidth: 42);
        var first = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir, surfaceWidth: 120, paneWidthEligible: true,
            new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        ItemCache.WriteWidth(widthsDir, widthKey, paneWidth: 99);
        var second = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir, surfaceWidth: 120, paneWidthEligible: true,
            new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("42", first.Value);
        Assert.Equal("99", second.Value);
    }

    // SPEC-V2-FRAMEWORK.md §5.0.1/§9.3.4: the widths store is keyed by resolved surface width, so
    // a --preview at one width and a live render at another never read or write the same entry.
    [Fact]
    public async Task WidthStore_IsPartitionedBySurfaceWidth_SoAPreviewAndALiveRenderDoNotCrossContaminate()
    {
        const string id = "argv-cache-key-width-partitioned";
        var command = new[] { "sh", "-c", "echo $CLAUDE_TUI_LINE_PANE_WIDTH" };
        var item = new PaneItem(null, null, null, null, Id: id, Command: command, Shell: false);
        var cacheDir = Path.GetTempPath();
        var widthsDir = Path.GetTempPath();

        ItemCache.WriteWidth(widthsDir, ItemCache.WidthKeyFor(id, command, cwd: null, surfaceWidth: 60), paneWidth: 55);
        ItemCache.WriteWidth(widthsDir, ItemCache.WidthKeyFor(id, command, cwd: null, surfaceWidth: 120), paneWidth: 115);

        var atPreviewWidth = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir, surfaceWidth: 60, paneWidthEligible: true,
            new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());
        var atLiveWidth = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir, surfaceWidth: 120, paneWidthEligible: true,
            new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Equal("55", atPreviewWidth.Value);
        Assert.Equal("115", atLiveWidth.Value);
    }

    // SPEC-V2-FRAMEWORK.md §2.5.1 rule 2: a content-sized pane's items are measured with
    // CLAUDE_TUI_LINE_PANE_WIDTH unset — not a guess, not zero — because a content pane's width is
    // derived from measuring its own content. paneWidthEligible: false is how the caller
    // (ItemValueResolver, via !SizeResolver.IsContentSized) signals that. This must hold even when a
    // stale width happens to be sitting in the widths store under the same key, so the ineligible
    // path can't accidentally read it.
    [Fact]
    public async Task ContentSizedPane_PaneWidthIneligible_LeavesEnvVarUnsetEvenWithAStampedWidthPresent()
    {
        const string id = "argv-content-pane-width-unset";
        var command = new[] { "sh", "-c", "echo $CLAUDE_TUI_LINE_PANE_WIDTH" };
        var item = new PaneItem(null, null, null, null, Id: id, Command: command, Shell: false);
        var cacheDir = Path.GetTempPath();
        var widthsDir = Path.GetTempPath();
        var widthKey = ItemCache.WidthKeyFor(id, command, cwd: null, surfaceWidth: 120);

        ItemCache.WriteWidth(widthsDir, widthKey, paneWidth: 42);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir, widthsDir, surfaceWidth: 120, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes: new RenderNoteCollector());

        Assert.Null(value.Value);
    }

    // ---- SPEC-V2-FRAMEWORK.md §4.0.1: maxLines is an opt-in, no-default per-item fairness cap ----

    [Fact]
    public async Task NoMaxLines_MultiLineOutput_KeepsEveryLineAndEmitsNoNote()
    {
        var item = new PaneItem(null, null, null, null, Id: "maxlines-unset",
            Command: new[] { "printf 'a\\nb\\nc\\n'" }, Shell: true);
        var notes = new RenderNoteCollector();

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes);

        Assert.Equal("a\nb\nc", value.Value);
        Assert.Empty(notes.Notes);
    }

    [Fact]
    public async Task MaxLinesSet_OutputExceedsCap_TruncatesAndEmitsMaxLinesNote()
    {
        var item = new PaneItem(null, null, null, null, Id: "maxlines-exceeded",
            Command: new[] { "printf 'a\\nb\\nc\\n'" }, Shell: true, MaxLines: 2);
        var notes = new RenderNoteCollector();

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: IsolatedTempDir(), widthsDir: IsolatedTempDir(), surfaceWidth: null, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes);

        Assert.Equal("a\nb", value.Value);
        Assert.Contains(notes.Notes, n => n.Message == "item 'maxlines-exceeded' emitted 3 lines; 2 kept (maxLines)");
    }

    [Fact]
    public async Task MaxLinesSet_OutputUnderCap_KeepsEverythingAndEmitsNoNote()
    {
        var item = new PaneItem(null, null, null, null, Id: "maxlines-under-cap",
            Command: new[] { "printf 'a\\nb\\n'" }, Shell: true, MaxLines: 5);
        var notes = new RenderNoteCollector();

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null, paneWidthEligible: false,
            values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes);

        Assert.Equal("a\nb", value.Value);
        Assert.Empty(notes.Notes);
    }
}
