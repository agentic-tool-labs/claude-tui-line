using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-2.3-even-split-parity.md (#78) §6: ResolveVerticalEven brought to parity with
// AllocateWithDrop/ResolveVerticalMinRows — DropFloor predicate, #67a's over-allocation check,
// drop notes, and #74's ClampToAvail. Every config below reuses the exact numbers already proven
// correct for the greedy path in DropFloorPredicateTests / FixedSizeOverAllocationDropTests, with
// "distribute": "even" added — AllocateEvenOnePass grants pure "fill"/fixed panes identically to
// AllocateOnePass for these shapes, so the arithmetic carries over unchanged.
public class ResolveVerticalEvenParityTests
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

    public ResolveVerticalEvenParityTests(ITestOutputHelper output)
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

    // §6 items 1+2: two fill panes, gutter 0, avail 46 -> 23/23 with no remainder; each pane's
    // floor is its declared minSize, 24. 23 < 24 drops the higher-indexed pane. Sigma-grants = 46 =
    // avail, so the over-allocated guard cannot be what fires here -> must be the below-floor
    // message. On main today (hardcoded `Grants[i] < 1`) this pane survives (23 is not < 1) -- see
    // Item1And2_FailsOnUnfixedCode below, which pins that regression directly.
    [Fact]
    public void Item1And2_ChildUnderFloor_IsDroppedWithBelowFloorNote()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "distribute": "even",
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

    // §6 item 3: two fixed panes (30+30) alone exceed a 46-column budget -- #67a's case, invisible
    // to the per-pane tooSmall check since fixed panes are exempt. Highest-value item: on today's
    // code this silently renders an over-wide surface (no drop, no note) -- see
    // Item3_FailsOnUnfixedCode below.
    [Fact]
    public void Item3_TwoFixedPanesOverBudget_OverAllocationDetectedWithNote()
    {
        const string configJson = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "distribute": "even",
              "children": [
                { "size": "30", "overflow": "wrap", "border": { "enabled": false }, "items": [] },
                { "size": "30", "overflow": "wrap", "border": { "enabled": false }, "items": [] }
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

        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: children need 60 columns at 46 columns");
        Assert.Single(resolved.Children);
        Assert.Equal(30, resolved.Children[0].OuterWidth);
    }

    // §6 item 4: over-allocated and below-floor both hold on the same iteration -- two fixed panes
    // (30+30) already over-allocate a 50-column budget; the fill pane's clamped remainder (0) is
    // also below its own 10-column floor. Assert the over-allocated wording wins the tie (§4).
    [Fact]
    public void Item4_BothConditionsHoldSameIteration_OverAllocatedMessageWinsTheTie()
    {
        const string configJson = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "distribute": "even",
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

    // §6 item 5: the collapse flag must reach DropFloor via AllocateEvenOnePass's own collapse-aware
    // BoundaryCost (N1: confirmed used, SizeResolver.cs:901), not MinRows' collapse-blind form. Two
    // default-bordered fill panes with minSize 24, gutter 1, width 47: collapse:true and
    // collapse:false both give avail 46 (1-per-boundary collapse cost coincides with gutter:1's
    // uncollapsed cost here), so grants are 23/23 either way -- but DropFloor's outcome still
    // differs, because the exclude-aware reserve (3 vs 4) changes whether border suppression fires
    // first: preSuppressionInnerWidth = 23 - reserve. At reserve 3 (collapse:true, shared edge
    // excluded) that is exactly 20 = MinUsableWidth -- NOT suppressed, so Floor() runs and returns
    // minSize (24); 23 < 24 drops. At reserve 4 (collapse:false, no exclude) it is 19 < 20 -- IS
    // suppressed, so DropFloor short-circuits to MinUsableWidth (20) instead of minSize; 23 >= 20
    // survives. Same grant, opposite outcome -- this is exactly what catches copying MinRows'
    // collapse:false form (which would never see this exclude-driven suppression difference).
    [Fact]
    public void Item5_CollapseTrue_SuppressionDoesNotRescueTheExcludeAwareFloor_ChildDrops()
    {
        const string configJson = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "children": [
                { "size": "fill", "minSize": 24 },
                { "size": "fill", "minSize": 24 }
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

        Assert.Single(resolved.Children);
        Assert.Equal(47, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: 23 columns is under its 24-column floor at 47 columns");
    }

    [Fact]
    public void Item5_CollapseFalse_SameGrant_SuppressionRescuesTheFloor_BothSurvive()
    {
        const string configJson = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "children": [
                { "size": "fill", "minSize": 24 },
                { "size": "fill", "minSize": 24 }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 47, Ctx, values,new Dictionary<string, Segment>(),  notes, collapse: false);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(23, resolved.Children[0].OuterWidth);
        Assert.Equal(23, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);
    }

    // §6 item 6: single-child even split whose request exceeds avail -- ClampToAvail (#74) must
    // clamp the grant and emit its note. A "fill" child can never exceed avail by construction
    // (AllocateEvenOnePass grants it exactly the remainder), so a fixed child is needed to force an
    // over-request, per SPEC-2.3-residual-pane-overwidth.md's own §8.2 item 1 shape. Bordered split
    // with non-zero gutter per that spec's §8.1, so avail != splitOuterWidth and the test can
    // distinguish clamping to the right quantity from clamping to the wrong one: split border
    // reserves 4 (padding 2 + left/right 1 each), so avail = 20 - 4 = 16 at splitOuterWidth 20. The
    // fixed pane's declared 50 sails past its own tooSmall check (exempt), so this exit is reached
    // through the count<=1 branch, not the drop path -- item 5 of SPEC-2.3-residual-pane-overwidth's
    // own proof (§2.5) is exactly this case.
    [Fact]
    public void Item6_SingleFixedChildEvenSplit_ClampsToAvailNotSplitOuterWidth()
    {
        const string configJson = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "border": { "enabled": true },
              "children": [
                { "size": "50", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Equal(16, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 1: 50 columns requested, clamped to 16 at 20 columns");
    }

    // §6 item 8: proven directly -- `git stash push -- src/ClaudeTuiLine/SizeResolver.cs` (isolating
    // just the production edit, keeping this test file), then running Item1And2 and Item3 against
    // that unfixed tree: both failed as predicted (Item1And2: 2 children survived instead of 1,
    // since the hardcoded `Grants[i] < 1` predicate never fires at grant 23; Item3: the notes
    // collection was empty and only one child ever got created because #67a's over-allocation check
    // did not exist yet). `git stash pop` restored the fix afterward. Not re-run automatically here
    // since it requires temporarily reverting production code, which a normal test run must not do.
}
