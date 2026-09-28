namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.10/§2.10.2: exercises the <c>collapse:true</c> compositor border grid
/// through the real config loader and the real split pipeline (<see cref="SizeResolver"/> +
/// <see cref="HeightLadder"/>/<see cref="PaneTreeRenderer"/> + <see cref="BorderGrid"/>), the same
/// path Program.cs takes — mirroring <see cref="SplitAcceptanceTests"/>'s approach for the
/// non-collapsed split.
/// </summary>
public class BorderGridTests
{
    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static readonly ItemContext BlankCtx = BlankSurfaceControl.Blank(Ctx);

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TwoPaneConfig(bool collapse, string style = "rounded") => $$"""
    {
      "surface": {
        "maxRows": 8,
        "border": { "collapse": {{(collapse ? "true" : "false")}} },
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "fill", "overflow": "wrap",
              "border": { "enabled": true, "color": "grey", "style": "{{style}}" } },
            { "size": "fill", "overflow": "wrap",
              "border": { "enabled": true, "color": "grey", "style": "{{style}}" },
              "items": [ { "item": "model" } ] }
          ]
        }
      }
    }
    """;

    private static Compositor.PaneContribution Render(string json, string columns, out SizeResolver.ResolvedPane resolved, out int boundaryCol) =>
        Render(json, columns, Ctx, out resolved, out boundaryCol);

    private static Compositor.PaneContribution Render(string json, string columns, ItemContext ctx, out SizeResolver.ResolvedPane resolved, out int boundaryCol)
    {
        var (topLevel, pane) = LoadConfig(json);
        var surfaceWidth = SurfaceLayout.ComputeWidth(columns, topLevel.ChromeReserve);
        Assert.True(surfaceWidth is int, "the test config must produce a real surface width");
        var values = ItemValueResolver.Resolve(pane, ctx, topLevel.Colors);
        var (r, contribution) = HeightLadder.Resolve(pane, surfaceWidth!.Value, topLevel.SurfaceMaxRows, ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector(), topLevel.Collapse);
        resolved = r;
        // This split root carries no border of its own (only its children declare borders), so
        // column 0 is the left child's own left edge and the shared boundary sits immediately
        // after the left child's own outer width.
        boundaryCol = resolved.Children[0].OuterWidth;
        return contribution;
    }

    private static string Markup(Compositor.PaneContribution contribution) =>
        string.Join('\n', contribution.Buffer.Rows.Select(r => r.Markup));

    [Fact]
    public void Collapse_EveryComposedRowIsExactlyTheSurfaceWidth()
    {
        var (topLevel, pane) = LoadConfig(TwoPaneConfig(collapse: true));
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;

        var contribution = Render(TwoPaneConfig(collapse: true), "112", out _, out _);

        Assert.All(contribution.Buffer.Rows, r =>
        {
            Assert.Equal(surfaceWidth, r.Width);
            Assert.Equal(r.Width, DisplayWidth.Measure(r.Markup));
        });

        // SPEC §10.1 blank-surface control: the rectangle invariant passes trivially on a blank
        // surface (a compositor bug that pads with the wrong width would still be caught), so it
        // must be re-asserted, plus the two renders must be content-distinguishable.
        var blankContribution = Render(TwoPaneConfig(collapse: true), "112", BlankCtx, out _, out _);
        Assert.All(blankContribution.Buffer.Rows, r =>
        {
            Assert.Equal(surfaceWidth, r.Width);
            Assert.Equal(r.Width, DisplayWidth.Measure(r.Markup));
        });
        BlankSurfaceControl.AssertContentDiffers(Markup(contribution), Markup(blankContribution));
    }

    [Fact]
    public void Collapse_TopOfInteriorBoundaryRendersTeeDownGlyph()
    {
        var contribution = Render(TwoPaneConfig(collapse: true), "112", out _, out var boundaryCol);
        var topRow = DisplayWidth.Strip(contribution.Buffer.Rows[0].Markup);

        Assert.Equal('┬', topRow[boundaryCol]);

        // SPEC §10.1 blank-surface control intentionally omitted: blanking the right pane's only
        // item collapses BOTH panes' content to zero rows, and PaneTreeRenderer.cs's height-suppression
        // heuristic (naturalTotal < 3 => omit both border edges) then drops this pane's entire border
        // contribution rather than rendering a 2-row empty box — the composited Buffer.Rows comes back
        // empty for the blank run, throwing before any glyph could be checked. That is a pre-existing
        // product-code edge case (a bordered pane with genuinely zero content rows), not something a
        // test-authoring pass should paper over or silently work around; flagged upstream instead.
    }

    [Fact]
    public void Collapse_BottomOfInteriorBoundaryRendersTeeUpGlyph()
    {
        var contribution = Render(TwoPaneConfig(collapse: true), "112", out _, out var boundaryCol);
        var bottomRow = DisplayWidth.Strip(contribution.Buffer.Rows[^1].Markup);

        Assert.Equal('┴', bottomRow[boundaryCol]);

        // SPEC §10.1 blank-surface control intentionally omitted: see the identical note in
        // Collapse_TopOfInteriorBoundaryRendersTeeDownGlyph — blanking collapses this pane's Buffer.Rows
        // to empty via a pre-existing PaneTreeRenderer.cs height-suppression edge case, flagged upstream.
    }

    [Fact]
    public void Collapse_HeavyStyle_UsesHeavyTeeGlyphs()
    {
        var contribution = Render(TwoPaneConfig(collapse: true, style: "heavy"), "112", out _, out var boundaryCol);
        var topRow = DisplayWidth.Strip(contribution.Buffer.Rows[0].Markup);
        var bottomRow = DisplayWidth.Strip(contribution.Buffer.Rows[^1].Markup);

        Assert.Equal('┳', topRow[boundaryCol]);
        Assert.Equal('┻', bottomRow[boundaryCol]);

        // SPEC §10.1 blank-surface control intentionally omitted: see the identical note in
        // Collapse_TopOfInteriorBoundaryRendersTeeDownGlyph — blanking collapses this pane's Buffer.Rows
        // to empty via a pre-existing PaneTreeRenderer.cs height-suppression edge case, flagged upstream.
    }

    [Fact]
    public void Collapse_AsciiStyle_UsesPlusGlyph()
    {
        var contribution = Render(TwoPaneConfig(collapse: true, style: "ascii"), "112", out _, out var boundaryCol);
        var topRow = DisplayWidth.Strip(contribution.Buffer.Rows[0].Markup);

        Assert.Equal('+', topRow[boundaryCol]);

        // SPEC §10.1 blank-surface control intentionally omitted: see the identical note in
        // Collapse_TopOfInteriorBoundaryRendersTeeDownGlyph — blanking collapses this pane's Buffer.Rows
        // to empty via a pre-existing PaneTreeRenderer.cs height-suppression edge case, flagged upstream.
    }

    [Fact]
    public void Collapse_InteriorBoundaryColumnIsSharedNotDoubled()
    {
        // §2.10.2: under collapse:true a shared boundary costs exactly one column, so the split's
        // two bordered children plus the single shared column plus the root's own two border
        // columns must exactly account for the surface width (no extra gutter reserved on top).
        var (topLevel, pane) = LoadConfig(TwoPaneConfig(collapse: true));
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var contribution = Render(TwoPaneConfig(collapse: true), "112", out var resolved, out _);

        var accountedFor = resolved.Children[0].OuterWidth
            + 1 /* the shared boundary column */
            + resolved.Children[1].OuterWidth;

        Assert.Equal(surfaceWidth, accountedFor);
        Assert.True(contribution.Buffer.Rows.Count > 0);

        // SPEC §10.1 blank-surface control: boundary-column accounting is gutter/border math,
        // independent of the right pane's item content, so it must still hold on a blank surface.
        var blankContribution = Render(TwoPaneConfig(collapse: true), "112", BlankCtx, out var blankResolved, out _);
        var blankAccountedFor = blankResolved.Children[0].OuterWidth + 1 + blankResolved.Children[1].OuterWidth;
        Assert.Equal(surfaceWidth, blankAccountedFor);
        BlankSurfaceControl.AssertContentDiffers(Markup(contribution), Markup(blankContribution));
    }

    [Fact]
    public void CollapseFalse_StillDrawsABlankGutterColumn_BetweenTheChildrensOwnBorders()
    {
        // Regression: collapse:false must keep drawing each child's own uncontested edge plus a
        // blank gutter column between them, exactly as before this task's changes.
        var contribution = Render(TwoPaneConfig(collapse: false), "112", out var resolved, out _);
        var row = DisplayWidth.Strip(contribution.Buffer.Rows[1].Markup);

        var leftOwnRightEdgeCol = resolved.Children[0].OuterWidth - 1;
        var gutterCol = leftOwnRightEdgeCol + 1;
        var rightOwnLeftEdgeCol = gutterCol + 1;

        Assert.NotEqual(' ', row[leftOwnRightEdgeCol]);
        Assert.Equal(' ', row[gutterCol]);
        Assert.NotEqual(' ', row[rightOwnLeftEdgeCol]);

        // SPEC §10.1 blank-surface control intentionally omitted: see the identical note in
        // Collapse_TopOfInteriorBoundaryRendersTeeDownGlyph — blanking collapses this pane's Buffer.Rows
        // to empty via a pre-existing PaneTreeRenderer.cs height-suppression edge case, flagged upstream.
    }
}
