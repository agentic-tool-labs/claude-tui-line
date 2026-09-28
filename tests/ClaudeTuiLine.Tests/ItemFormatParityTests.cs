using System.Text.RegularExpressions;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4 (revised): the no-<c>items</c> default render and an explicit
/// <c>{ "item": "&lt;id&gt;" }</c> config must produce the same text for every builtin id, through
/// the same formatting mechanism (<see cref="LeafItems.ApplyFormat"/>). Enumerates
/// <see cref="ItemRegistry.DefaultIds"/> itself — never a hand-written id list — so a new registry
/// row is covered automatically. Drives the real per-id production methods
/// (<see cref="ItemRegistry.ItemDefinition.BuildDefaultSegment"/>,
/// <see cref="ItemRegistry.ItemDefinition.ResolveValue"/>) and the real configured-items pipeline
/// (<see cref="LeafItems.Resolve"/> + <see cref="LeafContent.Decide"/>) rather than a hand-built
/// comparison, so a divergence here is a divergence Program.cs would actually render.
/// </summary>
public class ItemFormatParityTests
{
    private static readonly StatusInput Input = new()
    {
        Cwd = "/Users/example/git/repos/claude-tui-line",
        Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "jimcline", Name = "claude-tui-line" } },
        Worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" },
        Pr = new PrInfo { Number = 42, ReviewState = "approved" },
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        OutputStyle = new OutputStyleInfo { Name = "concise" },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 62.5, TotalInputTokens = 125_000, ContextWindowSize = 200_000 },
        RateLimits = new RateLimitsInfo { FiveHour = new RateWindowInfo { UsedPercentage = 30.0 }, SevenDay = new RateWindowInfo { UsedPercentage = 85.0 } },
        Agent = new AgentInfo { Name = "implementor" },
        Vim = new VimInfo { Mode = "NORMAL" },
    };

    private const string GitBranch = "main";
    private static readonly EngramProbeResult Engram = new(new EngramResult(Facts: 5, Verb: "recalled"), null, null, EngramState.Active, null);
    private static readonly ItemContext Ctx = new(Input, GitBranch, Engram, remoteUrlProbe: () => null);

    public static IEnumerable<object[]> DefaultIds() =>
        ItemRegistry.DefaultIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(DefaultIds))]
    public void ConfiguredItemText_MatchesDefaultRenderText(string id)
    {
        var definition = ItemRegistry.Find(id)!;
        var defaultText = definition.BuildDefaultSegment(Ctx)?.Plain;
        var rawValue = definition.ResolveValue(Ctx);

        Assert.True(rawValue is not null, $"fixture must give '{id}' a non-null raw value to be a meaningful comparison");

        var item = new PaneItem(id, null, null, null);
        var values = new Dictionary<string, string?> { [id] = rawValue };
        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal(defaultText, decision.Text);
    }

    [Theory]
    [MemberData(nameof(DefaultIds))]
    public void ConfiguredItemMarkup_MatchesDefaultRenderMarkup(string id)
    {
        var definition = ItemRegistry.Find(id)!;
        var defaultSegment = definition.BuildDefaultSegment(Ctx);
        var rawValue = definition.ResolveValue(Ctx);

        Assert.True(rawValue is not null, $"fixture must give '{id}' a non-null raw value to be a meaningful comparison");

        var item = new PaneItem(id, null, null, null);
        var values = new Dictionary<string, string?> { [id] = rawValue };
        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());
        var color = ColorResolution.Resolve(item.Color, values, new Dictionary<string, ColorResolution.ColorRule>());
        var renderedSegment = SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, color);

        // default-links-branch-directory.md §1.1: a DefaultLinkTemplate is invoked only from the
        // configured-items path (LeafItems.Resolve), never from the bare default pipeline this
        // fixture's `defaultSegment` comes from — so an id with a default link (here, `directory`,
        // whose fixture Cwd resolves one) legitimately gains an OSC-8 wrap the default-pipeline
        // segment never had. Unwrap it before the parity comparison, which is about formatting and
        // colour, not about link presence.
        var renderedMarkup = OscHyperlink.TryUnwrap(renderedSegment.Markup, out _, out var inner) ? inner : renderedSegment.Markup;

        Assert.Equal(defaultSegment?.Markup, renderedMarkup);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §6: pins Spectre's own nested-markup resolution, not just this
    /// codebase's string-building — an outer config <c>color</c> must claim only the text a
    /// builtin's internal markup doesn't already own. Renders through the real Spectre pipeline
    /// (<see cref="AnsiConsole.Create"/> into a captured writer) and inspects the actual ANSI SGR
    /// codes so a future Spectre change to that resolution turns this red instead of the
    /// statusline silently losing its threshold colours.
    /// </summary>
    [Fact]
    public void ConfiguredItem_WithConfigColor_KeepsInternalThresholdColor_OuterColorClaimsOnlyUnclaimedText()
    {
        var item = new PaneItem("context", null, new ColorResolution.ColorExpr.Literal("red"), null);
        var rawValue = ItemRegistry.Find("context")!.ResolveValue(Ctx);
        var values = new Dictionary<string, string?> { ["context"] = rawValue };
        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());
        var color = ColorResolution.Resolve(item.Color, values, new Dictionary<string, ColorResolution.ColorRule>());
        var renderedSegment = SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, color);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Write(new Markup(renderedSegment.Markup));
        var ansi = writer.ToString();

        var runs = Regex.Matches(ansi, "\x1b\\[[0-9;]*m([^\x1b]*)")
            .Select(m => (Code: m.Value[..(m.Value.IndexOf('m') + 1)], Text: m.Groups[1].Value))
            .Where(r => r.Text.Length > 0)
            .ToList();

        var pctRun = runs.FirstOrDefault(r => r.Text.Contains("62%"));
        var leadRun = runs.FirstOrDefault(r => r.Text.Contains("ctx:"));

        Assert.False(pctRun.Text is null, $"expected a distinctly-styled run containing '62%' in rendered ANSI output: {ansi}");
        Assert.False(leadRun.Text is null, $"expected a distinctly-styled run containing 'ctx:' in rendered ANSI output: {ansi}");
        Assert.NotEqual(leadRun.Code, pctRun.Code);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4/§6: a decorative row's own colour (model's fixed identity navy) is a
    /// default an item-level config colour REPLACES, not a span it nests around — the row claims
    /// its entire text internally, leaving nothing unclaimed for an outer colour to govern under
    /// the nesting rule, which is why nesting left it invisible. Reads the innermost active SGR
    /// span over the actual rendered characters (via contiguous ANSI runs), not the first/outer
    /// code in the stream — an earlier manual check read the wrong code and reported the outer
    /// config colour as applied when the bytes still said navy.
    /// </summary>
    [Fact]
    public void ConfiguredItem_DecorativeColor_WithConfigColor_ReplacesInternalColor()
    {
        var item = new PaneItem("model", null, new ColorResolution.ColorExpr.Literal("yellow"), null);
        var rawValue = ItemRegistry.Find("model")!.ResolveValue(Ctx);
        var values = new Dictionary<string, string?> { ["model"] = rawValue };
        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());
        var color = ColorResolution.Resolve(item.Color, values, new Dictionary<string, ColorResolution.ColorRule>());
        var renderedSegment = SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, color);

        var ansi = RenderToAnsi(renderedSegment.Markup);
        var runs = SgrRuns(ansi);

        var textRun = Assert.Single(runs, r => r.Text.Contains(rawValue!));
        Assert.All(runs, r => Assert.Equal(textRun.Code, r.Code));
        Assert.Equal(RenderSgrForLiteral("yellow"), textRun.Code);
        Assert.NotEqual(RenderSgrForLiteral("navy"), textRun.Code);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4/§6: unlike a decorative row, a semantic row's colour (rate-limits'
    /// per-window §6 threshold colour) is value-derived, so an item-level config colour must keep
    /// nesting around it rather than replacing it — an outer colour claims only the "5h:"/"7d:"
    /// labels the row left unclaimed, never the percentages themselves.
    /// </summary>
    [Fact]
    public void ConfiguredItem_SemanticColor_WithConfigColor_KeepsInternalThresholdColor_OuterColorClaimsOnlyUnclaimedText()
    {
        var item = new PaneItem("rate-limits", null, new ColorResolution.ColorExpr.Literal("red"), null);
        var rawValue = ItemRegistry.Find("rate-limits")!.ResolveValue(Ctx);
        var values = new Dictionary<string, string?> { ["rate-limits"] = rawValue };
        var resolved = LeafItems.Resolve(new[] { item }, values, Ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());
        var color = ColorResolution.Resolve(item.Color, values, new Dictionary<string, ColorResolution.ColorRule>());
        var renderedSegment = SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, color);

        var ansi = RenderToAnsi(renderedSegment.Markup);
        var runs = SgrRuns(ansi);

        var fiveHourRun = runs.FirstOrDefault(r => r.Text.Contains("30%"));
        var leadRun = runs.FirstOrDefault(r => r.Text.Contains("5h:"));

        Assert.False(fiveHourRun.Text is null, $"expected a distinctly-styled run containing '30%' in rendered ANSI output: {ansi}");
        Assert.False(leadRun.Text is null, $"expected a distinctly-styled run containing '5h:' in rendered ANSI output: {ansi}");
        Assert.NotEqual(leadRun.Code, fiveHourRun.Code);
    }

    private static string RenderToAnsi(string markup)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Write(new Markup(markup));
        return writer.ToString();
    }

    private static List<(string Code, string Text)> SgrRuns(string ansi) =>
        Regex.Matches(ansi, "\x1b\\[[0-9;]*m([^\x1b]*)")
            .Select(m => (Code: m.Value[..(m.Value.IndexOf('m') + 1)], Text: m.Groups[1].Value))
            .Where(r => r.Text.Length > 0)
            .ToList();

    private static string RenderSgrForLiteral(string colorSpec)
    {
        var ansi = RenderToAnsi($"[{colorSpec}]x[/]");
        var match = Regex.Match(ansi, "\x1b\\[[0-9;]*m");
        Assert.True(match.Success, $"expected an ANSI SGR code in rendered output for color '{colorSpec}'");
        return match.Value;
    }

    [Fact]
    public void ContextRawValue_StillParsesAsNumber_ForColorThresholdRules()
    {
        var raw = ItemRegistry.Find("context")!.ResolveValue(Ctx);

        Assert.True(double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out _),
            $"context's raw value ('{raw}') must stay a bare parseable number for §6.4 threshold rules");
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §6.4: a numeric "thresholds" rule sourced <c>from</c> a composite item
    /// (rate-limits has two windows, not one scalar) must evaluate against a bare parseable raw
    /// value, not the composite label text — and must actually reach the matching threshold branch
    /// rather than silently falling through to <see cref="ColorResolution.ColorRule.Default"/>, which
    /// is the wrong-in-the-worst-way outcome for a value that is over its limit. Pins both the
    /// resolved color and the real rendered ANSI SGR code so a regression that resolves the right
    /// string but fails to actually paint it would still be caught.
    /// </summary>
    [Fact]
    public void RateLimitsThresholdRule_UsesMaxAcrossWindows_OverLimitDoesNotFallToDefault()
    {
        var rawValue = ItemRegistry.Find("rate-limits")!.ResolveValue(Ctx);
        Assert.True(double.TryParse(rawValue, System.Globalization.CultureInfo.InvariantCulture, out var numeric),
            $"rate-limits' raw value ('{rawValue}') must be a bare parseable number for a from:\"rate-limits\" threshold rule to evaluate numerically");
        Assert.Equal(85, numeric); // fixture's SevenDay window (85%) exceeds FiveHour's (30%) — raw value is the max across windows

        var rule = new ColorResolution.ColorRule(
            Thresholds: new[]
            {
                new ColorResolution.ThresholdRule(80, new ColorResolution.ColorValue.Literal("red")),
                new ColorResolution.ThresholdRule(50, new ColorResolution.ColorValue.Literal("yellow")),
            },
            Match: null,
            Default: new ColorResolution.ColorValue.Literal("green"),
            From: "rate-limits");
        var defaultSpec = ((ColorResolution.ColorValue.Literal)rule.Default!).Spec;
        var values = new Dictionary<string, string?> { ["rate-limits"] = rawValue };

        var resolvedColor = ColorResolution.Resolve(new ColorResolution.ColorExpr.Inline(rule), values, new Dictionary<string, ColorResolution.ColorRule>());

        Assert.Equal("red", resolvedColor);
        Assert.NotEqual(defaultSpec, resolvedColor);

        string RenderSgr(string color)
        {
            var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.Standard,
                Out = new AnsiConsoleOutput(writer),
            });
            console.Write(new Markup($"[{color}]rl[/]"));
            var match = Regex.Match(writer.ToString(), "\x1b\\[[0-9;]*m");
            Assert.True(match.Success, $"expected an ANSI SGR code in rendered output for color '{color}'");
            return match.Value;
        }

        Assert.NotEqual(RenderSgr(defaultSpec), RenderSgr(resolvedColor!));
    }
}
