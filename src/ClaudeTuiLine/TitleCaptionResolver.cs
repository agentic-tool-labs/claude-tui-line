namespace ClaudeTuiLine;

/// <summary>
/// SPEC pane-id-title-align §3: resolves a pane's <see cref="Pane.Title"/> item through the same
/// item conversion path as any ordinary item (<see cref="LeafItems.Resolve"/> /
/// <see cref="LeafContent.Decide"/>), taking only the first line of a multi-line value (§3.7).
/// </summary>
internal static class TitleCaptionResolver
{
    internal readonly record struct TitleSegment(Segment Segment, bool WasMultiLine);

    /// <summary>
    /// SPEC pane-id-title-align §3.7: the caption's plain (ANSI-stripped) text width, with no
    /// colour resolution — the only thing <see cref="SizeResolver"/>'s measurement pass needs.
    /// Null when the title's value is not resolvable, so it contributes no width floor.
    /// </summary>
    internal static int? ResolveWidth(
        PaneItem title,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, Segment> compounds)
    {
        var resolved = LeafItems.Resolve(new[] { title }, values, ctx, compounds).FirstOrDefault();
        if (resolved is null || resolved.Value is null)
        {
            return null;
        }

        var decision = LeafContent.Decide(resolved, values, compounds);
        var lines = PaneAssembler.SplitBlockLines(decision.Text);
        return lines.Count > 0 ? lines[0].Length : 0;
    }

    /// <summary>
    /// SPEC pane-id-title-align §3.4: the caption's rendered <see cref="Segment"/>, coloured per
    /// the same rule as any item (declared <c>color</c>, else <paramref name="fallbackColorMarkup"/>
    /// — the pane's own border colour). Null when the title's value is not resolvable.
    /// </summary>
    internal static TitleSegment? ResolveSegment(
        PaneItem title,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds,
        string fallbackColorMarkup)
    {
        var resolved = LeafItems.Resolve(new[] { title }, values, ctx, compounds, tokens).FirstOrDefault();
        if (resolved is null || resolved.Value is null)
        {
            return null;
        }

        var decision = LeafContent.Decide(resolved, values, compounds);
        var color = ColorResolution.Resolve(resolved.Config.Color, values, tokens) ?? fallbackColorMarkup;
        var itemColor = resolved.Config.Parts is null ? color : null;

        var lines = PaneAssembler.SplitBlockLines(decision.Text);
        if (lines.Count <= 1)
        {
            var singleLine = lines.Count > 0 ? lines[0] : decision.Text;
            var segment = singleLine == decision.Text
                ? SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, itemColor, decision.Spans)
                : SegmentBuilder.BuildItemSegment(singleLine, color);
            return new TitleSegment(segment, false);
        }

        return new TitleSegment(SegmentBuilder.BuildItemSegment(lines[0], color), true);
    }
}
