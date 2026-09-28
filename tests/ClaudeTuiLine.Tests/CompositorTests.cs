using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.4 rules 1, 2 and 4: every pane row is padded to its own pane's width
/// before siblings are joined; sibling buffers are padded to a common height with full-width
/// blank rows; trailing whitespace on the composed root row is trimmed once, at the very end,
/// unless the rightmost contributing pane has a background color.
/// </summary>
public class CompositorTests
{
    private static Compositor.PaneContribution Contribution(int width, bool hasBackground, params (string Markup, int Width)[] rows) =>
        new(new PaneBuffer(rows.Select(r => new PaneRow(r.Markup, r.Width)).ToList()), width, hasBackground);

    [Fact]
    public void NoSiblings_ComposesToNoRows()
    {
        Assert.Empty(Compositor.ComposeRoot(Array.Empty<Compositor.PaneContribution>()));
    }

    [Fact]
    public void SinglePane_ShortRow_PaddedToWidthThenTrimmedBack_NoBackground()
    {
        var contribution = Contribution(10, hasBackground: false, ("abc", 3));

        var composed = Compositor.ComposeRoot(new[] { contribution });

        Assert.Equal(new[] { "abc" }, composed.Select(r => r.Markup));
    }

    [Fact]
    public void SinglePane_ShortRow_PaddingSurvivesTrim_WhenPaneHasBackground()
    {
        var contribution = Contribution(10, hasBackground: true, ("abc", 3));

        var composed = Compositor.ComposeRoot(new[] { contribution });

        Assert.Equal(new[] { "abc" + new string(' ', 7) }, composed.Select(r => r.Markup));
    }

    [Fact]
    public void TwoSiblings_JoinedLeftToRight_PerRow()
    {
        var left = Contribution(5, hasBackground: true, ("ab", 2), ("cde", 3));
        var right = Contribution(4, hasBackground: true, ("xy", 2), ("z", 1));

        var composed = Compositor.ComposeRoot(new[] { left, right });

        Assert.Equal(
            new[]
            {
                "ab" + new string(' ', 3) + "xy" + new string(' ', 2),
                "cde" + new string(' ', 2) + "z" + new string(' ', 3),
            },
            composed.Select(r => r.Markup));
    }

    [Fact]
    public void ShorterSibling_PaddedToCommonHeight_WithFullWidthBlankRows()
    {
        var tall = Contribution(3, hasBackground: true, ("a", 1), ("b", 1), ("c", 1));
        var shortPane = Contribution(2, hasBackground: true, ("x", 1));

        var composed = Compositor.ComposeRoot(new[] { tall, shortPane });

        Assert.Equal(3, composed.Count);
        Assert.Equal("a" + new string(' ', 2) + "x" + new string(' ', 1), composed[0].Markup);
        Assert.Equal("b" + new string(' ', 2) + new string(' ', 2), composed[1].Markup);
        Assert.Equal("c" + new string(' ', 2) + new string(' ', 2), composed[2].Markup);
    }

    [Fact]
    public void TrailingWhitespace_TrimmedOnlyWhenRightmostPaneHasNoBackground()
    {
        var left = Contribution(3, hasBackground: true, ("a", 1));
        var rightNoBg = Contribution(4, hasBackground: false, ("b", 1));

        var composed = Compositor.ComposeRoot(new[] { left, rightNoBg });

        // left's own rule-1 padding survives — only the composed ROW's trailing whitespace is
        // trimmed, and only because the RIGHTMOST pane has no background.
        Assert.Equal(new[] { "a" + new string(' ', 2) + "b" }, composed.Select(r => r.Markup));
    }

    [Fact]
    public void TrailingWhitespace_PreservedWhenRightmostPaneHasBackground()
    {
        var left = Contribution(3, hasBackground: true, ("a", 1));
        var rightWithBg = Contribution(4, hasBackground: true, ("b", 1));

        var composed = Compositor.ComposeRoot(new[] { left, rightWithBg });

        Assert.Equal(new[] { "a" + new string(' ', 2) + "b" + new string(' ', 3) }, composed.Select(r => r.Markup));
    }

    [Fact]
    public void LeftmostPaneHavingBackground_DoesNotPreventTrim_OnlyRightmostMatters()
    {
        var leftWithBg = Contribution(3, hasBackground: true, ("a", 1));
        var rightNoBg = Contribution(4, hasBackground: false, ("b", 1));

        var composed = Compositor.ComposeRoot(new[] { leftWithBg, rightNoBg });

        Assert.Equal(new[] { "a" + new string(' ', 2) + "b" }, composed.Select(r => r.Markup));
    }

    [Fact]
    public void PaddingIsMeasuredOnPaneRowWidth_NotOnMarkupStringLength()
    {
        // A styled row: markup text is longer than its rendered (ANSI-stripped) width.
        var contribution = Contribution(6, hasBackground: true, ("[red]ab[/]", 2));

        var composed = Compositor.ComposeRoot(new[] { contribution });

        Assert.Equal("[red]ab[/]" + new string(' ', 4), composed[0].Markup);
    }
}
