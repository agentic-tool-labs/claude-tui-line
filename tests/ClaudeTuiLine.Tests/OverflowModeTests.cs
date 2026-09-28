using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: the three overflow modes govern only a single segment wider than
/// the pane's own inner width, and two traps are non-negotiable wherever a segment is split —
/// break only on ANSI-stripped (<c>Plain</c>) text, and re-emit style on every continuation row.
/// </summary>
public class OverflowModeTests
{
    private static string Stripped(string markup) => Spectre.Console.Markup.Remove(markup);

    private static readonly int[] SweptWidths = { 20, 21, 24, 30, 40, 60, 80, 100, 160, 200 };

    // §13.2 (defect 16): a lone surrogate in rendered text means a cut fell inside a pair.
    private static void AssertNoLoneSurrogates(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                Assert.True(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]), $"lone high surrogate at index {i} in \"{s}\"");
            }
            else if (char.IsLowSurrogate(s[i]))
            {
                Assert.True(i > 0 && char.IsHighSurrogate(s[i - 1]), $"lone low surrogate at index {i} in \"{s}\"");
            }
        }
    }

    [Fact]
    public void Truncate_OversizedSegment_ClipsToWidth_EndsWithEllipsis()
    {
        var items = new[] { new Segment("[red]abcdefghijklmnopqrstuvwxyz[/]", "abcdefghijklmnopqrstuvwxyz") };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 10, OverflowMode.Truncate, ellipsis: "…", new RenderNoteCollector());

        var row = Assert.Single(buffer.Rows);
        Assert.Equal(10, row.Width);
        Assert.EndsWith("…", Stripped(row.Markup));
        Assert.Equal("abcdefghi…", Stripped(row.Markup));
    }

    [Fact]
    public void Wrap_OversizedSegment_ChunksAcrossContinuationRows_ReconstructsOriginalTextExactly()
    {
        const string original = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ9876543210";
        var items = new[] { new Segment($"[red]{original}[/]", original) };

        // innerWidth must be >= RowLayout's own 20-column usable-width floor: below that, Wrap's
        // pre-existing single-overwide-fallback-row behavior (RowLayout.cs, unmodified in Phase 2)
        // re-joins already-chunked segments back into one row, which is a separate, accepted edge
        // case (see the swept-width tests below), not what this test is exercising.
        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 24, OverflowMode.Wrap, ellipsis: "…", new RenderNoteCollector());

        Assert.True(buffer.Rows.Count > 1, "expected the oversized segment to be chunked across more than one row");
        Assert.All(buffer.Rows, row => Assert.True(row.Width <= 24, $"row {row.Width} wide exceeds innerWidth 24"));

        var reconstructed = string.Concat(buffer.Rows.Select(r => Stripped(r.Markup)));
        Assert.Equal(original, reconstructed);
    }

    [Fact]
    public void Overflow_OversizedSegment_IsLiteralPassthrough_MatchesRowLayoutWrapDirectly()
    {
        var items = new[]
        {
            new Segment("claude-tui-line", "claude-tui-line"),
            new Segment("[dim]main[/]", "main"),
            new Segment("[red]abcdefghijklmnopqrstuvwxyz0123456789[/]", "abcdefghijklmnopqrstuvwxyz0123456789"),
        };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 20, OverflowMode.Overflow, ellipsis: "…", new RenderNoteCollector());
        var expected = RowLayout.Wrap(items, 20);

        Assert.Equal(expected, buffer.Rows);
    }

    [Fact]
    public void SameOversizedInput_ThreeModesProduceDistinctBehavior()
    {
        const string original = "abcdefghijklmnopqrstuvwxyz0123456789";
        var items = new[] { new Segment($"[red]{original}[/]", original) };
        const int width = 24; // >= RowLayout's 20-column usable-width floor; see the chunking test above.

        var wrap = PaneRenderer.RenderLeaf(items, width, OverflowMode.Wrap, "…", new RenderNoteCollector());
        var truncate = PaneRenderer.RenderLeaf(items, width, OverflowMode.Truncate, "…", new RenderNoteCollector());
        var overflow = PaneRenderer.RenderLeaf(items, width, OverflowMode.Overflow, "…", new RenderNoteCollector());

        Assert.True(wrap.Rows.Count > 1); // nothing lost, spread across rows
        Assert.Single(truncate.Rows);
        Assert.True(truncate.Rows[0].Width <= width); // cut to fit
        Assert.Single(overflow.Rows);
        Assert.True(overflow.Rows[0].Width > width); // v1-identical: emitted whole, spills past the pane
        Assert.Equal(original, Stripped(overflow.Rows[0].Markup));
    }

    // --- Trap 1: break only on ANSI-stripped text (never mid-escape-sequence, never at the wrong
    // visible-character offset because the break was computed against the styled string). ---

    [Fact]
    public void Wrap_Trap_BreaksOnPlainTextOnly_NotOnStyledMarkup()
    {
        const string original = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ9876543210";
        var items = new[] { new Segment($"[bold red]{original}[/]", original) };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 24, OverflowMode.Wrap, ellipsis: "…", new RenderNoteCollector());

        var expectedChunks = Enumerable.Range(0, (original.Length + 23) / 24)
            .Select(i => original.Substring(i * 24, Math.Min(24, original.Length - i * 24)));

        Assert.Equal(expectedChunks, buffer.Rows.Select(r => Stripped(r.Markup)));
    }

    // --- Trap 2: style is re-emitted on every continuation row, not just the first chunk. ---

    [Fact]
    public void Wrap_Trap_ReEmitsStyleOnEveryContinuationRow()
    {
        const string original = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ9876543210";
        var items = new[] { new Segment($"[red]{original}[/]", original) };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 24, OverflowMode.Wrap, ellipsis: "…", new RenderNoteCollector());

        Assert.True(buffer.Rows.Count > 1);
        Assert.All(buffer.Rows, row =>
        {
            Assert.StartsWith("[red]", row.Markup);
            Assert.EndsWith("[/]", row.Markup);
        });
    }

    // --- ellipsis edge cases ---

    [Fact]
    public void Truncate_EmptyEllipsis_HardClipsWithNoMarker()
    {
        var items = new[] { new Segment("abcdefghijklmnopqrstuvwxyz", "abcdefghijklmnopqrstuvwxyz") };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 10, OverflowMode.Truncate, ellipsis: "", new RenderNoteCollector());

        var row = Assert.Single(buffer.Rows);
        Assert.Equal("abcdefghij", Stripped(row.Markup));
    }

    [Fact]
    public void Truncate_MarkerWiderThanInnerWidth_DroppedEntirely_HardClip()
    {
        var items = new[] { new Segment("abcdefghijklmnopqrstuvwxyz", "abcdefghijklmnopqrstuvwxyz") };

        // ellipsis is 3 chars wide ("..."), innerWidth is only 2 -> innerWidth <= ellipsis.Length.
        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 2, OverflowMode.Truncate, ellipsis: "...", new RenderNoteCollector());

        var row = Assert.Single(buffer.Rows);
        Assert.Equal("ab", Stripped(row.Markup)); // hard clip, marker dropped, no sacrificed cell
    }

    [Fact]
    public void Truncate_ZeroInnerWidth_ProducesEmptyRow()
    {
        var items = new[] { new Segment("abcdefgh", "abcdefgh") };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 0, OverflowMode.Truncate, ellipsis: "…", new RenderNoteCollector());

        var row = Assert.Single(buffer.Rows);
        Assert.Equal(string.Empty, Stripped(row.Markup));
    }

    // --- surrogate-pair cut safety (§13.2, defect 16): a cut index must never fall between a
    // UTF-16 high surrogate and its trailing low surrogate, or the pair splits into two lone
    // surrogates — invalid UTF-16 on the wire, not just a clipped glyph. ---

    [Fact]
    public void Wrap_SurrogatePairAtChunkBoundary_StaysIntactInOneRow()
    {
        const string emoji = "\U0001F600"; // 😀 — one character, two UTF-16 code units.

        // 23 'a's fill indices 0-22, so the emoji's high surrogate lands at index 23: the last
        // unit of a naive 24-wide chunk, with the low surrogate one past it.
        var original = new string('a', 23) + emoji + new string('b', 10);
        var items = new[] { new Segment(original, original) };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 24, OverflowMode.Wrap, ellipsis: "…", new RenderNoteCollector());

        var reconstructed = string.Concat(buffer.Rows.Select(r => Stripped(r.Markup)));
        Assert.Equal(original, reconstructed);

        foreach (var row in buffer.Rows)
        {
            AssertNoLoneSurrogates(Stripped(row.Markup));
        }

        Assert.Contains(buffer.Rows, row => Stripped(row.Markup).Contains(emoji));
    }

    [Fact]
    public void Truncate_SurrogatePairAtCutBoundary_StaysIntact()
    {
        const string emoji = "\U0001F600"; // 😀 — one character, two UTF-16 code units.

        // ellipsis is 1 char wide, innerWidth=10 -> contentBudget=9, which would ordinarily cut
        // exactly between the pair: the high surrogate (index 8) is the last unit of a naive
        // 9-wide clip, with the low surrogate (index 9) one past it.
        var original = new string('a', 8) + emoji + "cdefgh";
        var items = new[] { new Segment(original, original) };

        var buffer = PaneRenderer.RenderLeaf(items, innerWidth: 10, OverflowMode.Truncate, ellipsis: "…", new RenderNoteCollector());

        var row = Assert.Single(buffer.Rows);
        var text = Stripped(row.Markup);
        AssertNoLoneSurrogates(text);
        Assert.Equal(new string('a', 8) + emoji + "…", text);
    }

    // --- width-invariant sweeps: wrap/truncate never exceed innerWidth, excluding RowLayout's own
    // <20-usable-width single-overwide-fallback-row regime (RowLayout.cs is unmodified in Phase 2,
    // and that fallback can override the "never exceeds width" guarantee at very narrow widths —
    // Phase 1's own tests already exclude it the same way via IsFallbackWidth). ---

    public static IEnumerable<object[]> SweptWidthsData() => SweptWidths.Select(w => new object[] { w });

    [Theory]
    [MemberData(nameof(SweptWidthsData))]
    public void Truncate_NeverExceedsInnerWidth_AcrossSweptWidths(int innerWidth)
    {
        if (RowLayout.IsFallbackWidth(innerWidth))
        {
            return;
        }

        var items = RenderInvariantTests.FixtureWithOversizedSegment();
        var buffer = PaneRenderer.RenderLeaf(items, innerWidth, OverflowMode.Truncate, "…", new RenderNoteCollector());

        Assert.All(buffer.Rows, row => Assert.True(row.Width <= innerWidth, $"width={innerWidth}: row {row.Width} wide"));
    }

    [Theory]
    [MemberData(nameof(SweptWidthsData))]
    public void Wrap_NeverExceedsInnerWidth_AcrossSweptWidths(int innerWidth)
    {
        if (RowLayout.IsFallbackWidth(innerWidth))
        {
            return;
        }

        var items = RenderInvariantTests.FixtureWithOversizedSegment();
        var buffer = PaneRenderer.RenderLeaf(items, innerWidth, OverflowMode.Wrap, "…", new RenderNoteCollector());

        Assert.All(buffer.Rows, row => Assert.True(row.Width <= innerWidth, $"width={innerWidth}: row {row.Width} wide"));
    }
}
