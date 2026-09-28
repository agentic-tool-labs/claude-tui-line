namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.1: renders one leaf pane's content — the default 14-segment list when
/// <c>items</c> is empty, or the resolved <c>items</c> sequence otherwise — to rows exactly
/// <paramref name="innerWidth"/> wide. A pane's content is a vertical sequence of packed
/// single-row groups, in config order. Every pane rendered through this path is inside a split, so the
/// §2.6 single-line fallback never applies here (<c>allowFallback: false</c>) — narrowness is not
/// special, a narrow pane just wraps or truncates like any other.
/// </summary>
public static class PaneAssembler
{
    // SPEC-2.6-vertical-marker-splice.md §9.2.1: one PaneRenderer.RenderLeaf invocation's
    // contribution to the pane's row list. Segments is the exact list handed to RenderLeaf,
    // retained so ApplyRowBudget can re-invoke it with a row budget without re-deriving its
    // input — the provenance SPEC-3.1 §6 requires.
    internal readonly record struct RenderUnit(IReadOnlyList<Segment> Segments, IReadOnlyList<PaneRow> Rows);

    public static IReadOnlyList<PaneRow> RenderLeafRows(
        Pane pane,
        int innerWidth,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds,
        RenderNoteCollector notes,
        int? maxContentRows = null,
        bool itemsEmptied = false)
    {
        // §2.8.1/§2.8.2 addendum: a pane the ladder emptied (rung 3 took its last item) renders
        // zero content rows, not the "author declared nothing" default-segments fallback — the two
        // look identical on Items.Count alone, so the caller passes itemsEmptied to tell them apart.
        var units = pane.Items.Count == 0
            ? (itemsEmptied ? Array.Empty<RenderUnit>() : RenderDefaultRows(pane, innerWidth, ctx, notes))
            : RenderItemRows(pane, innerWidth, ctx, values, tokens, compounds, notes);

        var rawRows = ApplyRowBudget(units, maxContentRows, pane, innerWidth, notes);

        return rawRows.Select(row => AlignRow(row, innerWidth, pane.Align)).ToList();
    }

    // SPEC-2.6-vertical-marker-splice.md §9.2.2: the single place `units` is flattened. With no
    // budget, or with content that already fits, this is a plain concatenation — the
    // overwhelmingly common path, byte-identical to before. Only when `units`' rows exceed `cap`
    // does the owning unit (the one whose rows straddle the boundary) get re-invoked with a row
    // budget so the marker splices while its content is still Segments inside RowLayout.Wrap.
    private static IReadOnlyList<PaneRow> ApplyRowBudget(
        IReadOnlyList<RenderUnit> units, int? maxContentRows, Pane pane, int innerWidth, RenderNoteCollector notes)
    {
        if (maxContentRows is not int cap)
        {
            return units.SelectMany(u => u.Rows).ToList();
        }

        if (cap <= 0)
        {
            return Array.Empty<PaneRow>();
        }

        var total = units.Sum(u => u.Rows.Count);
        if (total <= cap)
        {
            return units.SelectMany(u => u.Rows).ToList();
        }

        var kept = new List<PaneRow>();
        var seen = 0;
        var k = -1;
        for (var idx = 0; idx < units.Count; idx++)
        {
            var nextSeen = seen + units[idx].Rows.Count;
            // Rows is never empty in practice: every RenderLeaf call site here passes a
            // non-empty segment list, and PackRow places segments[i] unconditionally. RowLayout.
            // Wrap does return zero rows for an empty segment list (RowLayout.cs:52-55), so a
            // future producer that passes one would reach this guard rather than being silently
            // treated as a straddling unit with no row to carry the marker.
            if (nextSeen >= cap && units[idx].Rows.Count >= 1)
            {
                k = idx;
                break;
            }

            kept.AddRange(units[idx].Rows);
            seen = nextSeen;
        }

        // total > cap and cap >= 1 guarantee a straddling unit is always found.
        var budget = cap - seen;
        var reNoted = new RenderNoteCollector();
        var overflow = ResolveOverflow(pane);
        var owner = PaneRenderer.RenderLeaf(
            units[k].Segments, innerWidth, overflow, pane.Ellipsis, reNoted,
            allowFallback: false, rowBudget: budget, markerRequired: true);
        kept.AddRange(owner.Rows);
        return kept;
    }

    private static IReadOnlyList<RenderUnit> RenderDefaultRows(Pane pane, int innerWidth, ItemContext ctx, RenderNoteCollector notes)
    {
        var segments = SegmentBuilder.Build(ctx);
        var overflow = ResolveOverflow(pane);
        var buffer = PaneRenderer.RenderLeaf(segments, innerWidth, overflow, pane.Ellipsis, notes, allowFallback: false);
        return new[] { new RenderUnit(segments, buffer.Rows) };
    }

    // SPEC-3.1-block-model.md §3: a block is an item whose resolved display text splits (per
    // SplitBlockLines) into more than one line. A block interrupts the current packed group and
    // renders its own lines as their own rows instead of joining the group — D4/D6 rule out any
    // separator or blank row on either side of it, so this is a straight append into `units` (the
    // single concatenation site both packed-group flushes and block lines feed, per SPEC-3.1 §6)
    // rather than a FlushGroup call, which would risk emitting an empty-group unit for an empty
    // group.
    internal static IReadOnlyList<RenderUnit> RenderItemRows(
        Pane pane,
        int innerWidth,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds,
        RenderNoteCollector notes)
    {
        var overflow = ResolveOverflow(pane);
        var units = new List<RenderUnit>();
        var packedGroup = new List<Segment>();

        void FlushGroup()
        {
            if (packedGroup.Count == 0)
            {
                return;
            }

            var buffer = PaneRenderer.RenderLeaf(packedGroup, innerWidth, overflow, pane.Ellipsis, notes, allowFallback: false);
            units.Add(new RenderUnit(packedGroup.ToArray(), buffer.Rows));
            packedGroup.Clear();
        }

        foreach (var resolved in LeafItems.Resolve(pane.Items, values, ctx, compounds, tokens))
        {
            if (resolved.Value is null)
            {
                continue;
            }

            var decision = LeafContent.Decide(resolved, values, compounds);
            var color = ColorResolution.Resolve(resolved.Config.Color, values, tokens);
            // SPEC-85 §12.5/D-B: a compound's item-level colour is already consumed per-part
            // (as each part's default) by LeafItems, so the single-line path must not wrap it
            // again here — that outer wrap would sit outside the Spans decomposition and violate
            // the §12.3 invariant. Every other path (blocks, the stripped-trailing-newline case)
            // keeps using `color` unchanged.
            var itemColor = resolved.Config.Parts is null ? color : null;

            var lines = SplitBlockLines(decision.Text);
            if (lines.Count > 1)
            {
                FlushGroup();
                foreach (var line in lines)
                {
                    var lineSegment = SegmentBuilder.BuildItemSegment(line, color);
                    var lineBuffer = PaneRenderer.RenderLeaf(new[] { lineSegment }, innerWidth, overflow, pane.Ellipsis, notes, allowFallback: false);
                    units.Add(new RenderUnit(new[] { lineSegment }, lineBuffer.Rows));
                }

                continue;
            }

            // D2: a lone trailing newline (or \r\n) doesn't make an item a block, but it also
            // isn't part of the single surviving line — SplitBlockLines already stripped it, so
            // reuse that stripped text rather than the raw decision.Text, or the stray \n would
            // leak into this row's markup and inflate its Plain.Length-measured width by one.
            var singleLine = lines[0];
            packedGroup.Add(singleLine == decision.Text
                ? SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, itemColor, decision.Spans)
                : SegmentBuilder.BuildItemSegment(singleLine, color));
        }

        FlushGroup();
        return units;
    }

    // SPEC-3.1-block-model.md §3 rule D2: strip exactly one trailing newline (and a \r
    // immediately before it) before splitting, then TrimEnd('\r') each resulting line — so
    // "foo\n" is one line (not a block) but "foo\n\n" is two, one of them a real blank line
    // that must survive as a padded row (D3), not be silently collapsed.
    internal static IReadOnlyList<string> SplitBlockLines(string value)
    {
        var end = value.Length;
        if (end > 0 && value[end - 1] == '\n')
        {
            end--;
            if (end > 0 && value[end - 1] == '\r')
            {
                end--;
            }
        }

        return value[..end].Split('\n').Select(line => line.TrimEnd('\r')).ToList();
    }

    // §2.6: "overflow" is only legal for a surface's single root pane; inside any split it would
    // corrupt the neighbor to its right, so a pane rendered through this path (always inside a
    // split) coerces an explicit "overflow" down to "truncate" rather than honoring it.
    internal static OverflowMode ResolveOverflow(Pane pane) =>
        pane.Overflow is OverflowMode mode && mode != OverflowMode.Overflow ? mode : OverflowMode.Truncate;

    private static PaneRow AlignRow(PaneRow row, int targetWidth, PaneAlign align)
    {
        var deficit = Math.Max(0, targetWidth - row.Width);

        var newMarkup = align switch
        {
            PaneAlign.Center => new string(' ', deficit / 2) + row.Markup + new string(' ', deficit - deficit / 2),
            PaneAlign.Right => new string(' ', deficit) + row.Markup,
            _ => row.Markup + new string(' ', deficit),
        };

        return new PaneRow(newMarkup, row.Width + deficit);
    }
}
