using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SizeResolver.MinRowsPackerInvocationCount is [ThreadStatic], which already keeps
// Columns112_LiveConfig_PackerInvocationCountStaysBounded's reset-render-read sequence isolated
// from every other test class regardless of xUnit's default cross-collection parallelism —
// this collection is belt-and-braces on top of that, not the fix itself. Keeps the isolation
// intact even if a future change (sizing going async, the counter losing [ThreadStatic]) would
// otherwise reopen the cross-test contamination that a shared, unsynchronized counter once
// produced here.
[CollectionDefinition("MinRowsDiagnostics", DisableParallelization = true)]
public class MinRowsDiagnosticsCollection
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3.1: <c>distribute: "min-rows"</c> against the same two-pane config
/// <see cref="SplitAcceptanceTests"/> exercises for <c>greedy</c>, with <c>"distribute": "min-rows"</c>
/// added to the root pane. Acceptance condition 1 (§2.3.1): "on a config small enough to
/// brute-force, the allocation this returns must equal the best found by exhaustively laying out
/// every legal width."
/// </summary>
[Collection("MinRowsDiagnostics")]
public class MinRowsDistributeTests
{
    private const string ConfigJson = """
    {
      "colors": { "model-accent": { "default": "blue" } },
      "surface": {
        "maxRows": 8,
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "distribute": "min-rows",
          "children": [
            { "size": "fill", "overflow": "wrap",
              "border": { "enabled": true, "color": "grey" } },
            { "size": "content", "overflow": "wrap",
              "border": { "enabled": true, "color": "@model-accent" },
              "items": [ { "item": "model", "color": "@model-accent" },
                         { "item": "effort" }, { "item": "thinking" }, { "item": "context" } ] }
          ]
        }
      }
    }
    """;

    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static readonly ItemContext BlankCtx = BlankSurfaceControl.Blank(Ctx);

    private readonly ITestOutputHelper _output;

    public MinRowsDistributeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ConfigJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Renders one candidate leaf in isolation at a given outer width, through the same public
    // pipeline the real root goes through (SizeResolver.Resolve + PaneTreeRenderer.Render) rather
    // than any internal min-rows helper — an independent ground truth, not a re-check of the code
    // under test.
    private static int RowsAt(Pane candidate, int outerWidth, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens) =>
        RowsAt(candidate, outerWidth, values, tokens, Ctx);

    private static int RowsAt(Pane candidate, int outerWidth, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens, ItemContext ctx)
    {
        var resolved = SizeResolver.Resolve(candidate, outerWidth, ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        return rendered.Buffer.Rows.Count;
    }

    private static string RenderMarkup(SizeResolver.ResolvedPane resolved, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens) =>
        string.Join('\n', PaneTreeRenderer.Render(resolved, ctx, values, tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector()).Buffer.Rows.Select(r => r.Markup));

    [Fact]
    public void Columns112_MinRows_MatchesBruteForceOptimalRowCount()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        var left = pane.Children[0];
        var right = pane.Children[1];
        var r = surfaceWidth - pane.Gutter;

        // left is `fill` (uncapped) and right has no configured maxSize, so for this config the
        // real allocator never leaves width unspent (§2.3.1: surplus flows to an uncapped
        // candidate until none remains) — every allocation it could ever produce has
        // w1 + w2 == r. That makes a single sweep over w1, with w2 = r - w1, exhaustive over the
        // allocations actually in competition, without leaning on rows_i's own monotonicity to
        // justify skipping any of them.
        var lo1 = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(left);
        var hi1 = r;

        var bestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, values, topLevel.Colors);
            var rows2 = RowsAt(right, w2, values, topLevel.Colors);
            bestScore = Math.Min(bestScore, Math.Max(rows1, rows2));
        }

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var achievedRows1 = RowsAt(left, resolved.Children[0].OuterWidth, values, topLevel.Colors);
        var achievedRows2 = RowsAt(right, resolved.Children[1].OuterWidth, values, topLevel.Colors);
        var achievedScore = Math.Max(achievedRows1, achievedRows2);

        Assert.Equal(bestScore, achievedScore);

        // SPEC §10.1 blank-surface control: the "achieved == brute-force optimum" invariant must
        // still hold with every item blanked (the algorithm must still pick the optimal split even
        // over trivial content), and the two runs' rendered content must differ.
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankBestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, blankValues, topLevel.Colors, BlankCtx);
            var rows2 = RowsAt(right, w2, blankValues, topLevel.Colors, BlankCtx);
            blankBestScore = Math.Min(blankBestScore, Math.Max(rows1, rows2));
        }
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankAchievedRows1 = RowsAt(left, blankResolved.Children[0].OuterWidth, blankValues, topLevel.Colors, BlankCtx);
        var blankAchievedRows2 = RowsAt(right, blankResolved.Children[1].OuterWidth, blankValues, topLevel.Colors, BlankCtx);
        var blankAchievedScore = Math.Max(blankAchievedRows1, blankAchievedRows2);
        Assert.Equal(blankBestScore, blankAchievedScore);
        BlankSurfaceControl.AssertContentDiffers(
            RenderMarkup(resolved, Ctx, values, topLevel.Colors),
            RenderMarkup(blankResolved, BlankCtx, blankValues, topLevel.Colors));
    }

    [Fact]
    public void Columns60_MinRows_MatchesBruteForceOptimalRowCount()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        var left = pane.Children[0];
        var right = pane.Children[1];
        var r = surfaceWidth - pane.Gutter;

        var lo1 = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(left);
        var hi1 = r;

        var bestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, values, topLevel.Colors);
            var rows2 = RowsAt(right, w2, values, topLevel.Colors);
            bestScore = Math.Min(bestScore, Math.Max(rows1, rows2));
        }

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var achievedRows1 = RowsAt(left, resolved.Children[0].OuterWidth, values, topLevel.Colors);
        var achievedRows2 = RowsAt(right, resolved.Children[1].OuterWidth, values, topLevel.Colors);
        var achievedScore = Math.Max(achievedRows1, achievedRows2);

        Assert.Equal(bestScore, achievedScore);

        // SPEC §10.1 blank-surface control: see the identical block in
        // Columns112_MinRows_MatchesBruteForceOptimalRowCount.
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankBestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, blankValues, topLevel.Colors, BlankCtx);
            var rows2 = RowsAt(right, w2, blankValues, topLevel.Colors, BlankCtx);
            blankBestScore = Math.Min(blankBestScore, Math.Max(rows1, rows2));
        }
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankAchievedRows1 = RowsAt(left, blankResolved.Children[0].OuterWidth, blankValues, topLevel.Colors, BlankCtx);
        var blankAchievedRows2 = RowsAt(right, blankResolved.Children[1].OuterWidth, blankValues, topLevel.Colors, BlankCtx);
        var blankAchievedScore = Math.Max(blankAchievedRows1, blankAchievedRows2);
        Assert.Equal(blankBestScore, blankAchievedScore);
        BlankSurfaceControl.AssertContentDiffers(
            RenderMarkup(resolved, Ctx, values, topLevel.Colors),
            RenderMarkup(blankResolved, BlankCtx, blankValues, topLevel.Colors));
    }

    [Fact]
    public void Columns112_LiveConfig_PackerInvocationCountStaysBounded()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        SizeResolver.MinRowsPackerInvocationCount = 0;
        SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var count = SizeResolver.MinRowsPackerInvocationCount;

        _output.WriteLine($"min-rows packer invocations for the live two-pane config at COLUMNS=112: {count}");

        // §2.3.1's own worked example, for this exact config shape: "tens of packer calls".
        // Re-derived after the maxT fix (Splice 2): maxT is now computed via n extra RowCountAt
        // calls (one per candidate, at its own floor) on top of the existing T-scan/binary-search
        // cost, and this config's real maxT stays modest since neither candidate is forced to wrap
        // at COLUMNS=112 — measured at 16 calls. 100 keeps a wide margin above that without merely
        // re-pinning to the observed number.
        Assert.InRange(count, 1, 100);
    }

    // SPEC-2.3.1-min-rows-seam-and-bound.md §1: with the old segment-count bound, both candidates
    // here (one item each) capped the T-scan at maxT = 1 — and a single 60-character item cannot
    // fit either candidate in one row at this width, so T = 1 is infeasible and the old code fell
    // straight to "every candidate at its own floor" without ever searching T = 2 or higher. The
    // brute force below asserts the true optimum, not merely "some legal allocation", so a stale
    // low maxT reappears here as a failing assertion rather than as a silently suboptimal render —
    // this is also §2.3.1's acceptance condition 1 re-run against wrapping content (verification
    // item 3), since the shared two-pane fixture above never forces a wrap.
    private const string LongItemConfigJson = """
    {
      "surface": {
        "maxRows": 20,
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "distribute": "min-rows",
          "children": [
            { "size": "fill", "overflow": "wrap", "items": [ { "item": "model" } ] },
            { "size": "fill", "overflow": "wrap", "items": [ { "item": "model" } ] }
          ]
        }
      }
    }
    """;

    private static readonly StatusInput LongItemInput = new()
    {
        Model = new ModelInfo { DisplayName = new string('x', 60) },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext LongItemCtx = new(LongItemInput, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadLongItemConfig()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, LongItemConfigJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NarrowWidth_MinRows_MatchesBruteForceOptimalRowCount_WithWrappingContent()
    {
        var (topLevel, pane) = LoadLongItemConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("90", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, LongItemCtx, topLevel.Colors);

        var left = pane.Children[0];
        var right = pane.Children[1];
        var r = surfaceWidth - pane.Gutter;

        // Both candidates here are `fill` (unlike the shared two-pane fixture above, where the
        // right child is `content` and floors at 0) — each floors at RowLayout.MinUsableWidth, so
        // the sweep's own lower and upper bounds must respect BOTH floors (w2 = r - w1 must never
        // go below right's floor either), not just left's, or the "brute force" would be scoring
        // widths the real allocator could never legally grant.
        var lo1 = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(left);
        var lo2 = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(right);
        var hi1 = r - lo2;

        var bestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, values, topLevel.Colors, LongItemCtx);
            var rows2 = RowsAt(right, w2, values, topLevel.Colors, LongItemCtx);
            bestScore = Math.Min(bestScore, Math.Max(rows1, rows2));
        }

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, LongItemCtx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        Assert.Equal(2, resolved.Children.Count);
        var achievedRows1 = RowsAt(left, resolved.Children[0].OuterWidth, values, topLevel.Colors, LongItemCtx);
        var achievedRows2 = RowsAt(right, resolved.Children[1].OuterWidth, values, topLevel.Colors, LongItemCtx);
        var achievedScore = Math.Max(achievedRows1, achievedRows2);

        Assert.True(achievedRows1 > 1 || achievedRows2 > 1, "fixture must wrap to exercise the maxT fix");
        Assert.Equal(bestScore, achievedScore);
    }

    // §2.3.3:1220-1222: the dropped-pane note is the *stated observable consequence* of min-rows'
    // own over-constrained fallback — greedy already emits it (§9.8.2); min-rows silently dropped
    // panes with no note at all until this fix threaded a RenderNoteCollector into
    // ResolveVerticalMinRows. SPEC-95-flex-side-by-side-wrapped.md §5.1/§5.1.1: the trailing
    // content candidate no longer bottoms out at width 0 — it declares no maxSize, so it gets the
    // broadened SearchFloor (RowLayout.MinUsableWidth + OwnBorderReserve = 20 + 4 = 24 here) and
    // is sized via SolveMinRows's feasible path at that floor, not the old degenerate `lo=0`
    // fallback. That changes pane 3's own drop-note total (500+500+24=1024, not 1000) but nothing
    // about which panes drop or survive: the two fixed panes are themselves unclamped
    // (AllocateMinRowsOnePass:555-562 mirrors AllocateOnePass's own unclamped fixed loop) and
    // together exceed the surface on their own (1000 > avail), so #67's Σgrants ≤ avail guard —
    // SPEC-2.3.1-min-rows-floor-sum.md §2/§4, "catches §2's fixed-pane overrun on the min-rows
    // side for free" — drops pane 3 first (its own pass is over-allocated at 1024), then pane 2
    // (the remaining two fixed panes alone still sum to 1000 > avail), leaving pane 1 alone, over
    // budget at 500 against an avail of 57, clamped rather than dropped (§4's residual
    // `current.Count <= 1` case). One survivor, exactly as before SPEC-95 — SPEC-95 changes the
    // reported total on the first drop, not the cascade's shape or its outcome.
    [Fact]
    public void OverConstrained_MinRows_EmitsDroppedPaneNote()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "min-rows",
              "children": [
                { "size": "500" },
                { "size": "500" },
                { "size": "content", "overflow": "wrap", "items": [ { "item": "model" } ] }
              ]
            }
          }
        }
        """;

        var path = Path.GetTempFileName();
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            File.WriteAllText(path, configJson);
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(1, resolved.Children.Count);
        Assert.Contains(notes.Notes, n => n.Message == $"pane 3 dropped: children need 1024 columns at {surfaceWidth} columns");
        Assert.Contains(notes.Notes, n => n.Message == $"pane 2 dropped: children need 1000 columns at {surfaceWidth} columns");
    }

    // SPEC-2.3.1-min-rows-seam-and-bound.md §4a: the seam previously reached zero call sites in
    // the min-rows path — a green stub-based test whose config happened to use
    // distribute: min-rows would have compared nothing. This is the test that would have caught
    // the false doc-comment claim.
    [Fact]
    public void MeasureOverride_MinRows_FiresForContentCandidateSurplusCap()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        var fired = false;
        int MeasureOverride(Pane p, int? w)
        {
            fired = true;
            return 5;
        }

        SizeResolver.Resolve(pane, surfaceWidth, Ctx, values, new Dictionary<string, Segment>(), MeasureOverride, new RenderNoteCollector());

        Assert.True(fired, "measureOverride must fire for the min-rows content candidate's surplus cap");
    }

    // SPEC-2.3.1-min-rows-seam-and-bound.md §4b, verification item 6: mirrors §10 req 6(b)'s
    // "deliberately misbehaving stub" precedent, applied to min-rows' own load-bearing assumption
    // (§2.3.1: rows_i(w) is non-increasing in w). A stub that violates it must not hang the binary
    // search and must still produce a legal allocation — defensive, not a claim that min-rows
    // corrects a caller's non-monotone rows_i.
    [Fact]
    public void RowCountOverride_NonMonotoneRows_DoesNotHangAndProducesLegalAllocation()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        int RowCountOverride(Pane p, int w) => w < 50 ? 1 : 5; // more rows at greater width

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values, new Dictionary<string, Segment>(), measureOverride: null, rowCountOverride: RowCountOverride, notes: new RenderNoteCollector());

        // The override reports fewer rows only below width 50 (the value maxT is derived from,
        // via each candidate's own floor) and more rows at or above it (the value the hi-width
        // feasibility check sees) — every T up to maxT is therefore infeasible by construction,
        // so the over-constrained fallback (and, for the content candidate whose floor is 0, the
        // drop-retry loop) legitimately engages. "Does not hang" and "produces a legal
        // allocation" does not mean "preserves every child" under a deliberately adversarial
        // override — §10 req 6(b)'s own precedent only requires termination, not preservation.
        Assert.InRange(resolved.Children.Count, 1, 2);
        Assert.All(resolved.Children, c => Assert.True(c.OuterWidth >= 0));
    }

    // SPEC-2.3.1-min-rows-floor-sum.md §7 item 1: impl3's exact repro — two `fill` panes whose
    // floors alone (24 each) exceed r (46) before any content is even considered, so no `T` can
    // ever be feasible and SolveMinRows's `return lo` fallback used to hand back 24+24=48 against a
    // 46-column budget with nothing reported. ResolveVerticalMinRows's drop-retry loop now catches
    // this the same way AllocateWithDrop's own `grants[i] < 1` test catches greedy's
    // under-allocation — asserting only the sum would pass even if the drop happened silently,
    // which is the regression #25 §5 already fixed once.
    [Fact]
    public void FloorSumExceedsBudget_MinRows_ClampsToAvailAndEmitsDropNote()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "min-rows",
              "children": [
                { "size": "fill" },
                { "size": "fill" }
              ]
            }
          }
        }
        """;

        var path = Path.GetTempFileName();
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            File.WriteAllText(path, configJson);
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var surfaceWidth = SurfaceLayout.ComputeWidth("50", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(1, resolved.Children.Count);
        Assert.True(resolved.Children.Sum(c => c.OuterWidth) <= surfaceWidth);
        Assert.Contains(notes.Notes, n => n.Message == $"pane 2 dropped: children need 48 columns at {surfaceWidth} columns");
    }

    // §7 item 2, superseded again by SPEC-2.3-suppression-predicate.md (#73): #67b's drop
    // predicate tests grants against floor(p), and until #73, ShouldSuppressBorder tested outer
    // width against MinUsableWidth — a mismatch that made suppression fire only where it could not
    // save a pane and stay silent exactly where it could (see that spec's §2). #73 makes the
    // predicate test the pane's own pre-suppression INNER width instead, so this test's own grant
    // (23, unchanged) now lands inside the suppression band it always should have. Each fill pane's
    // unsuppressed floor is 24: MinUsableWidth (20, RowLayout.cs:19) plus a default-bordered leaf's
    // 4-column OwnBorderReserve (no "border" key -> BoxBorder.Rounded, all four edges true,
    // Config.cs ResolveBorder). Grant is 23 (46 avail / 2 fill panes, gutter:1 costs the split's
    // one boundary column out of surfaceWidth 47). Pre-suppression inner width is 23 - 4 = 19,
    // under MinUsableWidth, so suppression now fires and the floor drops to 20; 23 >= 20 survives.
    // Both panes survive, borderless, at their full grant.
    [Fact]
    public void FloorSumExceedsBudget_Greedy_SurvivesBorderlessViaSuppression()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "children": [
                { "size": "fill" },
                { "size": "fill" }
              ]
            }
          }
        }
        """;

        var path = Path.GetTempFileName();
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            File.WriteAllText(path, configJson);
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var surfaceWidth = SurfaceLayout.ComputeWidth("50", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(23, resolved.Children[0].OuterWidth);
        Assert.Equal(23, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);

        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);
        Assert.Equal(4, reserve);
        Assert.True(SizeResolver.ShouldSuppressBorder(fillPane, 23 - reserve));
    }

    // §7 item 3: three candidates whose floors cannot fit in any combination — the loop must drop
    // twice, in order, and terminate at one pane rather than looping or under-dropping.
    [Fact]
    public void ThreeCandidates_MinRows_DropsToOneWithBothNotes()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "min-rows",
              "children": [
                { "size": "fill" },
                { "size": "fill" },
                { "size": "fill" }
              ]
            }
          }
        }
        """;

        var path = Path.GetTempFileName();
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            File.WriteAllText(path, configJson);
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var surfaceWidth = SurfaceLayout.ComputeWidth("50", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(1, resolved.Children.Count);
        Assert.Contains(notes.Notes, n => n.Message == $"pane 3 dropped: children need 72 columns at {surfaceWidth} columns");
        Assert.Contains(notes.Notes, n => n.Message == $"pane 2 dropped: children need 48 columns at {surfaceWidth} columns");
    }

    // §7 item 4: a config where a feasible `T` exists must be byte-identical to pre-#67 behavior
    // (25fa255) — the new invariant check must be unreachable whenever SolveMinRows's own
    // `sum <= r` check already succeeded, since water-fill already caps at `r`. If this test's
    // pinned widths ever change, the check moved to the wrong place.
    [Fact]
    public void FeasiblePath_MinRows_UnaffectedByOverAllocationGuard()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(54, resolved.Children[0].OuterWidth);
        Assert.Equal(54, resolved.Children[1].OuterWidth);

        // SPEC §10.1 blank-surface control: min-rows sizing is content-driven (rows_i(w) depends on
        // item text), so unlike this test's own pinned 54/54, only re-run to confirm the feasible
        // path is still reached (children count + the over-allocation guard's invariant, sum <=
        // surfaceWidth) rather than re-pinning exact widths that this content's absence has no
        // reason to reproduce.
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        Assert.Equal(2, blankResolved.Children.Count);
        Assert.True(blankResolved.Children.Sum(c => c.OuterWidth) <= surfaceWidth);
        BlankSurfaceControl.AssertContentDiffers(
            RenderMarkup(resolved, Ctx, values, topLevel.Colors),
            RenderMarkup(blankResolved, BlankCtx, blankValues, topLevel.Colors));
    }

    // §7 item 5: with the fix recomputing `avail` from `current.Count` on every iteration, dropping
    // the third pane releases one gutter column, which is exactly enough for the remaining two
    // floors (24 + 24 = 48) to fit the released budget (48) — this is the test that fails if
    // `avail` is hoisted out of the loop, since a stale n=3 `avail` would wrongly force a second
    // drop here.
    [Fact]
    public void BoundaryCostRecomputedPerIteration_MinRows_FitsAfterOneDropNotTwo()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "min-rows",
              "children": [
                { "size": "fill" },
                { "size": "fill" },
                { "size": "fill" }
              ]
            }
          }
        }
        """;

        var path = Path.GetTempFileName();
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            File.WriteAllText(path, configJson);
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var surfaceWidth = SurfaceLayout.ComputeWidth("52", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.Contains(notes.Notes, n => n.Message == $"pane 3 dropped: children need 72 columns at {surfaceWidth} columns");
        Assert.DoesNotContain(notes.Notes, n => n.Message.StartsWith("pane 2 dropped", StringComparison.Ordinal));
    }
}
