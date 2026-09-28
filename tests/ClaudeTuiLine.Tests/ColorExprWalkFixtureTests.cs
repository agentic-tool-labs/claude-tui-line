namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.5.1 step 5c: <see cref="ReferenceExtractorCoverageTests"/> proves every
/// reference-carrying member is handled (fail-closed over members), but reflection cannot prove
/// <c>Walk</c> actually visits every place those types live in the pane tree (fail-open over
/// sites) — it would stay green even if <c>ItemValueResolver.cs</c>'s pane-border or item-color
/// statements were deleted. This pins those site-visiting statements with one fixture exercising,
/// in a single document, a top-level pane border colour, a nested child pane's border colour (as
/// an <c>@name</c> token reference, exercising the <c>colors</c>-table link), an item-level
/// <c>color</c>, and (SPEC-44-color-token-in-rule-branches.md §4.3) an inline rule's threshold
/// branch naming an <c>@name</c> token — the new traversal-site class NE-1 found <c>Walk</c> did
/// not previously visit at all.
/// </summary>
public class ColorExprWalkFixtureTests
{
    [Fact]
    public void ScanReferencesFindsEveryColorExprSiteInOneDocument()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["accent"] = new ColorRuleJsonConfig { Default = "green" },
                ["danger"] = new ColorRuleJsonConfig { Default = "red" },
            },
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Border = new BorderConfig { Color = new ColorExprJsonConfig { Literal = "blue" } },
                    Children = new List<PaneConfig>
                    {
                        new PaneConfig
                        {
                            Border = new BorderConfig { Color = new ColorExprJsonConfig { Literal = "@accent" } },
                            Items = new List<PaneItemJsonConfig>
                            {
                                new PaneItemJsonConfig { Item = "directory", Color = new ColorExprJsonConfig { Literal = "red" } },
                                new PaneItemJsonConfig
                                {
                                    Item = "directory",
                                    Color = new ColorExprJsonConfig
                                    {
                                        Rule = new ColorRuleJsonConfig
                                        {
                                            From = "directory",
                                            Thresholds = new List<ThresholdJsonConfig>
                                            {
                                                new ThresholdJsonConfig { Min = 50, Color = "@danger" },
                                            },
                                            Default = "grey",
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = ConfigLoader.ResolveTopLevel(config);
        var root = ConfigLoader.ResolveRootPane(config, topLevel);
        var scan = ItemValueResolver.ScanReferences(root, "/surface/pane", topLevel.Colors);

        var paths = scan.ColorExprs.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "/surface/pane/border/color",
            "/surface/pane/children/0/border/color",
            "/surface/pane/children/0/items/0/color",
            "/surface/pane/children/0/items/1/color",
        };

        Assert.Equal(expected, paths);

        var childBorder = scan.ColorExprs.Single(c => c.Path == "/surface/pane/children/0/border/color");
        Assert.IsType<ColorResolution.ColorExpr.TokenRef>(childBorder.Expr);

        var branchToken = scan.ColorTokenReferences.Single(t => t.Name == "danger");
        Assert.Equal("/surface/pane/children/0/items/1/color/thresholds/0/color", branchToken.Path);
        Assert.True(branchToken.InRuleBranch);
    }
}
