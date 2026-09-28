using Spectre.Console;
using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-2.3-suppression-predicate.md §4: ShouldSuppressBorder now tests the pane's own
// PRE-suppression inner width (grant minus its own border reserve) rather than outer width — the
// 4-column disagreement band (§1) that #71's DropFloor made load-bearing (§2) is closed. Every
// expected value below is derived from the spec's own arithmetic (MinUsableWidth=20,
// OwnBorderReserve = 2 + left-edge + right-edge), not observed by running this change's
// implementation, per §8 item 4's explicit requirement.
public class BorderSuppressionPredicateTests
{
    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens = new Dictionary<string, ColorResolution.ColorRule>();
    private static readonly PaneBorder Bordered = new(new ColorResolution.ColorExpr.Literal("grey"), BoxBorder.Rounded, PaneBorderEdges.All);
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);

    private readonly ITestOutputHelper _output;

    public BorderSuppressionPredicateTests(ITestOutputHelper output)
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

    // §8 item 1 (the headline test): a bordered fill pane, full edges (reserve 4), granted 22 in a
    // 32-column split (fixed sibling takes 10, gutter 0). Pre-#73 this pane was dropped outright
    // (outer-width predicate: 22 >= 20, so suppression never fired; unsuppressed floor 24 > 22).
    // Post-#73: pre-suppression inner width is 22 - 4 = 18 < 20, so suppression fires and the
    // floor drops to 20; 22 >= 20 survives.
    [Fact]
    public void Item1_Band20To23_SurvivesAndPredicateAgreesSuppressionFires()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill" },
                { "size": "10", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 32, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(22, resolved.Children[0].OuterWidth);
        Assert.Empty(notes.Notes);

        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);
        Assert.Equal(4, reserve);
        Assert.True(SizeResolver.ShouldSuppressBorder(fillPane, 22 - reserve),
            "grant 22's pre-suppression inner width (18) is under MinUsableWidth, so the " +
            "predicate that let this pane survive must independently agree it suppresses");

        // Whether the RENDERED content then occupies the reclaimed width 22 (defect B,
        // SPEC-2.3-suppression-predicate.md §6.3) is covered separately below by
        // DefectB_Item1_..., DefectB_Item7_..., and DefectB_Item8_....
    }

    // §8 item 2: same pane, granted 19. Pre-suppression inner width 15 < 20 still suppresses, and
    // the post-suppression floor (20) still exceeds the grant — genuinely too narrow, drops with
    // the below-floor note. Pins that row 1 of the spec's own table (§2) is unchanged by this fix.
    // The fixed sibling is listed first here (unlike items 1/3): the drop-retry loop always drops
    // whichever pane is LAST in its current list regardless of which one actually failed its
    // floor check (see FloorSumExceedsBudget_Greedy et al.), so the fill pane must be last for
    // its own grant/floor to appear in the note.
    [Fact]
    public void Item2_Below20_StillDrops()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "13", "border": { "enabled": false } },
                { "size": "fill" }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 32, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: 19 columns is under its 20-column floor at 32 columns");
    }


    // §8 item 3: same pane, granted 24 (its full unsuppressed floor: MinUsableWidth 20 + reserve
    // 4). Pre-suppression inner width is exactly 20 — not under it — so suppression does not
    // fire and the pane keeps its border. The boundary this fix must not move.
    [Fact]
    public void Item3_AtFloor_KeepsBorder()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill" },
                { "size": "8", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 32, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(24, resolved.Children[0].OuterWidth);
        Assert.Empty(notes.Notes);

        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);
        Assert.False(SizeResolver.ShouldSuppressBorder(fillPane, 24 - reserve),
            "grant 24's pre-suppression inner width (20) is not under MinUsableWidth");
    }

    // §8 item 5: a pane with edges {left:false, right:false} has reserve 2, not 4, so its own
    // suppression band is one column narrower and starts one column earlier than a fully-edged
    // pane's. Drop/survive outcomes cannot discriminate a reserve regression here (the suppressed
    // floor is the constant MinUsableWidth regardless of reserve, so any grant that clears the
    // true, larger unsuppressed floor also clears a wrongly-computed smaller one) — the
    // discriminating check is OwnBorderReserve's own return value, fed into ShouldSuppressBorder
    // rather than a hardcoded 4, so a regression to "24" or "4" here fails this test either by
    // making the reserve assertion itself wrong, or by moving the boundary the two assertions
    // below pin.
    [Fact]
    public void Item5_ReserveVariant_BoundaryMovesWithReserve()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill", "border": { "edges": { "left": false, "right": false } } },
                { "size": "10", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);

        Assert.Equal(2, reserve);
        Assert.False(SizeResolver.ShouldSuppressBorder(fillPane, 22 - reserve),
            "reserve 2: grant 22's inner width (20) is not under MinUsableWidth");
        Assert.True(SizeResolver.ShouldSuppressBorder(fillPane, 21 - reserve),
            "reserve 2: grant 21's inner width (19) is under MinUsableWidth, one column earlier " +
            "than a fully-edged (reserve 4) pane's own band");
    }

    // §8 item 6: SPEC-2.3-suppression-predicate.md §4's "collapse mismatch" — before this fix,
    // DropFloor's suppression check had no excludeLeft/excludeRight while Floor/DropFloor's own
    // floor computation did, so under collapse:true the allocator could reason about a pane with
    // edge-excluded reserve while the suppression check reasoned about one without. A 3-child
    // collapse:true vertical split with two fixed outer panes (exempt from the tooSmall check,
    // per #67a) isolates the middle fill child: its own excludeLeft/excludeRight are both true
    // (it faces a shared boundary on each side), so its reserve is 2 (padding only, no verticals)
    // rather than the 4 a non-excluded read would wrongly use. Granted 21, its unsuppressed floor
    // (with the correct exclude-aware reserve) is 22 — 21 doesn't clear it — but its
    // pre-suppression inner width (21 - 2 = 19) is under MinUsableWidth, so suppression fires and
    // it survives at the lower floor (20). Before this fix, DropFloor's suppression check would
    // have tested grant (21) directly against outer-width's own bar (20) and also not fired
    // (21 >= 20), landing on the *unsuppressed*, exclude-aware floor of 22 — 21 < 22 would have
    // dropped it. Surviving here is exactly the allocator and the predicate agreeing.
    [Fact]
    public void Item6_Collapse_AllocatorAndPredicateAgreeOnExcludeAwareReserve()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "children": [
                { "size": "5", "border": { "enabled": false } },
                { "size": "fill" },
                { "size": "5", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 33, Ctx, values,new Dictionary<string, Segment>(),  notes, collapse: true);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(3, resolved.Children.Count);
        Assert.Equal(21, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);

        var middlePane = pane.Children[1];
        var excludeAwareReserve = SizeResolver.OwnBorderReserve(middlePane, excludeLeft: true, excludeRight: true);
        Assert.Equal(2, excludeAwareReserve);
        Assert.True(SizeResolver.ShouldSuppressBorder(middlePane, 21 - excludeAwareReserve),
            "grant 21's exclude-aware pre-suppression inner width (19) is under MinUsableWidth");
    }

    // Defect B (SPEC-2.3-suppression-predicate.md §6.3): a suppressed pane's border reserve is
    // reclaimed for content rather than spent on blank chrome. These three tests bypass
    // ItemValueResolver/config (hand-built values dictionary keyed by PaneItem.Id, matching
    // HeightLadderTests' pattern) and render end-to-end through PaneTreeRenderer.Render, since
    // the claim under test is about PaneBorderRenderer.Wrap's actual output, not SizeResolver's
    // arithmetic. Expected widths are derived from the same grant-22/reserve-4 arithmetic used
    // above, not observed from a run (§8's explicit requirement).
    private static string StripMarkupTags(string markup) => System.Text.RegularExpressions.Regex.Replace(markup, @"\[[^\]]*\]", "");

    private static (SizeResolver.ResolvedPane FillNode, IReadOnlyDictionary<string, string?> Values, RenderNoteCollector Notes) ResolveSuppressedFillPane(IReadOnlyList<PaneItem> fillItems, IReadOnlyDictionary<string, string?> values)
    {
        var fillPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "fill", Bordered, OverflowMode.Truncate, "…", null, fillItems);
        var fixedPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "10", NoBorder, null, "…", null, Array.Empty<PaneItem>());
        var parent = new Pane(PaneSplit.Vertical, new[] { fillPane, fixedPane }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>(), Gutter: 0);

        var notes = new RenderNoteCollector();
        var resolved = SizeResolver.Resolve(parent, 32, Ctx, values,new Dictionary<string, Segment>(),  notes);
        Assert.Equal(22, resolved.Children[0].OuterWidth); // same grant-22/reserve-4 scenario as Item1/Item3 above

        return (resolved.Children[0], values, notes);
    }

    // §8 item 1 (the headline test for defect B): a bordered fill pane forced into suppression at
    // grant 22 (reserve 4). Pre-defect-B, PaneBorderRenderer.Wrap laid content out at 22 - 4 = 18
    // and spent the 4 reclaimed columns on blank chrome; post-defect-B, content occupies the full
    // outer width 22. A single 30-char filler item, truncated, proves this directly: the pane's own
    // Ellipsis ("…", one column) lands at column 22 — 21 'X's plus the ellipsis — only if the pane
    // actually offers 22 columns to truncate into; under 18 the ellipsis would land at column 18.
    [Fact]
    public void DefectB_Item1_SuppressedContentOccupiesFullOuterWidth()
    {
        var items = new[] { new PaneItem(null, null, null, null, Id: "a") };
        var values = new Dictionary<string, string?> { ["a"] = new string('X', 30) };
        var (fillNode, resolvedValues, notes) = ResolveSuppressedFillPane(items, values);

        var contribution = PaneTreeRenderer.Render(fillNode, Ctx, resolvedValues, Tokens,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(3, contribution.Buffer.Rows.Count); // top border row, content row, bottom border row
        Assert.All(contribution.Buffer.Rows, r => Assert.Equal(22, r.Width));

        var contentText = StripMarkupTags(contribution.Buffer.Rows[1].Markup);
        Assert.Equal(new string('X', 21) + "…", contentText);
    }

    // §8 item 7: "Item 2's config" (this file's Item1_Band20To23_... test above, same fill pane at
    // grant 22, no items configured) asserting inner width 22 directly on the rendered content
    // row's own Width — the number that fails under defect A alone (18) and passes only once
    // defect B lands too. Declined-to-strike per §6.3.
    [Fact]
    public void DefectB_Item7_ReclaimedInnerWidthIsFullOuterWidth()
    {
        var (fillNode, resolvedValues, notes) = ResolveSuppressedFillPane(Array.Empty<PaneItem>(), new Dictionary<string, string?>());

        var contribution = PaneTreeRenderer.Render(fillNode, Ctx, resolvedValues, Tokens,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(22, contribution.Buffer.Rows[1].Width);
    }

    // §8 item 8: distinguishes "reclaimed" from "drew spaces" — a partial fix that widens the
    // content row's reported Width to 22 while still leaving the old 18-wide content padded with
    // 4 blank columns would pass items 1 and 7 above but fail this one. The 22-char filler content
    // must run edge to edge, with no leading/trailing space where the border glyphs used to be.
    [Fact]
    public void DefectB_Item8_BlankChromeIsGoneNotMerelyInvisible()
    {
        var items = new[] { new PaneItem(null, null, null, null, Id: "a") };
        var values = new Dictionary<string, string?> { ["a"] = new string('X', 30) };
        var (fillNode, resolvedValues, notes) = ResolveSuppressedFillPane(items, values);

        var contribution = PaneTreeRenderer.Render(fillNode, Ctx, resolvedValues, Tokens,new Dictionary<string, Segment>(),  notes);
        var contentText = StripMarkupTags(contribution.Buffer.Rows[1].Markup);

        Assert.False(contentText.StartsWith(' '), "leading columns must be content, not reclaimed-but-unused blank padding");
        Assert.False(contentText.EndsWith(' '), "trailing columns must be content, not reclaimed-but-unused blank padding");
        Assert.DoesNotContain("  ", contentText); // no interior run of blanks either
    }

    // ---- SPEC-2.8.2-height-suppression-empty-content.md §4 (task #80): height suppression
    // requires a beneficiary. A bordered pane with zero content rows must render an empty 2-row
    // box (there is room and nothing to gain by dropping it), not vanish — reclaiming the edge
    // rows only makes sense when there is content to reclaim them for. Below a 2-row budget the
    // box genuinely cannot be drawn, so suppression there is unchanged. Rendered end-to-end
    // through PaneTreeRenderer.Render, as #73b's tests did — the claim is about emitted output.

    private static readonly PaneBorder EmptyBordered = new(new ColorResolution.ColorExpr.Literal("grey"), BoxBorder.Rounded, PaneBorderEdges.All);
    private static readonly PaneBorder EmptyBorderless = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> EmptyTokens = new Dictionary<string, ColorResolution.ColorRule>();
    private static readonly IReadOnlyDictionary<string, string?> NoValues = new Dictionary<string, string?>();

    // An empty Items list is "author configured no items" to PaneAssembler (falls back to
    // rendering the default segment set) — not the same thing as zero content rows. A configured
    // item whose id has no entry in NoValues resolves to nothing, which is the actual
    // zero-content-row case §4 is testing.
    private static readonly PaneItem UnresolvedItem = new(null, null, null, null, Id: "unresolved");

    private static Pane EmptyLeaf(PaneBorder border, int? maxRows = null, int? minSize = null) =>
        new(PaneSplit.None, Array.Empty<Pane>(), "content", border, OverflowMode.Truncate, "…", maxRows, new[] { UnresolvedItem }, MinSize: minSize);

    // §4 item 1, the headline case: no maxRows, no ClipRows (natural-height path), zero content
    // rows. Must be shown to FAIL (emit 0) on unmodified main before the fix — confirmed via E1.
    [Fact]
    public void Render_NaturalHeight_BorderedPaneWithZeroContentRows_EmitsEmptyBoxNotNothing()
    {
        var pane = EmptyLeaf(EmptyBordered);
        var resolved = SizeResolver.Resolve(pane, 20, Ctx, NoValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, NoValues, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
    }

    // §4 item 2 / §1.2: same as item 1, but with an explicit MinSize set — the case PaneCollapse
    // (§2.11.2) is forbidden to remove per ConfigCheck.cs:784's "author said so; always wins"
    // rule. Height suppression never consults collapse at all, so pre-fix this pane vanished
    // exactly like an unprotected one — this is why §1 is a defect and not a feature, not merely
    // a missing case in an otherwise-sound heuristic.
    [Fact]
    public void Render_NaturalHeight_MinSizeProtectedPaneWithZeroContentRows_Survives()
    {
        var pane = EmptyLeaf(EmptyBordered, minSize: 5);
        var resolved = SizeResolver.Resolve(pane, 20, Ctx, NoValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, NoValues, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
    }

    // §4 item 3 / §1.1's variant: ClipRows: 2, zero content rows. The case most likely to be
    // missed by a fix aimed only at :159 (the natural-height branch).
    [Fact]
    public void Render_ClipRowsTwo_ZeroContentRows_EmitsEmptyBoxNotNothing()
    {
        var pane = EmptyLeaf(EmptyBordered);
        var resolved = SizeResolver.Resolve(pane, 20, Ctx, NoValues,new Dictionary<string, Segment>(),  new RenderNoteCollector()) with { ClipRows = 2 };

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, NoValues, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
    }

    // §4 item 4: ClipRows: 1, zero content rows. Unchanged behaviour — the box genuinely does not
    // fit in a 1-row budget, so 0 rows out is still correct. Guards against over-correcting §3.1.
    [Fact]
    public void Render_ClipRowsOne_ZeroContentRows_StillEmitsNothing()
    {
        var pane = EmptyLeaf(EmptyBordered);
        var resolved = SizeResolver.Resolve(pane, 20, Ctx, NoValues,new Dictionary<string, Segment>(),  new RenderNoteCollector()) with { ClipRows = 1 };

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, NoValues, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(0, contribution.Buffer.Rows.Count);
    }

    // §4 item 5: ClipRows: 0. Unchanged.
    [Fact]
    public void Render_ClipRowsZero_StillEmitsNothing()
    {
        var pane = EmptyLeaf(EmptyBordered);
        var resolved = SizeResolver.Resolve(pane, 20, Ctx, NoValues,new Dictionary<string, Segment>(),  new RenderNoteCollector()) with { ClipRows = 0 };

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, NoValues, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(0, contribution.Buffer.Rows.Count);
    }

    // §4 item 6, the highest-value regression test in the list: ClipRows: 1 with one content row.
    // §1.1's whole reason for existing — a careless fix requiring contentRows.Count > 0 AND
    // budget >= 2 would break this: exactly one row of content, no edges, budget spent entirely
    // on content rather than chrome.
    [Fact]
    public void Render_ClipRowsOne_OneContentRow_SpendsWholeBudgetOnContentNoEdges()
    {
        var item = new PaneItem(null, null, null, null, Id: "a");
        var values = new Dictionary<string, string?> { ["a"] = new string('X', 20) };
        var pane = EmptyLeaf(EmptyBordered) with { Items = new[] { item } };
        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector()) with { ClipRows = 1 };

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, values, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Single(contribution.Buffer.Rows);
        Assert.Contains("X", contribution.Buffer.Rows[0].Markup);
    }

    // §4 item 7: ClipRows: 2 with two content rows. Unchanged — 2 content rows, no edges.
    [Fact]
    public void Render_ClipRowsTwo_TwoContentRows_SpendsWholeBudgetOnContentNoEdges()
    {
        var items = new[] { new PaneItem(null, null, null, null, Id: "a"), new PaneItem(null, null, null, null, Id: "b") };
        var values = new Dictionary<string, string?> { ["a"] = new string('X', 20), ["b"] = new string('X', 20) };
        var pane = EmptyLeaf(EmptyBordered) with { Items = items };
        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector()) with { ClipRows = 2 };

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, values, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
        Assert.All(contribution.Buffer.Rows, r => Assert.Contains("X", r.Markup));
    }

    // §4 item 8 (ownDeclaredTiny still wins, unchanged by this task) is already covered by
    // HeightLadderTests.Render_OwnDeclaredTinyMaxRows_KeepsBorderAndDropsContentInstead — that
    // test's pane has maxRows: 2 with a ClipRows: 2 budget, so heightSuppressed is false on both
    // the pre- and post-fix code (ownDeclaredTiny short-circuits it), and omitEdges collapses to
    // heightSuppressed's own false regardless of this task's beneficiary clause. Not duplicated
    // here.

    // §4 item 9: an unbordered pane with zero content rows emits 0 rows before and after — Wrap
    // returns contentRows unchanged whenever border.Style is null, never reaching omitEdges.
    [Fact]
    public void Render_Unbordered_ZeroContentRows_StillEmitsNothing()
    {
        var pane = EmptyLeaf(EmptyBorderless);
        var resolved = SizeResolver.Resolve(pane, 20, Ctx, NoValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var contribution = PaneTreeRenderer.Render(resolved, Ctx, NoValues, EmptyTokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.Equal(0, contribution.Buffer.Rows.Count);
    }
}
