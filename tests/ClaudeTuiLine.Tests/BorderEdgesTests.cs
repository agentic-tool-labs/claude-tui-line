using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.10/§2.10.1: per-edge border config under <c>collapse: false</c> — the
/// <c>edges</c> object, the <c>all</c>/<c>none</c>/<c>outline</c>/<c>inside</c> shorthands, rule 1's
/// nearest-declaration-wins override, and the <c>border-inside-on-leaf</c> diagnostic.
/// </summary>
public class BorderEdgesTests
{
    private static readonly ResolvedConfig TopLevel = new(
        new ColorResolution.ColorExpr.Literal("grey"),
        BoxBorder.Rounded,
        PaneBorderEdges.All,
        ChromeReserve: 3,
        ColorSystem: ColorSystemSupport.Standard,
        Colors: new Dictionary<string, ColorResolution.ColorRule>());

    [Fact]
    public void ExplicitEdgesObject_OmittedBooleansDefaultTrue()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Border = new BorderConfig { Edges = new BorderEdgesConfig { Top = false } },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(new PaneBorderEdges(Top: false, Right: true, Bottom: true, Left: true), pane.Border.Edges);
        Assert.NotNull(pane.Border.Style);
    }

    [Fact]
    public void ShorthandNone_AllEdgesOff_StyleCollapsesToNull()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Border = new BorderConfig { Shorthand = "none" } } },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.None, pane.Border.Edges);
        Assert.Null(pane.Border.Style);
    }

    [Fact]
    public void ShorthandAll_AllEdgesOnExplicitly()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Border = new BorderConfig { Shorthand = "all" } } },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.All, pane.Border.Edges);
        Assert.NotNull(pane.Border.Style);
    }

    [Fact]
    public void UnrecognizedShorthand_FallsBackToAllEdges()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Border = new BorderConfig { Shorthand = "diagonal" } } },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.All, pane.Border.Edges);
    }

    [Fact]
    public void ShorthandOutline_OwnEdgesAll_ChildrenForcedOff()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Border = new BorderConfig { Shorthand = "outline", Enabled = true },
                    Children = new List<PaneConfig> { new(), new() },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.All, pane.Border.Edges);
        Assert.NotNull(pane.Border.Style);

        Assert.Equal(2, pane.Children.Count);
        foreach (var child in pane.Children)
        {
            Assert.Equal(PaneBorderEdges.None, child.Border.Edges);
            Assert.Null(child.Border.Style);
        }
    }

    [Fact]
    public void ShorthandOutline_ChildOwnDeclarationOverridesPropagation()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Border = new BorderConfig { Shorthand = "outline" },
                    Children = new List<PaneConfig>
                    {
                        new() { Border = new BorderConfig { Edges = new BorderEdgesConfig { Left = false } } },
                        new(),
                    },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(new PaneBorderEdges(Top: true, Right: true, Bottom: true, Left: false), pane.Children[0].Border.Edges);
        Assert.NotNull(pane.Children[0].Border.Style);

        Assert.Equal(PaneBorderEdges.None, pane.Children[1].Border.Edges);
    }

    [Fact]
    public void ShorthandInside_VerticalSplit_ComputesColumnDividerEdgesForThreeChildren()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Border = new BorderConfig { Shorthand = "inside" },
                    Children = new List<PaneConfig> { new(), new(), new() },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.None, pane.Border.Edges);
        Assert.Null(pane.Border.Style);

        Assert.Equal(new PaneBorderEdges(Top: false, Right: true, Bottom: false, Left: false), pane.Children[0].Border.Edges);
        Assert.Equal(new PaneBorderEdges(Top: false, Right: true, Bottom: false, Left: true), pane.Children[1].Border.Edges);
        Assert.Equal(new PaneBorderEdges(Top: false, Right: false, Bottom: false, Left: true), pane.Children[2].Border.Edges);
    }

    [Fact]
    public void ShorthandInside_HorizontalSplit_ComputesRowDividerEdgesForTwoChildren()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Border = new BorderConfig { Shorthand = "inside" },
                    Children = new List<PaneConfig> { new(), new() },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(new PaneBorderEdges(Top: false, Right: false, Bottom: true, Left: false), pane.Children[0].Border.Edges);
        Assert.Equal(new PaneBorderEdges(Top: true, Right: false, Bottom: false, Left: false), pane.Children[1].Border.Edges);
    }

    // §2.10.1 rule 1 is ambiguous about whether "inside"'s propagation continues past the declaring
    // split's direct children into deeper, un-overridden nested splits, or stops at one level. This
    // test locks in the interim, non-speculative interpretation actually implemented: propagation
    // stops after one level, so a grandchild the "inside" split never directly divides gets no
    // inherited directive at all and resolves to the ordinary default (all four edges on) rather
    // than either candidate reading of continued propagation. Flagged for peer/architect review.
    [Fact]
    public void ShorthandInside_DoesNotPropagatePastDirectChildren()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Border = new BorderConfig { Shorthand = "inside" },
                    Children = new List<PaneConfig>
                    {
                        new()
                        {
                            Split = "vertical",
                            Children = new List<PaneConfig> { new(), new() },
                        },
                        new(),
                    },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        var nestedSplit = pane.Children[0];
        Assert.Equal(new PaneBorderEdges(Top: false, Right: true, Bottom: false, Left: false), nestedSplit.Border.Edges);

        foreach (var grandchild in nestedSplit.Children)
        {
            Assert.Equal(PaneBorderEdges.All, grandchild.Border.Edges);
        }
    }

    [Fact]
    public void ShorthandInside_OnLeaf_SilencesBorderEntirely()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Border = new BorderConfig { Shorthand = "inside" } } },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.None, pane.Border.Edges);
        Assert.Null(pane.Border.Style);
    }

    [Fact]
    public void ShorthandInside_OnLeaf_ReportsBorderInsideOnLeafDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Border = new BorderConfig { Shorthand = "inside" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d =>
            d.Code == "border-inside-on-leaf" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Path == "/surface/pane/border" &&
            d.Message == "a leaf has no interior, so this silences its border entirely");
    }

    [Fact]
    public void ShorthandInside_OnSplitWithChildren_DoesNotReportBorderInsideOnLeaf()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Border = new BorderConfig { Shorthand = "inside" },
                    Children = new List<PaneConfig> { new(), new() },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "border-inside-on-leaf");
    }

    [Fact]
    public void NoBorderDeclaration_DefaultsToAllEdges()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig() },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneBorderEdges.All, pane.Border.Edges);
    }

    [Fact]
    public void TopLevelBorder_ShorthandNone_EdgesNoneAndStyleNull()
    {
        var config = new UserConfig { Border = new BorderConfig { Shorthand = "none" } };

        var resolved = ConfigLoader.ResolveTopLevel(config);

        Assert.Equal(PaneBorderEdges.None, resolved.Edges);
        Assert.Null(resolved.Style);
    }
}
