namespace ClaudeTuiLine;

/// <summary>
/// Greedy row-wrapping against the terminal width, per CAPTURE.md's "Wrapping" section.
/// </summary>
public static class RowLayout
{
    private const string SeparatorMarkup = " [dim]|[/] ";

    // Public: SPEC-V2-FRAMEWORK.md §2.3's content-pane intrinsic-width measurement (the width a
    // pane's items would need unwrapped on one row) must reproduce this exact packing arithmetic
    // rather than approximate it — one implementation of "how wide would this pack unwrapped."
    public const int SeparatorWidth = 3;

    // Canonical fallback threshold per SPEC.md §6/§6b (bash-exact, `-lt 20`): below 20 columns
    // of usable width, emit a single unwrapped line.
    public const int MinUsableWidth = 20;

    /// <param name="availableWidth">
    /// The width this call has to pack into, already reduced by whatever the caller's surface
    /// and box arithmetic decided (SPEC.md §6 "MEASURED") — <see cref="RowLayout"/> has no
    /// knowledge of COLUMNS, chromeReserve, or border reserve, and is indifferent to where this
    /// number came from. Null means no usable width is known at all.
    /// </param>
    /// <param name="allowFallback">
    /// SPEC-V2-FRAMEWORK.md §2.6: the single-unwrapped-row fallback is a property of the
    /// surface, not of a pane — it only applies when the surface has exactly one pane. Callers
    /// rendering a pane inside a split pass <c>false</c> so a narrow pane packs and wraps or
    /// truncates like any other pane instead of emitting one overwide row.
    /// </param>
    /// <param name="rowBudget">
    /// SPEC-2.6-vertical-marker-splice.md §9.3: caps the number of rows this call may produce.
    /// Null means unbounded — the original, still-default behaviour. When packing would exceed
    /// the budget, the last row is re-packed against a width reduced by <paramref name="ellipsis"/>
    /// and spliced with the marker via <see cref="SegmentTruncation.Truncate"/>, subject to the
    /// same §2.6 riders as the horizontal axis (<see cref="SegmentTruncation.MarkerFits"/>).
    /// </param>
    /// <param name="markerRequired">
    /// SPEC-2.6-vertical-marker-splice.md §9.1 Q4: true when the pane as a whole is truncated even
    /// though this particular call's own content fits within <paramref name="rowBudget"/> — the
    /// last row must still carry the marker. Ignored when <paramref name="rowBudget"/> is null.
    /// </param>
    public static IReadOnlyList<PaneRow> Wrap(
        IReadOnlyList<Segment> segments,
        int? availableWidth,
        bool allowFallback = true,
        int? rowBudget = null,
        string ellipsis = "",
        bool markerRequired = false)
    {
        var rows = new List<PaneRow>();
        if (segments.Count == 0)
        {
            return rows;
        }

        if (availableWidth is null || (allowFallback && availableWidth < MinUsableWidth))
        {
            var fallbackMarkup = string.Join(SeparatorMarkup, segments.Select(s => s.Markup));
            var fallbackWidth = segments.Sum(s => s.Plain.Length) + SeparatorWidth * (segments.Count - 1);
            rows.Add(new PaneRow(fallbackMarkup, fallbackWidth));
            return rows;
        }

        var width = availableWidth.Value;
        var cap = rowBudget ?? int.MaxValue;
        if (cap <= 0)
        {
            return rows;
        }

        var spliceMarker = rowBudget is not null && SegmentTruncation.MarkerFits(width, ellipsis);
        var contentWidth = spliceMarker ? width - ellipsis.Length : width;

        var i = 0;

        // Rows 0 .. cap-2 pack at FULL width. With no budget, cap-1 == int.MaxValue-1 and this
        // loop consumes everything — which is exactly today's algorithm, unchanged.
        while (i < segments.Count && rows.Count < cap - 1)
        {
            rows.Add(Compose(PackRow(segments, ref i, width)));
        }

        if (i >= segments.Count)
        {
            return rows; // content exhausted before the capped row
        }

        // ---- the capped row (index cap-1). One-row lookahead. ----
        var resume = i;
        var tentative = PackRow(segments, ref i, width);
        var overflowed = i < segments.Count;

        if (!overflowed && !markerRequired)
        {
            rows.Add(Compose(tentative)); // fits in exactly cap rows, nothing lost
            return rows;
        }

        if (!spliceMarker)
        {
            rows.Add(Compose(tentative)); // §2.6 riders: keep cap full rows, no marker
            return rows;
        }

        // Re-pack the capped row against the reduced width, then splice.
        var j = resume;
        var final = PackRow(segments, ref j, contentWidth);
        var last = final.Count - 1;
        var prefixWidth = final.Take(last).Sum(s => s.Plain.Length) + SeparatorWidth * last;
        final[last] = SegmentTruncation.Truncate(final[last], width - prefixWidth, ellipsis);
        rows.Add(Compose(final));
        return rows;
    }

    // Packs one row starting at segments[i], advancing i past everything placed.
    // The FIRST segment is placed unconditionally regardless of width — this preserves the
    // original loop's `if (!rowStarted)` behaviour and guarantees the returned list is never
    // empty.
    private static List<Segment> PackRow(IReadOnlyList<Segment> segments, ref int i, int width)
    {
        var placed = new List<Segment> { segments[i] };
        var rowWidth = segments[i].Plain.Length;
        i++;
        while (i < segments.Count && rowWidth + SeparatorWidth + segments[i].Plain.Length <= width)
        {
            rowWidth += SeparatorWidth + segments[i].Plain.Length;
            placed.Add(segments[i]);
            i++;
        }

        return placed;
    }

    // The arithmetic here must match PackRow's incremental accumulation exactly — see the
    // same requirement on the single-row fallback above.
    private static int WidthOf(IReadOnlyList<Segment> placed) =>
        placed.Sum(s => s.Plain.Length) + SeparatorWidth * (placed.Count - 1);

    private static PaneRow Compose(IReadOnlyList<Segment> placed) =>
        new(string.Join(SeparatorMarkup, placed.Select(s => s.Markup)), WidthOf(placed));

    /// <summary>
    /// True when <see cref="Wrap"/> would take its single-line fallback for the given
    /// <paramref name="availableWidth"/> — null, or below <see cref="MinUsableWidth"/>. Callers
    /// that render a bordered container around the wrapped rows (SPEC.md §6b's narrow-width
    /// suppression) consult this instead of duplicating the threshold, and must pass the same
    /// <paramref name="availableWidth"/> they gave <see cref="Wrap"/>.
    /// </summary>
    public static bool IsFallbackWidth(int? availableWidth) => availableWidth is null || availableWidth < MinUsableWidth;
}
