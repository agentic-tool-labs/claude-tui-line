namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2/§4/§6: the one decision — given a resolved item, does its own
/// internal colour survive, or does an item-level config colour replace it, and does its own
/// <c>link</c> template (§3.2) resolve to an OSC 8 hyperlink around its markup — shared by
/// <see cref="SizeResolver"/> (which only needs the resulting width) and
/// <see cref="PaneAssembler"/> (which needs the resulting rows), so the fixpoint's measurement
/// pass and the final render can never disagree about which items fit.
/// </summary>
public static class LeafContent
{
    /// <param name="Text">Plain text — the sole width metric (§2.4).</param>
    /// <param name="Markup">
    /// SPEC-V2-FRAMEWORK.md §4: the item's own markup, including any internal per-fragment colour
    /// it applies to itself, and any OSC 8 hyperlink its <c>link</c> config wraps around it.
    /// </param>
    /// <param name="Spans">
    /// SPEC-V2-FRAMEWORK.md §3.3: a compound item's per-part decomposition, carried through to the
    /// <see cref="Segment"/> the assembler builds so truncation and wrapping can preserve per-part
    /// colour (SPEC-85 §5.2). Null for every non-compound item, and cleared by any transformation
    /// here that puts markup outside the decomposition (SPEC-85 §12.3).
    /// </param>
    public readonly record struct ItemDecision(string Text, string Markup, IReadOnlyList<StyledSpan>? Spans = null);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §5: the id names a <c>link</c> template's <c>{other-id}</c>
    /// placeholders reference, so <see cref="ItemValueResolver"/> can add them to its up-front
    /// resolution set — the same placeholder syntax <see cref="TryBuildLink"/> expands, not a
    /// second parser. <c>{}</c> (the item's own value) is not a reference and is excluded.
    /// </summary>
    internal static IEnumerable<string> LinkPlaceholderIds(string template) =>
        PlaceholderTemplate.Tokenize(template)
            .Where(token => token.IsPlaceholder && token.Text.Length > 0)
            .Select(token => token.Text);

    public static ItemDecision Decide(
        LeafItems.ResolvedItem resolved,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, Segment> compounds)
    {
        var text = resolved.Display!.Plain;
        var markup = resolved.Display!.Markup;
        var spans = resolved.Display!.Spans;

        if (resolved.Config.Parts is null && resolved.Config.Color is not null && !IsSemantic(resolved.Config))
        {
            markup = Spectre.Console.Markup.Escape(text);
            spans = null;
        }

        var template = resolved.Config.Link is { Length: > 0 } configured
            ? configured
            : resolved.DefaultLink;

        if (template is { Length: > 0 }
            && resolved.Value is { } ownValue
            && TryBuildLink(template, ownValue, values, compounds, out var url))
        {
            markup = OscHyperlink.Wrap(url, markup);
        }

        return new ItemDecision(text, markup, spans);
    }

    // A row's own colour is decorative unless its registry entry says otherwise (§4/§6): an
    // item-level config colour replaces a decorative colour rather than nesting around it, so a
    // decorative row's internal markup is discarded here and the outer colour becomes the sole
    // colour. Rows with no registry entry (e.g. a command item) carry no internal colour to begin
    // with, so this is a no-op for them. A compound item (SPEC-V2-FRAMEWORK.md §3.3) is excluded
    // from this replacement entirely: its item-level colour is already consumed per-part, as the
    // default for parts that don't set their own, so there is no outer wrap left to apply here.
    private static bool IsSemantic(PaneItem config)
    {
        var id = config.Id ?? config.Item;
        return id is not null && ItemRegistry.Find(id)?.ColorKind == ItemRegistry.ItemColorKind.Semantic;
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §3.2: expands a <c>link</c> template's <c>{}</c> (this item's own raw
    /// value — never its formatted display text, so a <c>format</c> string has no bearing on the
    /// URL) and <c>{other-id}</c> (another item's raw value from the same resolution pass)
    /// placeholders. Every substituted value is ANSI-stripped, since a raw value may originate
    /// from a command provider's unsanitized stdout and a URL has no business carrying escape
    /// bytes. Any <c>{other-id}</c> that isn't in <paramref name="values"/> (or resolved to null)
    /// suppresses the link only — the item itself still renders, per §3.2's link-is-best-effort
    /// rule.
    /// </summary>
    private static bool TryBuildLink(
        string template,
        string ownValue,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, Segment> compounds,
        out string url)
    {
        var missing = false;
        var built = new System.Text.StringBuilder();
        foreach (var token in PlaceholderTemplate.Tokenize(template))
        {
            if (!token.IsPlaceholder)
            {
                built.Append(token.Text);
                continue;
            }

            var raw = token.Text.Length == 0 ? ownValue : values.GetValueOrDefault(token.Text);

            // SPEC-87 §12.4/§4.1: a compound never writes into `values` (§0), so this is consulted
            // only on that miss, and only its Plain — markup is dropped unconditionally, a URL is
            // not a place for SGR bytes. A suppressed compound has no map entry (§12.3), so this
            // falls through to the same missing-placeholder path below, matching E3.
            if (raw is null && token.Text.Length > 0 && compounds.TryGetValue(token.Text, out var compoundSegment))
            {
                raw = compoundSegment.Plain;
            }

            if (raw is null)
            {
                missing = true;
                continue;
            }

            built.Append(AnsiStrip.Strip(raw));
        }

        url = missing ? string.Empty : built.ToString();
        return !missing;
    }
}
