namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: renders a leaf pane's items to a <see cref="PaneBuffer"/>.
/// Overflow modes govern only a single segment wider than the pane's own inner width — general
/// multi-segment row packing is untouched and stays on the unmodified <see cref="RowLayout.Wrap"/>
/// in every mode. <c>overflow</c> mode is therefore a literal passthrough to
/// <see cref="RowLayout.Wrap"/>, which is what makes it parity-preserving by construction
/// (§2.7's golden gate exercises exactly this path).
/// </summary>
public static class PaneRenderer
{
    public static PaneBuffer RenderLeaf(
        IReadOnlyList<Segment> items, int? innerWidth, OverflowMode overflow, string ellipsis,
        RenderNoteCollector notes, bool allowFallback = true,
        int? rowBudget = null, bool markerRequired = false)
    {
        if (innerWidth is not int width)
        {
            // No COLUMNS: RowLayout.Wrap's own null-width contract (single unwrapped row)
            // applies identically regardless of overflow mode — there is no pane width to
            // measure "wider than the pane" against.
            return new PaneBuffer(RowLayout.Wrap(items, null, allowFallback, rowBudget, ellipsis, markerRequired));
        }

        var prepared = overflow switch
        {
            OverflowMode.Truncate => items
                .Select(s =>
                {
                    if (s.Plain.Length <= width)
                    {
                        return s;
                    }

                    notes.Add($"segment truncated to fit {width} columns");
                    return SegmentTruncation.Truncate(s, width, ellipsis);
                })
                .ToList(),
            OverflowMode.Wrap => items
                .SelectMany(s => s.Plain.Length > width ? SegmentTruncation.WrapToWidth(s, width) : new List<Segment> { s })
                .ToList(),
            _ => items, // Overflow: v1-identical, oversized segments pass through untouched.
        };

        return new PaneBuffer(RowLayout.Wrap(prepared, width, allowFallback, rowBudget, ellipsis, markerRequired));
    }
}
