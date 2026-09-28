namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: cutting a <see cref="Segment"/> to fit a width, on either axis.
/// Moved out of <see cref="PaneRenderer"/> (SPEC-2.6-vertical-marker-splice.md §9.4) so
/// <see cref="RowLayout"/> can call it too without inverting the PaneRenderer → RowLayout
/// dependency direction.
/// </summary>
internal static class SegmentTruncation
{
    /// SPEC-V2-FRAMEWORK.md §2.6, the two riders, in one place for both axes: an empty `ellipsis`
    /// is a hard clip that spends no cell, and a marker not strictly narrower than the space it
    /// would sit in is dropped rather than allowed to consume it.
    internal static bool MarkerFits(int innerWidth, string ellipsis) =>
        ellipsis.Length > 0 && ellipsis.Length < innerWidth;

    // §2.6 truncate: cut to fit, ending with the marker; the marker's own width is budgeted
    // against innerWidth, and is dropped entirely (hard clip, no sacrificed cell) when
    // innerWidth is not greater than the marker's width.
    internal static Segment Truncate(Segment segment, int innerWidth, string ellipsis)
    {
        // §3.3/SPEC-85 §5.2: a compound segment's Spans must survive truncation so surviving
        // parts keep their per-part colour (the "one genuinely hard finding" — SegmentTruncation
        // otherwise degrades any composite markup to unstyled text). Every non-compound segment
        // has Spans == null and falls through to the untouched logic below, byte-for-byte.
        if (segment.Spans is not null)
        {
            return TruncateSpans(segment, innerWidth, ellipsis);
        }

        if (innerWidth <= 0)
        {
            return Restyle(segment, string.Empty);
        }

        if (!MarkerFits(innerWidth, ellipsis))
        {
            return Restyle(segment, segment.Plain[..SafeCutIndex(segment.Plain, Math.Min(innerWidth, segment.Plain.Length))]);
        }

        var contentBudget = innerWidth - ellipsis.Length;
        var clipped = segment.Plain[..SafeCutIndex(segment.Plain, Math.Min(contentBudget, segment.Plain.Length))];
        var newPlain = clipped + ellipsis;

        if (!OscHyperlink.TryUnwrap(segment.Markup, out var url, out var innerMarkup))
        {
            return RestyleSimple(segment, newPlain);
        }

        // §3.2 rule 3 / ruling d: a truncated link closes itself before the ellipsis — the
        // ellipsis keeps whatever colour applies but is never clickable ("clicking '…' must
        // never navigate").
        var innerSegment = new Segment(innerMarkup, segment.Plain);
        var restyledContent = RestyleSimple(innerSegment, clipped);
        var restyledEllipsis = RestyleSimple(innerSegment, ellipsis);
        return new Segment(OscHyperlink.Wrap(url, restyledContent.Markup) + restyledEllipsis.Markup, newPlain);
    }

    // Span-aware counterpart of Truncate's body above, for a compound segment (Spans != null).
    // §5.2: the ellipsis is appended outside the last surviving span, unstyled — never restyled
    // into the severed span's colour.
    private static Segment TruncateSpans(Segment segment, int innerWidth, string ellipsis)
    {
        if (innerWidth <= 0)
        {
            return RestyleSlice(segment, 0, 0);
        }

        if (!MarkerFits(innerWidth, ellipsis))
        {
            var hardCut = SafeCutIndex(segment.Plain, Math.Min(innerWidth, segment.Plain.Length));
            return RestyleSlice(segment, 0, hardCut);
        }

        var contentBudget = innerWidth - ellipsis.Length;
        var cutIndex = SafeCutIndex(segment.Plain, Math.Min(contentBudget, segment.Plain.Length));
        var styledContent = RestyleSlice(segment, 0, cutIndex);
        var escapedEllipsis = Spectre.Console.Markup.Escape(ellipsis);
        var newMarkup = styledContent.Markup + escapedEllipsis;
        var newPlain = styledContent.Plain + ellipsis;

        // The ellipsis sits outside any link the content carries, so the result is no longer a
        // link-wrapped decomposition and cannot satisfy §12.3's invariant — a truncated segment is
        // terminal (nothing slices it again), so it drops its Spans instead. With no link the
        // ellipsis is just one more unstyled span and the decomposition survives intact.
        if (styledContent.Spans is not { } contentSpans || OscHyperlink.TryUnwrap(styledContent.Markup, out _, out _))
        {
            return new Segment(newMarkup, newPlain);
        }

        var spans = new List<StyledSpan>(contentSpans) { new(ellipsis, escapedEllipsis) };
        return new Segment(newMarkup, newPlain, spans);
    }

    // §5.1/§5.2 of SPEC-85: slices a Segment to [start, end) of its Plain. When the segment
    // carries no span decomposition this is byte-for-byte today's Restyle. When it does, a span
    // entirely outside the slice is dropped, a span entirely inside is copied verbatim (markup
    // included — the surviving-spans-keep-their-markup rule), and a span the boundary lands
    // inside is cut and re-styled through the single-span Restyle/RestyleSimple path so the
    // degradation from a composite markup a cut cannot preserve is bounded to that one fragment.
    internal static Segment RestyleSlice(Segment original, int start, int end)
    {
        if (original.Spans is not { } spans)
        {
            return Restyle(original, original.Plain[start..end]);
        }

        // §12.3: an OSC 8 link wraps the style markup from outside the decomposition, so it is
        // unwrapped before slicing and re-applied after — the same layering Restyle uses, and
        // what re-opens the link on every continuation row when WrapToWidth chunks a segment.
        var linked = OscHyperlink.TryUnwrap(original.Markup, out var url, out _);

        var surviving = new List<StyledSpan>();
        var offset = 0;
        foreach (var span in spans)
        {
            var spanStart = offset;
            var spanEnd = offset + span.Plain.Length;
            offset = spanEnd;

            if (spanEnd <= start || spanStart >= end)
            {
                continue;
            }

            if (spanStart >= start && spanEnd <= end)
            {
                surviving.Add(span);
                continue;
            }

            var sliceStart = Math.Max(start, spanStart) - spanStart;
            var sliceEnd = Math.Min(end, spanEnd) - spanStart;
            var restyled = Restyle(new Segment(span.Markup, span.Plain), span.Plain[sliceStart..sliceEnd]);
            surviving.Add(new StyledSpan(restyled.Plain, restyled.Markup));
        }

        if (surviving.Count == 0)
        {
            // Nothing survives: emit a bare empty segment rather than an empty link or an empty
            // colour wrap. §8.9 — no decoration may be emitted around no text.
            return new Segment(string.Empty, string.Empty);
        }

        var plain = string.Concat(surviving.Select(s => s.Plain));
        var styleMarkup = string.Concat(surviving.Select(s => s.Markup));
        return new Segment(linked ? OscHyperlink.Wrap(url, styleMarkup) : styleMarkup, plain, surviving);
    }

    // §2.6 wrap, trap 1 (break on plain text only) and trap 2 (style re-emitted on every
    // continuation row): chunks the oversized segment's Plain text into innerWidth-wide pieces
    // and restyles each piece independently, so every resulting Segment carries its own style.
    internal static List<Segment> WrapToWidth(Segment segment, int innerWidth)
    {
        var chunks = new List<Segment>();
        if (innerWidth <= 0)
        {
            chunks.Add(RestyleSlice(segment, 0, 0));
            return chunks;
        }

        var i = 0;
        while (i < segment.Plain.Length)
        {
            var end = SafeCutIndex(segment.Plain, Math.Min(i + innerWidth, segment.Plain.Length));
            chunks.Add(RestyleSlice(segment, i, end));
            i = end;
        }

        return chunks;
    }

    // §13.2 (defect 16): Plain is UTF-16, so a non-BMP character (most emoji) is stored as a
    // surrogate pair — two code units. A cut index landing between them would split the pair into
    // two lone surrogates, which is invalid UTF-16 on the wire, not just a clipped glyph. Advancing
    // past the pair keeps both units on the retained side; every truncate/wrap cut point in this
    // class routes through here rather than slicing Plain directly.
    internal static int SafeCutIndex(string plain, int index) =>
        index > 0 && index < plain.Length && char.IsLowSurrogate(plain[index]) && char.IsHighSurrogate(plain[index - 1])
            ? index + 1
            : index;

    // §3.2 rule 3: an OSC 8 link (see OscHyperlink) wraps a segment's *style* markup, not its
    // Plain text, so restyling a linked segment must unwrap the link, restyle the inner styled
    // markup with RestyleSimple, and re-wrap around the same URL — rather than trying to
    // pattern-match the link's raw escape bytes as if they were an ordinary style prefix/suffix.
    // Every call site (truncation, wrap-chunking) goes through this rather than RestyleSimple
    // directly, so a link is preserved (and, for a wrapped segment, reopened per continuation
    // row) without each caller needing its own link-awareness.
    internal static Segment Restyle(Segment original, string newPlain)
    {
        if (!OscHyperlink.TryUnwrap(original.Markup, out var url, out var innerMarkup))
        {
            return RestyleSimple(original, newPlain);
        }

        var innerSegment = new Segment(innerMarkup, original.Plain);
        var restyledInner = RestyleSimple(innerSegment, newPlain);
        return new Segment(OscHyperlink.Wrap(url, restyledInner.Markup), newPlain);
    }

    // Re-styles a modified Plain string. Segment.Markup is not always a simple single-tag wrap
    // around Plain (SegmentBuilder.cs builds composite/concatenated markup for some segments) —
    // so this only rewrites the simple case, verified by exact substring/prefix/suffix matching,
    // and gracefully degrades to unstyled markup otherwise rather than guessing wrong.
    internal static Segment RestyleSimple(Segment original, string newPlain)
    {
        var newMarkup = TryGetSimpleWrap(original, out var prefix, out var suffix)
            ? prefix + Spectre.Console.Markup.Escape(newPlain) + suffix
            : Spectre.Console.Markup.Escape(newPlain);

        return new Segment(newMarkup, newPlain);
    }

    internal static bool TryGetSimpleWrap(Segment segment, out string prefix, out string suffix)
    {
        prefix = string.Empty;
        suffix = string.Empty;

        var escapedPlain = Spectre.Console.Markup.Escape(segment.Plain);
        var index = segment.Markup.IndexOf(escapedPlain, StringComparison.Ordinal);
        if (index < 0 || index != segment.Markup.LastIndexOf(escapedPlain, StringComparison.Ordinal))
        {
            return false; // absent, or ambiguous (more than one occurrence) — do not guess.
        }

        var candidatePrefix = segment.Markup[..index];
        var candidateSuffix = segment.Markup[(index + escapedPlain.Length)..];

        if (candidatePrefix.Length == 0 && candidateSuffix.Length == 0)
        {
            return true; // fully unstyled: Markup is exactly the escaped Plain, no tags at all.
        }

        if (!candidatePrefix.EndsWith(']') || candidateSuffix != "[/]")
        {
            return false;
        }

        prefix = candidatePrefix;
        suffix = candidateSuffix;
        return true;
    }
}
