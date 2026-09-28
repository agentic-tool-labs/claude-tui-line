using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.2/§8: the pane tree is parsed and round-tripped in full (including
/// nested <see cref="Pane.Children"/>) even though Phase 2 only ever renders the root pane.
/// <see cref="Pane.Overflow"/> is deliberately left null when unconfigured — §2.6's default is
/// context-sensitive (root vs. split-child) and is the renderer's decision, not the parser's.
/// </summary>
public class PaneTests
{
    private static readonly ResolvedConfig TopLevel = new(
        new ColorResolution.ColorExpr.Literal("grey"),
        BoxBorder.Rounded,
        PaneBorderEdges.All,
        ChromeReserve: 3,
        ColorSystem: ColorSystemSupport.Standard,
        Colors: new Dictionary<string, ColorResolution.ColorRule>());

    private static string WriteTempConfig(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void SurfaceAbsent_RootPane_InheritsTopLevelBorderAndDefaults()
    {
        var pane = ConfigLoader.ResolveRootPane(config: null, TopLevel);

        Assert.Equal(PaneSplit.None, pane.Split);
        Assert.Empty(pane.Children);
        Assert.Equal("auto", pane.Size);
        Assert.Equal(TopLevel.BorderColor, pane.Border.Color);
        Assert.Same(TopLevel.Style, pane.Border.Style);
        Assert.Null(pane.Overflow); // context-sensitive default: resolved by the renderer, not here
        Assert.Equal("…", pane.Ellipsis);
        Assert.Null(pane.MaxRows);
        Assert.Empty(pane.Items);
    }

    [Fact]
    public void SurfaceAbsent_TopLevelItems_PopulateRootPaneItems()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory" },
                new() { Item = "context", Format = "ctx:{}%", Color = new ColorExprJsonConfig { Literal = "cyan" }, Overflow = "truncate" },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(2, pane.Items.Count);
        Assert.Equal("directory", pane.Items[0].Item);
        Assert.Null(pane.Items[0].Overflow);
        Assert.Equal("context", pane.Items[1].Item);
        Assert.Equal("ctx:{}%", pane.Items[1].Format);
        Assert.Equal(new ColorResolution.ColorExpr.Literal("cyan"), pane.Items[1].Color);
        Assert.Equal(OverflowMode.Truncate, pane.Items[1].Overflow);
    }

    [Fact]
    public void SurfacePresent_TopLevelItemsAreIgnored_PaneTreeIsAuthoritative()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Items = new List<PaneItemJsonConfig> { new() { Item = "git-branch" } },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        var item = Assert.Single(pane.Items);
        Assert.Equal("git-branch", item.Item);
    }

    [Fact]
    public void SurfacePresent_PaneFields_AllHonored()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "VERTICAL",
                    Size = "45",
                    Border = new BorderConfig { Enabled = true, Color = new ColorExprJsonConfig { Literal = "blue" }, Style = "heavy" },
                    Overflow = "Wrap",
                    Ellipsis = "",
                    MaxRows = 2,
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneSplit.None, pane.Split); // no children configured — a stray "split" with no children normalizes to a leaf (§2.2)
        Assert.Equal("45", pane.Size);
        Assert.Equal(new ColorResolution.ColorExpr.Literal("blue"), pane.Border.Color);
        Assert.Same(BoxBorder.Heavy, pane.Border.Style);
        Assert.Equal(OverflowMode.Wrap, pane.Overflow);
        Assert.Equal("", pane.Ellipsis);
        Assert.Equal(2, pane.MaxRows);
    }

    [Fact]
    public void UnrecognizedSplit_FallsBackToNone()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Split = "diagonal" } },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneSplit.None, pane.Split);
    }

    [Fact]
    public void UnrecognizedSplitWithChildren_StaysAContainerOnTheDefaultAxis()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "diagonal",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "context" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "model" } } },
                    },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(PaneSplit.Vertical, pane.Split);
        Assert.Equal(2, pane.Children.Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("wrap", OverflowMode.Wrap)]
    [InlineData("TRUNCATE", OverflowMode.Truncate)]
    [InlineData("overflow", OverflowMode.Overflow)]
    [InlineData("not-a-mode", null)]
    public void OverflowModeParsing_MatchesSpecStrings(string? raw, OverflowMode? expected)
    {
        Assert.Equal(expected, OverflowModeParsing.Parse(raw));
    }

    [Fact]
    public void Children_AreParsedRecursively_EachWithItsOwnFields()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "auto", Overflow = "wrap" },
                        new()
                        {
                            Size = "32%",
                            Border = new BorderConfig { Enabled = true, Color = new ColorExprJsonConfig { Literal = "blue" } },
                            Overflow = "truncate",
                            MaxRows = 2,
                            Items = new List<PaneItemJsonConfig> { new() { Item = "context" } },
                        },
                    },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(2, pane.Children.Count);
        Assert.Equal("auto", pane.Children[0].Size);
        Assert.Equal(OverflowMode.Wrap, pane.Children[0].Overflow);

        var right = pane.Children[1];
        Assert.Equal("32%", right.Size);
        Assert.Equal(new ColorResolution.ColorExpr.Literal("blue"), right.Border.Color);
        Assert.Equal(OverflowMode.Truncate, right.Overflow);
        Assert.Equal(2, right.MaxRows);
        Assert.Equal("context", Assert.Single(right.Items).Item);
    }

    [Fact]
    public void ChildBorderAbsent_DefaultsIndependently_SameRulesAsTopLevel()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig> { new(), new() { Border = new BorderConfig { Enabled = false } } },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Equal(new ColorResolution.ColorExpr.Literal("grey"), pane.Children[0].Border.Color);
        Assert.Same(BoxBorder.Rounded, pane.Children[0].Border.Style); // unconfigured -> same defaults as top-level
        Assert.Null(pane.Children[1].Border.Style); // enabled:false -> no border, independent of sibling
    }

    // End-to-end through real JSON text, to pin the wire property names (SPEC-V2-FRAMEWORK.md
    // §2.9's example) and the size int-vs-string converter, not just the in-memory DTOs above.

    [Fact]
    public void EndToEnd_JsonSizeAsNumber_ParsesToItsStringForm()
    {
        var path = WriteTempConfig("""{ "surface": { "pane": { "size": 45 } } }""");
        try
        {
            var pane = ConfigLoader.LoadRootPane(path, TopLevel);
            Assert.Equal("45", pane.Size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EndToEnd_JsonSizeAsPercentString_ParsesVerbatim()
    {
        var path = WriteTempConfig("""{ "surface": { "pane": { "size": "32%" } } }""");
        try
        {
            var pane = ConfigLoader.LoadRootPane(path, TopLevel);
            Assert.Equal("32%", pane.Size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EndToEnd_TwoPaneExampleFromSpec_ParsesFullTree()
    {
        // SPEC-V2-FRAMEWORK.md §2.9's example, verbatim wire shape (not yet rendered as a split
        // in Phase 2 — this only proves the tree parses and round-trips faithfully).
        var path = WriteTempConfig("""
        {
          "surface": {
            "maxRows": 8,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "children": [
                { "size": "auto", "overflow": "truncate",
                  "border": { "enabled": true, "color": "grey" } },
                { "size": 45, "valign": "middle", "align": "center",
                  "border": { "enabled": true, "color": "blue" },
                  "items": [ { "item": "model-short", "color": "blue" } ] }
              ]
            }
          }
        }
        """);
        try
        {
            var pane = ConfigLoader.LoadRootPane(path, TopLevel);

            Assert.Equal(PaneSplit.Vertical, pane.Split);
            Assert.Equal(2, pane.Children.Count);
            Assert.Equal("auto", pane.Children[0].Size);
            Assert.Equal(OverflowMode.Truncate, pane.Children[0].Overflow);
            Assert.Equal("45", pane.Children[1].Size);
            Assert.Equal(new ColorResolution.ColorExpr.Literal("blue"), pane.Children[1].Border.Color);
            Assert.Equal("model-short", Assert.Single(pane.Children[1].Items).Item);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
