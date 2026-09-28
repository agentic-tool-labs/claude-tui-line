using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-88-responsive-split-fallback.md §6: V1-V13. Builds <see cref="Pane"/> trees directly
/// (the <see cref="SizeResolverFixpointTests"/> convention) for the resolver-level tests, and
/// <see cref="UserConfig"/>/<see cref="PaneConfig"/> objects (the <see cref="BorderEdgesTests"/>
/// convention) for the <c>--check</c>/<c>--accepted</c>/<c>--schema</c> tests.
/// </summary>
public class SplitFlexTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
    private static readonly PaneBorder Bordered = new(new ColorResolution.ColorExpr.Literal("grey"), BoxBorder.Rounded, PaneBorderEdges.All);
    private static readonly StatusInput Input = new();
    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> EmptyColors = new Dictionary<string, ColorResolution.ColorRule>();

    private static Pane Leaf(string size = "fill", int? minSize = null, PaneBorder? border = null, int? maxSize = null) =>
        new(PaneSplit.None, Array.Empty<Pane>(), size, border ?? NoBorder, null, "…", null, Array.Empty<PaneItem>(), MinSize: minSize, MaxSize: maxSize);

    private static Pane FlexSplit(IReadOnlyList<Pane> children, int gutter = 0, PaneDistribute distribute = PaneDistribute.Greedy, PaneBorder? border = null) =>
        new(PaneSplit.Flex, children, "fill", border ?? NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: gutter, Distribute: distribute);

    private static Pane VerticalSplit(IReadOnlyList<Pane> children, int gutter = 0, PaneBorder? border = null, string size = "fill") =>
        new(PaneSplit.Vertical, children, size, border ?? NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: gutter);

    private static Pane HorizontalSplit(IReadOnlyList<Pane> children, PaneBorder? border = null, string size = "fill") =>
        new(PaneSplit.Horizontal, children, size, border ?? NoBorder, null, "…", null, Array.Empty<PaneItem>());

    private static string RenderMarkup(SizeResolver.ResolvedPane resolved, IReadOnlyDictionary<string, string?> values) =>
        string.Join('\n', PaneTreeRenderer.Render(resolved, Ctx, values, EmptyColors, new Dictionary<string, Segment>(), new RenderNoteCollector()).Buffer.Rows.Select(r => r.Markup));

    // ---- V1: flex stacks when side by side does not fit but stacked does ----

    [Fact]
    public void V1_FlexStacks_WhenSideBySideDoesNotFitButStackedDoes()
    {
        var root = FlexSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        // sideBySideFloor = 30 + 30 + 1 = 61; stackedFloor = max(30, 30) = 30. 40 sits between them.
        var resolved = SizeResolver.Resolve(root, 40, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.Equal(2, resolved.Children.Count);
        Assert.All(resolved.Children, c => Assert.Equal(40, c.OuterWidth));
        Assert.Single(notes.Notes);
        Assert.Equal("pane 2: flex split stacked; children need 61 columns at 40 columns", notes.Notes[0].Message);
    }

    // ---- V2: flex does not stack when side by side already fits; matches declared-vertical ----

    [Fact]
    public void V2_FlexDoesNotStack_WhenSideBySideFits_MatchesDeclaredVertical()
    {
        var flexRoot = FlexSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);
        var verticalRoot = VerticalSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);

        var flexValues = ItemValueResolver.Resolve(flexRoot, Ctx, EmptyColors);
        var flexNotes = new RenderNoteCollector();
        var flexResolved = SizeResolver.Resolve(flexRoot, 70, Ctx, flexValues, new Dictionary<string, Segment>(), flexNotes);

        var verticalValues = ItemValueResolver.Resolve(verticalRoot, Ctx, EmptyColors);
        var verticalNotes = new RenderNoteCollector();
        var verticalResolved = SizeResolver.Resolve(verticalRoot, 70, Ctx, verticalValues, new Dictionary<string, Segment>(), verticalNotes);

        Assert.Equal(PaneSplit.Vertical, flexResolved.EffectiveSplit);
        Assert.Empty(flexNotes.Notes);
        Assert.Equal(verticalResolved.Children.Select(c => c.OuterWidth), flexResolved.Children.Select(c => c.OuterWidth));
        Assert.Equal(RenderMarkup(verticalResolved, verticalValues), RenderMarkup(flexResolved, flexValues));
    }

    // ---- V3: declared-vertical backward compatibility at the width regime V1 exercises for flex.
    // Pins the exact pre-existing drop outcome (one child dropped, the standard below-floor note) so
    // a declared-vertical pane with no Flex anywhere in its tree is provably unaffected by the
    // ResolveNode/Floor refactor, at the same width where a Flex sibling would newly stack. ----

    [Fact]
    public void V3_DeclaredVertical_UnaffectedByFlexRefactor_AtTheV1StackingWidth()
    {
        var root = VerticalSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 40, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(PaneSplit.Vertical, resolved.EffectiveSplit);
        Assert.Single(resolved.Children);
        Assert.Equal(40, resolved.Children[0].OuterWidth);
        Assert.Single(notes.Notes);
        Assert.Equal("pane 2 dropped: 19 columns is under its 30-column floor at 40 columns", notes.Notes[0].Message);
    }

    // ---- V4: the stack note fires exactly once, only on a real stack ----

    [Fact]
    public void V4_StackNote_FiresExactlyOnce_OnlyOnARealStack()
    {
        Pane BuildRoot() => FlexSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);

        (int Width, int ExpectedNoteCount)[] cases =
        {
            (20, 0), // over-constrained in both arrangements: falls back to side by side, no note.
            (40, 1), // stacks: exactly one note.
            (70, 0), // side by side fits: no note.
        };

        foreach (var (width, expectedCount) in cases)
        {
            var root = BuildRoot();
            var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
            var notes = new RenderNoteCollector();

            SizeResolver.Resolve(root, width, Ctx, values, new Dictionary<string, Segment>(), notes);

            Assert.Equal(expectedCount, notes.Notes.Count(n => n.Message.Contains("flex split stacked")));
        }
    }

    // ---- V5: distribute:"min-rows" also stacks — the Flex dispatch sits above both vertical entry
    // points (§4.1), so a min-rows flex pane stacks exactly like a greedy one. ----

    [Fact]
    public void V5_MinRowsDistribute_AlsoStacks()
    {
        var root = FlexSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1, distribute: PaneDistribute.MinRows);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 40, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.Single(notes.Notes);
        Assert.Contains("flex split stacked", notes.Notes[0].Message);
    }

    // ---- V6: --check diagnostics ----

    [Fact]
    public void V6a_FlexSplit_NoUnknownEnumValueDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    Children = new List<PaneConfig> { new(), new() },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void V6b_GutterAndDistributeOnFlexSplit_NoKeyNotApplicableDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    Gutter = 1,
                    Distribute = "min-rows",
                    Children = new List<PaneConfig> { new(), new() },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && (d.Path == "/surface/pane/gutter" || d.Path == "/surface/pane/distribute"));
    }

    [Fact]
    public void V6d_ChildlessFlex_NormalizesToLeaf_WithTheSameDiagnosticAsChildlessVertical()
    {
        var flexConfig = new UserConfig { Surface = new SurfaceConfig { Pane = new PaneConfig { Split = "flex" } } };
        var verticalConfig = new UserConfig { Surface = new SurfaceConfig { Pane = new PaneConfig { Split = "vertical" } } };
        var topLevel = ConfigLoader.ResolveTopLevel(null);

        var flexPane = ConfigLoader.ResolveRootPane(flexConfig, topLevel);
        Assert.Equal(PaneSplit.None, flexPane.Split);

        var flexDiagnostics = ConfigChecker.Check(flexConfig);
        var verticalDiagnostics = ConfigChecker.Check(verticalConfig);

        var flexSplitDiagnostic = Assert.Single(flexDiagnostics, d => d.Path == "/surface/pane/split");
        var verticalSplitDiagnostic = Assert.Single(verticalDiagnostics, d => d.Path == "/surface/pane/split");
        Assert.Equal(verticalSplitDiagnostic with { }, flexSplitDiagnostic with { });
    }

    // ---- V7: --accepted --json registry coherence ----

    [Fact]
    public void V7_AcceptedCommand_SplitRowListsFlex_AsTheSharedRegistryObject()
    {
        var result = AcceptedCommand.Build();
        var splitRow = Assert.Single(result.Keys, k => k.Key == "split");

        Assert.Same(ConfigLoader.SplitAcceptedTokens, splitRow.Accepted);
        Assert.Contains("flex", splitRow.Accepted!);
    }

    // ---- V8: --schema --json describes flex; a bordered stacking flex pane renders sane borders ----

    [Fact]
    public void V8a_SchemaCommand_SplitFieldDescribesFlex()
    {
        var schema = SchemaCommand.Build();
        var splitField = schema.Structures
            .SelectMany(s => s.Fields)
            .Single(f => f.Name == "split");

        Assert.Contains("flex", splitField.Description);
    }

    [Fact]
    public void V8b_BorderedFlexPane_ThatStacks_RendersSaneBorders()
    {
        var root = FlexSplit(
            new[] { Leaf(minSize: 30, border: Bordered), Leaf(minSize: 30, border: Bordered) },
            gutter: 1, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 40, Ctx, values, new Dictionary<string, Segment>(), notes);
        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, EmptyColors, new Dictionary<string, Segment>(), new RenderNoteCollector());

        // §4.5.1 — if this is visually broken (not merely imperfect) the Reviewer/Architect must
        // see it directly rather than through a false-passing assertion; report rather than fix.
        // Nested parent/child borders legitimately produce rows of differing total character count
        // (a plain parent-border row vs. a nested child-corner row), so this checks for the presence
        // of both border layers and the absence of an exception/collapse, not a uniform row width.
        Assert.NotEmpty(rendered.Buffer.Rows);
        var stripped = rendered.Buffer.Rows.Select(r => DisplayWidth.Strip(r.Markup)).ToList();
        Assert.Contains(stripped, s => s.Contains('│')); // parent border
        Assert.Contains(stripped, s => s.Contains('╭') || s.Contains('┌')); // a child's own border
    }

    // ---- V9: the effective orientation is never Flex; no class-B consumer ever observes Flex ----

    [Fact]
    public void V9_EffectiveSplit_IsNeverFlex_AtSeveralWidths()
    {
        int[] widths = { 10, 20, 40, 61, 70, 200 };

        foreach (var width in widths)
        {
            var root = FlexSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);
            var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
            var notes = new RenderNoteCollector();

            var resolved = SizeResolver.Resolve(root, width, Ctx, values, new Dictionary<string, Segment>(), notes);

            Assert.NotEqual(PaneSplit.Flex, resolved.EffectiveSplit);
            Assert.All(resolved.Children, c => Assert.NotEqual(PaneSplit.Flex, c.EffectiveSplit));

            // Class-B consumers: PaneTreeRenderer and BorderGrid both read EffectiveSplit — this
            // exercises both without throwing, which a stray Split==Flex read would not guarantee.
            // Borderless, itemless leaves legitimately render zero content rows, so completing
            // without an exception (rather than a row count) is the assertion here.
            PaneTreeRenderer.Render(resolved, Ctx, values, EmptyColors, new Dictionary<string, Segment>(), new RenderNoteCollector());
        }
    }

    // ---- V10: Floor() under sibling competition — an integration test on rendered output/notes
    // across the whole window where the buggy (widened-Horizontal) and correct (min) parent floor
    // for the flex child disagree. Shape and window per SPEC-88-responsive-split-fallback.md §6.1:
    // parent: vertical, gutter g=1, children [fixed S=20, flex(gutter 1, two minSize F=24 children)].
    // correct parent floor = S + F + g = 45; buggy parent floor = S + 2F + 1 + g = 70.
    // Window: W in [45, 70). ----

    private const int V10_S = 20;
    private const int V10_G = 1;
    private const int V10_F = 24;

    private static Pane V10Root() => VerticalSplit(
        new[]
        {
            Leaf("20"),
            FlexSplit(new[] { Leaf(minSize: V10_F), Leaf(minSize: V10_F) }, gutter: 1),
        },
        gutter: V10_G);

    [Fact]
    public void V10_ParentDoesNotDrop_AcrossTheWholeDivergenceWindow()
    {
        for (var w = V10_S + V10_G + V10_F; w < V10_S + V10_G + 2 * V10_F + 1; w++)
        {
            var root = V10Root();
            var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
            var notes = new RenderNoteCollector();

            var resolved = SizeResolver.Resolve(root, w, Ctx, values, new Dictionary<string, Segment>(), notes);
            var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, EmptyColors, new Dictionary<string, Segment>(), new RenderNoteCollector());

            Assert.True(2 == resolved.Children.Count, $"at W={w}, both the fixed sibling and the flex child must survive");
            Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
            Assert.Single(notes.Notes, n => n.Message.Contains("flex split stacked"));
            Assert.Equal(PaneSplit.Horizontal, resolved.Children[1].EffectiveSplit);
            Assert.All(rendered.Buffer.Rows, r => Assert.False(string.IsNullOrWhiteSpace(DisplayWidth.Strip(r.Markup)), $"row must carry visible content at W={w}"));
        }
    }

    [Fact]
    public void V10_BoundaryPin_JustBelowTheWindow_ParentDoesDrop()
    {
        var w = V10_S + V10_G + V10_F - 1; // 44: even fully stacked, the flex child cannot be honoured.
        var root = V10Root();
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, w, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Single(resolved.Children);
        Assert.Contains(notes.Notes, n => n.Message.Contains("dropped"));
    }

    // ---- V11: Floor() for a Flex pane returns the min of its two branches. Floor() is private, so
    // this is necessarily indirect — via the drop-floor behaviour an ancestor observes, the same
    // seam DropFloorPredicateTests.Item5 uses for the ordinary-pane collapse:true case.
    //
    // NOT DONE: SPEC-88 §3.4.2's own worked numbers (child0 100/98, child1 0/0, giving
    // sideBySideFloor=99 < stackedFloor=100) describe a *simplified* formula, not this codebase's
    // actual OwnBorderReserve arithmetic. Under the real formula, exclusion under collapse:true only
    // ever discounts one border-padding bit per excluded edge (max 2, only at a middle child in a
    // 3+-child split), while the boundary charge for the same collapse:true split is exactly
    // childCount-1 (>=2 whenever a middle child exists to be discounted) — so for every Pane
    // construction I could derive, sideBySideFloor's achievable minimum is a TIE with stackedFloor's
    // max, never strictly below it (proof: sideBySideFloor = sum(discounted child floors) + boundary
    // >= discounted(argmax) + boundary >= discounted(argmax) + discount(argmax) = full(argmax) =
    // stackedFloor, since boundary >= discount(argmax) is forced whenever discount(argmax) > 0). A
    // tie does not distinguish `min(...)` from the wrong "widen the Horizontal branch" fix (both
    // pick the same value on a tie), so I could not build the literal distinguishing regression V11
    // asks for. The literal tie is ALSO unreachable as a stable "does not drop" assertion even on its
    // own terms: reaching it requires a flanking Content-kind child (Floor()==0 unconditionally,
    // unlike a MinSize child), but the allocator's own DropFloor (§SPEC-2.3-drop-predicate.md §3(a))
    // deliberately floors a Content pane's *grant* viability at 1 cell rather than 0 — precisely to
    // avoid an always-false grant test — so a Content-flanked construction that ties at the
    // orientation-decision layer still drops one cell short once real allocation runs. Flagging this
    // gap rather than deciding it silently; the tests below cover the ordinary (non-adversarial)
    // direction under collapse:false, then the same claim under collapse:true using MinSize-floored
    // flanking children (stable under DropFloor, unlike Content) — this is no longer a literal tie,
    // but it is the best construction that both (a) approaches the tie and (b) survives the real
    // allocator, so it stands in for the strict-inversion regression V11 asks for.
    [Fact]
    public void V11_FlexFloor_MatchesTheSmallerBranch_InTheOrdinaryDirection()
    {
        // sideBySideFloor = 24 + 24 + 1 = 49 (gutter 1, no collapse); stackedFloor = max(24,24) = 24.
        // An ancestor granting exactly 24 must NOT drop this flex child — it would if Floor(flex)
        // used sideBySideFloor (49) instead of the smaller stackedFloor (24).
        var flexChild = FlexSplit(new[] { Leaf(minSize: 24), Leaf(minSize: 24) }, gutter: 1);
        var root = VerticalSplit(new[] { Leaf("0"), flexChild }, gutter: 0);
        // A zero-width fixed sibling isolates the flex child's own floor as the whole story: avail
        // at outerWidth 24 goes entirely to the flex child.
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 24, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(24, resolved.Children[1].OuterWidth);
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
    }

    [Fact]
    public void V11_CollapseTrue_NearTieCase_DoesNotDrop()
    {
        // See the class-level V11 comment: a bordered middle child flanked by two MinSize(1)
        // children in a 3-child split. sideBySideFloor(collapse:true) = 1 + (20 + both-edges-
        // excluded reserve 2) + 1 + boundary(2) = 26; stackedFloor = max(1, 20 + full reserve 4, 1) =
        // 24 — close to, but (per the class comment) provably never below, stackedFloor. Since 26 >
        // 24, the flex orientation itself resolves to stacked at outerWidth 24, which routes through
        // the Horizontal branch's direct per-child resolve (no AllocateWithDrop grant-splitting) —
        // the one construction that both approaches the tie and stays stable under the allocator's
        // separate DropFloor viability rule.
        var dominant = Leaf(border: Bordered); // fill, bordered: full floor 24, both-excluded floor 22.
        var flexChild = FlexSplit(new[] { Leaf(minSize: 1), dominant, Leaf(minSize: 1) });
        var values = ItemValueResolver.Resolve(flexChild, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(flexChild, 24, Ctx, values, new Dictionary<string, Segment>(), notes, collapse: true);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.Equal(3, resolved.Children.Count);
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
    }

    // ---- V12: nested flex resolves correctly; E6 — no measurable render-time regression at depth 2 ----

    [Fact]
    public void V12_NestedFlex_ResolvesCorrectly_AtBothLevels()
    {
        var innerFlex = FlexSplit(new[] { Leaf(minSize: 24), Leaf(minSize: 24) }, gutter: 1);
        var outerFlex = FlexSplit(new[] { Leaf("10"), innerFlex }, gutter: 1);

        // Outer's own two branches: side-by-side needs Floor(fixed 10) + Floor(innerFlex) +
        // boundary(1) = 10 + 24 + 1 = 35, which fits at width 40, so the outer itself stays side by
        // side and the fixed leaf reserves exactly 10, leaving 29 for the inner flex child — below
        // the inner's own side-by-side floor of 49, so only the INNER flex stacks.
        var values = ItemValueResolver.Resolve(outerFlex, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();
        var resolved = SizeResolver.Resolve(outerFlex, 40, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(PaneSplit.Vertical, resolved.EffectiveSplit);
        Assert.Equal(2, resolved.Children.Count);
        var innerResolved = resolved.Children[1];
        Assert.Equal(PaneSplit.Horizontal, innerResolved.EffectiveSplit);
        Assert.Equal(2, innerResolved.Children.Count);
        Assert.All(innerResolved.Children, c => Assert.NotEqual(PaneSplit.Flex, c.EffectiveSplit));
    }

    [Fact]
    public void V12_E6_NestedFlexDepthTwo_NoMeasurableRenderRegression()
    {
        var innerFlex = FlexSplit(new[] { Leaf(minSize: 24), Leaf(minSize: 24) }, gutter: 1);
        var outerFlex = FlexSplit(new[] { Leaf(minSize: 10), innerFlex }, gutter: 1);
        var values = ItemValueResolver.Resolve(outerFlex, Ctx, EmptyColors);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 500; i++)
        {
            SizeResolver.Resolve(outerFlex, 40, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());
        }

        stopwatch.Stop();
        // §3.4.3 (E6): realistic nesting depth is 1-2 and the doubled Floor() cost is expected to be
        // negligible there; this is a coarse smoke bound, not a strict perf gate.
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"500 depth-2 nested-flex resolves took {stopwatch.ElapsedMilliseconds}ms");
    }

    // ---- V13: CheckStructuralSizes for Flex (§4.5.3) ----

    [Fact]
    public void V13a_FlexHeadlineCase_TheAndDoesNotOverReport()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 30 },
                        new() { MinSize = 30 },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void V13b_FlexImpossibleInBothArrangements_TheAndDoesReport()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "50" },
                        new() { Size = "50" },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("/surface/pane", diagnostic.Path);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("fixed-sizes-exceed-parent", diagnostic.Code);
        Assert.Contains("side by side", diagnostic.Message);
        Assert.Contains("stacked", diagnostic.Message);
    }

    [Fact]
    public void V13c_SameConfigsDeclaredVerticalAndHorizontal_ByteIdenticalToCurrentMain()
    {
        UserConfig Config(string split) => new()
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = split,
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "50" },
                        new() { Size = "50" },
                    },
                },
            },
        };

        // §7/V13(c): CheckSplitBounds and CheckHorizontalSplitChildren are called unchanged for
        // declared vertical/horizontal — this pins their output at 8437c37, so the flex branch
        // cannot perturb the declared directions.
        //
        // The fixture is deliberately FixedSize-only. SPEC-91 added a per-child minSize check to
        // CheckHorizontalSplitChildren, and this test stays green precisely because no child here
        // declares one — that is correct scoping, not an oversight. Do not add a minSize-bearing
        // case: it would test SPEC-91's check rather than this test's subject, which is #88's
        // non-interference. SPEC-91's own coverage is V1/V2/V3/V5/V7 in ConfigCheckTests.cs and
        // V6/V6b below.
        var verticalDiagnostics = ConfigChecker.Check(Config("vertical"));
        Assert.Single(verticalDiagnostics);
        Assert.Equal("fixed-sizes-exceed-parent", verticalDiagnostics[0].Code);
        Assert.Equal("/surface/pane", verticalDiagnostics[0].Path);

        var horizontalDiagnostics = ConfigChecker.Check(Config("horizontal"));
        Assert.Equal(2, horizontalDiagnostics.Count);
        Assert.All(horizontalDiagnostics, d => Assert.Equal("fixed-sizes-exceed-parent", d.Code));
        Assert.Equal("/surface/pane/children/0", horizontalDiagnostics[0].Path);
        Assert.Equal("/surface/pane/children/1", horizontalDiagnostics[1].Path);
    }

    // ---- SPEC-91: horizontal/flex per-child minSize check (§9.3, §13 V6/V6b) ----

    [Fact]
    public void V6_FlexChildrenMinSizeOverBound_TheAndNowReports()
    {
        // §9.3: #91 closes CheckHorizontalSplitChildren's minSize gap, so `stacked` is no longer
        // empty for this config and the AND in CheckFlexSplitBounds now fires — a behaviour change
        // in code #91 does not touch. Must not regress V13(a) in the same run (checked below).
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50 },
                        new() { MinSize = 50 },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("/surface/pane", diagnostic.Path);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("fixed-sizes-exceed-parent", diagnostic.Code);

        // V13(a) must still pass in the same run.
        var v13a = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 30 },
                        new() { MinSize = 30 },
                    },
                },
            },
        };
        Assert.Empty(ConfigChecker.Check(v13a));
    }

    [Fact]
    public void V6b_FlexCompositeMessage_QuotesMinSizeWording()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50 },
                        new() { MinSize = 50 },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("side by side", diagnostic.Message);
        Assert.Contains("stacked", diagnostic.Message);
        Assert.Contains("minSize", diagnostic.Message);
    }

    // ---- SPEC-92: fixed-parent minSize-sum check (§9, §10 V5/V6). Named with a `Spec92_` prefix
    // per SPEC-92 §14 to avoid colliding with this file's existing V5 (SPEC-88) and V6 (SPEC-91). ----

    // SPEC-92 §9: parent uses fixed `size` (not `maxSize`) — the only test that would catch a
    // regression re-narrowing CheckSplitBounds's merged guard back to MaxSize-only. Distinct from
    // V6 above despite the identical MinSize values: V6's parent reaches its bound via
    // `split.MaxSize`, this one via `SizeResolver.FixedSize(split)`. Fails against 62687bb.
    [Fact]
    public void Spec92_V5_FlexFixedParentChildrenMinSizeOverBound_TheAndReports()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    Size = "40",
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50 },
                        new() { MinSize = 50 },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("/surface/pane", diagnostic.Path);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("fixed-sizes-exceed-parent", diagnostic.Code);
        Assert.Contains("side by side", diagnostic.Message);
        Assert.Contains("stacked", diagnostic.Message);

        // SPEC-91's V6 (maxSize parent, same shape) must still pass in the same run.
        var v6 = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "flex",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50 },
                        new() { MinSize = 50 },
                    },
                },
            },
        };
        Assert.Single(ConfigChecker.Check(v6));
    }

    // SPEC-92 §9: parent uses fixed `size`, tripping both branches of CheckSplitBounds on the same
    // split — guards framework `:6060-6061` by confirming the fixed-sum and minSize-sum diagnostics
    // quote the same boundaryCost number rather than each recomputing it.
    [Fact]
    public void Spec92_V6_BoundaryCostNotDoubleCounted_BothDiagnosticsShareTheSameValue()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "40",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "25", MinSize = 25, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "25", MinSize = 25, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config).ToList();

        Assert.Equal(2, diagnostics.Count);
        var fixedCostMatch = System.Text.RegularExpressions.Regex.Match(diagnostics[0].Message, @"boundary cost \((\d+)\)");
        var minCostMatch = System.Text.RegularExpressions.Regex.Match(diagnostics[1].Message, @"boundary cost \((\d+)\)");
        Assert.True(fixedCostMatch.Success);
        Assert.True(minCostMatch.Success);
        Assert.Equal(fixedCostMatch.Groups[1].Value, minCostMatch.Groups[1].Value);
    }

    // ---- SPEC-88-AMENDMENT-flex-content-orientation.md §8: content-sized-leaf regression tests ----
    // Each uses a measureOverride (honoured by the orientation predicate per §6) to give a leaf's
    // natural width a known, controlled value rather than depending on the default builtin segment
    // list's actual rendered width.

    // §8.1: Jim's shape — two content-sized bordered leaves whose combined natural width (120)
    // exceeds W (97). Must stack; both panes present, nothing dropped.
    //
    // SPEC-95-flex-side-by-side-wrapped.md §10 test 6: re-examined, not assumed, per that spec.
    // This config was already implicitly Greedy (FlexSplit's own default), so under SPEC-95 §5.2
    // — which adds a new min-rows-only branch and leaves greedy/even untouched — this test's
    // assertion (stacks) is unaffected. Pinned to `distribute: Greedy` explicitly so it keeps
    // testing what it was written to test even if FlexSplit's default ever changes, and so it does
    // not read as coverage of Jim's actual (min-rows) shape, which SPEC-95's own new tests below
    // now cover and which goes side by side at this width instead.
    [Fact]
    public void V7_ContentLeaves_StackWhenCombinedNaturalWidthExceedsW_JimsShape()
    {
        var root = FlexSplit(new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, gutter: 0, distribute: PaneDistribute.Greedy);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        int MeasureOverride(Pane p, int? w) => 60;

        var resolved = SizeResolver.Resolve(root, 97, Ctx, values, new Dictionary<string, Segment>(), MeasureOverride, notes);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.Equal(2, resolved.Children.Count);
        // SPEC-97 §2.3.5: a stacked content leaf is measured and capped at the split's inner width,
        // not granted the parent's outer width — 60 (the stub measurement) is below the ceiling here.
        Assert.All(resolved.Children, c => Assert.Equal(60, c.OuterWidth));
    }

    // §8.2: the stack note interpolates the real side-by-side need (120), not the stale
    // sideBySideFloor (0 for a content leaf) — a regression here would read "need 0 columns".
    [Fact]
    public void V8_StackNote_ReportsSideBySideNeed_NotStaleZeroFloor()
    {
        var root = FlexSplit(new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, gutter: 0);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        int MeasureOverride(Pane p, int? w) => 60;

        SizeResolver.Resolve(root, 97, Ctx, values, new Dictionary<string, Segment>(), MeasureOverride, notes);

        Assert.Single(notes.Notes);
        Assert.Equal("pane 2: flex split stacked; children need 120 columns at 97 columns", notes.Notes[0].Message);
    }

    // §8.3: still side by side when the natural width genuinely fits (40 <= 97) — byte-identical to
    // declaring "vertical" outright, same as V2 but for content-sized leaves.
    [Fact]
    public void V9_ContentLeaves_SideBySide_WhenNaturalWidthFits_ByteIdenticalToDeclaredVertical()
    {
        var flexRoot = FlexSplit(new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, gutter: 0);
        var verticalRoot = VerticalSplit(new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, gutter: 0);

        int MeasureOverride(Pane p, int? w) => 20;

        var flexValues = ItemValueResolver.Resolve(flexRoot, Ctx, EmptyColors);
        var flexNotes = new RenderNoteCollector();
        var flexResolved = SizeResolver.Resolve(flexRoot, 97, Ctx, flexValues, new Dictionary<string, Segment>(), MeasureOverride, flexNotes);

        var verticalValues = ItemValueResolver.Resolve(verticalRoot, Ctx, EmptyColors);
        var verticalNotes = new RenderNoteCollector();
        var verticalResolved = SizeResolver.Resolve(verticalRoot, 97, Ctx, verticalValues, new Dictionary<string, Segment>(), MeasureOverride, verticalNotes);

        Assert.Equal(PaneSplit.Vertical, flexResolved.EffectiveSplit);
        Assert.Empty(flexNotes.Notes);
        Assert.Equal(verticalResolved.Children.Select(c => c.OuterWidth), flexResolved.Children.Select(c => c.OuterWidth));
        Assert.Equal(RenderMarkup(verticalResolved, verticalValues), RenderMarkup(flexResolved, flexValues));
    }

    // §8.4: fill (non-content) children at a width strictly between StackedFloor (25) and
    // SideBySideFloor (52) — 40 sits strictly inside, unlike SPEC-94 §5 E3's equal-floors width.
    // sideBySideNeed must reduce to the unchanged sideBySideFloor value for non-content children.
    [Fact]
    public void V10_FillChildren_StrictlyBetweenFloors_StacksUnaffectedByAmendment()
    {
        var root = FlexSplit(new[] { Leaf(minSize: 25), Leaf(minSize: 25) }, gutter: 2);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 40, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.Single(notes.Notes);
        Assert.Equal("pane 2: flex split stacked; children need 52 columns at 40 columns", notes.Notes[0].Message);
    }

    // §8.5 / §3.3: a content-sized child that has its own split (children) is not a content-sized
    // LEAF, so the predicate must use its Floor (20, from its one leaf child's minSize), never
    // natural measurement. If the predicate wrongly natural-measured it, this override would blow
    // the side-by-side need past outerWidth and force a stack instead of the floor-correct fit.
    [Fact]
    public void V11_ContentSizedSplitChild_OrientationUsesItsFloor_NotNaturalMeasurement()
    {
        var splitChild = VerticalSplit(new[] { Leaf(minSize: 20) }, gutter: 0, size: "content");
        var otherChild = Leaf(minSize: 20);
        var root = FlexSplit(new[] { splitChild, otherChild }, gutter: 0);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        int MeasureOverride(Pane p, int? w) => ReferenceEquals(p, splitChild) ? 999_999 : 20;

        var resolved = SizeResolver.Resolve(root, 45, Ctx, values, new Dictionary<string, Segment>(), MeasureOverride, notes);

        Assert.Equal(PaneSplit.Vertical, resolved.EffectiveSplit);
        Assert.Empty(notes.Notes);
    }

    // ---- SPEC-95-flex-side-by-side-wrapped.md §10: distribute:"min-rows" can shrink content-sized
    // leaves to fit, so a flex pane on min-rows should go side by side at widths where its children's
    // NATURAL width does not fit but their WRAPPED width does — Jim's actual live shape. Every test
    // here uses rowCountOverride to give each content leaf a deterministic, monotone rows(w) ~=
    // ceil(200/w) (natural width 200, matching the measureOverride below) rather than depending on
    // real segment/wrap measurement, matching MinRowsDistributeTests' own convention. ----

    private static int Spec95NaturalWidth(Pane p, int? w) => 200;

    private static int Spec95RowsAtWidth(Pane p, int w) => w <= 0 ? 0 : (int)Math.Ceiling(200.0 / w);

    private static Pane Spec95JimsShape(int gutter = 0) => FlexSplit(
        new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) },
        gutter: gutter, distribute: PaneDistribute.MinRows);

    // Test 1: Jim's shape at 80 columns. Side by side, both panes present, nothing dropped, each
    // pane at least MinUsableWidth.
    [Fact]
    public void Spec95_V1_JimsShape_MinRows_SideBySideAt80Columns()
    {
        var root = Spec95JimsShape();
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, notes);

        Assert.Equal(PaneSplit.Vertical, resolved.EffectiveSplit); // Vertical == side by side (SPEC-88 §1.3).
        Assert.Equal(2, resolved.Children.Count);
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
        Assert.All(resolved.Children, c => Assert.True(c.OuterWidth >= RowLayout.MinUsableWidth, $"pane granted {c.OuterWidth} columns, must be at least MinUsableWidth"));
    }

    // Test 2: the #94 crash (a pane silently dropped) does not return now that side by side is
    // reachable again on the min-rows path — §3.3's routing concern.
    [Fact]
    public void Spec95_V2_JimsShape_MinRows_NoDropAt80_97_100Columns()
    {
        foreach (var width in new[] { 80, 97, 100 })
        {
            var root = Spec95JimsShape();
            var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
            var notes = new RenderNoteCollector();

            var resolved = SizeResolver.Resolve(root, width, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, notes);

            Assert.Equal(2, resolved.Children.Count);
            Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
        }
    }

    // Test 3 (§5.1 unit test): min-rows sizes content leaves at all, rather than falling back to
    // SolveMinRows' :847 `lo` fallback. RowCountOverride mimics SPEC-95 §9 E1's own finding — the
    // real packer returns 0 rows at outer width 0 — so with the pre-fix `Floor()`-based lo (0 for a
    // content leaf), maxT bottoms out at 0, the T-search never runs, and both candidates fall back
    // to width 0. Confirmed to fail against the pre-fix code (see verification notes); passes once
    // SearchFloor replaces Floor as SolveMinRows' lo.
    [Fact]
    public void Spec95_V3_MinRows_SizesContentLeavesAtAll_NotTheLoFallback()
    {
        var root = new Pane(PaneSplit.Vertical, new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, "fill", NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: 0, Distribute: PaneDistribute.MinRows);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.All(resolved.Children, c => Assert.True(c.OuterWidth > 0, "SolveMinRows must not fall back to the degenerate lo=0 allocation"));
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
    }

    // Test 4: narrow widths still stack. Below 2 * (MinUsableWidth + border reserve) = 48, even at
    // each candidate's own SearchFloor (24) the pair cannot both fit — minRowsFeasible is false and
    // the (unchanged, Floor()-based) stacked branch wins, exactly as before #95.
    [Fact]
    public void Spec95_V4_NarrowWidth_StillStacks()
    {
        var root = Spec95JimsShape();
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 40, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, notes);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.Equal(2, resolved.Children.Count);
    }

    // Test 5: greedy and even are unchanged — the guard on §5.2 leaking into modes it must not
    // touch. Neither can shrink a child, so the orientation decision matches SPEC-88-AMENDMENT §2
    // exactly: side by side only once outerWidth reaches sideBySideNeed (400 here); stacked below
    // that, since stackedFloor for a content leaf (Floor()'s unchanged SizeKind.Content => 0) is
    // satisfiable at every non-negative width.
    [Fact]
    public void Spec95_V5_GreedyAndEven_OrientationUnchanged_AtSeveralWidths()
    {
        foreach (var distribute in new[] { PaneDistribute.Greedy, PaneDistribute.Even })
        {
            foreach (var width in new[] { 40, 80, 100, 400, 500 })
            {
                var root = FlexSplit(new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, gutter: 0, distribute: distribute);
                var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
                var notes = new RenderNoteCollector();

                var resolved = SizeResolver.Resolve(root, width, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, notes);

                var expected = width >= 400 ? PaneSplit.Vertical : PaneSplit.Horizontal;
                Assert.Equal(expected, resolved.EffectiveSplit);
            }
        }
    }

    // Test 7 (§5.4): packer cost. Under §5.4(a), a flex min-rows resolve that goes side by side via
    // minRowsFeasible must not double MinRowsPackerInvocationCount relative to a declared-vertical
    // min-rows resolve of the same tree at the same width — the cached first pass from the
    // orientation decision is reused rather than recomputed.
    [Fact]
    public void Spec95_V7_PackerCost_FlexMinRows_DoesNotDoubleVsDeclaredVertical()
    {
        Pane BuildFlex() => new(PaneSplit.Flex, new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, "fill", NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: 0, Distribute: PaneDistribute.MinRows);
        Pane BuildVertical() => new(PaneSplit.Vertical, new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, "fill", NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: 0, Distribute: PaneDistribute.MinRows);

        var flexRoot = BuildFlex();
        var flexValues = ItemValueResolver.Resolve(flexRoot, Ctx, EmptyColors);
        SizeResolver.MinRowsPackerInvocationCount = 0;
        var flexResolved = SizeResolver.Resolve(flexRoot, 80, Ctx, flexValues, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, new RenderNoteCollector());
        var flexCount = SizeResolver.MinRowsPackerInvocationCount;

        // Sanity: the flex pane actually went side by side via the new minRowsFeasible branch —
        // the scenario §5.4(a) is caching for. If this stacks instead, the comparison below is moot.
        Assert.Equal(PaneSplit.Vertical, flexResolved.EffectiveSplit);

        var verticalRoot = BuildVertical();
        var verticalValues = ItemValueResolver.Resolve(verticalRoot, Ctx, EmptyColors);
        SizeResolver.MinRowsPackerInvocationCount = 0;
        SizeResolver.Resolve(verticalRoot, 80, Ctx, verticalValues, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, new RenderNoteCollector());
        var verticalCount = SizeResolver.MinRowsPackerInvocationCount;

        Assert.Equal(verticalCount, flexCount);
    }

    // Test 8 (§5.1.1): a content leaf whose declared maxSize sits below the broadened SearchFloor
    // (RowLayout.MinUsableWidth + OwnBorderReserve = 24 for a bordered leaf) must never be
    // searched or granted above that maxSize — the maxSize clause is tested first and wins.
    // Asserts directly on the granted width: MinRowsDropNoteTests passing unchanged is necessary
    // but not sufficient for this guarantee, since that test only observes drop notes.
    [Fact]
    public void Spec95_V8_MaxSize_SurvivesTheBroadenedSearchFloor()
    {
        var cappedLeaf = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", Bordered, null, "…", null, Array.Empty<PaneItem>(), MaxSize: 5);
        var root = new Pane(PaneSplit.Vertical, new[] { cappedLeaf }, "fill", NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: 0, Distribute: PaneDistribute.MinRows);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, notes);

        Assert.Single(resolved.Children);
        Assert.True(resolved.Children[0].OuterWidth <= 5, $"granted {resolved.Children[0].OuterWidth} columns, must never exceed declared maxSize 5");
    }

    // Test 9 (§5.1.3): positive coverage that SearchFloor's broadened floor reaches every
    // declared-vertical min-rows resolve, not only ones a flex pane decided to become vertical.
    // Three content leaves (no maxSize) at a width wide enough to admit a feasible T: every leaf
    // must get a non-zero grant and none may be dropped.
    [Fact]
    public void Spec95_V9_DeclaredVertical_MinRows_SizesContentLeavesOutsideFlex()
    {
        var root = new Pane(PaneSplit.Vertical, new[] { Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered), Leaf(size: "content", border: Bordered) }, "fill", NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: 0, Distribute: PaneDistribute.MinRows);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(root, 200, Ctx, values, new Dictionary<string, Segment>(), Spec95NaturalWidth, Spec95RowsAtWidth, notes);

        Assert.Equal(3, resolved.Children.Count);
        Assert.All(resolved.Children, c => Assert.True(c.OuterWidth > 0, "declared-vertical min-rows must not fall back to the degenerate lo=0 allocation for content leaves"));
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
    }

    // ---- SPEC-96: a bordered stacked split must charge its own border reserve to every child,
    // not the full outer width. ----

    [Fact]
    public void Spec96_T1_StackedBorderedParent_ReservesBorderWidth()
    {
        var root = HorizontalSplit(new[] { Leaf(), Leaf() }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        Assert.All(resolved.Children, c => Assert.Equal(80 - SizeResolver.OwnBorderReserve(root), c.OuterWidth));
    }

    [Fact]
    public void Spec96_T2_StackedUnborderedParent_Unaffected()
    {
        var root = HorizontalSplit(new[] { Leaf(), Leaf() }, border: NoBorder);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        Assert.All(resolved.Children, c => Assert.Equal(80, c.OuterWidth));
    }

    [Fact]
    public void Spec96_T4_NestedStackedCompounding_BothReservesApply()
    {
        var innerChild = HorizontalSplit(new[] { Leaf() }, border: Bordered);
        var root = HorizontalSplit(new[] { innerChild }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        var resolvedInner = resolved.Children[0];
        var grandchild = resolvedInner.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(root);
        Assert.Equal(80 - reserve, resolvedInner.OuterWidth);
        Assert.Equal(80 - reserve - reserve, grandchild.OuterWidth);
    }

    [Fact]
    public void Spec96_T6_FlexReflow_LongModelStringForcesStack_NoOverflow()
    {
        var longModelLeaf = Leaf(minSize: 60);
        var root = FlexSplit(new[] { longModelLeaf, Leaf(minSize: 10) }, gutter: 1, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();
        // sideBySideNeed = 60 + 10 + 1 = 71 (does not fit at 65); stackedFloor = max(60, 10) = 60
        // (fits at 65) — mirrors the "Opus 5" trigger: a longer segment pushes side-by-side need
        // past the width, flex flips to stacked, and the latent reserve bug used to overflow here.
        var resolved = SizeResolver.Resolve(root, 65, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Equal(PaneSplit.Horizontal, resolved.EffectiveSplit);
        var ceiling = 65 - SizeResolver.OwnBorderReserve(root);
        Assert.All(resolved.Children, c => Assert.True(c.OuterWidth <= ceiling, $"child granted {c.OuterWidth}, parent's inner width is only {ceiling}"));
    }

    // ---- SPEC-97: a stacked child honours its own size/minSize/maxSize against the split's
    // inner width (the ceiling), independently of its siblings. ----

    [Fact]
    public void Spec97_S1_Fixed_GrantedItsDeclaredWidth()
    {
        var root = HorizontalSplit(new[] { Leaf(size: "20"), Leaf() }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        Assert.Equal(20, resolved.Children[0].OuterWidth);
        Assert.Equal(80 - SizeResolver.OwnBorderReserve(root), resolved.Children[1].OuterWidth);
    }

    [Fact]
    public void Spec97_S2_FixedAboveCeiling_GrantedCeiling_ClampNoteEmitted()
    {
        var root = HorizontalSplit(new[] { Leaf(size: "200") }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), notes);

        var ceiling = 80 - SizeResolver.OwnBorderReserve(root);
        Assert.Equal(ceiling, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == $"pane 1: 200 columns requested, clamped to {ceiling} at {ceiling} columns");
    }

    [Fact]
    public void Spec97_S3_Percent_HalfOfCeiling_AwayFromZeroRounding()
    {
        var root = HorizontalSplit(new[] { Leaf(size: "50%") }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        // outer 81, reserve 2 (NoBorder edges default All -> Bordered's edges are also All per the
        // shared field above), ceiling odd enough that .5 actually arises.
        var resolved = SizeResolver.Resolve(root, 81, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        var ceiling = 81 - SizeResolver.OwnBorderReserve(root);
        var expected = (int)Math.Round(0.5 * ceiling, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, resolved.Children[0].OuterWidth);
    }

    [Fact]
    public void Spec97_S5_MinSizeAboveCeiling_ClampedNotDropped()
    {
        var root = HorizontalSplit(new[] { Leaf(minSize: 200) }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var notes = new RenderNoteCollector();
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), notes);

        Assert.Single(resolved.Children);
        Assert.Equal(80 - SizeResolver.OwnBorderReserve(root), resolved.Children[0].OuterWidth);
    }

    [Fact]
    public void Spec97_S6_MaxSize_CapsFillChildBelowCeiling()
    {
        var root = HorizontalSplit(new[] { Leaf(maxSize: 10) }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        Assert.Equal(10, resolved.Children[0].OuterWidth);
    }

    [Fact]
    public void Spec97_S7_ClampOrder_DeclaredBoundsBeforeCeiling()
    {
        var root = HorizontalSplit(new[] { Leaf(size: "10", minSize: 20), Leaf(size: "60", maxSize: 30) }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        Assert.Equal(20, resolved.Children[0].OuterWidth);
        Assert.Equal(30, resolved.Children[1].OuterWidth);
    }

    [Fact]
    public void Spec97_S9_OrientationDecision_UnchangedByStackedSizing()
    {
        var withoutSize = FlexSplit(new[] { Leaf(minSize: 30), Leaf(minSize: 30) }, gutter: 1);
        var withSize = FlexSplit(new[] { Leaf(size: "15", minSize: 30), Leaf(minSize: 30) }, gutter: 1);

        foreach (var width in new[] { 40, 61, 100 })
        {
            var v1 = ItemValueResolver.Resolve(withoutSize, Ctx, EmptyColors);
            var r1 = SizeResolver.Resolve(withoutSize, width, Ctx, v1, new Dictionary<string, Segment>(), new RenderNoteCollector());
            var v2 = ItemValueResolver.Resolve(withSize, Ctx, EmptyColors);
            var r2 = SizeResolver.Resolve(withSize, width, Ctx, v2, new Dictionary<string, Segment>(), new RenderNoteCollector());

            Assert.Equal(r1.EffectiveSplit, r2.EffectiveSplit);
        }
    }

    [Fact]
    public void Spec97_S14_NoSizeConfigs_ByteIdentical_ToPreChangeBehaviour()
    {
        // Every child is "fill" (the default and only size these use) — StackedWidth's Fill arm
        // is `_ => avail`, exactly the pre-SPEC-97 grant. This is the migration guarantee.
        var root = HorizontalSplit(new[] { Leaf(), Leaf(), Leaf() }, border: Bordered);
        var values = ItemValueResolver.Resolve(root, Ctx, EmptyColors);
        var resolved = SizeResolver.Resolve(root, 80, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());

        var ceiling = 80 - SizeResolver.OwnBorderReserve(root);
        Assert.All(resolved.Children, c => Assert.Equal(ceiling, c.OuterWidth));
    }
}
