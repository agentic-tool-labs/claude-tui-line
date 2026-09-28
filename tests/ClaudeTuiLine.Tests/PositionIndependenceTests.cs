using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.5: leaf rendering is a pure function of (items, innerWidth) — COLUMNS
/// is read exactly once, at the root (Program.cs → SurfaceLayout), and never reaches
/// <see cref="PaneRenderer.RenderLeaf"/>. A leaf must therefore render identically no matter where
/// in the pane tree it sits, or how wide the surrounding surface is, as long as its OWN resolved
/// inner width is unchanged. The surrounding surface width was never a parameter RenderLeaf takes
/// to begin with, so this proves the stronger, more useful property directly: no hidden or shared
/// state leaks between calls, by interleaving a differently-parameterized call between two
/// identical ones.
/// </summary>
public class PositionIndependenceTests
{
    private static readonly IReadOnlyList<Segment> RootItems = new List<Segment>
    {
        new("[cyan]directory[/]", "directory"),
        new("[green]git-branch[/]", "git-branch"),
        new("context: 42%", "context: 42%"),
    };

    private static readonly IReadOnlyList<Segment> UnrelatedSiblingItems = new List<Segment>
    {
        new("[yellow]model[/]", "model"),
        new("cost: $1.23", "cost: $1.23"),
    };

    [Theory]
    [InlineData(OverflowMode.Wrap)]
    [InlineData(OverflowMode.Truncate)]
    [InlineData(OverflowMode.Overflow)]
    public void SameItemsAndInnerWidth_RenderIdentically_RegardlessOfInterveningCalls(OverflowMode overflow)
    {
        // "Root of an 80-col surface": the inner width a root pane would resolve to from an
        // 80-column surface at this fixture's chrome/border reserve.
        var asRoot = PaneRenderer.RenderLeaf(RootItems, innerWidth: 24, overflow, "…", new RenderNoteCollector());

        // An unrelated pane at a different width, rendered in between — if RenderLeaf held any
        // shared/static state, this would corrupt the next call.
        var unrelated = PaneRenderer.RenderLeaf(UnrelatedSiblingItems, innerWidth: 60, overflow, "…", new RenderNoteCollector());

        // "3rd child of a split inside a 200-col surface" (splits aren't wired in Phase 2, so this
        // is constructed directly): a different surrounding surface, but the SAME resolved inner
        // width for this particular child.
        var asSplitChild = PaneRenderer.RenderLeaf(RootItems, innerWidth: 24, overflow, "…", new RenderNoteCollector());

        Assert.Equal(asRoot.Rows.Select(r => r.Markup), asSplitChild.Rows.Select(r => r.Markup));
        Assert.Equal(asRoot.Rows.Select(r => r.Width), asSplitChild.Rows.Select(r => r.Width));

        // Sanity: the interleaved call actually exercised different behavior, so the equality
        // above is not vacuously true.
        Assert.NotEqual(
            asRoot.Rows.Select(r => r.Markup),
            unrelated.Rows.Select(r => r.Markup));
    }
}
