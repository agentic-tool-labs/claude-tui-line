using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-2.3-drop-predicate.md (#67b): both drop-retry loops (AllocateWithDrop, ResolveVerticalMinRows)
// now test each pane's grant against its drop floor (DropFloor in SizeResolver.cs) rather than the
// old `grants[i] < 1`. This file holds the spec's own verification items that are not already
// covered by an existing test file. Every expected numeric value below is derived from the spec's
// own arithmetic (floor(p), MinUsableWidth=20, RowLayout.cs:19, and a default-bordered leaf's
// 4-column OwnBorderReserve), not observed by running this change's implementation.
public class DropFloorPredicateTests
{
    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private readonly ITestOutputHelper _output;

    public DropFloorPredicateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig(string configJson)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, configJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Item 1 (§6): impl3's repro under greedy, expectations derived rather than observed.
    // avail = 46 (gutter: 0, unbordered split), two fill panes each get 46 / 2 = 23 with no
    // remainder. Each pane's floor is its declared minSize, 24. 23 < 24 drops the higher-indexed
    // pane; Sigma-grants = 46 = avail, so #70's over-allocated guard cannot be what fires here — this
    // must be the below-floor message. After the drop the sole survivor is re-granted the whole
    // 46-column budget.
    //
    // The spec's own JSON literally says "split": "horizontal", but this codebase's "horizontal"
    // stacks children at full width and never divides it (SizeResolver.cs:4-11) — "vertical" is the
    // split kind that divides width among children, which is what every number in this derivation,
    // and every sibling test in this codebase, assumes. Using "vertical" here; flagged to the peer
    // as a spec/terminology mismatch rather than silently worked around.
    [Fact]
    public void Item1_GreedyRepro_DropsHigherIndexedPaneBelowFloor()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill", "minSize": 24, "border": { "enabled": false } },
                { "size": "fill", "minSize": 24, "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 46, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Equal(46, resolved.Children[0].OuterWidth);
        Assert.Single(notes.Notes);
        Assert.Equal("pane 2 dropped: 23 columns is under its 24-column floor at 46 columns", notes.Notes[0].Message);
    }

    // Item 2 (§6, min-rows path): SolveMinRows's infeasible fallback ("no T fits") grants each
    // surviving candidate exactly its own floor, never less — grant < Floor is structurally
    // unreachable there, so a below-floor negative test (item 1's shape) cannot exist for min-rows.
    // Architect ruling: assert the positive invariant instead. Three "fill" panes, gutter 1,
    // distribute: min-rows, at surface width 52: floor-sum (3 x 24 = 72) exceeds the 51-column
    // avail (52 - 1 gutter x 2 boundaries), so the drop-retry loop fires once; releasing one
    // boundary column on drop leaves avail 48, which the remaining two candidates' floors (24 + 24)
    // consume exactly, with no surplus to distribute. Both survivors are granted precisely their
    // floor — the tightest case the invariant can be checked against, since one column less on
    // either would fail it. Floor is computed via SizeResolver.OwnBorderReserve (a pure formula
    // function, not the fallback path under test) rather than pinned to an observed grant value.
    [Fact]
    public void Item2_MinRowsInfeasibleFallback_SurvivingCandidatesAllMeetOwnFloor()
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

        var (topLevel, pane) = LoadConfig(configJson);
        var surfaceWidth = SurfaceLayout.ComputeWidth("52", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(2, resolved.Children.Count);

        var floor = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(pane.Children[0]);
        Assert.All(resolved.Children, c => Assert.True(c.OuterWidth >= floor,
            $"grant {c.OuterWidth} is below floor {floor}"));
        Assert.Equal(floor, resolved.Children[0].OuterWidth);
        Assert.Equal(floor, resolved.Children[1].OuterWidth);
    }

    // Item 3 (§6): a content pane granted 0 still drops. Fails if the predicate is
    // `grants[i] < Floor(...)` without `Math.Max(1, ...)` — Floor is 0 for SizeKind.Content
    // (SizeResolver.cs:354), so a bare comparison is `0 < 0`, never true, and this pane would
    // silently survive at width 0. A fixed pane consumes the entire 46-column budget, leaving the
    // content pane's clamped request at 0; 0 < Math.Max(1, 0) = 1 must still catch it.
    [Fact]
    public void Item3_ContentPaneGrantedZero_StillDrops()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "46", "border": { "enabled": false } },
                { "size": "content", "overflow": "wrap", "border": { "enabled": false },
                  "items": [ { "item": "model" } ] }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 46, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Equal(46, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: 0 columns is under its 1-column floor at 46 columns");
    }

    // Item 5 (§6, §3(c)): collapsed split, edge children. Under collapse:true a 2-child split's
    // children each exclude the shared interior edge from their own border reserve (Config.cs
    // Floor: excludeLeft/excludeRight), so a default-bordered leaf's reserve there is 3, not the
    // uncollapsed 4 — floor 23, not 24. splitOuterWidth 47 minus one collapsed boundary column
    // (BoundaryCost under collapse:true costs 1 per interior boundary, not gutter x boundaries)
    // leaves avail 46, split 23/23 with no remainder: both panes are granted exactly their correct
    // (4-arg, exclude-aware) floor and must survive. A drop test that used the 1-arg Floor overload
    // instead (collapse:false, no excludes) would compute a uniform floor of 24 for both — 23 < 24 —
    // and wrongly drop both down to one. Calls the 6-arg Resolve overload directly with
    // collapse:true so the config need not thread `surface.border.collapse` through the loader.
    [Fact]
    public void Item5_CollapsedSplitEdgeChildren_SurviveAtTheirExcludeAwareFloor()
    {
        const string configJson = """
        {
          "surface": {
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

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 47, Ctx, values,new Dictionary<string, Segment>(),  notes, collapse: true);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(23, resolved.Children[0].OuterWidth);
        Assert.Equal(23, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);
    }

    // Item 6 (§6, §3(d)): a contradictory `{"size": 20, "minSize": 30}` fixed pane renders at its
    // declared 20 with no drop and no note, pinning the `!= SizeKind.Fixed` exemption. Paired with
    // a fill pane so the exemption is load-bearing: without it, the fixed pane's own grant (20)
    // would be tested against its declared floor (30) and wrongly dropped.
    [Fact]
    public void Item6_ContradictoryFixedPane_NotDroppedDespiteMinSizeAboveDeclaredSize()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "20", "minSize": 30, "border": { "enabled": false } },
                { "size": "fill", "minSize": 10, "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 40, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(20, resolved.Children[0].OuterWidth);
        Assert.Equal(20, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);
    }

    // Item 7 (§6): a comfortable config where every grant clears its floor. The new predicate must
    // be inert here — no drop, no note — the same outcome as before #67b.
    [Fact]
    public void Item7_ComfortableConfig_NoDropNoNote()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill", "border": { "enabled": false } },
                { "size": "fill", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 200, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(100, resolved.Children[0].OuterWidth);
        Assert.Equal(100, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);
    }

    // Item 8 (§6): the "both at once" sub-case — over-allocated and below-floor hold on the same
    // iteration, and over-allocated must win the tie (§4). Two fixed panes (30 + 30) already
    // over-allocate a 50-column budget on their own; the fill pane's clamped remainder (0, since
    // rem went negative) is also below its own 10-column floor. Both conditions hold on the first
    // iteration; assert the over-allocated message, not a floor-worded one.
    [Fact]
    public void Item8_BothConditionsHoldSameIteration_OverAllocatedMessageWins()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "30", "border": { "enabled": false } },
                { "size": "30", "border": { "enabled": false } },
                { "size": "fill", "minSize": 10, "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 50, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Contains(notes.Notes, n => n.Message == "pane 3 dropped: children need 60 columns at 50 columns");
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("column floor"));
    }
}
