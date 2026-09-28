using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6.6/§6.6.1 (Defect 15): border colour had two resolvers that disagreed on
/// decoration-only specs like <c>dim</c>/<c>bold</c> — the pane-tree path (<c>PaneTreeRenderer.cs</c>,
/// markup) kept them, the single-pane <c>Panel</c> path (<see cref="ColorResolution.ResolveBorderColor"/>)
/// dropped them because it narrowed to a bare <c>Color</c>. <see cref="ColorResolution.ResolveBorderColor"/>
/// is the sole call site behind the single-pane path (<c>Program.cs</c>'s <c>ComputeRows</c>), so exercising
/// it directly here covers that path without needing a full console render.
/// </summary>
public class BorderColorResolutionTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoValues = new Dictionary<string, string?>();
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> NoTokens = new Dictionary<string, ColorResolution.ColorRule>();

    [Theory]
    [InlineData("dim", Decoration.Dim)]
    [InlineData("bold", Decoration.Bold)]
    public void ResolveBorderColor_DecorationOnlySpec_PreservesDecoration(string spec, Decoration expected)
    {
        var expr = new ColorResolution.ColorExpr.Literal(spec);

        var style = ColorResolution.ResolveBorderColor(expr, NoValues, NoTokens);

        Assert.Equal(expected, style.Decoration);
        Assert.Equal(Color.Default, style.Foreground);
    }

    [Theory]
    [InlineData("dim")]
    [InlineData("bold")]
    [InlineData("olive")]
    public void ResolveBorderColor_AgreesWithPaneTreePath_ForSameSpec(string spec)
    {
        var expr = new ColorResolution.ColorExpr.Literal(spec);

        // The pane-tree path resolves via ColorResolution.Resolve and drops the spec straight into
        // Spectre markup as a tag (PaneTreeRenderer.cs / PaneBorderRenderer.Wrap); parsing that same
        // tag text is exactly what Spectre does when it renders it, so this is that path's actual
        // result, not a second implementation of it.
        var markupSpec = ColorResolution.Resolve(expr, NoValues, NoTokens);
        Assert.NotNull(markupSpec);
        Assert.True(Style.TryParse(markupSpec, out var treePathStyle));

        var singlePaneStyle = ColorResolution.ResolveBorderColor(expr, NoValues, NoTokens);

        Assert.Equal(treePathStyle.Foreground, singlePaneStyle.Foreground);
        Assert.Equal(treePathStyle.Decoration, singlePaneStyle.Decoration);
    }
}
