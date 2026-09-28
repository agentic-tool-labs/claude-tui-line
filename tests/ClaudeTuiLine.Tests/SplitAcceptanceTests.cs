namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.9: the two-pane acceptance target for splits, run against the exact
/// JSON from the spec through the real config loader and the real split pipeline
/// (<see cref="SizeResolver"/> + <see cref="PaneTreeRenderer"/>) — the same path Program.cs
/// takes. Per §2.9's own instruction, these assert behaviour/invariants (the cap binds,
/// requests are monotone non-increasing, every composed row is exactly the surface width) rather
/// than the specific integers in the spec's worked trace,
/// since those are consequences of real width measurement the spec explicitly declines to
/// freeze ("these integers are derived, not asserted").
/// </summary>
public class SplitAcceptanceTests
{
    private const string ConfigJson = """
    {
      "colors": { "model-accent": { "default": "blue" } },
      "surface": {
        "maxRows": 8,
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "fill", "overflow": "wrap",
              "border": { "enabled": true, "color": "grey" } },
            { "size": "content", "overflow": "wrap",
              "border": { "enabled": true, "color": "@model-accent" },
              "items": [ { "item": "model", "color": "@model-accent" },
                         { "item": "effort" }, { "item": "thinking" }, { "item": "context" } ] }
          ]
        }
      }
    }
    """;

    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static readonly ItemContext BlankCtx = BlankSurfaceControl.Blank(Ctx);

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadAcceptanceConfig()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ConfigJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SizeResolver.ResolvedPane Resolve(string columns)
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth(columns, topLevel.ChromeReserve);
        Assert.True(surfaceWidth is int, "the acceptance config must produce a real surface width");
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        return SizeResolver.Resolve(pane, surfaceWidth!.Value, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
    }

    private static string RenderMarkup(SizeResolver.ResolvedPane resolved, ResolvedConfig topLevel, ItemContext ctx, IReadOnlyDictionary<string, string?> values) =>
        string.Join('\n', PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector()).Buffer.Rows.Select(r => r.Markup));

    /// <summary>SPEC §10.1: the blank-surface counterpart of <see cref="Resolve"/> — same tree, every item forced empty via <see cref="BlankCtx"/>.</summary>
    private static SizeResolver.ResolvedPane ResolveBlank(string columns)
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth(columns, topLevel.ChromeReserve);
        Assert.True(surfaceWidth is int, "the acceptance config must produce a real surface width");
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        return SizeResolver.Resolve(pane, surfaceWidth!.Value, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
    }

    [Fact]
    public void Columns112_RootSplitsIntoExactlyTwoChildren()
    {
        var resolved = Resolve("112");

        Assert.Equal(2, resolved.Children.Count);
    }

    [Fact]
    public void Columns112_EveryComposedRowIsExactlyTheSurfaceWidth()
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.All(rendered.Buffer.Rows, r =>
        {
            Assert.Equal(surfaceWidth, r.Width);
            Assert.Equal(r.Width, DisplayWidth.Measure(r.Markup));
        });

        // SPEC §10.1 blank-surface control: the invariant must still hold with every item empty,
        // and the populated/blank renders must be content-distinguishable.
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankRendered = PaneTreeRenderer.Render(blankResolved, BlankCtx, blankValues, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        Assert.All(blankRendered.Buffer.Rows, r =>
        {
            Assert.Equal(surfaceWidth, r.Width);
            Assert.Equal(r.Width, DisplayWidth.Measure(r.Markup));
        });
        BlankSurfaceControl.AssertContentDiffers(
            string.Join('\n', rendered.Buffer.Rows.Select(r => r.Markup)),
            string.Join('\n', blankRendered.Buffer.Rows.Select(r => r.Markup)));
    }

    [Fact]
    public void Columns60_EveryComposedRowIsExactlyTheSurfaceWidth()
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.All(rendered.Buffer.Rows, r =>
        {
            Assert.Equal(surfaceWidth, r.Width);
            Assert.Equal(r.Width, DisplayWidth.Measure(r.Markup));
        });

        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankRendered = PaneTreeRenderer.Render(blankResolved, BlankCtx, blankValues, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        Assert.All(blankRendered.Buffer.Rows, r =>
        {
            Assert.Equal(surfaceWidth, r.Width);
            Assert.Equal(r.Width, DisplayWidth.Measure(r.Markup));
        });
        BlankSurfaceControl.AssertContentDiffers(
            string.Join('\n', rendered.Buffer.Rows.Select(r => r.Markup)),
            string.Join('\n', blankRendered.Buffer.Rows.Select(r => r.Markup)));
    }

    [Fact]
    public void Columns60_MaxSizeCapBindsBeforeAnyRemeasurement()
    {
        // §2.3 step 4's cap = (surface - gutter) - reserve, where reserve is the fill sibling's
        // own floor (MinUsableWidth + its own border reserve) — a structural bound independent
        // of the content pane's own measurement, per the spec's own §2.9 trace: "floor(left) =
        // MinUsableWidth 20 + borderReserve 4 = 24".
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var fillFloor = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(pane.Children[0]);
        var expectedCap = surfaceWidth - pane.Gutter - fillFloor;

        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var right = resolved.Children[1];

        Assert.True(right.OuterWidth <= expectedCap,
            $"right pane ({right.OuterWidth}) must never exceed the step-4 cap ({expectedCap})");

        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankRight = blankResolved.Children[1];
        Assert.True(blankRight.OuterWidth <= expectedCap,
            $"blank-surface right pane ({blankRight.OuterWidth}) must never exceed the step-4 cap ({expectedCap})");
        BlankSurfaceControl.AssertContentDiffers(
            RenderMarkup(resolved, topLevel, Ctx, values), RenderMarkup(blankResolved, topLevel, BlankCtx, blankValues));
    }

    [Fact]
    public void Columns60_FixpointActuallyRanASecondPass_NotJustThePassOneCap()
    {
        // If the fixpoint stopped after pass 1, right would be granted exactly the step-4 cap
        // (see Columns60_MaxSizeCapBindsBeforeAnyRemeasurement). A strictly smaller final width
        // is only reachable if pass 2 re-measured under that cap and the packed items wrapped
        // narrower than it — proof, not assumption, that the loop iterated at least twice.
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var fillFloor = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(pane.Children[0]);
        var passOneCap = surfaceWidth - pane.Gutter - fillFloor;

        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var right = resolved.Children[1];

        Assert.True(right.OuterWidth < passOneCap,
            $"right pane ({right.OuterWidth}) must be strictly below the pass-1 cap ({passOneCap}) to prove re-measurement occurred");

        // Blank-surface control: with every item empty, the content pane never wraps in the first
        // place, so a second pass is not expected to (and need not) drive it strictly below the
        // pass-1 cap. We assert the weaker structural bound (still within the cap) plus
        // distinguishability, rather than re-asserting the strict "<" that is specific to
        // wrap-triggered re-measurement of non-empty content.
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankRight = blankResolved.Children[1];
        Assert.True(blankRight.OuterWidth <= passOneCap,
            $"blank-surface right pane ({blankRight.OuterWidth}) must never exceed the pass-1 cap ({passOneCap})");
        BlankSurfaceControl.AssertContentDiffers(
            RenderMarkup(resolved, topLevel, Ctx, values), RenderMarkup(blankResolved, topLevel, BlankCtx, blankValues));
    }

    [Fact]
    public void Columns60_FreedColumnsReachTheFillSibling_LeftGrantExceedsItsPassOneFloor()
    {
        // §2.3/§2.9: freed space must reach the sibling, not sit unused inside the anchor. The
        // anchor staying under its own cap (see the two tests above) is consistent with a
        // re-measure that is a complete no-op; the assertion that actually proves the freed
        // columns landed somewhere is the sibling's own final grant exceeding what it got in
        // pass 1 — pass 1 leaves a `fill` pane at exactly its own floor.
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var fillFloor = RowLayout.MinUsableWidth + SizeResolver.OwnBorderReserve(pane.Children[0]);

        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var left = resolved.Children[0];

        Assert.True(left.OuterWidth > fillFloor,
            $"left pane ({left.OuterWidth}) must exceed its pass-1 floor-driven grant ({fillFloor}) once the right pane's wrap-aware re-measure frees columns back to it");

        // Blank-surface control: with the content pane's items empty, there is nothing to wrap,
        // so no columns are freed back to the fill sibling — it stays at its floor. We assert that
        // structural floor still holds (rather than re-asserting the strict ">" that only holds
        // when real content wraps) plus distinguishability from the populated run.
        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankLeft = blankResolved.Children[0];
        Assert.True(blankLeft.OuterWidth >= fillFloor,
            $"blank-surface left pane ({blankLeft.OuterWidth}) must never fall below its floor ({fillFloor})");
        BlankSurfaceControl.AssertContentDiffers(
            RenderMarkup(resolved, topLevel, Ctx, values), RenderMarkup(blankResolved, topLevel, BlankCtx, blankValues));
    }

    [Fact]
    public void Columns60_RequestsAreMonotoneNonIncreasing_FinalGrantNeverExceedsColumns112Grant()
    {
        var right112 = Resolve("112").Children[1];
        var right60 = Resolve("60").Children[1];

        Assert.True(right60.OuterWidth <= right112.OuterWidth,
            "a narrower surface must never grant the anchor pane more than a wider one did");

        var blankRight112 = ResolveBlank("112").Children[1];
        var blankRight60 = ResolveBlank("60").Children[1];
        Assert.True(blankRight60.OuterWidth <= blankRight112.OuterWidth,
            "blank-surface: a narrower surface must never grant the anchor pane more than a wider one did");
        Assert.True(blankRight112.OuterWidth < right112.OuterWidth,
            "blank-surface content pane must measure narrower than its populated counterpart");
    }

    [Fact]
    public void Columns112_EveryPaneSpansTheFullSurfaceHeight()
    {
        // §2.2: a vertical split's children share its height, so every child's border box must
        // extend the full surface height rather than just its own natural content height. Checked
        // via the right pane's own border-vertical columns, not the row count 7.
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var surfaceRowCount = rendered.Buffer.Rows.Count;
        var rightPane = resolved.Children[1];
        var rightStartCol = resolved.Children[0].OuterWidth + pane.Gutter;

        var rowsWithRightPaneBorder = rendered.Buffer.Rows.Count(r =>
        {
            var plain = DisplayWidth.Strip(r.Markup);
            return plain[rightStartCol] != ' ' && plain[rightStartCol + rightPane.OuterWidth - 1] != ' ';
        });

        Assert.Equal(surfaceRowCount, rowsWithRightPaneBorder);

        var blankValues = ItemValueResolver.Resolve(pane, BlankCtx, topLevel.Colors);
        var blankResolved = SizeResolver.Resolve(pane, surfaceWidth, BlankCtx, blankValues,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankRendered = PaneTreeRenderer.Render(blankResolved, BlankCtx, blankValues, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var blankRowCount = blankRendered.Buffer.Rows.Count;
        var blankRightPane = blankResolved.Children[1];
        var blankRightStartCol = blankResolved.Children[0].OuterWidth + pane.Gutter;
        var blankRowsWithRightPaneBorder = blankRendered.Buffer.Rows.Count(r =>
        {
            var plain = DisplayWidth.Strip(r.Markup);
            return plain[blankRightStartCol] != ' ' && plain[blankRightStartCol + blankRightPane.OuterWidth - 1] != ' ';
        });
        Assert.Equal(blankRowCount, blankRowsWithRightPaneBorder);
        BlankSurfaceControl.AssertContentDiffers(
            string.Join('\n', rendered.Buffer.Rows.Select(r => r.Markup)),
            string.Join('\n', blankRendered.Buffer.Rows.Select(r => r.Markup)));
    }
}
