using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5: every other test in this suite hand-builds its own <c>values</c>
/// dictionary and passes it directly into SizeResolver/PaneTreeRenderer/PaneAssembler/LeafItems, so
/// none of them exercise the production wiring that actually populates <c>values</c> from a real
/// config's <c>items</c> array — <c>ConfigLoader.LoadAll</c> -&gt; <c>ItemValueResolver.ResolveAsync</c>
/// -&gt; <c>SizeResolver.Resolve</c> -&gt; <c>PaneTreeRenderer.Render</c>, the exact chain
/// <c>Program.cs</c> itself runs. This drives that chain end to end, for a configured item id other
/// than <c>model-short</c>, and asserts its resolved value actually reaches the rendered text.
/// </summary>
public class EndToEndItemValuesTests
{
    private static readonly StatusInput Input = new() { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };

    private const string ConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "fill", "border": { "enabled": true }, "items": [ { "item": "model" } ] },
            { "size": "content", "border": { "enabled": true }, "items": [ { "item": "model-short" } ] }
          ]
        }
      }
    }
    """;

    [Fact]
    public async Task ConfiguredNonModelShortItem_RenderedThroughRealProductionPath_ProducesNonEmptyText()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ConfigJson);
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var ctx = new ItemContext(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var values = (await ItemValueResolver.ResolveAsync(
            pane, ctx, topLevel.Colors, rawStdinJson: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null,
            new RenderNoteCollector())).Values;

        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var plainText = string.Join('\n', rendered.Buffer.Rows.Select(r => Markup.Remove(r.Markup)));

        Assert.Contains("Claude Opus 4.5", plainText);
    }

    private const string CompoundOnlyConfigJson = """
    {
      "surface": {
        "pane": {
          "items": [ { "id": "badge", "parts": [ { "text": "agent:" }, { "text": "static" } ] } ]
        }
      }
    }
    """;

    private const string CompoundWithBuiltinPartConfigJson = """
    {
      "surface": {
        "pane": {
          "items": [ { "id": "badge", "parts": [ { "text": "model:" }, { "item": "model-short" } ] } ]
        }
      }
    }
    """;

    private const string CompoundAlongsideOrdinaryItemConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "fill", "border": { "enabled": true }, "items": [ { "id": "badge", "parts": [ { "text": "tag:", "color": "aqua" } ] } ] },
            { "size": "content", "border": { "enabled": true }, "items": [ { "item": "model-short" } ] }
          ]
        }
      }
    }
    """;

    // SPEC-87 §12.4's additivity claim, made falsifiable: a config whose compounds are never
    // reached through the new cross-pane fallback (no item selector, link, or from names a
    // compound id anywhere else) must render byte-identically whether it's given the real,
    // whole-tree compound map or an empty one — the new lookup path is provably never consulted.
    private static async Task<string> RenderRowsAsync(string configJson, IReadOnlyDictionary<string, Segment> compounds)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, configJson);
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var ctx = new ItemContext(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var values = (await ItemValueResolver.ResolveAsync(
            pane, ctx, topLevel.Colors, rawStdinJson: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null,
            new RenderNoteCollector())).Values;

        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values, compounds, new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors, compounds, new RenderNoteCollector());

        return string.Join('\n', rendered.Buffer.Rows.Select(r => r.Markup));
    }

    private static async Task AssertByteIdenticalWithAndWithoutRealCompoundMapAsync(string configJson)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, configJson);
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var ctx = new ItemContext(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var values = (await ItemValueResolver.ResolveAsync(
            pane, ctx, topLevel.Colors, rawStdinJson: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null,
            new RenderNoteCollector())).Values;

        var realCompounds = LeafItems.BuildCompoundMap(pane, values, ctx, topLevel.Colors);

        var withRealMap = await RenderRowsAsync(configJson, realCompounds);
        var withEmptyMap = await RenderRowsAsync(configJson, new Dictionary<string, Segment>());

        Assert.Equal(withRealMap, withEmptyMap);
    }

    [Fact]
    public Task CompoundTextOnly_NoCrossPaneReference_RendersByteIdenticallyWithEmptyCompoundsMap() =>
        AssertByteIdenticalWithAndWithoutRealCompoundMapAsync(CompoundOnlyConfigJson);

    [Fact]
    public Task CompoundWithBuiltinPart_NoCrossPaneReference_RendersByteIdenticallyWithEmptyCompoundsMap() =>
        AssertByteIdenticalWithAndWithoutRealCompoundMapAsync(CompoundWithBuiltinPartConfigJson);

    [Fact]
    public Task CompoundAlongsideOrdinaryItem_NoCrossPaneReference_RendersByteIdenticallyWithEmptyCompoundsMap() =>
        AssertByteIdenticalWithAndWithoutRealCompoundMapAsync(CompoundAlongsideOrdinaryItemConfigJson);
}
