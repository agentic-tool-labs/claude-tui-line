namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.8.3: a vertical split's children share one band
/// (<c>height(vertical split) = max(height(children))</c>), and a pane may declare
/// <c>height: "content"</c> to close its own border immediately under its last content row
/// instead of stretching to the band, with <c>valign</c> then placing that shorter box within
/// the band. The right pane here always has fewer items than the left, so it is always the
/// shorter sibling — these tests assert on that relationship programmatically (via rendering the
/// right pane alone) rather than on hardcoded row counts.
/// </summary>
public class PaneHeightContentTests
{
    private const string FillConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "20", "border": { "enabled": true },
              "items": [ { "item": "model" }, { "item": "effort" } ] },
            { "size": "20", "border": { "enabled": true },
              "items": [ { "item": "model" } ] }
          ]
        }
      }
    }
    """;

    private const string ContentConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "20", "border": { "enabled": true },
              "items": [ { "item": "model" }, { "item": "effort" } ] },
            { "size": "20", "height": "content", "border": { "enabled": true },
              "items": [ { "item": "model" } ] }
          ]
        }
      }
    }
    """;

    private const string ContentBottomValignConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "20", "border": { "enabled": true },
              "items": [ { "item": "model" }, { "item": "effort" } ] },
            { "size": "20", "height": "content", "valign": "bottom", "border": { "enabled": true },
              "items": [ { "item": "model" } ] }
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

    private static (Pane RootPane, SizeResolver.ResolvedPane Resolved, Compositor.PaneContribution Rendered, IReadOnlyDictionary<string, string?> Values, IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens) RenderAt(string json, string columns)
    {
        var (topLevel, pane) = LoadConfig(json);
        var surfaceWidth = SurfaceLayout.ComputeWidth(columns, topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        return (pane, resolved, rendered, values, topLevel.Colors);
    }

    // The right pane's own natural (unpadded) row count — rendering it as a standalone leaf at
    // its already-resolved outer width, independent of whatever the split composed it into.
    private static int RightPaneNaturalRowCount(Pane rootPane, SizeResolver.ResolvedPane resolved, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        var right = resolved.Children[1];
        var alone = new SizeResolver.ResolvedPane(right.Source, right.OuterWidth, Array.Empty<SizeResolver.ResolvedPane>());
        return PaneTreeRenderer.Render(alone, Ctx, values, tokens,new Dictionary<string, Segment>(),  new RenderNoteCollector()).Buffer.Rows.Count;
    }

    [Fact]
    public void HeightContent_DoesNotChangeTotalSurfaceRowCount()
    {
        var fill = RenderAt(FillConfigJson, "80");
        var content = RenderAt(ContentConfigJson, "80");

        Assert.Equal(fill.Rendered.Buffer.Rows.Count, content.Rendered.Buffer.Rows.Count);
    }

    [Fact]
    public void HeightContent_RightPaneBorderSpansExactlyItsNaturalHeight_NotTheFullBand()
    {
        var (rootPane, resolved, rendered, values, tokens) = RenderAt(ContentConfigJson, "80");
        var surfaceRows = rendered.Buffer.Rows.Count;
        var rightNatural = RightPaneNaturalRowCount(rootPane, resolved, values, tokens);

        Assert.True(rightNatural < surfaceRows,
            $"fixture must give the right pane fewer natural rows ({rightNatural}) than the band ({surfaceRows}) for this test to be meaningful");

        var rightPane = resolved.Children[1];
        var rightStartCol = resolved.Children[0].OuterWidth + rootPane.Gutter;

        var borderedRowCount = rendered.Buffer.Rows.Count(r =>
        {
            var plain = DisplayWidth.Strip(r.Markup);
            return plain[rightStartCol] != ' ' && plain[rightStartCol + rightPane.OuterWidth - 1] != ' ';
        });

        Assert.Equal(rightNatural, borderedRowCount);
    }

    [Fact]
    public void HeightContent_DefaultValignTop_BandRemainderBelowIsBlankOutsideAnyBorder()
    {
        var (rootPane, resolved, rendered, values, tokens) = RenderAt(ContentConfigJson, "80");
        var surfaceRows = rendered.Buffer.Rows.Count;
        var rightNatural = RightPaneNaturalRowCount(rootPane, resolved, values, tokens);

        var rightPane = resolved.Children[1];
        var rightStartCol = resolved.Children[0].OuterWidth + rootPane.Gutter;

        for (var row = rightNatural; row < surfaceRows; row++)
        {
            var plain = DisplayWidth.Strip(rendered.Buffer.Rows[row].Markup);
            var band = plain.Substring(rightStartCol, rightPane.OuterWidth);
            Assert.True(band.All(c => c == ' '), $"row {row} in the right pane's band remainder must be blank surface background, got '{band}'");
        }
    }

    [Fact]
    public void HeightContent_LeftFillSiblingStillSpansTheFullBand()
    {
        var (rootPane, resolved, rendered, values, tokens) = RenderAt(ContentConfigJson, "80");
        var surfaceRows = rendered.Buffer.Rows.Count;
        var leftPane = resolved.Children[0];

        var borderedRowCount = rendered.Buffer.Rows.Count(r =>
        {
            var plain = DisplayWidth.Strip(r.Markup);
            return plain[0] != ' ' && plain[leftPane.OuterWidth - 1] != ' ';
        });

        Assert.Equal(surfaceRows, borderedRowCount);
    }

    [Fact]
    public void HeightContent_ValignBottom_BoxSitsAtBandBottomInstead()
    {
        var (rootPane, resolved, rendered, values, tokens) = RenderAt(ContentBottomValignConfigJson, "80");
        var surfaceRows = rendered.Buffer.Rows.Count;
        var rightNatural = RightPaneNaturalRowCount(rootPane, resolved, values, tokens);
        var deficit = surfaceRows - rightNatural;

        var rightPane = resolved.Children[1];
        var rightStartCol = resolved.Children[0].OuterWidth + rootPane.Gutter;

        for (var row = 0; row < deficit; row++)
        {
            var plain = DisplayWidth.Strip(rendered.Buffer.Rows[row].Markup);
            var band = plain.Substring(rightStartCol, rightPane.OuterWidth);
            Assert.True(band.All(c => c == ' '), $"row {row} must be blank before the bottom-aligned box begins, got '{band}'");
        }

        var borderedRowCount = rendered.Buffer.Rows.Count(r =>
        {
            var plain = DisplayWidth.Strip(r.Markup);
            return plain[rightStartCol] != ' ' && plain[rightStartCol + rightPane.OuterWidth - 1] != ' ';
        });

        Assert.Equal(rightNatural, borderedRowCount);
    }
}
