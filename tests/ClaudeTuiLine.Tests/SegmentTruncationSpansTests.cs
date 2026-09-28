namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-85-ADDENDUM-spans-threading.md §12.7/§12.10: the two pre-existing bugs the addendum found
/// in the already-uncommitted Spans-aware truncation code — D-D (the ellipsis wasn't its own span)
/// and D-E (no OSC 8 link handling in the span-aware path).
/// </summary>
public class SegmentTruncationSpansTests
{
    private static Segment Compound(params (string Plain, string Markup)[] parts)
    {
        var spans = parts.Select(p => new StyledSpan(p.Plain, p.Markup)).ToList();
        return SegmentBuilder.BuildCompoundSegment(spans);
    }

    // D-D: after truncation-with-ellipsis, the Spans decomposition must still account for the
    // ellipsis — concat(span.Plain) == Plain and concat(span.Markup) == Markup.
    [Fact]
    public void TruncateSpans_EllipsisBecomesItsOwnSpan_InvariantHolds()
    {
        var segment = Compound(("agent:", "[grey]agent:[/]"), ("WORKER-7", "[aqua]WORKER-7[/]"));

        var truncated = SegmentTruncation.Truncate(segment, innerWidth: 9, ellipsis: "…");

        Assert.NotNull(truncated.Spans);
        Assert.Equal(string.Concat(truncated.Spans!.Select(s => s.Plain)), truncated.Plain);
        Assert.Equal(string.Concat(truncated.Spans!.Select(s => s.Markup)), truncated.Markup);
        Assert.EndsWith("…", truncated.Plain);
        Assert.Equal("…", truncated.Spans![^1].Plain);
    }

    // D-E: a linked compound's truncation marker sits outside the link — clicking "…" must never
    // navigate, matching the non-span Truncate path's stated rule (§3.2 rule 3 / ruling d).
    [Fact]
    public void TruncateSpans_LinkedCompound_EllipsisIsOutsideTheLink()
    {
        var inner = Compound(("agent:", "[grey]agent:[/]"), ("WORKER-7", "[aqua]WORKER-7[/]"));
        var linked = new Segment(OscHyperlink.Wrap("https://example/x", inner.Markup), inner.Plain, inner.Spans);

        var truncated = SegmentTruncation.Truncate(linked, innerWidth: 9, ellipsis: "…");

        // The ellipsis sits after the link's own close sequence, not inside it — the whole
        // truncated Markup is no longer a single clean wrap (TryUnwrap on it is expected False),
        // but the link portion up to its Close still unwraps to the pre-ellipsis content.
        Assert.False(OscHyperlink.TryUnwrap(truncated.Markup, out _, out _));
        Assert.EndsWith("…", truncated.Markup);
        var closeIndex = truncated.Markup.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);
        Assert.True(closeIndex >= 0 && closeIndex < truncated.Markup.Length - "…".Length);
        Assert.True(OscHyperlink.TryUnwrap(truncated.Markup[..(closeIndex + OscHyperlink.Close.Length)], out _, out var innerMarkup));
        Assert.DoesNotContain("…", innerMarkup);
    }

    // D-E: RestyleSlice on a linked compound must unwrap before slicing and re-wrap after, so a
    // wrapped compound reopens its link on every continuation row.
    [Fact]
    public void RestyleSlice_LinkedCompound_PreservesLinkAroundSlicedSpans()
    {
        var inner = Compound(("agent:", "[grey]agent:[/]"), ("WORKER-7", "[aqua]WORKER-7[/]"));
        var linked = new Segment(OscHyperlink.Wrap("https://example/x", inner.Markup), inner.Plain, inner.Spans);

        var sliced = SegmentTruncation.RestyleSlice(linked, 0, 6);

        Assert.True(OscHyperlink.TryUnwrap(sliced.Markup, out var url, out _));
        Assert.Equal("https://example/x", url);
        Assert.Equal("agent:", sliced.Plain);
        Assert.NotNull(sliced.Spans);
    }

    // RestyleSlice with nothing surviving must emit a genuinely empty Segment, not an empty link
    // or an empty colour wrap (§8.9: no decoration around no text).
    [Fact]
    public void RestyleSlice_EmptySlice_ReturnsBareEmptySegment()
    {
        var inner = Compound(("agent:", "[grey]agent:[/]"), ("WORKER-7", "[aqua]WORKER-7[/]"));
        var linked = new Segment(OscHyperlink.Wrap("https://example/x", inner.Markup), inner.Plain, inner.Spans);

        var sliced = SegmentTruncation.RestyleSlice(linked, 0, 0);

        Assert.Equal(string.Empty, sliced.Plain);
        Assert.Equal(string.Empty, sliced.Markup);
    }
}
