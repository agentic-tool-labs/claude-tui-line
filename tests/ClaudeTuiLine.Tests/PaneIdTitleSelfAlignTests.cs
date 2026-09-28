using System.Text.RegularExpressions;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC pane-id-title-align: smoke coverage for pane <c>id</c>, the <c>title</c>/<c>titleAlign</c>
/// in-border caption, and <c>selfAlign</c>'s auto-margin leftover distribution. Not the spec's full
/// 39-case matrix (§7) — see the implementor's report for what remains.
/// </summary>
public class PaneIdTitleSelfAlignTests
{
    private static readonly ResolvedConfig TopLevel = new(
        new ColorResolution.ColorExpr.Literal("grey"),
        BoxBorder.Rounded,
        PaneBorderEdges.All,
        ChromeReserve: 3,
        ColorSystem: ColorSystemSupport.Standard,
        Colors: new Dictionary<string, ColorResolution.ColorRule>());

    private static readonly ItemContext Ctx = new(
        new StatusInput
        {
            Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
            Effort = new EffortInfo { Level = "high" },
            Thinking = new ThinkingInfo { Enabled = true },
            ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
        },
        gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static IReadOnlyList<PaneRow> RenderRows(Pane pane, int outerWidth, bool collapse = false)
    {
        var values = ItemValueResolver.Resolve(pane, Ctx, TopLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, outerWidth, Ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());
        return PaneTreeRenderer.Render(resolved, Ctx, values, TopLevel.Colors, new Dictionary<string, Segment>(), new RenderNoteCollector(), collapse: collapse).Buffer.Rows;
    }

    private static string RenderMarkup(Pane pane, int outerWidth) =>
        string.Join('\n', RenderRows(pane, outerWidth).Select(r => r.Markup));

    [Fact]
    public void NoNewKeysDeclared_ResolvesToDefaultsWithNoRenderEffect()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Items = new List<PaneItemJsonConfig> { new() { Item = "model" } } } },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);

        Assert.Null(pane.Id);
        Assert.Null(pane.Title);
        Assert.Equal(PaneSelfAlign.Left, pane.SelfAlign);
        Assert.Equal(PaneTitleAlign.Left, pane.TitleAlign);

        Assert.Empty(ConfigChecker.Check(config));
    }

    [Fact]
    public void DuplicatePaneId_ReportsError()
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
                        new() { Id = "left", Size = "10", Items = new List<PaneItemJsonConfig>() },
                        new() { Id = "left", Size = "10", Items = new List<PaneItemJsonConfig>() },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "duplicate-pane-id" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PaneIdShadowingItemId_ReportsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Id = "model", Items = new List<PaneItemJsonConfig> { new() { Item = "model", Id = "model" } } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "pane-id-shadows-item-id" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void EmptyPaneId_ReportsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { Id = "  ", Items = new List<PaneItemJsonConfig>() } },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "empty-pane-id" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Title_DrawnIntoTopBorderRow_NotAsContentRow()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Border = new BorderConfig { Shorthand = "all" },
                    Title = new PaneItemJsonConfig { Item = "model" },
                    Items = new List<PaneItemJsonConfig> { new() { Item = "model" } },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);
        Assert.NotNull(pane.Title);
        Assert.True(pane.Title!.IsTitle);

        var rows = RenderMarkup(pane, 40).Split('\n');
        Assert.Contains("Claude Opus 4.5", rows[0]);
        Assert.Equal(3, rows.Length);
    }

    [Fact]
    public void TitleWithoutBorder_ReportsError()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Border = new BorderConfig { Shorthand = "none" },
                    Title = new PaneItemJsonConfig { Item = "model" },
                    Items = new List<PaneItemJsonConfig>(),
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "title-without-border" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TitleAlignWithoutTitle_ReportsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { TitleAlign = "right", Items = new List<PaneItemJsonConfig>() } },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "title-align-without-title" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SelfAlignRight_PushesPaneToRightEdgeOfLeftoverRow()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Gutter = 0,
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "10", Border = new BorderConfig { Shorthand = "none" }, Items = new List<PaneItemJsonConfig>() },
                        new() { Size = "10", Border = new BorderConfig { Shorthand = "none" }, SelfAlign = "right", Items = new List<PaneItemJsonConfig>() },
                    },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel);
        var row = RenderRows(pane, 40)[0];
        var plain = Regex.Replace(row.Markup, @"\[/?[^\]]*\]", "");

        Assert.Equal(40, row.Width);
        Assert.Contains(new string(' ', 20), plain);
    }

    [Fact]
    public void SelfAlign_UnknownEnumValue_ReportsError()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig { Pane = new PaneConfig { SelfAlign = "diagonal", Items = new List<PaneItemJsonConfig>() } },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path.EndsWith("/selfAlign"));
    }

    // Review round 1, Finding A: E6 was wrong (fresh BorderGrid.cs read shows AddHorizontalRun
    // writes across a child's own top-edge column range under collapse:true, not just at the
    // synthetic boundary column) — spec §3.8 branch 2 applies: caption dropped, warning emitted.
    [Fact]
    public void TitleWithCollapse_CaptionDropped_AndDiagnosticEmitted()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Border = new BorderConfig { Collapse = true },
                Pane = new PaneConfig
                {
                    Border = new BorderConfig { Shorthand = "all" },
                    Title = new PaneItemJsonConfig { Item = "model" },
                    Items = new List<PaneItemJsonConfig> { new() { Item = "model" } },
                },
            },
        };

        var pane = ConfigLoader.ResolveRootPane(config, TopLevel with { Collapse = true });
        var rows = RenderRows(pane, 40, collapse: true);
        Assert.DoesNotContain("Claude Opus 4.5", rows[0].Markup);
        Assert.Equal(40, rows[0].Width);

        var diagnostics = ConfigChecker.Check(config);
        Assert.Contains(diagnostics, d => d.Code == "title-with-collapse" && d.Severity == DiagnosticSeverity.Warning);
    }

    // Review round 1, Finding B: CheckSelfAlign's fill-sibling/distribute:even "no leftover space"
    // heuristic is §4.2-specific (Vertical/side-by-side leftover-width redistribution) and must not
    // fire for a Horizontal (stacked) split, where §4.4's independent per-child alignment applies
    // regardless of siblings/distribute.
    [Fact]
    public void SelfAlignOnHorizontalSplitWithDistributeEven_NoFalsePositiveNoEffectWarning()
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
                        new() { Items = new List<PaneItemJsonConfig>() },
                        new() { SelfAlign = "right", Items = new List<PaneItemJsonConfig>() },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);
        Assert.DoesNotContain(diagnostics, d => d.Code == "self-align-no-effect");
    }
}
