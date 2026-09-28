using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

public class RowLayoutTests
{
    private static Segment Seg(string plain) => new($"<{plain}>", plain);

    private const string Sep = " [dim]|[/] "; // 3 visible columns, per CAPTURE.md's "Wrapping" section

    [Fact]
    public void NoColumnsEnv_EmitsSingleUnwrappedRow()
    {
        var segments = new[] { Seg("a"), Seg("b") };

        var rows = RowLayout.Wrap(segments, availableWidth: null);

        var row = Assert.Single(rows);
        Assert.Equal($"<a>{Sep}<b>", row.Markup);
    }

    [Fact]
    public void UnparsableColumnsEnv_EmitsSingleUnwrappedRow()
    {
        // COLUMNS parsing now happens in SurfaceLayout (see SurfaceLayoutTests); an unparsable
        // COLUMNS reaches RowLayout the same way an unset one does: as a null availableWidth.
        var segments = new[] { Seg("a"), Seg("b") };

        var rows = RowLayout.Wrap(segments, availableWidth: null);

        var row = Assert.Single(rows);
        Assert.Equal($"<a>{Sep}<b>", row.Markup);
    }

    [Fact]
    public void UsableWidthBelow20_FallsBackToSingleRow()
    {
        // avail=19, which is "< 20 usable" per CAPTURE.md/SPEC.md §6.
        var segments = new[] { Seg("aaaaaaaaaa"), Seg("bbbbbbbbbb") }; // 10 + 3 + 10 = 23, would not fit at avail=19 or 20

        var rows = RowLayout.Wrap(segments, availableWidth: 19);

        var row = Assert.Single(rows);
        Assert.Equal($"<aaaaaaaaaa>{Sep}<bbbbbbbbbb>", row.Markup);
    }

    [Fact]
    public void UsableWidth20_PacksNormallyInsteadOfFallback()
    {
        // avail=20, which is NOT "< 20", so normal greedy packing applies. Combined width
        // (10 + 3 + 10 = 23) exceeds 20, so the two segments must split.
        var segments = new[] { Seg("aaaaaaaaaa"), Seg("bbbbbbbbbb") };

        var rows = RowLayout.Wrap(segments, availableWidth: 20);

        Assert.Equal(2, rows.Count);
        Assert.Equal("<aaaaaaaaaa>", rows[0].Markup);
        Assert.Equal("<bbbbbbbbbb>", rows[1].Markup);
    }

    [Fact]
    public void ExactFitBoundary_FitsOnOneRow()
    {
        // Two 12-wide segments + 3-wide separator = 27, exactly avail.
        var segments = new[] { Seg(new string('a', 12)), Seg(new string('b', 12)) };

        var rows = RowLayout.Wrap(segments, availableWidth: 27);

        var row = Assert.Single(rows);
        Assert.Equal($"<{new string('a', 12)}>{Sep}<{new string('b', 12)}>", row.Markup);
    }

    [Fact]
    public void OneOverExactFitBoundary_Splits()
    {
        var segments = new[] { Seg(new string('a', 12)), Seg(new string('b', 12)) };

        var rows = RowLayout.Wrap(segments, availableWidth: 26); // one short of the 27 needed

        Assert.Equal(2, rows.Count);
        Assert.Equal($"<{new string('a', 12)}>", rows[0].Markup);
        Assert.Equal($"<{new string('b', 12)}>", rows[1].Markup);
    }

    [Fact]
    public void OversizedSingleSegment_GetsItsOwnRow_NeverSplit()
    {
        var oversized = new string('x', 50);
        var segments = new[] { Seg(oversized), Seg("small") };

        var rows = RowLayout.Wrap(segments, availableWidth: 21);

        Assert.Equal(2, rows.Count);
        Assert.Equal($"<{oversized}>", rows[0].Markup); // whole 50-char segment intact, not truncated/split
        Assert.Equal("<small>", rows[1].Markup);
    }

    [Fact]
    public void OversizedSegment_24ColumnsAtAvail20_KeepsFullWidthUnsplit()
    {
        // Regression for the COLUMNS 21-24 parity defect (SPEC.md §3): RowLayout's own packing
        // was already correct here — Spectre's Profile.Width re-wrapping this row was the actual
        // bug — but this pins the row's exact width so a future RowLayout change can't silently
        // start splitting it too.
        const string branch = "jimcline/claude-tui-line"; // 24 columns, per the sweep's failing case
        var segments = new[] { Seg(branch) };

        var rows = RowLayout.Wrap(segments, availableWidth: 20);

        var row = Assert.Single(rows);
        Assert.Equal($"<{branch}>", row.Markup);
        Assert.Equal(26, row.Markup.Length); // "<" + 24-char segment + ">" — unsplit despite avail=20
    }

    [Fact]
    public void SeparatorAccounting_ThreeRowsAcrossFiveSegments()
    {
        var w10 = new string('a', 10);
        var segments = Enumerable.Range(0, 5).Select(_ => Seg(w10)).ToArray();

        // avail = 23: two 10-wide segments + 1 separator (3) = 23 fits; a third would need 36.
        var rows = RowLayout.Wrap(segments, availableWidth: 23);

        Assert.Equal(3, rows.Count);
        Assert.Equal($"<{w10}>{Sep}<{w10}>", rows[0].Markup);
        Assert.Equal($"<{w10}>{Sep}<{w10}>", rows[1].Markup);
        Assert.Equal($"<{w10}>", rows[2].Markup);
    }

    [Fact]
    public void BorderReservedWidth_ShrinksPackingBy4_SplitsWhatBorderlessFitsOnOneRow()
    {
        var segments = new[] { Seg(new string('a', 15)), Seg(new string('b', 15)) }; // 15 + 3 + 15 = 33

        var borderless = RowLayout.Wrap(segments, availableWidth: 33); // fits exactly
        var bordered = RowLayout.Wrap(segments, availableWidth: 29);   // border's own 4-column reserve already applied by the caller; no longer fits

        Assert.Single(borderless);
        Assert.Equal(2, bordered.Count);
    }

    [Fact]
    public void IsFallbackWidth_BorderedContentWidth19_IsSuppressed()
    {
        // avail=19, below MinUsableWidth (SPEC.md §6b) — the caller has already subtracted the
        // border's own reserve before this point.
        Assert.True(RowLayout.IsFallbackWidth(availableWidth: 19));
    }

    [Fact]
    public void IsFallbackWidth_BorderedContentWidth20_IsNotSuppressed()
    {
        // avail=20, exactly MinUsableWidth: not fallback.
        Assert.False(RowLayout.IsFallbackWidth(availableWidth: 20));
    }

    [Fact]
    public void IsFallbackWidth_ColumnsUnset_IsSuppressed()
    {
        Assert.True(RowLayout.IsFallbackWidth(availableWidth: null));
    }

    [Fact]
    public void IsFallbackWidth_Columns24_MustUseTheSameAvailableWidthWrapUsed()
    {
        // At the border's own content width (avail=19, after its 4-column reserve) -> fallback.
        // At the borderless width (avail=23) -> NOT fallback. A caller that asked with the wrong
        // one here would suppress nothing and re-wrap the fallback row instead (SPEC.md §3) — the
        // predicate must be asked with the same availableWidth Wrap received, never a mismatched one.
        Assert.True(RowLayout.IsFallbackWidth(availableWidth: 19));
        Assert.False(RowLayout.IsFallbackWidth(availableWidth: 23));
    }

    [Fact]
    public void NoSegments_ReturnsEmptyRowList()
    {
        var rows = RowLayout.Wrap(Array.Empty<Segment>(), availableWidth: 79);
        Assert.Empty(rows);
    }

    // SPEC-2.6-vertical-marker-splice.md §9.7 test 11: with no usable width known at all, Wrap's
    // null-width contract (a single unwrapped row) fires before rowBudget/markerRequired are ever
    // consulted — a caller passing maxContentRows through with COLUMNS unset must not see the
    // budget silently truncate or splice a marker onto the single fallback row.
    [Fact]
    public void NullAvailableWidth_IgnoresRowBudgetEntirely_StillEmitsOneUnwrappedRow()
    {
        var segments = new[] { Seg("a"), Seg("b"), Seg("c") };

        var rows = RowLayout.Wrap(segments, availableWidth: null, rowBudget: 1, ellipsis: "…", markerRequired: true);

        var row = Assert.Single(rows);
        Assert.Equal($"<a>{Sep}<b>{Sep}<c>", row.Markup);
    }
}
