namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-85-ADDENDUM-spans-threading.md §12.10: verifies Spans actually reaches the production
/// Segment PaneAssembler builds for a compound item, exercised through PaneAssembler.RenderItemRows
/// (not by hand-constructing a Segment) — the acceptance gate for the addendum's Gap 1 (D-A/D-B/D-C).
/// </summary>
public class PaneAssemblerSpansTests
{
    private static readonly ItemContext Ctx = new(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens = new Dictionary<string, ColorResolution.ColorRule>();

    private static string Stripped(string markup) => AnsiStrip.Strip(Spectre.Console.Markup.Remove(markup));

    private static PaneItem CompoundItem(string? color = "grey", string? link = null) => new(
        Item: null, Format: null, Color: color is null ? null : new ColorResolution.ColorExpr.Literal(color), Overflow: null,
        Id: "agent-badge", Link: link,
        Parts: new[]
        {
            new PaneItemPart(Text: "agent:", Item: null, From: null, Extract: null, Case: null, Format: null,
                Color: new ColorResolution.ColorExpr.Literal("grey")),
            new PaneItemPart(Text: null, Item: null, From: "agent", Extract: "[^:]+$", Case: "upper", Format: null,
                Color: new ColorResolution.ColorExpr.Literal("aqua")),
        });

    private static IReadOnlyDictionary<string, string?> Values => new Dictionary<string, string?> { ["agent"] = "team:worker-7" };

    // §12.10 item 1: this test must fail against the pre-addendum tree (ItemDecision carried no
    // Spans, and PaneAssembler's single-line path called the 3-arg BuildItemSegment, which never
    // received them) and pass once Spans is threaded through.
    [Fact]
    public void CompoundItem_ProductionSegment_CarriesPerPartSpans()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var segment = Assert.Single(Assert.Single(units).Segments);
        Assert.NotNull(segment.Spans);
        Assert.Equal(2, segment.Spans!.Count);
        Assert.Equal("agent:", segment.Spans[0].Plain);
        Assert.Equal("WORKER-7", segment.Spans[1].Plain);
        Assert.Equal(string.Concat(segment.Spans.Select(s => s.Plain)), segment.Plain);
    }

    // D-B: the item-level `color: grey` must not wrap the whole compound a second time — it is
    // already consumed per-part as each part's default, so the outer wrap must be absent, not
    // merely redundant.
    [Fact]
    public void CompoundItem_ItemLevelColor_DoesNotDoubleWrapOutsideParts()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var segment = Assert.Single(Assert.Single(units).Segments);

        Assert.Equal(string.Concat(segment.Spans!.Select(s => s.Markup)), segment.Markup);
        Assert.Equal("agent:WORKER-7", Stripped(segment.Markup));
    }

    // D-C: a `link` on the compound wraps the whole thing from outside, per §12.3's revised
    // invariant ("modulo an OSC 8 link wrap") — Spans still decomposes the pre-link style markup.
    [Fact]
    public void CompoundItem_WithLink_WrapsWholeCompound_SpansSurviveUnderneath()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem(link: "https://example/{}") });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var segment = Assert.Single(Assert.Single(units).Segments);

        // {} substitutes the compound's own resolved value — its concatenated Plain (LeafItems.cs:41).
        Assert.True(OscHyperlink.TryUnwrap(segment.Markup, out var url, out var inner));
        Assert.Equal("https://example/agent:WORKER-7", url);
        Assert.NotNull(segment.Spans);
        Assert.Equal(string.Concat(segment.Spans!.Select(s => s.Markup)), inner);
    }

    // D-F (found via the real-CLI narrow-width acceptance check, not hand-built spans): LeafItems.cs
    // used to build each part's StyledSpan.Markup via SegmentBuilder.BuildItemSegment(text, color),
    // which bakes an escaped raw SGR reset after the text — breaking
    // SegmentTruncation.TryGetSimpleWrap's exact "[/]"-suffix match and silently dropping the
    // colour of any span whose truncation cut lands inside it (as opposed to a span that survives
    // whole, which keeps its original markup unrestyled and so masked the bug).
    [Fact]
    public void CompoundItem_TruncatedProductionSegment_BothSpansKeepColour()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var segment = Assert.Single(Assert.Single(units).Segments);

        // "agent:" (6) + "WORKER-7" (8) = 14 plain chars; innerWidth 9 with a 1-wide ellipsis
        // cuts at plain index 8 — inside the second (aqua) span, forcing RestyleSlice's
        // partial-slice path rather than its whole-span-survives shortcut.
        var truncated = SegmentTruncation.Truncate(segment, innerWidth: 9, ellipsis: "…");

        Assert.NotNull(truncated.Spans);
        Assert.Equal(3, truncated.Spans!.Count);
        Assert.Contains("[grey]", truncated.Spans[0].Markup);
        Assert.Contains("[aqua]", truncated.Spans[1].Markup);
    }

    // SPEC-87 §12.1: an item elsewhere in the tree selecting a compound by id resolves it via
    // the whole-tree compound map, not a same-pane-only lookup.
    [Fact]
    public void ItemSelector_InAnotherPane_ResolvesCompoundFromWholeTreeMap()
    {
        var declaringPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });
        var selectingItem = new PaneItem(Item: "agent-badge", Format: null, Color: null, Overflow: null);
        var selectingPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { selectingItem });
        var root = new Pane(PaneSplit.Horizontal, new[] { declaringPane, selectingPane }, "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            null, "…", null, Array.Empty<PaneItem>());

        var compounds = LeafItems.BuildCompoundMap(root, Values, Ctx, Tokens);
        var resolved = LeafItems.Resolve(new[] { selectingItem }, Values, Ctx, compounds, Tokens);

        var item = Assert.Single(resolved);
        Assert.NotNull(item.Display?.Spans);
        Assert.Equal(2, item.Display!.Spans!.Count);
        Assert.Equal("agent:", item.Display.Spans[0].Plain);
        Assert.Equal("WORKER-7", item.Display.Spans[1].Plain);
        Assert.Equal(string.Concat(item.Display.Spans.Select(s => s.Plain)), item.Display.Plain);
    }

    // SPEC-87 §12.4: the ordinary values-dictionary lookup happens before the compounds fallback
    // is even consulted, so a registry/command id with no collision resolves exactly as before.
    [Fact]
    public void CommandIdSelector_WithNoCollision_ResolvesOrdinarilyWithoutCompoundsFallback()
    {
        var item = new PaneItem(null, null, null, null, Id: "cmd-x");
        var values = new Dictionary<string, string?> { ["cmd-x"] = "ordinary-output" };
        var decoyCompounds = new Dictionary<string, Segment>
        {
            ["cmd-x"] = SegmentBuilder.BuildCompoundSegment(new[] { new StyledSpan("decoy", "[red]decoy[/]") }),
        };

        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, decoyCompounds, Tokens);

        var resolvedItem = Assert.Single(resolved);
        Assert.Equal("ordinary-output", resolvedItem.Value);
        Assert.Null(resolvedItem.Display?.Spans);
        Assert.DoesNotContain("decoy", resolvedItem.Display?.Markup ?? string.Empty);
    }

    // SPEC-87 §12.4: ConfigChecker has no duplicate-id diagnostic, so a compound and an ordinary
    // item can structurally share one id. LeafItems.Resolve's own lookup order — ordinary
    // `values` before the compounds fallback — is what guarantees the ordinary value wins.
    [Fact]
    public void CollidingId_BetweenOrdinaryValueAndCompoundsMap_OrdinaryValueWins()
    {
        var item = new PaneItem(null, null, null, null, Id: "shared-id");
        var values = new Dictionary<string, string?> { ["shared-id"] = "ordinary-value" };
        var collidingCompounds = new Dictionary<string, Segment>
        {
            ["shared-id"] = SegmentBuilder.BuildCompoundSegment(new[] { new StyledSpan("compound-value", "[red]compound-value[/]") }),
        };

        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, collidingCompounds, Tokens);

        var resolvedItem = Assert.Single(resolved);
        Assert.Equal("ordinary-value", resolvedItem.Value);
    }

    // SPEC-87 §12.3/§12.4: a compound whose every value-part is empty is omitted from
    // BuildCompoundMap entirely, so an item elsewhere selecting it resolves to null exactly
    // like §2.3 suppression — and no diagnostic is warranted for a legitimately empty result.
    [Fact]
    public void SuppressedCompound_SelectedFromAnotherPane_ResolvesToNullValueAndDisplay()
    {
        var emptyPart = new PaneItemPart(Text: null, Item: null, From: "missing-source", Extract: null, Case: null, Format: null, Color: null);
        var declaringItem = new PaneItem(Item: null, Format: null, Color: null, Overflow: null, Id: "empty-badge", Parts: new[] { emptyPart });
        var declaringPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { declaringItem });
        var root = new Pane(PaneSplit.Horizontal, new[] { declaringPane }, "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            null, "…", null, Array.Empty<PaneItem>());

        var values = new Dictionary<string, string?>();
        var compounds = LeafItems.BuildCompoundMap(root, values, Ctx, Tokens);
        Assert.Empty(compounds);

        var selectingItem = new PaneItem(Item: "empty-badge", Format: null, Color: null, Overflow: null);
        var resolved = LeafItems.Resolve(new[] { selectingItem }, values, Ctx, compounds, Tokens);

        var resolvedItem = Assert.Single(resolved);
        Assert.Null(resolvedItem.Value);
        Assert.Null(resolvedItem.Display);
    }

    // SPEC-87 §12.9.1: the selecting item's own colour is a floor — it fills in only where a
    // span carries no colour of its own already, whether that colour came from an explicit
    // part-level `color` or from a value-derived threshold rule.
    [Fact]
    public void CompoundColorFloor_FillsOnlyPartsWithNoColourOfTheirOwn()
    {
        var explicitPart = new PaneItemPart(Text: "P1:", Item: null, From: null, Extract: null, Case: null, Format: null,
            Color: new ColorResolution.ColorExpr.Literal("blue"));
        var thresholdRule = new ColorResolution.ColorRule(
            Thresholds: new[] { new ColorResolution.ThresholdRule(50, new ColorResolution.ColorValue.Literal("maroon")) },
            Match: null,
            Default: new ColorResolution.ColorValue.Literal("olive"),
            From: "pct");
        var thresholdPart = new PaneItemPart(Text: null, Item: null, From: "pct", Extract: null, Case: null, Format: null,
            Color: new ColorResolution.ColorExpr.Inline(thresholdRule));
        var unstyledPart = new PaneItemPart(Text: "P3", Item: null, From: null, Extract: null, Case: null, Format: null, Color: null);

        var declaringItem = new PaneItem(Item: null, Format: null, Color: null, Overflow: null, Id: "badge",
            Parts: new[] { explicitPart, thresholdPart, unstyledPart });
        var selectingItem = new PaneItem(Item: "badge", Format: null, Color: new ColorResolution.ColorExpr.Literal("red"), Overflow: null);

        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { declaringItem, selectingItem });

        var values = new Dictionary<string, string?> { ["pct"] = "75" };
        var compounds = LeafItems.BuildCompoundMap(pane, values, Ctx, Tokens);
        var resolved = LeafItems.Resolve(new[] { selectingItem }, values, Ctx, compounds, Tokens);

        var item = Assert.Single(resolved);
        var spans = item.Display!.Spans!;
        Assert.Equal(3, spans.Count);
        Assert.Equal("[blue]P1:[/]", spans[0].Markup);
        Assert.Equal("[maroon]75[/]", spans[1].Markup);
        Assert.Equal("[red]P3[/]", spans[2].Markup);
    }
}
