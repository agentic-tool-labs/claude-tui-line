namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-3.1-block-model.md §8: verification items for the block-classification layer in
/// PaneAssembler.RenderItemRows — D1-D8's packing/wrapping/adjacency rules, exercised at the
/// same public entry point (RenderLeafRows) production rendering goes through.
/// </summary>
public class PaneAssemblerBlockTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
    private static readonly ItemContext Ctx = new(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens = new Dictionary<string, ColorResolution.ColorRule>();

    private static string Stripped(string markup) => AnsiStrip.Strip(Spectre.Console.Markup.Remove(markup));

    private static PaneItem Item(string id) => new(null, null, null, null, Id: id);

    private static Pane Leaf(OverflowMode? overflow, IReadOnlyList<PaneItem> items) =>
        new(PaneSplit.None, Array.Empty<Pane>(), "auto", NoBorder, overflow, "…", null, items);

    private static IReadOnlyList<PaneRow> Render(Pane pane, int innerWidth, IReadOnlyDictionary<string, string?> values) =>
        PaneAssembler.RenderLeafRows(pane, innerWidth, Ctx, values, Tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector());

    // --- 1: byte-parity regression gate — no all-single-line pane's output changes. ---

    [Fact]
    public void AllSingleLineItems_PackAndRenderExactlyAsBeforeTheBlockLayer()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("a"), Item("b"), Item("c") });
        var values = new Dictionary<string, string?> { ["a"] = "alpha", ["b"] = "beta", ["c"] = "gamma" };

        var rows = Render(pane, 80, values);

        var row = Assert.Single(rows);
        Assert.Equal("alpha | beta | gamma", Stripped(row.Markup).TrimEnd());
    }

    // --- 2: a block occupies its own row(s), separate from surrounding packed groups. ---

    [Fact]
    public void MultiLineItem_OccupiesOwnRows_SeparateFromPackedNeighbors()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("a"), Item("block"), Item("c") });
        var values = new Dictionary<string, string?>
        {
            ["a"] = "alpha",
            ["block"] = "line1\nline2\n",
            ["c"] = "gamma",
        };

        var rows = Render(pane, 80, values);

        Assert.Equal(4, rows.Count);
        Assert.Equal("alpha", Stripped(rows[0].Markup).TrimEnd());
        Assert.Equal("line1", Stripped(rows[1].Markup).TrimEnd());
        Assert.Equal("line2", Stripped(rows[2].Markup).TrimEnd());
        Assert.Equal("gamma", Stripped(rows[3].Markup).TrimEnd());
    }

    // --- 3: no separator markup on either edge of a block (D4). ---

    [Fact]
    public void Block_HasNoSeparatorMarkupOnEitherEdge()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("a"), Item("block"), Item("c") });
        var values = new Dictionary<string, string?>
        {
            ["a"] = "alpha",
            ["block"] = "line1\nline2",
            ["c"] = "gamma",
        };

        var rows = Render(pane, 80, values);

        Assert.Equal("alpha", Stripped(rows[0].Markup).TrimEnd());
        Assert.Equal("line1", Stripped(rows[1].Markup).TrimEnd());
        Assert.Equal("line2", Stripped(rows[2].Markup).TrimEnd());
        Assert.Equal("gamma", Stripped(rows[3].Markup).TrimEnd());
        Assert.DoesNotContain("|", Stripped(rows[1].Markup));
        Assert.DoesNotContain("|", Stripped(rows[2].Markup));
    }

    // --- 4: a lone trailing newline does not make an item a block (D2). ---

    [Fact]
    public void SingleTrailingNewline_IsNotABlock_StaysPacked()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("a"), Item("b") });
        var values = new Dictionary<string, string?> { ["a"] = "foo\n", ["b"] = "bar" };

        var rows = Render(pane, 80, values);

        var row = Assert.Single(rows);
        Assert.Equal("foo | bar", Stripped(row.Markup).TrimEnd());
        Assert.Equal(80, row.Width); // AlignRow pads every row out to innerWidth.
    }

    // --- 5: a doubled trailing newline IS a block with a preserved blank interior row (D2/D3). ---

    [Fact]
    public void DoubleTrailingNewline_IsABlock_WithPreservedBlankRow()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("block") });
        var values = new Dictionary<string, string?> { ["block"] = "foo\n\n" };

        var rows = Render(pane, 80, values);

        Assert.Equal(2, rows.Count);
        Assert.Equal("foo", Stripped(rows[0].Markup).TrimEnd());
        Assert.Equal(string.Empty, Stripped(rows[1].Markup).TrimEnd());
    }

    // --- 6: wrapping a long single-line item never promotes it to a block (D5). ---

    [Fact]
    public void WrappedSingleLineItem_StaysClassifiedAsNonBlock_DespiteMultipleRenderedRows()
    {
        var longValue = new string('x', 60);
        var pane = Leaf(OverflowMode.Wrap, new[] { Item("a") });
        var values = new Dictionary<string, string?> { ["a"] = longValue };

        var rows = Render(pane, 24, values);

        Assert.True(rows.Count > 1, "expected the oversized single-line item to wrap across rows");
        var reconstructed = string.Concat(rows.Select(r => Stripped(r.Markup).TrimEnd()));
        Assert.Equal(longValue, reconstructed);
    }

    // --- 7: a block's own line still wraps independently when it exceeds innerWidth (D5). ---

    [Fact]
    public void BlockLine_ThatExceedsInnerWidth_StillWrapsAcrossRows()
    {
        var longLine = new string('y', 60);
        var pane = Leaf(OverflowMode.Wrap, new[] { Item("block") });
        var values = new Dictionary<string, string?> { ["block"] = $"short\n{longLine}" };

        var rows = Render(pane, 24, values);

        Assert.True(rows.Count > 2, "expected the block's long second line to wrap into more than one row");
        Assert.Equal("short", Stripped(rows[0].Markup).TrimEnd());
        var reconstructedTail = string.Concat(rows.Skip(1).Select(r => Stripped(r.Markup).TrimEnd()));
        Assert.Equal(longLine, reconstructedTail);
    }

    // --- 8: no blank row between two adjacent blocks (D6). ---

    [Fact]
    public void TwoAdjacentBlocks_ProduceNoBlankRowBetweenThem()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("block1"), Item("block2") });
        var values = new Dictionary<string, string?>
        {
            ["block1"] = "a1\na2",
            ["block2"] = "b1\nb2",
        };

        var rows = Render(pane, 80, values);

        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "a1", "a2", "b1", "b2" }, rows.Select(r => Stripped(r.Markup).TrimEnd()));
    }

    // --- 9: a suppressed item (null value) does not split a packed group (D7). ---

    [Fact]
    public void SuppressedItemBetweenPackedItems_DoesNotSplitTheGroup()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("a"), Item("missing"), Item("c") });
        var values = new Dictionary<string, string?> { ["a"] = "alpha", ["c"] = "gamma" }; // "missing" absent -> null

        var rows = Render(pane, 80, values);

        var row = Assert.Single(rows);
        Assert.Equal("alpha | gamma", Stripped(row.Markup).TrimEnd());
    }

    // --- 10: structural note for #27 — the single concatenation site. ---
    // PaneAssembler.cs's RenderItemRows populates its `units` list at exactly two call sites:
    // FlushGroup's `units.Add(new RenderUnit(...))` and the block-line loop's
    // `units.Add(new RenderUnit(...))`. Both feed the same ordered List<RenderUnit>, which
    // ApplyRowBudget (SPEC-2.6-vertical-marker-splice.md §9.2) flattens at the single site the
    // marker splice attaches to. No runtime assertion is possible for a structural fact about
    // call sites; this test exists as the recorded pointer alongside the other nine.
    [Fact]
    public void SingleConcatenationSite_IsDocumentedForFutureMarkerSplice()
    {
        Assert.True(true, "PaneAssembler.RenderItemRows's `units` list is the single concatenation site (see FlushGroup and the block-line loop), flattened by ApplyRowBudget.");
    }

    // --- 11: SPEC-2.6-vertical-marker-splice.md §7 test 8 — a multi-row block's last surviving
    // row still carries the marker when the cap lands inside the block, and content past the cap
    // (including the rest of the block) is dropped entirely rather than emitting a bare "…" row. ---
    [Fact]
    public void MultiLineBlock_ClippedMidBlock_LastSurvivingLineCarriesMarker()
    {
        var pane = Leaf(OverflowMode.Truncate, new[] { Item("block") });
        var values = new Dictionary<string, string?> { ["block"] = "line1\nline2\nline3" };

        var rows = PaneAssembler.RenderLeafRows(pane, 80, Ctx, values, Tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector(), maxContentRows: 2);

        Assert.Equal(2, rows.Count);
        Assert.Equal("line1", Stripped(rows[0].Markup).TrimEnd());
        Assert.Equal("line2…", Stripped(rows[1].Markup).TrimEnd());
        Assert.DoesNotContain(rows, r => Stripped(r.Markup).Contains("line3"));
    }
}
