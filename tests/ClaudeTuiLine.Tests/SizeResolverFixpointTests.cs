using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §10.6: three dedicated fixpoint tests, each using a stubbed measurer for
/// the "content" pane so the test isolates the fixpoint's own mechanics (clamping, pass count,
/// termination) from real segment measurement. Every scenario shares one split shape: a
/// borderless, gutterless two-child vertical split at outer width 50 — `fill` left (floor 20,
/// since it is unresolved percent/fill) and `content` right (floor 0, no min/maxSize) — so
/// `reserve = 20` and step 4's `cap = 50 - 20 = 30` in every pass, and the only thing that varies
/// between tests is what the stub reports.
/// </summary>
public class SizeResolverFixpointTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
    private static readonly StatusInput Input = new();
    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static Pane Leaf(string size) =>
        new(PaneSplit.None, Array.Empty<Pane>(), size, NoBorder, null, "…", null, Array.Empty<PaneItem>());

    private static Pane Split(Pane left, Pane right) =>
        new(PaneSplit.Vertical, new[] { left, right }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());

    private static SizeResolver.ResolvedPane ResolveWithStub(Func<int?, int> contentRequest)
    {
        var root = Split(Leaf("fill"), Leaf("content"));
        return SizeResolver.Resolve(root, 50, Ctx, new Dictionary<string, string?>(), new Dictionary<string, Segment>(),
            (p, granted) => p.Size == "content" ? contentRequest(granted) : 0, new RenderNoteCollector());
    }

    [Fact]
    public void Convergence_AnchorDegradesUnderCap_FreedColumnsLandInFillSibling()
    {
        // Pass 1 (six-step alloc on the initial ask of 40): cap = 30, so right is granted 30.
        // Pass 2 re-measures right at granted=30 and it genuinely shrinks to 15 (its "degrade").
        // Pass 3 re-measures at granted=15 and it is stable, so the loop converges there.
        var resolved = ResolveWithStub(granted => granted switch
        {
            null => 40,
            30 => 15,
            _ => 15,
        });

        var right = resolved.Children[1];
        var left = resolved.Children[0];

        Assert.Equal(15, right.OuterWidth);
        // The assertion required by §10.6(a): the freed columns are verified to have landed in
        // the fill sibling (50 - 15 = 35), not merely that the anchor shrank.
        Assert.Equal(35, left.OuterWidth);
    }

    [Fact]
    public void MonotoneClamp_StubRequestsMoreWhenGrantedLess_IsClampedToPreviousRequest()
    {
        // granted=30 -> legitimately shrinks to 20 (a real pass-2 change).
        // granted=20 -> misbehaves and asks for 999, MORE than its own previous request (20).
        // The clamp must hold it at 20, and the loop must still terminate within MaxPasses.
        var resolved = ResolveWithStub(granted => granted switch
        {
            null => 40,
            30 => 20,
            20 => 999,
            _ => 20,
        });

        var right = resolved.Children[1];

        Assert.Equal(20, right.OuterWidth);
        Assert.True(right.OuterWidth < 999, "the misbehaving 999 request must never reach the resolved width");
    }

    [Fact]
    public void PassCap_StubChangesRequestEveryPass_StopsAtThreePassesWithLastResolvedSizes()
    {
        // A stub that always asks for one less than whatever it was granted never converges on
        // its own — every pass produces a different request. With MaxPasses = 3 (1 initial +
        // 2 re-measurements), the trace is: ask 40 -> granted 30 -> ask 29 -> granted 29
        // -> ask 28 -> granted 28. A fourth pass would drop it to 27; it must not run.
        var resolved = ResolveWithStub(granted => granted switch
        {
            null => 40,
            int g => g - 1,
        });

        var right = resolved.Children[1];
        var left = resolved.Children[0];

        Assert.Equal(28, right.OuterWidth);
        Assert.Equal(22, left.OuterWidth);
        Assert.Equal(50, right.OuterWidth + left.OuterWidth);
    }
}
