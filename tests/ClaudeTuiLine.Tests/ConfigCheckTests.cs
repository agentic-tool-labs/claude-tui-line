namespace ClaudeTuiLine.Tests;

public class ConfigCheckTests
{
    [Fact]
    public void ValidMinimalConfig_ProducesNoDiagnostics()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownItemSelector_ReportsUnknownItemId()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "not-a-real-builtin" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-item-id" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DerivedFromUnknownSource_ReportsUnknownItemId()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "custom", From = "not-a-real-source" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-item-id" && d.Path == "/items/0/from" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DerivedFromAnotherDerivedItem_ReportsFromDerivedSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "b" },
                new() { Id = "b", From = "directory" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "from-derived-source" && d.Path == "/items/0/from" && d.Severity == DiagnosticSeverity.Error);
    }

    // SPEC-85-ADDENDUM-spans-threading.md §12.8: an item-level `from` naming a compound item's own
    // id can never work — a compound writes no value into the resolution dictionary.
    [Fact]
    public void FromNamingCompoundItem_ReportsFromCompoundSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "agent-badge" },
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "from-compound-source" && d.Path == "/items/0/from" && d.Severity == DiagnosticSeverity.Error);
    }

    // §12.8: a compound part's own `from` naming a compound id is the same hole at part position.
    [Fact]
    public void PartFromNamingCompoundItem_ReportsFromCompoundSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Id = "a",
                    Parts = new List<PaneItemPartJsonConfig> { new() { From = "agent-badge" } },
                },
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "from-compound-source" && d.Path == "/items/0/parts/0/from" && d.Severity == DiagnosticSeverity.Error);
    }

    // SPEC-87 §6.1, hole #1b: a part's `item` naming a compound id is an error, distinct from
    // from-compound-source because a part's `item` renders an item rather than reading a value.
    [Fact]
    public void PartItemNamingCompoundItem_ReportsPartCompoundSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Id = "a",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Item = "agent-badge" } },
                },
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "part-compound-source" && d.Path == "/items/0/parts/0/item" && d.Severity == DiagnosticSeverity.Error);
    }

    // SPEC-87 §6.2: a compound part naming its own compound id is caught by the same general rule,
    // with no dedicated self-reference code needed (§3.4).
    [Fact]
    public void PartItemNamingOwnCompoundId_ReportsPartCompoundSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Id = "badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Item = "badge" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "part-compound-source" && d.Path == "/items/0/parts/0/item" && d.Severity == DiagnosticSeverity.Error);
        Assert.Single(diagnostics, d => d.Path == "/items/0/parts/0/item");
    }

    // SPEC-87 §6.3: a part's `item` naming a registry or command id is legal and must not trip the
    // new compound-source branch.
    [Fact]
    public void PartItemNamingRegistryOrCommandId_ReportsNoDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Id = "a",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Item = "directory" }, new() { Item = "cmd" } },
                },
                new() { Id = "cmd", Command = new List<string> { "tool" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Empty(diagnostics);
    }

    // §12.8.3: precedence — an item carrying both `parts` and `from` reports the compound reason,
    // not from-derived-source, for the *referencing* item's own from (§4.4 rules `parts` wins on
    // the node that declares both; this checks the id being pointed AT is reported as compound).
    [Fact]
    public void FromNamingCompoundItem_DoesNotAlsoReportFromDerivedSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "agent-badge" },
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "from-derived-source" && d.Path == "/items/0/from");
    }

    [Fact]
    public void LinkPlaceholderNamingNothing_ReportsUnknownLinkTargetAsWarning()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory", Link = "https://example.com/{missing}" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-link-target" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ColorRuleFromNamingNothing_ReportsUnknownColorSourceAsWarning()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig { From = "missing-source", Default = "green" },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color-source" && d.Severity == DiagnosticSeverity.Warning);
    }

    // SPEC-87 §6.9-10, hole #4: a colour rule's `from` naming a compound has no single value to
    // read (a compound has a colour per part), so it warns and falls through to the default colour
    // rather than erroring — matching unknown-color-source's severity per §1.1's coherence rule.
    [Fact]
    public void ColorRuleFromNamingCompoundItem_ReportsColorFromCompoundSourceAsWarning()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig { From = "agent-badge", Default = "green" },
                    },
                },
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Single(diagnostics);
        Assert.Contains(diagnostics, d => d.Code == "color-from-compound-source" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // SPEC-87 §12.2: hole #4's fix is two-sided — the diagnostic fires AND the rule's resolved
    // colour at render time falls through to its default rather than picking up anything derived
    // from the compound (ColorResolution.Resolve never reads the compound map at all).
    [Fact]
    public void ColorRuleFromNamingCompoundItem_DiagnosticFiresAndResolvedColorFallsThroughToDefault()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig { From = "agent-badge", Default = "green" },
                    },
                },
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Single(diagnostics);
        Assert.Contains(diagnostics, d => d.Code == "color-from-compound-source" && d.Severity == DiagnosticSeverity.Warning);

        var rule = new ColorResolution.ColorRule(Thresholds: null, Match: null, Default: new ColorResolution.ColorValue.Literal("green"), From: "agent-badge");
        var values = new Dictionary<string, string?>();
        var tokens = new Dictionary<string, ColorResolution.ColorRule>();

        var resolvedColor = ColorResolution.Resolve(new ColorResolution.ColorExpr.Inline(rule), values, tokens);

        Assert.Equal("green", resolvedColor);
    }

    // SPEC-87 §12.3/§12.8.5: an item-level selector naming a compound id (`{"item":"<id>"}`) is
    // not one of the reference forms this spec's diagnostics cover — a legitimately-declared
    // compound selected this way reports nothing.
    [Fact]
    public void ItemSelectorNamingCompoundElsewhere_ReportsNoDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Id = "empty-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Item = "worktree" } },
                },
                new() { Item = "empty-badge" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ColorTokenNamingNothing_ReportsUnknownColorTokenAsWarning()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "@missing-token" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color-token" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Warning);
    }

    // SPEC-44-color-token-in-rule-branches.md §10 items 2-4: the diagnostic-emission side of the
    // rule-branch token restriction (§3.2/§4.3). The runtime-resolution side (does the value
    // actually render/not-render) lives in ColorValueBranchResolutionTests.cs.

    [Fact]
    public void NonConstantColorTokenInRuleBranch_ReportsNonConstantColorTokenAsWarning()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["busy"] = new ColorRuleJsonConfig
                {
                    From = "directory",
                    Thresholds = new List<ThresholdJsonConfig> { new() { Min = 50, Color = "olive" } },
                },
            },
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig
                        {
                            From = "directory",
                            Thresholds = new List<ThresholdJsonConfig> { new() { Min = 50, Color = "@busy" } },
                            Default = "grey",
                        },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d =>
            d.Code == "non-constant-color-token" &&
            d.Path == "/items/0/color/thresholds/0/color" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message == "colour token '@busy' is used in a rule branch at /items/0/color/thresholds/0/color, so it must be a constant colour (a 'default' literal with no 'from', 'thresholds', or 'match')");
    }

    [Fact]
    public void ChainedColorTokenInRuleBranch_ReportsNonConstantColorTokenAsWarning()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["a"] = new ColorRuleJsonConfig { Default = "@b" },
                ["b"] = new ColorRuleJsonConfig { Default = "red" },
            },
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig
                        {
                            From = "directory",
                            Thresholds = new List<ThresholdJsonConfig> { new() { Min = 50, Color = "@a" } },
                            Default = "grey",
                        },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d =>
            d.Code == "non-constant-color-token" &&
            d.Path == "/items/0/color/thresholds/0/color" &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void UnknownColorTokenInRuleBranch_ReportsUnknownColorTokenAtSameSeverityAsOnABorder()
    {
        var branchConfig = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig
                        {
                            From = "directory",
                            Thresholds = new List<ThresholdJsonConfig> { new() { Min = 50, Color = "@nope" } },
                            Default = "grey",
                        },
                    },
                },
            },
        };

        var branchDiagnostics = ConfigChecker.Check(branchConfig);

        var branchDiagnostic = Assert.Single(branchDiagnostics, d =>
            d.Code == "unknown-color-token" && d.Path == "/items/0/color/thresholds/0/color");

        // Uniformity check (§10 item 4): a branch-position unknown token must be flagged at the same
        // severity as the existing, unrestricted top-position unknown-token case.
        var borderConfig = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "@nope" } },
            },
        };

        var borderDiagnostics = ConfigChecker.Check(borderConfig);
        var borderDiagnostic = Assert.Single(borderDiagnostics, d => d.Code == "unknown-color-token");

        Assert.Equal(borderDiagnostic.Severity, branchDiagnostic.Severity);
        Assert.Equal(DiagnosticSeverity.Warning, branchDiagnostic.Severity);
    }

    [Fact]
    public void UnrecognizedLiteralColor_ReportsUnknownColorAsError()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "not-a-real-color" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedThresholdColor_ReportsUnknownColorAtThresholdPath()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig
                        {
                            Thresholds = new List<ThresholdJsonConfig> { new() { Min = 80, Color = "not-a-real-color" } },
                            Default = "green",
                        },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/items/0/color/thresholds/0/color" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedMatchColor_ReportsUnknownColorAtMatchPath()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig
                        {
                            Match = new List<MatchJsonConfig> { new() { EqualsValue = "x", Color = "not-a-real-color" } },
                            Default = "green",
                        },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/items/0/color/match/0/color" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ColorsTableEntryWithBadDefault_ReportsUnknownColorAtColorsPath()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["accent"] = new ColorRuleJsonConfig { Default = "not-a-real-color" },
            },
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/colors/accent/default" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HexLiteralUnderDefaultColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "#ff8800" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void HexLiteralUnderTruecolorColorSystem_ProducesNoColorDownConvertedDiagnostic()
    {
        var config = new UserConfig
        {
            ColorSystem = "truecolor",
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "#ff8800" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void NamedColorLiteralUnderDefaultColorSystem_ProducesNoColorDownConvertedDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "green" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void HexLiteralUnderEightBitColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            ColorSystem = "256",
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "#ff8800" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void NumericLiteralAboveFifteenUnderDefaultColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "207" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" &&
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("256-color"));
    }

    [Fact]
    public void NumericLiteralAtOrBelowFifteenUnderDefaultColorSystem_ProducesNoColorDownConvertedDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "9" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void NonStandardNamedColorUnderDefaultColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "cyan" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" &&
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("256-color"));
    }

    [Fact]
    public void DecorationOnlyLiteral_ProducesNoColorDownConvertedDiagnosticUnderAnyColorSystem()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "dim" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void SingleElementArgvWithWhitespaceWithoutShell_ReportsCommandShape()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "git status" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "command-shape" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ShellTrueWithMultiElementArgv_ReportsCommandShellArgv()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Shell = true, Command = new List<string> { "echo", "hi" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "command-shell-argv" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ShellTrueWithSingleElementArgv_ProducesNoCommandDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Shell = true, Command = new List<string> { "git status" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code is "command-shape" or "command-shell-argv");
    }

    [Fact]
    public void MultiElementArgvWithoutShell_ProducesNoCommandDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "git", "status" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code is "command-shape" or "command-shell-argv");
    }

    [Fact]
    public void UnrecognizedPaneSize_ReportsUnknownEnumValue()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Size = "huge",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/size" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PaneSizeAuto_ReportsDeprecatedSizeAlias()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Size = "auto",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "deprecated-size-alias" && d.Path == "/surface/pane/size" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void PaneSizeFill_ProducesNoDeprecatedSizeAliasDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Size = "fill",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "deprecated-size-alias");
    }

    [Fact]
    public void UnrecognizedItemCase_ReportsUnknownEnumValue()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", From = "directory", Case = "sideways" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/items/0/case" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedTopLevelBorderStyle_ReportsUnknownEnumValue()
    {
        var config = new UserConfig
        {
            Border = new BorderConfig { Style = "sparkly" },
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/border/style" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedValign_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Valign = "upside-down",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/valign" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'upside-down' is not a valign — expected top, middle, or bottom");
    }

    [Fact]
    public void UnrecognizedAlign_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Align = "inward",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/align" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'inward' is not a align — expected left, center, or right");
    }

    [Fact]
    public void UnrecognizedSplit_ReportsUnknownEnumValueNamingAcceptedSet()
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
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'diagonal' is not a split — expected none, horizontal, vertical, or flex");
    }

    [Fact]
    public void ExplicitSplitNone_ProducesNoUnknownEnumValueDiagnosticForSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "none",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void UnrecognizedDistribute_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Distribute = "min-row",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/distribute" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'min-row' is not a distribute — expected greedy, min-rows, or even");
    }

    [Fact]
    public void ExplicitDistributeGreedy_ProducesNoUnknownEnumValueDiagnosticForDistribute()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Distribute = "greedy",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/distribute");
    }

    [Fact]
    public void UnrecognizedHeight_ReportsUnknownEnumValueNamingAcceptedSet()
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
                        new() { Height = "shrink", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/children/0/height" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'shrink' is not a height — expected content or fill");
    }

    [Fact]
    public void ExplicitHeightContent_ProducesNoUnknownEnumValueDiagnosticForHeight()
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
                        new() { Height = "content", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/children/0/height");
    }

    [Fact]
    public void UnrecognizedColorSystem_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            ColorSystem = "24bit",
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/colorSystem" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'24bit' is not a colorSystem — expected standard, 256, or truecolor");
    }

    [Fact]
    public void ExplicitColorSystemStandard_ProducesNoUnknownEnumValueDiagnosticForColorSystem()
    {
        var config = new UserConfig
        {
            ColorSystem = "standard",
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/colorSystem");
    }

    [Fact]
    public void MinSizeExceedsMaxSize_ReportsMinExceedsMax()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    MinSize = 50,
                    MaxSize = 20,
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "min-exceeds-max" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Error);
    }

    // SPEC-V2-FRAMEWORK.md §4.0.1: maxLines is an opt-in ceiling — zero or negative has no
    // meaningful reading, so it is rejected the same way min-exceeds-max is.
    [Fact]
    public void NonPositiveMaxLines_ReportsInvalidMaxLines()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "echo", "hi" }, MaxLines = 0 } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "invalid-max-lines" && d.Path == "/surface/pane/items/0/maxLines" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PositiveMaxLines_ReportsNoInvalidMaxLines()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "echo", "hi" }, MaxLines = 3 } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "invalid-max-lines");
    }

    [Fact]
    public void FixedChildrenExceedFixedParent_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "20",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "15", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "15", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "fixed-sizes-exceed-parent" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Error);
    }

    // SPEC-92 §9: parent uses `maxSize` — the case that already fired before #92; #92 only
    // changes the wording (§5.3), from naming "maxSize" to naming "this pane's own bound", so a
    // config declaring `size` instead is not told about a key it never wrote.
    [Fact]
    public void MinSizeSumExceedsParentMaxSize_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    MaxSize = 20,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 15, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { MinSize = 15, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics, d => d.Code == "fixed-sizes-exceed-parent" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("this pane's own bound (20)", diagnostic.Message);
        Assert.DoesNotContain("maxSize (", diagnostic.Message);
    }

    // SPEC-92 §9: parent uses fixed `size` (not `maxSize`) — this is SPEC-92 V1, the fixture that
    // fails against 62687bb, where `CheckSplitBounds:940`'s old guard (`split.MaxSize is int`) was
    // false because the parent's bound came from `SizeResolver.FixedSize`, not `MaxSize`.
    [Fact]
    public void MinSizeSumExceedsFixedParentSize_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "40",
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 30, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { MinSize = 30, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("fixed-sizes-exceed-parent", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("/surface/pane", diagnostic.Path);
        Assert.Contains("minSize sum (60)", diagnostic.Message);
        Assert.Contains("this pane's own bound (40)", diagnostic.Message);
    }

    // SPEC-92 §9: no fixed `size` and no `maxSize` — the parent has no bound at all, so the
    // merged guard (§5.2) must `yield break` exactly as the two separate guards did before it.
    // Guards framework `:6073-6075`'s fill/content silence.
    [Fact]
    public void UnboundedVerticalParent_MinSizeSumNeverChecked()
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
                        new() { MinSize = 999, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { MinSize = 999, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Empty(diagnostics);
    }

    // SPEC-92 §9: parent uses fixed `size`, tripping both the fixed-sum and minSize-sum branches
    // on the same split. Guards §5.2's order-preservation claim: the fixed diagnostic must still
    // yield before the minSize one, and both must report against the same bound.
    [Fact]
    public void FixedParentTripsBothFixedSumAndMinSizeSum_ReportsBothInOrder()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "40",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "25", MinSize = 25, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "25", MinSize = 25, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config).ToList();

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("fixed-sizes-exceed-parent", d.Code));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(diagnostics, d => Assert.Equal("/surface/pane", d.Path));
        Assert.Contains("fixed sizes (50)", diagnostics[0].Message);
        Assert.Contains("minSize sum (50)", diagnostics[1].Message);
    }

    [Fact]
    public void FixedChildrenWithinFixedParent_ProducesNoStructuralDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "50",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "10", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "10", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "fixed-sizes-exceed-parent");
    }

    [Fact]
    public void OverflowOverflowInsideSplit_ReportsOverflowForbiddenPosition()
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
                        new() { Overflow = "overflow", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "overflow-forbidden-position" && d.Path == "/surface/pane/children/0/overflow" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OverflowOverflowOnSoleRootPane_ProducesNoOverflowDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Overflow = "overflow",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "overflow-forbidden-position");
    }

    [Fact]
    public void ContentSizedPaneWithNoItems_ReportsPaneNoItemsAsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "content" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "pane-no-items" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void FillSizedPaneWithNoItems_ReportsPaneNoItemsAsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "fill" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "pane-no-items" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void FixedSizedPaneWithNoItems_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "10" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void PercentSizedPaneWithNoItems_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "50%" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void SplitPaneWithNoOwnItems_ProducesNoPaneNoItemsDiagnostic()
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
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void ContentSizedPaneWithExplicitMinSize_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "content", MinSize = 5 },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void OverflowOnSplitNode_ReportsLeafOnlyKeyOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Overflow = "truncate",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "leaf-only-key-on-split" && d.Path == "/surface/pane/overflow" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void EllipsisOnSplitNode_ReportsLeafOnlyKeyOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Ellipsis = "...",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "leaf-only-key-on-split" && d.Path == "/surface/pane/ellipsis" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void OverflowOnLeafChildInsideSplit_ProducesNoLeafOnlyKeyDiagnostic()
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
                        new() { Overflow = "truncate", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "leaf-only-key-on-split");
    }

    [Fact]
    public void DistributeOnHorizontalSplit_ReportsKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Distribute = "even",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/distribute" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void DistributeOnVerticalSplit_ProducesNoKeyNotApplicableDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Distribute = "even",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/distribute");
    }

    [Fact]
    public void UnrecognizedDistributeOnHorizontalSplit_DoesNotAlsoReportKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Distribute = "diagonal",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/distribute");
        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/distribute");
    }

    [Fact]
    public void GutterOnHorizontalSplit_ReportsKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Gutter = 1,
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/gutter" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void GutterOnVerticalSplit_ProducesNoKeyNotApplicableDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Gutter = 1,
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/gutter");
    }

    [Fact]
    public void ItemsAlongsideChildren_ReportsKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/items" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ItemsOnLeafWithoutChildren_ProducesNoKeyNotApplicableDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/items");
    }

    [Fact]
    public void EmptyChildrenList_ReportsKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Children = new List<PaneConfig>(),
                    Items = new List<PaneItemJsonConfig> { new() { Item = "model" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Single(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void AbsentChildrenKey_ProducesNoKeyNotApplicableDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Items = new List<PaneItemJsonConfig> { new() { Item = "model" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children");
    }

    [Fact]
    public void NonEmptyChildrenWithoutSplitKey_ProducesNoKeyNotApplicableOnChildren()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig>() },
                        new() { Items = new List<PaneItemJsonConfig>() },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children");
    }

    [Fact]
    public void NestedEmptyChildrenList_ReportsAtNestedPath()
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
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Children = new List<PaneConfig>(), Items = new List<PaneItemJsonConfig> { new() { Item = "model" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children/1/children" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void MisspelledSplitWithChildren_ReportsUnknownEnumValueNotKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertikal",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig>() },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split");
        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children");
    }

    [Fact]
    public void EmptyChildrenListWithNoItemsOrMinSize_ReportsBothKeyNotApplicableAndPaneNoItems()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Children = new List<PaneConfig>(),
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children");
        Assert.Contains(diagnostics, d => d.Code == "pane-no-items" && d.Path == "/surface/pane");
    }

    [Fact]
    public void SplitOnChildlessPaneWithItems_ReportsKeyNotApplicableOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/split" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/items");
    }

    [Fact]
    public void SplitOnChildlessPaneWithNoItems_ReportsKeyNotApplicableOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/split" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SplitWithNonEmptyChildren_ProducesNoKeyNotApplicableOnSplit()
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
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void ExplicitNoneSplitOnChildlessPane_ProducesNoKeyNotApplicableOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "none",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void AbsentSplitKey_ProducesNoKeyNotApplicableDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable");
    }

    [Fact]
    public void MisspelledSplitOnChildlessPane_DoesNotAlsoReportKeyNotApplicableOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertcal",
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split");
        Assert.DoesNotContain(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void SplitWithEmptyChildrenList_ReportsBothChildrenAndSplitKeyNotApplicable()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>(),
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children");
        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void NestedSplitOnChildlessPane_ReportsAtNestedPath()
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
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Split = "vertical", Items = new List<PaneItemJsonConfig> { new() { Item = "model" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "key-not-applicable" && d.Path == "/surface/pane/children/1/split" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SingleHorizontalSplitChildExceedsFixedParent_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Size = "10",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "20", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "fixed-sizes-exceed-parent" && d.Path == "/surface/pane/children/0" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HorizontalSplitChildrenWithinParentBound_ProducesNoStructuralDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Size = "10",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "5", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "5", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "fixed-sizes-exceed-parent");
    }

    [Fact]
    public void HorizontalSplitChildMinSizeExceedsMaxSizeParent_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Single(diagnostics);
        Assert.Equal("fixed-sizes-exceed-parent", diagnostics[0].Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.Equal("/surface/pane/children/0", diagnostics[0].Path);
        Assert.Contains("50", diagnostics[0].Message);
        Assert.Contains("40", diagnostics[0].Message);
    }

    [Fact]
    public void HorizontalSplitChildMinSizeExceedsFixedParent_ReportsFixedSizesExceedParent()
    {
        // Guards §5.3(a): a bound computed from `split.MaxSize` alone (mirroring CheckSplitBounds:940)
        // would miss this, since the parent here is fixed-size, not maxSize.
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Size = "10",
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 20, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Single(diagnostics);
        Assert.Equal("fixed-sizes-exceed-parent", diagnostics[0].Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.Equal("/surface/pane/children/0", diagnostics[0].Path);
        Assert.Contains("20", diagnostics[0].Message);
        Assert.Contains("10", diagnostics[0].Message);
    }

    [Fact]
    public void HorizontalSplitChildMinSizeUnderUnboundedParent_ProducesNoStructuralDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 999, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "fixed-sizes-exceed-parent");
    }

    [Fact]
    public void HorizontalSplitChildWithBothSizeAndMinSizeOverBound_ReportsAtMostOneDiagnostic()
    {
        // §5.3(c): `else if`, not independent `if` — at most one diagnostic per child even when
        // both the fixed size and the minSize exceed the bound. The fixed size wins when both are
        // declared and both exceed; the minSize is still caught when the fixed size alone does not.
        UserConfig Config(string? size, int minSize) => new()
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Size = "10",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = size, MinSize = minSize, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var fixedWins = ConfigChecker.Check(Config("20", 30));
        Assert.Single(fixedWins);
        Assert.Equal("fixed-sizes-exceed-parent", fixedWins[0].Code);
        Assert.Contains("20", fixedWins[0].Message);
        Assert.DoesNotContain("30", fixedWins[0].Message);

        var minSizeCaught = ConfigChecker.Check(Config("5", 30));
        Assert.Single(minSizeCaught);
        Assert.Equal("fixed-sizes-exceed-parent", minSizeCaught[0].Code);
        Assert.Contains("30", minSizeCaught[0].Message);
    }

    [Fact]
    public void HorizontalSplitChildMinSizeDiagnostic_NamesBoundWithNoBoundaryCost()
    {
        // §5.3(b): the per-child form gives every child the full parent extent, so no divider is
        // reserved and the bound named in the message is the parent's bound unadjusted — here 40,
        // not 40 minus any boundary cost.
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Single(diagnostics);
        Assert.Contains("(40)", diagnostics[0].Message);
    }

    [Fact]
    public void HorizontalSplitChildMinSizeOverBound_ProducesAtLeastOneErrorSeverityDiagnostic()
    {
        // §13 V8: pins the same gate RunCheck applies (diagnostics.Any(Error) => exit 1, ok: false).
        // RunCheck itself is untouched by #91 (§12) and not re-tested here.
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 50, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ---- SPEC-V2-FRAMEWORK.md §4.2.1: argv-placeholder declaration faults ----

    [Fact]
    public void ArgvPlaceholderNamingUnknownId_ReportsUnknownItemId()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{not-a-real-id}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-item-id" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderNamingDerivedItem_ReportsPlaceholderDerivedSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "directory" },
                new() { Id = "cmd", Command = new List<string> { "tool", "{a}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-derived-source" && d.Path == "/items/1/command" && d.Severity == DiagnosticSeverity.Error);
    }

    // §12.8.5 hole #2: an argv placeholder naming a compound item passed --check and silently
    // substituted nothing before this fix, the same "self-declared id" hole as from-compound-source.
    [Fact]
    public void ArgvPlaceholderNamingCompoundItem_ReportsPlaceholderCompoundSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Id = "agent-badge",
                    Parts = new List<PaneItemPartJsonConfig> { new() { Text = "agent:" } },
                },
                new() { Id = "cmd", Command = new List<string> { "tool", "{agent-badge}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-compound-source" && d.Path == "/items/1/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderNamingAnotherCommandItem_ReportsPlaceholderCommandSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "other-cmd", Command = new List<string> { "echo", "hi" } },
                new() { Id = "cmd", Command = new List<string> { "tool", "{other-cmd}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-command-source" && d.Path == "/items/1/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderBareSelfReference_ReportsPlaceholderSelfReference()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-self-reference" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderNamedSelfReference_ReportsPlaceholderSelfReferenceNotCommandSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{cmd}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-self-reference" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == "placeholder-command-source");
    }

    [Fact]
    public void ShellTrueWithTwoIdsManglingToSameEnvVar_ReportsPlaceholderEnvCollision()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "agent-short", From = "directory" },
                new() { Id = "agent.short", From = "directory" },
                new() { Id = "cmd", Shell = true, Command = new List<string> { "echo \"$CLAUDE_TUI_LINE_VAL_AGENT_SHORT {agent-short} {agent.short}\"" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-env-collision" && d.Path == "/items/2/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NonShellCommandWithTwoIdsManglingToSameEnvVar_ProducesNoEnvCollisionDiagnostic()
    {
        // §4.2: the env-collision check is scoped to shell:true — non-shell substitutes directly
        // into argv and has no shared environment namespace for two ids to collide in.
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "agent-short", From = "directory" },
                new() { Id = "agent.short", From = "directory" },
                new() { Id = "cmd", Command = new List<string> { "tool", "{agent-short}", "{agent.short}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "placeholder-env-collision");
    }

    [Fact]
    public void ArgvPlaceholderNamingKnownBuiltin_ProducesNoArgvPlaceholderDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{directory}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code is "unknown-item-id" or "placeholder-derived-source" or
            "placeholder-command-source" or "placeholder-self-reference" or "placeholder-env-collision");
    }

    // ---- §9.4.2: unknown-key diagnostic. Fixtures are parsed JSON, not object initializers —
    // extension data only exists on the deserialized shape. ----

    private static UserConfig Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.UserConfig)!;

    [Fact]
    public void UnknownKeyOnItem_ReportsWithSuggestion()
    {
        var config = Parse("""{"items":[{"item":"context","colour":"aqua"}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Severity == DiagnosticSeverity.Warning &&
            d.Path == "/items/0/colour" && d.Message == "unknown key 'colour' on an item — did you mean 'color'?");
    }

    [Fact]
    public void UnknownKeyOnItem_AbbreviationPrefixRule_ReportsWithSuggestion()
    {
        var config = Parse("""{"items":[{"ttl":5}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/ttl" &&
            d.Message == "unknown key 'ttl' on an item — did you mean 'ttlSeconds'?");
    }

    [Fact]
    public void UnknownKeyOnItem_NoQualifyingSuggestion_OmitsDidYouMean()
    {
        var config = Parse("""{"items":[{"zzzzzz":1}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/zzzzzz" &&
            d.Message == "unknown key 'zzzzzz' on an item");
    }

    [Fact]
    public void UnknownKeyOnItem_CaseOnlyMismatch_IsReportedAndSuggested()
    {
        var config = Parse("""{"items":[{"Color":"red"}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/Color" &&
            d.Message == "unknown key 'Color' on an item — did you mean 'color'?");
    }

    [Fact]
    public void UnknownKeyAtTopLevel_ReportsWithSuggestion()
    {
        var config = Parse("""{"colour":"red"}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/colour" &&
            d.Message == "unknown key 'colour' on the top-level config — did you mean 'colors'?");
    }

    [Fact]
    public void UnknownKeyOnNestedPane_Reports()
    {
        // §9.4.2: "maxLines" is a vocabulary mismatch ("Lines" vs "Rows"), not a typo or an
        // abbreviation — EditDistance("maxLines", "maxRows") == 4 clears neither the distance
        // bound (<=2) nor the prefix rule, and that is intended: no rule here addresses the
        // vocabulary-confusion class, so the bare diagnostic naming no key is correct.
        var config = Parse("""{"surface":{"pane":{"split":"vertical","children":[{"maxLines":3}]}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" &&
            d.Path == "/surface/pane/children/0/maxLines" &&
            d.Message == "unknown key 'maxLines' on a pane");
    }

    [Fact]
    public void UnknownKeyOnPane_GenuineTie_NamesBothCandidatesOrdinallySorted()
    {
        // "xalign" is distance 1 from both "align" and "valign" (neither is a prefix relation
        // of the other), so this is a real tie over PaneConfig's actual known-key set — not a
        // synthetic case — and both candidates must be named, ordinally sorted.
        var config = Parse("""{"surface":{"pane":{"split":"vertical","xalign":"x"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" &&
            d.Path == "/surface/pane/xalign" &&
            d.Message == "unknown key 'xalign' on a pane — did you mean 'align' or 'valign'?");
    }

    [Fact]
    public void UnknownKeyOnItem_ShortKeyBelowPrefixFloor_OmitsDidYouMean()
    {
        // §9.4.2 D3: "c" is a bare prefix of several known keys (case, color, colors,
        // colorSystem, children) but the shorter string is below the three-character floor,
        // so none of them qualify.
        var config = Parse("""{"items":[{"c":1}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/c" &&
            d.Message == "unknown key 'c' on an item");
    }

    [Fact]
    public void UnknownKeyOnBorderObject_ReportsWithSuggestion()
    {
        var config = Parse("""{"border":{"styl":"rounded"}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/border/styl" &&
            d.Message == "unknown key 'styl' on a border — did you mean 'style'?");
    }

    [Fact]
    public void BorderShorthandString_ProducesNoUnknownKeyDiagnostics()
    {
        var config = Parse("""{"border":"outline"}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key");
    }

    [Fact]
    public void UnknownKeyOnColorRuleInsideItem_ReportsWithSuggestion()
    {
        var config = Parse("""{"items":[{"item":"x","color":{"from":"y","defualt":"red"}}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/color/defualt" &&
            d.Message == "unknown key 'defualt' on a color rule — did you mean 'default'?");
    }

    [Fact]
    public void ColorsTableTokenNames_AreNeverUnknownKeys()
    {
        var config = Parse("""{"colors":{"my-weird-name":{"from":"x","default":"red"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key");
    }

    [Fact]
    public void CleanConfig_ProducesNoUnknownKeyDiagnostic()
    {
        var config = Parse("""
        {
            "border": {"enabled": true, "style": "rounded"},
            "layout": {"chromeReserve": 3},
            "surface": {
                "maxRows": 8,
                "pane": {
                    "split": "vertical",
                    "children": [
                        {"size": "fill", "items": [{"item": "directory"}]},
                        {"size": "fill", "items": [{"item": "model"}]}
                    ]
                }
            },
            "colors": {"accent": {"from": "directory", "default": "blue"}}
        }
        """);

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key");
    }

    [Fact]
    public void MultipleUnknownKeysOnOneObject_ComeOutInOrdinalKeyOrder()
    {
        var config = Parse("""{"items":[{"zoo":1,"alpha":2}]}""");

        var diagnostics = ConfigChecker.Check(config)
            .Where(d => d.Code == "unknown-key")
            .ToList();

        var alphaIndex = diagnostics.FindIndex(d => d.Path == "/items/0/alpha");
        var zooIndex = diagnostics.FindIndex(d => d.Path == "/items/0/zoo");

        Assert.True(alphaIndex >= 0 && zooIndex >= 0 && alphaIndex < zooIndex);
    }

    [Fact]
    public void UnknownKeyDiagnostics_AreNeverErrorSeverity()
    {
        var config = Parse("""
        {
            "colour": "red",
            "items": [{"item":"x","colour":"aqua","ttl":5,"zzzzzz":1,"Color":"red","color":{"from":"y","defualt":"red"}}],
            "border": {"styl":"rounded"},
            "surface": {"pane": {"split":"vertical","children":[{"maxLines":3}]}}
        }
        """);

        var unknownKeyDiagnostics = ConfigChecker.Check(config).Where(d => d.Code == "unknown-key").ToList();

        Assert.NotEmpty(unknownKeyDiagnostics);
        Assert.All(unknownKeyDiagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    // ---- item-specific-config.md §8: itemSettings. ----

    [Fact]
    public void ItemSettingsAbsent_UnknownKeyDiagnosticsUnaffected()
    {
        var config = Parse("""{"items":[{"item":"directory"}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Path.StartsWith("/itemSettings", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownKeyOnItemSettingsDirectory_ReportsWithSuggestion()
    {
        var config = Parse("""{"itemSettings":{"directory":{"depht":2}}}""");

        var diagnostics = ConfigChecker.Check(config);

        var matches = diagnostics.Where(d => d.Code == "unknown-key" && d.Path == "/itemSettings/directory/depht").ToList();
        Assert.Single(matches);
        Assert.Equal("unknown key 'depht' on the directory item's settings — did you mean 'depth'?", matches[0].Message);
    }

    [Fact]
    public void Check_UnknownKeyInWorktreeSettings_IsReported()
    {
        var config = Parse("""{"itemSettings":{"worktree":{"showBranchh":true}}}""");

        var diagnostics = ConfigChecker.Check(config);

        var matches = diagnostics.Where(d => d.Code == "unknown-key" && d.Path == "/itemSettings/worktree/showBranchh").ToList();
        Assert.Single(matches);
    }

    [Fact]
    public void Check_WorktreeShowBranch_NotReportedAsUnknownKey()
    {
        var config = Parse("""{"itemSettings":{"worktree":{"showBranch":false}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key" && d.Path.Contains("worktree"));
    }

    [Fact]
    public void Check_OutputStyleShowLabel_ParsesAndIsNotReportedAsUnknownKey()
    {
        var config = Parse("""{"itemSettings":{"outputStyle":{"showLabel":false}}}""");

        Assert.False(config.ItemSettings!.OutputStyle!.ShowLabel);

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key" && d.Path.Contains("outputStyle"));
    }

    [Fact]
    public void Check_UnknownKeyInOutputStyleSettings_LandsInExtra()
    {
        var config = Parse("""{"itemSettings":{"outputStyle":{"showLabel":false,"bogus":true}}}""");

        Assert.True(config.ItemSettings!.OutputStyle!.Extra!.ContainsKey("bogus"));
    }

    [Fact]
    public void UnknownKeyOnItemSettings_ReportsWithSuggestion()
    {
        var config = Parse("""{"itemSettings":{"drectory":{}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/itemSettings/drectory" &&
            d.Message == "unknown key 'drectory' on itemSettings — did you mean 'directory'?");
    }

    [Fact]
    public void RateLimitsWindows_InvalidValue_ProducesValidationError()
    {
        var config = Parse("""{"itemSettings":{"rateLimits":{"windows":"9y"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Path == "/itemSettings/rateLimits/windows" && d.Code == "unknown-enum-value");
    }

    [Fact]
    public void RateLimitsWindows_ValidValue_ProducesNoValidationError()
    {
        var config = Parse("""{"itemSettings":{"rateLimits":{"windows":"5h"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Path == "/itemSettings/rateLimits/windows");
    }

    [Fact]
    public void DirectoryDepth_ZeroOrNegative_ProducesValidationError()
    {
        var zero = Parse("""{"itemSettings":{"directory":{"depth":0}}}""");
        var negative = Parse("""{"itemSettings":{"directory":{"depth":-1}}}""");

        Assert.Contains(ConfigChecker.Check(zero), d => d.Path == "/itemSettings/directory/depth" && d.Code == "invalid-depth");
        Assert.Contains(ConfigChecker.Check(negative), d => d.Path == "/itemSettings/directory/depth" && d.Code == "invalid-depth");
    }

    [Fact]
    public void Check_DirectoryOpenWithUnknownToken_ReportsDiagnostic()
    {
        var config = Parse("""{"itemSettings":{"directory":{"openWith":"sublime"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Path == "/itemSettings/directory/openWith" && d.Code == "unknown-enum-value"));
        Assert.Contains("files", diagnostic.Message);
        Assert.Contains("vscode", diagnostic.Message);
    }

    [Fact]
    public void Check_DirectoryOpenWithValidToken_NoDiagnostic()
    {
        var vscode = Parse("""{"itemSettings":{"directory":{"openWith":"vscode"}}}""");
        var files = Parse("""{"itemSettings":{"directory":{"openWith":"files"}}}""");

        Assert.DoesNotContain(ConfigChecker.Check(vscode), d => d.Path == "/itemSettings/directory/openWith");
        Assert.DoesNotContain(ConfigChecker.Check(files), d => d.Path == "/itemSettings/directory/openWith");
    }

    [Fact]
    public void Check_DirectoryOpenWith_NoLongerReportedAsUnknownKey()
    {
        var config = Parse("""{"itemSettings":{"directory":{"openWith":"vscode"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key" && d.Path.Contains("openWith"));
    }

    [Fact]
    public void DirectoryDepth_Positive_ProducesNoValidationError()
    {
        var config = Parse("""{"itemSettings":{"directory":{"depth":2}}}""");

        Assert.DoesNotContain(ConfigChecker.Check(config), d => d.Path == "/itemSettings/directory/depth");
    }

    [Fact]
    public void ItemSettingsLinear_UnknownKey_ReportsWithSuggestion()
    {
        // item-specific-config.md §12.9 T17.
        var config = Parse("""{"itemSettings":{"linear":{"workspce":"x"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/itemSettings/linear/workspce" &&
            d.Message == "unknown key 'workspce' on the linear item's settings — did you mean 'workspace'?");
    }

    [Fact]
    public void BorderConfig_KnownKeySet_ExcludesShorthand()
    {
        // TASK-21-SPEC.md §9.3: the executable form of §3's NEEDS-EVIDENCE item. On this runtime,
        // [JsonIgnore] does not remove Shorthand from .Properties (it stays present, IsExtensionData
        // == false) — confirmed by the raw-properties assertion below — so ConfigCheck.cs's
        // KnownKeys() also filters it out by name; the second assertion is that effective set,
        // which is what actually reaches the unknown-key/suggestion diagnostic.
        var rawNames = ConfigJsonContext.Default.BorderConfig.Properties.Select(p => p.Name).ToList();
        Assert.Contains("Shorthand", rawNames);

        var knownNames = ConfigJsonContext.Default.BorderConfig.Properties
            .Where(p => !p.IsExtensionData && p.Name != "Shorthand")
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(
            new[] { "enabled", "color", "style", "edges", "collapse" }.OrderBy(n => n, StringComparer.Ordinal),
            knownNames.OrderBy(n => n, StringComparer.Ordinal));
    }
}
