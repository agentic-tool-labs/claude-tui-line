using System.Text.Json;
using ClaudeTuiLine;
using ClaudeTuiLineShared;
using Spectre.Console;

// §9.1: argv is inspected only far enough to notice it is empty before the render path below
// takes over unchanged — no flag may change what the no-argv path emits.
if (args.Length == 0)
{
    return await RunAsync();
}

return await RunCli(args);

static async Task<int> RunAsync(string? explicitConfigPath = null)
{
    try
    {
        string? rawInput;
        try
        {
            rawInput = await Console.In.ReadToEndAsync().ConfigureAwait(false);
        }
        catch
        {
            rawInput = null;
        }

        // SPEC-101-calibrate-chrome-reserve.md §5.1: after stdin drains (so Claude Code's pipe
        // never sees EPIPE) and before config/git/Engram/item work, none of which a calibration
        // probe needs. Returns and never reaches the surface-sizing root below, so SPEC-V2-
        // FRAMEWORK.md §2.5 ("COLUMNS read exactly once, at the surface-sizing root") still holds.
        if (CalibrateCommand.TryRunHookProbe(rawInput) is int probeExitCode)
        {
            return probeExitCode;
        }

        var input = ParseInput(rawInput);

        // Fired as soon as cwd is known (i.e. right after stdin is parsed), not awaited yet —
        // it overlaps the config load, the Engram telemetry read, and segment building below.
        var gitProbeTask = GitBranch.ProbeAsync(input.Cwd);

        var (topLevel, pane, configPath, unreadableReason, unreadableReasonProtectedLength) = ConfigResolution.LoadRenderConfig(explicitConfigPath);

        // SPEC-V2-FRAMEWORK.md §9.2.1: an asserted config that can't be read draws the reason,
        // not the config and not the defaults — the render path's only output channel is this
        // one row, so nothing below it (items, git probe, borders) runs.
        if (unreadableReason is not null)
        {
            var diagnosticWidth = SurfaceLayout.ComputeWidth(Environment.GetEnvironmentVariable("COLUMNS"), chromeReserve: 0);
            Console.Out.WriteLine(ConfigUnreadableMessage.Format(configPath, unreadableReason, diagnosticWidth, unreadableReasonProtectedLength));
            return 0;
        }

        var engramProbeTask = EngramProbe.BuildAsync(input.SessionId, DateTimeOffset.UtcNow, topLevel.ItemSettings?.Engram, ItemCache.ResolveCacheDir());

        EngramProbeResult? engram;
        try
        {
            engram = await engramProbeTask.ConfigureAwait(false);
        }
        catch
        {
            engram = null;
        }

        string? gitBranch;
        try
        {
            gitBranch = await gitProbeTask.ConfigureAwait(false);
        }
        catch
        {
            gitBranch = null;
        }

        var ctx = new ItemContext(input, gitBranch, engram, () => RemoteUrl.Probe(input.Cwd, ItemCache.ResolveCacheDir()), topLevel.ItemSettings,
            () => HierarchyStatus.Probe(input.Cwd, input.SessionId, DateTimeOffset.UtcNow));

        var columnsEnv = Environment.GetEnvironmentVariable("COLUMNS");

        // SurfaceLayout.ComputeWidth is the one place chromeReserve is subtracted (SPEC.md §6
        // "MEASURED"; SPEC-V2-FRAMEWORK.md §2.5: COLUMNS read exactly once, at the surface-sizing
        // root). Everything below this takes a plain width and has no knowledge of COLUMNS.
        var surfaceWidth = SurfaceLayout.ComputeWidth(columnsEnv, topLevel.ChromeReserve);

        var console = CreateRenderConsole(topLevel);

        // §9.8.2: the render path constructs a collector and discards it — drawing code stays on
        // the one path whether or not anyone reads what it collects.
        var notes = new RenderNoteCollector();

        // §5/§6.5: one values/tokens resolution, up front, shared by sizing, rendering, and border
        // colour alike — needed even on the legacy path below, since a border's colour rule can
        // name a builtin's "from" independent of whether any item is placed.
        var tokens = topLevel.Colors;
        var cacheDir = ItemCache.ResolveCacheDir();
        var widthsDir = ItemCache.ResolveWidthsCacheDir();
        IReadOnlyDictionary<string, string?> values;
        IReadOnlyCollection<string> unavailableIds;
        try
        {
            var resolution = await ItemValueResolver.ResolveAsync(pane, ctx, tokens, rawInput, cacheDir, widthsDir, surfaceWidth, notes).ConfigureAwait(false);
            values = resolution.Values;
            unavailableIds = resolution.UnavailableIds;
        }
        catch
        {
            values = ItemValueResolver.Resolve(pane, ctx, tokens);
            unavailableIds = UnresolvedCommandIds(pane);
        }

        // §11: splits only size once a real width is known. A configured split with COLUMNS unset
        // has no budget to divide, so it falls back to the same single-leaf-pane pipeline a
        // no-surface config uses — an engineering default for a combination no acceptance case or
        // required test covers, not a spec-mandated behavior. §8: a root leaf pane with top-level
        // items configured (no surface) also routes here — the split-tree pipeline already renders
        // a single non-split leaf correctly, so a leaf's own items render the same way whether or
        // not it happens to sit inside a split, rather than needing a second items pipeline in the
        // legacy branch below.
        // §9.3.2: which pipeline runs and whether a border wraps the result is decided by
        // ComputeRows, shared with RunPreview below, so the real render and a preview capture
        // can't compute that decision two different ways.
        // SPEC-87 §12.7.1: one compound id -> Segment map, built once above both entry points
        // (this pipeline's ComputeRows and its own transitive SizeResolver.Resolve call) so the
        // two never resolve a compound to a different Segment instance.
        var compounds = LeafItems.BuildCompoundMap(pane, values, ctx, tokens);

        var (rows, renderingPanel, boxBorder, borderColor) =
            ComputeRows(pane, surfaceWidth, ctx, values, tokens, compounds, notes, input.Cwd, widthsDir, unavailableIds, topLevel.SurfaceMaxRows, topLevel.Collapse);
        DrawRows(console, rows, renderingPanel, boxBorder, borderColor, surfaceWidth);

        // SPEC-101 §12.4: opposite end of RunAsync from the §5.1 probe branch above; §12.3 rule 2
        // makes the two mutually exclusive (an in-progress calibration suppresses the nudge).
        CalibrateCommand.MaybeAppendNudge(input.Version, topLevel.CalibrationPrompt, surfaceWidth);

        return 0;
    }
    catch (Exception ex)
    {
        // A render failure must not exit silently identical to a correctly-configured empty
        // statusline (exit 0, empty stdout, empty stderr) — that made a real defect indistinguishable
        // from "nothing configured to render". The exit code stays 0 regardless: this runs on every
        // prompt render, and a nonzero exit or a stack trace on stderr would spam the terminal.
        Console.Out.WriteLine("claude-tui-line: render failed (see stderr)");
        Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        return 0;
    }
}

// SPEC-V2-FRAMEWORK.md §2.4 rule 5: panes are sized by this file's own arithmetic, not by
// Spectre — Profile.Width stays at the sentinel for all pane rendering so Spectre never
// re-wraps a row the pane pipeline deliberately laid out. §9.3.2: `--preview` renders through
// this exact console (instance and configuration alike), not the ColorsConsole.Create instance
// `--colors` uses, so its stdout is what the no-argv render path would have produced.
static IAnsiConsole CreateRenderConsole(ResolvedConfig topLevel, TextWriter? output = null)
{
    var console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = topLevel.ColorSystem,
        Interactive = InteractionSupport.No,
        Out = new AnsiConsoleOutput(output ?? Console.Out),
    });

    console.Profile.Width = int.MaxValue / 2;
    return console;
}

// §9.3.2: which pipeline runs (split-tree vs the single legacy leaf) and whether a border wraps
// the result — the same decision RunAsync always made inline — now lives in exactly one place, so
// RunPreview's capture can't diverge from what the real render path would have produced. Returns
// the content rows either way; DrawRows below decides how to put them on a console, since
// --preview's --json form needs the rows without a border drawn around them.
static (IReadOnlyList<PaneRow> Rows, bool RenderingPanel, BoxBorder? BoxBorder, Style BorderColor) ComputeRows(
    Pane pane,
    int? surfaceWidth,
    ItemContext ctx,
    IReadOnlyDictionary<string, string?> values,
    IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
    IReadOnlyDictionary<string, Segment> compounds,
    RenderNoteCollector notes,
    string? cwd,
    string widthsDir,
    IReadOnlyCollection<string> unavailableIds,
    int surfaceMaxRows,
    bool collapseBorders = false)
{
    var useSplitPipeline = surfaceWidth is int
        && (pane.Items.Count > 0 || (pane.Split != PaneSplit.None && pane.Children.Count > 0));

    if (useSplitPipeline)
    {
        // SPEC-V2-FRAMEWORK.md §2.11: collapse-eligible panes are pruned from the raw tree before
        // sizing ever sees them, so a collapsed pane never reserves width or draws a border, and a
        // fully-collapsed root emits zero rows — the same shape as the empty-segments short-circuit
        // the legacy leaf path below already takes.
        var collapsedPane = PaneCollapse.Collapse(pane, values, ctx, compounds, unavailableIds);
        if (collapsedPane is null)
        {
            return (Array.Empty<PaneRow>(), false, null, Style.Plain);
        }

        // SPEC-V2-FRAMEWORK.md §2.8.1: surfaceMaxRows and every pane's own maxRows are enforced
        // by the degrade ladder, which resolves both the sized tree and its rendered contribution
        // in one pass so a rejected intermediate attempt's width resolution never leaks out.
        var (resolvedRoot, rootContribution) = HeightLadder.Resolve(collapsedPane, surfaceWidth!.Value, surfaceMaxRows, ctx, values, tokens, compounds, notes, collapseBorders);
        // §5.0.1/§9.3.4: unconditional now that the widths store is keyed by resolved surface
        // width — a --preview at one width and a live render at another write distinct entries,
        // so stamping here can no longer corrupt a render at a different width.
        StampPaneWidths(resolvedRoot, cwd, widthsDir, surfaceWidth!.Value);

        return (rootContribution.Buffer.Rows, false, null, Style.Plain);
    }

    var segments = SegmentBuilder.Build(ctx);
    if (segments.Count == 0)
    {
        return (Array.Empty<PaneRow>(), false, null, Style.Plain);
    }

    // Border reserve is a property of the box being drawn (2 verticals + 2 padding cells),
    // not of the terminal — it reduces the surface to the pane's own content budget
    // (SPEC-V2-FRAMEWORK.md §2.10: leaf inner width = outer - reserve(p)).
    var borderReserve = SizeResolver.OwnBorderReserve(pane);
    var contentWidth = surfaceWidth is int sw ? Math.Max(0, sw - borderReserve) : (int?)null;

    // §2.6: the single root pane defaults to `overflow` (v1-identical passthrough) so an
    // unconfigured surface renders exactly as it always has.
    var overflow = pane.Overflow ?? OverflowMode.Overflow;
    var buffer = PaneRenderer.RenderLeaf(segments, contentWidth, overflow, pane.Ellipsis, notes);

    // §2.4: row padding to the pane's own width (trimmed back at the very end) only applies
    // once a pane width is actually known; with no COLUMNS there is no width to pad to, so
    // the buffer's own rows are used as-is.
    IReadOnlyList<PaneRow> rows = contentWidth is int width
        ? Compositor.ComposeRoot(new[] { new Compositor.PaneContribution(buffer, width, HasBackground: false) })
        : buffer.Rows;

    // SPEC.md §3/§6b: one predicate — did §6 take its single-line fallback at this content
    // width? — decides border suppression; renderingPanel decides Panel construction. Only a
    // rendered panel guarantees Spectre won't re-break a row the pane pipeline deliberately
    // left overwide (fallback row, or an unsplit oversized segment outside a panel).
    var fallback = RowLayout.IsFallbackWidth(contentWidth);
    var suppressBorder = pane.Border.Style is not null && fallback;
    var renderingPanel = pane.Border.Style is not null && !suppressBorder;
    var borderColor = renderingPanel
        ? ColorResolution.ResolveBorderColor(pane.Border.Color, values, tokens)
        : Style.Plain;

    return (rows, renderingPanel, renderingPanel ? pane.Border.Style : null, borderColor);
}

// §2.11.2: the sync fallback (ItemValueResolver.Resolve) never spawns commands at all, so it has
// no way to tell "this command legitimately printed nothing" from "it didn't answer" apart. Treating
// every placed command item as unavailable here is the conservative read of that gap — collapsing a
// pane on a signal this path can't actually produce is exactly the flicker §2.11.2 rules out.
static IReadOnlyCollection<string> UnresolvedCommandIds(Pane root) =>
    ItemValueResolver.WalkItems(root, "")
        .Select(e => e.Item)
        .Where(item => item.Command is { Count: > 0 } && item.Id is { Length: > 0 })
        .Select(item => item.Id!)
        .ToHashSet(StringComparer.Ordinal);

// The one place either drawing form (bordered Panel vs plain per-row MarkupLine) is produced, so
// RunAsync's real console and RunPreview's captured one can't draw the same rows two different ways.
static void DrawRows(IAnsiConsole console, IReadOnlyList<PaneRow> rows, bool renderingPanel, BoxBorder? boxBorder, Style borderColor, int? surfaceWidth)
{
    if (renderingPanel)
    {
        var panel = new Panel(new Markup(string.Join('\n', rows.Select(r => OscHyperlink.EscapeForRender(r.Markup)))))
            .Padding(1, 0)
            .Border(boxBorder!)
            .BorderStyle(borderColor);

        panel.Width = surfaceWidth!.Value;
        console.Write(panel);
        return;
    }

    foreach (var row in rows)
    {
        console.MarkupLine(OscHyperlink.EscapeForRender(row.Markup));
    }
}

// §9.3/§9.3.2/§9.4: --preview renders through the same ComputeRows/DrawRows/CreateRenderConsole
// the no-argv render path uses — nothing here is a second implementation of sizing, drawing, or
// console configuration, only of the CLI-level concerns (stdin-vs-synthetic input, the config
// exit-3 gate, and --json-vs-bare formatting) that are specific to this subcommand.
static async Task<int> RunPreview(bool json, string? explicitConfigPath, int? columns)
{
    var configPath = explicitConfigPath ?? ConfigPath.ResolveConfigPath();

    if (configPath is not null)
    {
        var result = ConfigLoader.ReadConfigForCheck(configPath);

        // §9.4: exit 3 belongs to the config, not to --check specifically — --preview hits the
        // same gate, via the same ReadConfigForCheck RunCheck already uses.
        if (result.Status == ConfigReadStatus.ParseError)
        {
            return WriteConfigUnreadable(json, configPath, result.ErrorMessage ?? "could not be parsed");
        }

        if (result.Status == ConfigReadStatus.NoFile && explicitConfigPath is not null)
        {
            return WriteConfigUnreadable(json, configPath, "no such file");
        }
    }

    var (topLevel, pane) = ConfigLoader.LoadAll(configPath);

    string? rawInput = null;
    if (Console.IsInputRedirected)
    {
        try
        {
            rawInput = await Console.In.ReadToEndAsync().ConfigureAwait(false);
        }
        catch
        {
            rawInput = null;
        }
    }

    // §9.3: real stdin JSON if given, else the one shared synthetic fixture --items also uses for
    // its own `example` field — and an admission on stderr, since a preview built from invented
    // values that doesn't say so reads as the item being broken rather than untested.
    var usedSynthetic = string.IsNullOrWhiteSpace(rawInput);

    StatusInput input;
    ItemContext ctx;
    if (usedSynthetic)
    {
        input = SyntheticFixture.Input;
        ctx = SyntheticFixture.CreateItemContext(topLevel.ItemSettings);
    }
    else
    {
        input = ParseInput(rawInput);
        var gitProbeTask = GitBranch.ProbeAsync(input.Cwd);
        var engramProbeTask = EngramProbe.BuildAsync(input.SessionId, DateTimeOffset.UtcNow, topLevel.ItemSettings?.Engram, ItemCache.ResolveCacheDir());

        EngramProbeResult? engram;
        try
        {
            engram = await engramProbeTask.ConfigureAwait(false);
        }
        catch
        {
            engram = null;
        }

        string? gitBranch;
        try
        {
            gitBranch = await gitProbeTask.ConfigureAwait(false);
        }
        catch
        {
            gitBranch = null;
        }

        ctx = new ItemContext(input, gitBranch, engram, () => RemoteUrl.Probe(input.Cwd, ItemCache.ResolveCacheDir()), topLevel.ItemSettings,
            () => HierarchyStatus.Probe(input.Cwd, input.SessionId, DateTimeOffset.UtcNow));
    }

    // §9.3: --columns sets the width; absent, COLUMNS; absent that, a default of 100.
    // SurfaceLayout.ComputeWidth stays the one place chromeReserve is subtracted — only this
    // fallback chain (columns/COLUMNS/100) is specific to --preview.
    int resolvedColumns;
    string columnsOrigin;
    if (columns is int explicitColumns)
    {
        resolvedColumns = explicitColumns;
        columnsOrigin = "--columns";
    }
    else if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var envColumns))
    {
        resolvedColumns = envColumns;
        columnsOrigin = "COLUMNS";
    }
    else
    {
        resolvedColumns = 100;
        columnsOrigin = "the default of 100";
    }

    var usableColumns = SurfaceLayout.ComputeWidth(resolvedColumns.ToString(), topLevel.ChromeReserve)!.Value;

    var tokens = topLevel.Colors;
    var cacheDir = ItemCache.ResolveCacheDir();
    var widthsDir = ItemCache.ResolveWidthsCacheDir();
    var notes = new RenderNoteCollector();
    IReadOnlyDictionary<string, string?> values;
    IReadOnlyCollection<string> unavailableIds;
    try
    {
        var resolution = await ItemValueResolver.ResolveAsync(pane, ctx, tokens, rawInput, cacheDir, widthsDir, usableColumns, notes).ConfigureAwait(false);
        values = resolution.Values;
        unavailableIds = resolution.UnavailableIds;
    }
    catch
    {
        values = ItemValueResolver.Resolve(pane, ctx, tokens);
        unavailableIds = UnresolvedCommandIds(pane);
    }

    // SPEC-87 §12.7.1: one compound id -> Segment map, built once above both entry points this
    // preview pipeline reaches (ComputeRows and its own transitive SizeResolver.Resolve call).
    var compounds = LeafItems.BuildCompoundMap(pane, values, ctx, tokens);

    if (json)
    {
        // §5.0.1/§9.3.4: stamping here is safe unconditionally — the widths store is keyed by
        // resolved surface width, so a preview at one width and a live render at another write
        // distinct entries and can't corrupt each other's stamped pane width.
        var (jsonRows, jsonRenderingPanel, jsonBoxBorder, jsonBorderColor) =
            ComputeRows(pane, usableColumns, ctx, values, tokens, compounds, notes, cwd: null, widthsDir, unavailableIds, topLevel.SurfaceMaxRows, topLevel.Collapse);

        // §9.3.4: a row is a line of the rendered surface, borders included — the same draw the
        // bare form produces, captured through the same console configuration rather than a second
        // one, then AnsiStrip'd (never Markup.Remove, which cannot safely unwrap a raw OSC 8
        // hyperlink — see OscHyperlink.EscapeForRender's own doc comment).
        var writer = new StringWriter();
        var captureConsole = CreateRenderConsole(topLevel, writer);
        DrawRows(captureConsole, jsonRows, jsonRenderingPanel, jsonBoxBorder, jsonBorderColor, usableColumns);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.None).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        // §9.3.4: a bordered pane's top and bottom border lines are rows too, so bare --preview and
        // --preview --json report the same line count for the same render. `width` is computed by
        // the same function the layout used, never a second measurement — reused directly from
        // PaneRow.Width wherever the layout produced the rendered line, and taken from the rendered
        // text's own length only where no such layout value exists (Panel-drawn border/content
        // lines, whose border decoration Spectre adds outside PaneRow tracking). A content row's
        // pre-border width — what the layout measured before a panel was wrapped around it, useful
        // for spotting raggedness in a split pipeline — is reported separately as `contentWidth`;
        // a border line has no such number, so the field is left absent for it.
        List<PreviewRowJson> rowsJson;
        if (jsonRenderingPanel)
        {
            var topText = AnsiStrip.Strip(lines[0]);
            rowsJson = new List<PreviewRowJson>(lines.Count) { new(topText, topText.Length) };
            for (var i = 0; i < jsonRows.Count; i++)
            {
                var text = AnsiStrip.Strip(lines[i + 1]);
                rowsJson.Add(new PreviewRowJson(text, text.Length, jsonRows[i].Width));
            }

            var bottomText = AnsiStrip.Strip(lines[^1]);
            rowsJson.Add(new PreviewRowJson(bottomText, bottomText.Length));
        }
        else
        {
            rowsJson = jsonRows.Zip(lines, (row, line) =>
            {
                var text = AnsiStrip.Strip(line);
                return new PreviewRowJson(text, row.Width, row.Width);
            }).ToList();
        }

        var notesJson = notes.Notes.Select(n => new PreviewNoteJson(n.Message)).ToList();

        if (usedSynthetic)
        {
            Console.Error.WriteLine("claude-tui-line: no stdin given (or stdin was empty/a terminal) — previewing with the built-in synthetic payload instead of real input");
        }

        var payload = new PreviewResultJson(resolvedColumns, usableColumns, rowsJson, notesJson);
        Console.Out.WriteLine(JsonSerializer.Serialize(payload, PreviewJsonContext.Default.PreviewResultJson));
        return 0;
    }

    var (bareRows, renderingPanel, boxBorder, borderColor) =
        ComputeRows(pane, usableColumns, ctx, values, tokens, compounds, notes, cwd: null, widthsDir, unavailableIds, topLevel.SurfaceMaxRows, topLevel.Collapse);

    // §9.3.2: bare --preview writes through the render path's own console configuration (forced
    // ANSI, not the auto-detecting instance --colors uses), captured first so the columns/notes
    // lines that follow on stderr can't interleave with it.
    var bareWriter = new StringWriter();
    var bareConsole = CreateRenderConsole(topLevel, bareWriter);
    DrawRows(bareConsole, bareRows, renderingPanel, boxBorder, borderColor, usableColumns);
    Console.Out.Write(bareWriter.ToString());

    if (usedSynthetic)
    {
        Console.Error.WriteLine("claude-tui-line: no stdin given (or stdin was empty/a terminal) — previewing with the built-in synthetic payload instead of real input");
    }

    Console.Error.WriteLine($"claude-tui-line: preview at {resolvedColumns} columns (from {columnsOrigin})");

    foreach (var note in notes.Notes)
    {
        Console.Error.WriteLine($"claude-tui-line: {note.Message}");
    }

    return 0;
}

// §9.1/§9.1.1: the CLI surface, entered only for non-empty argv.
static async Task<int> RunCli(string[] args)
{
    var json = args.Contains("--json");
    var check = false;
    var version = false;
    var items = false;
    var colors = false;
    var preview = false;
    var accepted = false;
    var fixture = false;
    var schema = false;
    var calibrate = false;
    string? calibrateSaw = null;
    var calibrateNoEllipsis = false;
    string? calibrateSet = null;
    var calibrateConfirm = false;
    var calibrateReject = false;
    var calibrateCancel = false;
    var calibrateStatus = false;
    var calibrateDismiss = false;
    string? configPath = null;
    int? columns = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--check":
                check = true;
                break;
            case "--version":
                version = true;
                break;
            case "--items":
                items = true;
                break;
            case "--colors":
                colors = true;
                break;
            case "--preview":
                preview = true;
                break;
            case "--accepted":
                accepted = true;
                break;
            case "--fixture":
                fixture = true;
                break;
            case "--schema":
                schema = true;
                break;
            case "--calibrate":
                calibrate = true;
                break;
            case "--saw":
                if (i + 1 >= args.Length)
                {
                    return WriteUsageError(json, "--saw requires a digit argument");
                }

                calibrateSaw = args[++i];
                break;
            case "--no-ellipsis":
                calibrateNoEllipsis = true;
                break;
            case "--set":
                if (i + 1 >= args.Length)
                {
                    return WriteUsageError(json, "--set requires a digit argument");
                }

                calibrateSet = args[++i];
                break;
            case "--confirm":
                calibrateConfirm = true;
                break;
            case "--reject":
                calibrateReject = true;
                break;
            case "--cancel":
                calibrateCancel = true;
                break;
            case "--status":
                calibrateStatus = true;
                break;
            case "--dismiss":
                calibrateDismiss = true;
                break;
            case "--json":
                break;
            case "--config":
                if (i + 1 >= args.Length)
                {
                    return WriteUsageError(json, "--config requires a path argument");
                }

                configPath = args[++i];
                break;
            case "--columns":
                if (i + 1 >= args.Length)
                {
                    return WriteUsageError(json, "--columns requires a numeric argument");
                }

                if (!int.TryParse(args[++i], out var columnsValue) || columnsValue <= 0)
                {
                    return WriteUsageError(json, "--columns requires a positive integer argument");
                }

                columns = columnsValue;
                break;
            default:
                return WriteUsageError(json, $"unrecognized argument: '{args[i]}'");
        }
    }

    // §9.4.4: --check, --version, --items, --colors, --preview, --accepted, --fixture, and --schema
    // are modes — exactly zero or one may appear in argv. This replaces the old pairwise mutual-
    // exclusion table (unmaintainable past four commands: six pairs become ten on a fifth), so the
    // rule holds for an eighth command without being edited.
    var modeCount = new[] { check, version, items, colors, preview, accepted, fixture, schema, calibrate }.Count(selected => selected);
    if (modeCount > 1)
    {
        return WriteUsageError(json, "--check, --version, --items, --colors, --preview, --accepted, --fixture, --schema, and --calibrate are mutually exclusive");
    }

    // SPEC-101 §12.9: the --calibrate sub-flags only mean anything alongside --calibrate itself —
    // stated as its own usage error rather than left to fall out of acceptedModifiers, since these
    // take arguments and there are eight of them.
    var calibrateSubFlagUsed = calibrateSaw is not null || calibrateNoEllipsis || calibrateSet is not null
        || calibrateConfirm || calibrateReject || calibrateCancel || calibrateStatus || calibrateDismiss;
    if (calibrateSubFlagUsed && !calibrate)
    {
        return WriteUsageError(json, "--saw, --no-ellipsis, --set, --confirm, --reject, --cancel, --status, and --dismiss require --calibrate");
    }

    var calibrateSubFlagCount = new[] { calibrateSaw is not null, calibrateNoEllipsis, calibrateSet is not null, calibrateConfirm, calibrateReject, calibrateCancel, calibrateStatus, calibrateDismiss }.Count(selected => selected);
    if (calibrateSubFlagCount > 1)
    {
        return WriteUsageError(json, "--saw, --no-ellipsis, --set, --confirm, --reject, --cancel, --status, and --dismiss are mutually exclusive");
    }

    // §9.4.4: --json, --columns, and --config are modifiers, not modes — each mode's accepted set
    // is looked up here rather than tested pairwise, so a sixth mode is one more table entry
    // instead of a new rule for every existing one. Render (modeCount == 0) is the mode selected
    // when none of the others is, per §9.2.1, and reads --config the same way --check/--preview do.
    var (modeLabel, acceptedModifiers) = check ? ("--check", new[] { "json", "config" })
        : version ? ("--version", Array.Empty<string>())
        : items ? ("--items", new[] { "json" })
        : colors ? ("--colors", new[] { "json" })
        : preview ? ("--preview", new[] { "json", "columns", "config" })
        : accepted ? ("--accepted", new[] { "json" })
        : fixture ? ("--fixture", Array.Empty<string>())
        : schema ? ("--schema", new[] { "json" })
        // SPEC-101 §6.2: --config is deliberately absent here — the hook can never receive
        // --config, so letting the CLI accept it would let the two silently disagree about where
        // the calibration state/record files live. The generic --config check below turns this
        // omission into the named usage error.
        : calibrate ? ("--calibrate", new[] { "json" })
        : ("rendering", new[] { "config" });

    if (json && !acceptedModifiers.Contains("json"))
    {
        return WriteUsageError(json, $"--json is not valid with {modeLabel}");
    }

    // §1.1.3: unlike --items/--colors, --accepted has no plain-text form yet — bare --accepted
    // is a usage error naming the correct form, so a plain-text form stays purely additive
    // whenever one is added, rather than stranding JSON as the bare default forever.
    if (accepted && !json)
    {
        return WriteUsageError(json, "bare --accepted is not supported; use --accepted --json");
    }

    // §5.4: --schema follows --accepted's precedent exactly — requiring --json now, while the
    // surface has no users, makes a plain-text form purely additive later.
    if (schema && !json)
    {
        return WriteUsageError(json, "bare --schema is not supported; use --schema --json");
    }

    if (columns is not null && !acceptedModifiers.Contains("columns"))
    {
        return WriteUsageError(json, $"--columns is not valid with {modeLabel}");
    }

    if (configPath is not null && !acceptedModifiers.Contains("config"))
    {
        return WriteUsageError(json, $"--config is not valid with {modeLabel}");
    }

    if (modeCount == 0)
    {
        return await RunAsync(configPath);
    }

    if (version)
    {
        Console.Out.WriteLine(AssemblyVersionInfo.InformationalVersion);
        return 0;
    }

    if (colors)
    {
        return RunColors(json);
    }

    if (items)
    {
        return RunItems(json);
    }

    if (preview)
    {
        return await RunPreview(json, configPath, columns);
    }

    if (accepted)
    {
        return RunAccepted();
    }

    if (fixture)
    {
        return RunFixture();
    }

    if (schema)
    {
        return RunSchema();
    }

    if (calibrate)
    {
        return CalibrateCommand.RunCli(json, calibrateSaw, calibrateNoEllipsis, calibrateSet, calibrateConfirm, calibrateReject, calibrateCancel, calibrateStatus, calibrateDismiss);
    }

    return RunCheck(json, configPath);
}

// §12.3.1/§12.7.1/§12.7.2: writes §9.3.1's fixture to stdout with cwd replaced by the process's
// real working directory — the one payload commands/migrate.md, commands/setup.md, and
// commands/revert.md pipe into --preview or the user's own script instead of each hand-rolling
// its own incomplete literal.
static int RunFixture()
{
    var payload = SyntheticFixture.WithRealCwd(Environment.CurrentDirectory);
    Console.Out.WriteLine(JsonSerializer.Serialize(payload, StatusInputJsonContext.Default.StatusInput));
    return 0;
}

// §9.1.1: static by construction — reads and resolves the config, runs nothing, spawns no
// subprocess. §9.4's exit codes: 0 clean, 1 at least one error diagnostic, 2 usage, 3 unreadable.
static int RunCheck(bool json, string? explicitConfigPath)
{
    var configPath = explicitConfigPath ?? ConfigPath.ResolveConfigPath();
    UserConfig? config = null;

    if (configPath is not null)
    {
        var result = ConfigLoader.ReadConfigForCheck(configPath);

        // §9.2: an explicit --config that doesn't exist is an error, never a silent fallback to
        // defaults. §9.2.1's row 1 (no --config, nothing at the searched path ⇒ defaults) is the
        // only NoFile case that legitimately proceeds, so it's the one excluded below.
        if (result.Status == ConfigReadStatus.ParseError)
        {
            return WriteConfigUnreadable(json, configPath, result.ErrorMessage ?? "could not be parsed");
        }

        if (result.Status == ConfigReadStatus.NoFile && explicitConfigPath is not null)
        {
            return WriteConfigUnreadable(json, configPath, "no such file");
        }

        config = result.Config;
    }

    var diagnostics = ConfigChecker.Check(config);
    var hasError = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    if (json)
    {
        var payload = new CheckResultJson(
            !hasError,
            diagnostics.Select(d => new DiagnosticJson(d.Path, SeverityText(d.Severity), d.Code, d.Message)).ToList());
        Console.Out.WriteLine(JsonSerializer.Serialize(payload, CheckJsonContext.Default.CheckResultJson));
    }
    else
    {
        foreach (var d in diagnostics)
        {
            Console.Out.WriteLine($"{SeverityText(d.Severity)} {d.Path}: {d.Message} [{d.Code}]");
        }
    }

    return hasError ? 1 : 0;
}

static string SeverityText(DiagnosticSeverity severity) => severity == DiagnosticSeverity.Error ? "error" : "warning";

// §9.6.2/§9.6.2.2: reads no config and probes nothing — every value comes from the registry and
// the one shared synthetic fixture, so there is no failure mode here beyond a crash. Always exits 0.
static int RunItems(bool json)
{
    var result = ItemsCommand.Build();

    if (json)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(result, ItemsJsonContext.Default.ItemsResultJson));
    }
    else
    {
        Console.Out.WriteLine(ItemsCommand.RenderPlainText(result));
    }

    return 0;
}

// §9.6.3/§9.6.3.1: reads no config and probes nothing — every value comes from the registry, so
// there is no failure mode here beyond a crash. Always exits 0. Bare (non-JSON) output is the
// deliberate exception to §9.6.2.2's plain-only rule: a colour swatch has no payload except the
// colour, so this goes through ColorsConsole.Create, which auto-detects Ansi support (so piping
// still degrades to bare names) but pins ColorSystem to Standard so the swatch matches the main
// render path's default rather than the terminal's maximum capability.
static int RunColors(bool json)
{
    var result = ColorsCommand.Build();

    if (json)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(result, ColorsJsonContext.Default.ColorsResultJson));
    }
    else
    {
        var console = ColorsConsole.Create(Console.Out);

        foreach (var line in ColorsCommand.RenderMarkupLines(result))
        {
            console.MarkupLine(line);
        }
    }

    return 0;
}

// §1.1.3: reads no config and probes nothing — every value comes from the parser-colocated
// registries #38 built, so there is no failure mode here beyond a crash. Always exits 0. No
// plain-text form: --accepted requires --json, enforced before this is ever reached.
static int RunAccepted()
{
    var result = AcceptedCommand.Build();
    Console.Out.WriteLine(JsonSerializer.Serialize(result, AcceptedJsonContext.Default.AcceptedResultJson));
    return 0;
}

// SPEC-84-mcp-schema-explorer.md §5.5: reads no config and probes nothing — every value comes from
// ItemsCommand/ColorsCommand/AcceptedCommand's own results plus the hand-authored structures table,
// so there is no failure mode here beyond a crash. Always exits 0. No plain-text form: --schema
// requires --json, enforced before this is ever reached.
static int RunSchema()
{
    var result = SchemaCommand.Build();
    Console.Out.WriteLine(JsonSerializer.Serialize(result, SchemaJsonContext.Default.SchemaResultJson));
    return 0;
}

// §9.4: exit 2, usage error. §9.6: --json emits the failure envelope here too, but a usage error
// never has a filesystem path to report — "" would claim one, so the field is omitted entirely
// (CheckFailureJson.Path is null here and JsonIgnore'd on write), same as diagnostics[] being
// absent rather than empty for the same reason one section over.
static int WriteUsageError(bool json, string message)
{
    if (json)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new CheckFailureJson(false, "usage", null, message), CheckJsonContext.Default.CheckFailureJson));
    }
    else
    {
        Console.Error.WriteLine($"claude-tui-line: {message}");
    }

    return 2;
}

// §9.4: exit 3, the config could not be read or parsed at all. §9.6: the human form prints prose
// to stderr here; the JSON form still goes to stdout, since a caller who asked for JSON gets JSON
// regardless of exit code.
static int WriteConfigUnreadable(bool json, string path, string message)
{
    if (json)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new CheckFailureJson(false, "config-unreadable", path, message), CheckJsonContext.Default.CheckFailureJson));
    }
    else
    {
        Console.Error.WriteLine($"claude-tui-line: {path}: {message}");
    }

    return 3;
}

// §4/§5.0.1: after sizing converges, stamps every non-content-sized leaf's actual inner width into
// the widths store, so the next render's TTL-expired respawn (CommandProvider) sees the pane it
// will actually render into rather than whatever width the last stamp at this surface width recorded.
static void StampPaneWidths(SizeResolver.ResolvedPane node, string? cwd, string widthsDir, int surfaceWidth)
{
    var pane = node.Source;
    if (!SizeResolver.IsContentSized(pane))
    {
        var borderReserve = SizeResolver.OwnBorderReserve(pane);
        var innerWidth = Math.Max(0, node.OuterWidth - borderReserve);

        foreach (var item in pane.Items)
        {
            if (item.Id is { Length: > 0 } id && item.Command is { Count: > 0 } command)
            {
                ItemCache.WriteWidth(widthsDir, ItemCache.WidthKeyFor(id, command, cwd, surfaceWidth), innerWidth);
            }
        }
    }

    foreach (var child in node.Children)
    {
        StampPaneWidths(child, cwd, widthsDir, surfaceWidth);
    }
}

static StatusInput ParseInput(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return new StatusInput();
    }

    try
    {
        return JsonSerializer.Deserialize(raw, StatusInputJsonContext.Default.StatusInput) ?? new StatusInput();
    }
    catch
    {
        return new StatusInput();
    }
}

