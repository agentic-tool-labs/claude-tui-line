using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

public class SegmentBuilderTests
{
    private static string Expect(string tag, string plain) => $"[{tag}]{Markup.Escape(plain)}[/]";

    private static StatusInput Empty() => new();

    private static ItemContext Ctx(StatusInput input, string? gitBranch = null, EngramResult? engram = null) =>
        new(input, gitBranch, engram is null ? null : new EngramProbeResult(engram, null, null, EngramState.Active, null), remoteUrlProbe: () => null);

    private static ItemContext CtxWithSettings(StatusInput input, ItemSettingsJsonConfig itemSettings) =>
        new(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

    // Each autocompact test gets its own directory so tests never race each other or touch the
    // developer's real ~/.claude/settings.json (SPEC-102-showlabel-and-autocompact.md §E).
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctl-autocompact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteSettings(string dir, string fileName, string json)
    {
        var claudeDir = Path.Combine(dir, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, fileName), json);
    }

    // The context item now renders "ctx:0%" even with no context data
    // (SPEC context-zero-render §2.8), so single-item tests filter it out to keep
    // asserting only about the item under test.
    private static IReadOnlyList<Segment> Others(IReadOnlyList<Segment> segments) =>
        segments.Where(s => !s.Plain.StartsWith("ctx:", StringComparison.Ordinal)).ToList();

    // --- Segment 1: Directory ---

    [Fact]
    public void Directory_Present_UsesBasenameInTeal()
    {
        var input = Empty();
        input.Cwd = "/Users/example/git/repos/claude-tui-line";

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("claude-tui-line", seg.Plain);
        Assert.Equal(Expect("teal", "claude-tui-line"), seg.Markup);
    }

    [Fact]
    public void Directory_Absent_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));
        Assert.Empty(Others(segments));
    }

    [Fact]
    public void Directory_HostileBasename_IsMarkupEscaped()
    {
        var input = Empty();
        input.Cwd = "/tmp/[hacked]";

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("[hacked]", seg.Plain);
        Assert.Equal(Expect("teal", "[hacked]"), seg.Markup); // brackets doubled ([[hacked]]), not passed through raw
    }

    // item-specific-config.md T2/T3: itemSettings.directory.depth.

    [Fact]
    public void Directory_DepthTwo_ShowsTwoTrailingSegments()
    {
        var input = Empty();
        input.Cwd = "/Users/example/git/repos/claude-tui-line";
        var settings = new DirectoryItemSettings { Depth = 2 };

        Assert.Equal("repos/claude-tui-line", SegmentBuilder.ResolveDirectory(input.Cwd, settings));
        Assert.Equal("repos/claude-tui-line", SegmentBuilder.BuildDirectory(input.Cwd, settings)!.Plain);
    }

    [Fact]
    public void Directory_DepthAbsent_UsesBasename()
    {
        var input = Empty();
        input.Cwd = "/Users/example/git/repos/claude-tui-line";

        Assert.Equal("claude-tui-line", SegmentBuilder.ResolveDirectory(input.Cwd, settings: null));
        Assert.Equal("claude-tui-line", SegmentBuilder.ResolveDirectory(input.Cwd, new DirectoryItemSettings()));
    }

    [Fact]
    public void Directory_TwoPlacementsOfSameId_ShareTheSameDepth()
    {
        // item-specific-config.md T10: resolution is per-id, not per-placement — two placements
        // of `directory` in the same render see the same itemSettings.
        var input = Empty();
        input.Cwd = "/Users/example/git/repos/claude-tui-line";
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { Depth = 2 } });

        var items = new List<PaneItem> { new("directory", null, null, null), new("directory", null, null, null) };
        var noBorder = new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
        var root = new Pane(PaneSplit.None, Array.Empty<Pane>(), "fill", noBorder, null, "…", null, items);

        var values = ItemValueResolver.Resolve(root, ctx);
        var resolved = LeafItems.Resolve(items, values, ctx, new Dictionary<string, Segment>());

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, r => Assert.Equal("repos/claude-tui-line", r.Value));
    }

    // --- Segment 2: Git branch ---

    [Fact]
    public void GitBranch_Present_Green()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), gitBranch: "main"));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("main", seg.Plain);
        Assert.Equal(Expect("green", "main"), seg.Markup);
    }

    [Fact]
    public void GitBranch_HostileName_IsMarkupEscaped()
    {
        const string hostile = "[red]x[/]";
        var segments = SegmentBuilder.Build(Ctx(Empty(), gitBranch: hostile));

        var seg = Assert.Single(Others(segments));
        Assert.Equal(hostile, seg.Plain);
        Assert.Equal(Expect("green", hostile), seg.Markup);
    }

    [Fact]
    public void GitBranch_Empty_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), gitBranch: ""));
        Assert.Empty(Others(segments));
    }

    // --- Segment 3: GitHub repo ---

    [Fact]
    public void Repo_Present_DimOwnerSlashName()
    {
        var input = Empty();
        input.Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "jimcline", Name = "claude-tui-line" } };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("jimcline/claude-tui-line", seg.Plain);
        Assert.Equal(Expect("dim", "jimcline/claude-tui-line"), seg.Markup);
    }

    [Fact]
    public void Repo_MissingOwner_SuppressesSegment()
    {
        var input = Empty();
        input.Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = null, Name = "claude-tui-line" } };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(Others(segments));
    }

    // --- repo-host (opt-in only, not part of Build()'s default pipeline) ---

    [Fact]
    public void RepoHost_Present_ResolvesToHost()
    {
        var repo = new RepoInfo { Host = "github.com", Owner = "JimCline", Name = "claude-tui-line" };

        Assert.Equal("github.com", SegmentBuilder.ResolveRepoHost(repo));
        Assert.Equal(Expect("dim", "github.com"), SegmentBuilder.BuildRepoHost(repo)!.Markup);
    }

    [Fact]
    public void RepoHost_Null_Suppressed()
    {
        var repo = new RepoInfo { Host = null, Owner = "JimCline", Name = "claude-tui-line" };

        Assert.Null(SegmentBuilder.ResolveRepoHost(repo));
        Assert.Null(SegmentBuilder.BuildRepoHost(repo));
    }

    [Fact]
    public void RepoHost_Empty_Suppressed()
    {
        var repo = new RepoInfo { Host = "", Owner = "JimCline", Name = "claude-tui-line" };

        Assert.Null(SegmentBuilder.ResolveRepoHost(repo));
        Assert.Null(SegmentBuilder.BuildRepoHost(repo));
    }

    [Fact]
    public void RepoHost_NullRepo_SuppressedNoException()
    {
        Assert.Null(SegmentBuilder.ResolveRepoHost(null));
        Assert.Null(SegmentBuilder.BuildRepoHost(null));
    }

    [Fact]
    public void Repo_WithHostSet_TextStaysOwnerSlashName_HostDoesNotLeak()
    {
        var repo = new RepoInfo { Host = "github.com", Owner = "JimCline", Name = "claude-tui-line" };

        Assert.Equal("JimCline/claude-tui-line", SegmentBuilder.ResolveRepo(repo));
    }

    // --- Segment 4: Worktree ---

    [Fact]
    public void Worktree_NameOnly()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("worktree:feature-x", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x"), seg.Markup);
    }

    [Fact]
    public void Worktree_NameAndBranch_DefaultOmitsBranch()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("worktree:feature-x", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x"), seg.Markup);
    }

    [Fact]
    public void Worktree_ShowBranchFalse_OmitsBranch()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { Worktree = new WorktreeItemSettings { ShowBranch = false } });

        var segments = SegmentBuilder.Build(ctx);

        var seg = Assert.Single(Others(segments));
        Assert.Equal("worktree:feature-x", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x"), seg.Markup);
    }

    [Fact]
    public void Worktree_ShowBranchTrue_RendersBranch()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { Worktree = new WorktreeItemSettings { ShowBranch = true } });

        var segments = SegmentBuilder.Build(ctx);

        var seg = Assert.Single(Others(segments));
        Assert.Equal("worktree:feature-x(main)", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x(main)"), seg.Markup);
    }

    [Fact]
    public void Worktree_ShowBranchNull_OmitsBranch()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { Worktree = new WorktreeItemSettings() });

        var segments = SegmentBuilder.Build(ctx);

        var seg = Assert.Single(Others(segments));
        Assert.Equal("worktree:feature-x", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x"), seg.Markup);
    }

    [Fact]
    public void Worktree_ShowBranchFalse_NameOnly_Unchanged()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { Worktree = new WorktreeItemSettings { ShowBranch = false } });

        var segments = SegmentBuilder.Build(ctx);

        var seg = Assert.Single(Others(segments));
        Assert.Equal("worktree:feature-x", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x"), seg.Markup);
    }

    [Fact]
    public void Worktree_ShowBranchFalse_ResolvedValueOmitsBranch()
    {
        var worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };
        var settings = new WorktreeItemSettings { ShowBranch = false };

        Assert.Equal("feature-x", SegmentBuilder.ResolveWorktree(worktree, settings));
    }

    [Fact]
    public void Worktree_DefaultResolvedValueOmitsBranch()
    {
        var worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };

        Assert.Equal("feature-x", SegmentBuilder.ResolveWorktree(worktree));
    }

    // --- Segment 5: PR ---

    [Theory]
    [InlineData("approved", "PR #42 [approved]")]
    [InlineData("changes_requested", "PR #42 [changes]")]
    [InlineData("draft", "PR #42 [draft]")]
    [InlineData("merged", "PR #42 [merged]")] // unknown state falls through to the raw state
    [InlineData(null, "PR #42")]
    public void PullRequest_StateMapping(string? reviewState, string expectedPlain)
    {
        var input = Empty();
        input.Pr = new PrInfo { Number = 42, ReviewState = reviewState };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal(expectedPlain, seg.Plain);
        Assert.Equal(Expect("olive", expectedPlain), seg.Markup);
    }

    [Fact]
    public void PullRequest_NumberAbsent_NoSegment()
    {
        var input = Empty();
        input.Pr = new PrInfo { ReviewState = "approved" };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(Others(segments));
    }

    // --- Segment 6: Model ---

    [Fact]
    public void Model_Present_Navy()
    {
        var input = Empty();
        input.Model = new ModelInfo { DisplayName = "Claude Opus 4.5" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("Claude Opus 4.5", seg.Plain);
        Assert.Equal(Expect("navy", "Claude Opus 4.5"), seg.Markup);
    }

    // --- Segment 7: Effort ---

    [Fact]
    public void Effort_Present_Dim()
    {
        var input = Empty();
        input.Effort = new EffortInfo { Level = "high" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("effort:high", seg.Plain);
        Assert.Equal(Expect("dim", "effort:high"), seg.Markup);
    }

    // --- Segment 8: Thinking ---

    [Fact]
    public void Thinking_EnabledTrue_ShowsPurple()
    {
        var input = Empty();
        input.Thinking = new ThinkingInfo { Enabled = true };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("thinking", seg.Plain);
        Assert.Equal(Expect("purple", "thinking"), seg.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Thinking_NotTrue_NoSegment(bool? enabled)
    {
        var input = Empty();
        input.Thinking = new ThinkingInfo { Enabled = enabled };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(Others(segments));
    }

    // --- Segment 9: Output style ---

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData("DEFAULT")]
    public void OutputStyle_DefaultCaseInsensitive_Suppressed(string name)
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = name };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(Others(segments));
    }

    [Fact]
    public void OutputStyle_NonDefault_ShowsYellow()
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = "concise" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("style:concise", seg.Plain);
        Assert.Equal(Expect("yellow", "style:concise"), seg.Markup);
    }

    [Fact]
    public void OutputStyle_ShowLabelFalse_OmitsLabel()
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = "concise" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { OutputStyle = new OutputStyleItemSettings { ShowLabel = false } });

        var segments = SegmentBuilder.Build(ctx);

        var seg = Assert.Single(Others(segments));
        Assert.Equal("concise", seg.Plain);
        Assert.Equal(Expect("yellow", "concise"), seg.Markup);
    }

    [Fact]
    public void OutputStyle_ShowLabelTrue_MatchesDefault()
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = "concise" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { OutputStyle = new OutputStyleItemSettings { ShowLabel = true } });

        var segments = SegmentBuilder.Build(ctx);

        var seg = Assert.Single(Others(segments));
        Assert.Equal("style:concise", seg.Plain);
        Assert.Equal(Expect("yellow", "style:concise"), seg.Markup);
    }

    [Fact]
    public void ResolveOutputStyle_UnaffectedByShowLabel()
    {
        var style = new OutputStyleInfo { Name = "concise" };

        Assert.Equal("concise", SegmentBuilder.ResolveOutputStyle(style));
        Assert.Null(SegmentBuilder.ResolveOutputStyle(new OutputStyleInfo { Name = "default" }));
    }

    [Fact]
    public void ApplyFormat_ShowLabelNull_MatchesTwoArgOverload()
    {
        Assert.Equal(LeafItems.ApplyFormat("style:{}", "Explanatory"), LeafItems.ApplyFormat("style:{}", "Explanatory", null));
    }

    [Fact]
    public void ApplyFormat_ShowLabelFalse_ReturnsBareValue()
    {
        Assert.Equal("Explanatory", LeafItems.ApplyFormat("style:{}", "Explanatory", false));
    }

    [Fact]
    public void OutputStyle_TwoPlacementsOfSameId_ShareTheSameShowLabel()
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = "concise" };
        var ctx = CtxWithSettings(input, new ItemSettingsJsonConfig { OutputStyle = new OutputStyleItemSettings { ShowLabel = false } });

        var items = new List<PaneItem> { new("output-style", null, null, null), new("output-style", null, null, null) };
        var noBorder = new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
        var root = new Pane(PaneSplit.None, Array.Empty<Pane>(), "fill", noBorder, null, "…", null, items);

        var values = ItemValueResolver.Resolve(root, ctx);
        var resolved = LeafItems.Resolve(items, values, ctx, new Dictionary<string, Segment>());

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, r => Assert.Equal("concise", r.Display?.Plain));
    }

    // --- Segment 10: Context (threshold boundaries + token counts) ---

    [Theory]
    [InlineData(49.0, "green")]
    [InlineData(50.0, "olive")]
    [InlineData(79.0, "olive")]
    [InlineData(80.0, "maroon")]
    public void Context_ThresholdBoundaries_NoTokenCounts(double usedPercentage, string expectedTag)
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo { UsedPercentage = usedPercentage };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        var pct = (int)usedPercentage;
        Assert.Equal($"ctx:{pct}%", seg.Plain);
        Assert.Equal($"ctx:[{expectedTag}]{pct}%[/]", seg.Markup);
    }

    [Fact]
    public void Context_WithTokenCounts_AppendsDimUsage()
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo
        {
            UsedPercentage = 62.0,
            TotalInputTokens = 125000,
            ContextWindowSize = 200000,
        };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("ctx:62% (125k/200k)", seg.Plain);
        Assert.Equal("ctx:[olive]62%[/] [dim](125k/200k)[/]", seg.Markup);
    }

    [Fact]
    public void Context_Absent_RendersZeroPercent()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));

        var seg = Assert.Single(segments);
        Assert.Equal("ctx:0%", seg.Plain);
    }

    [Fact]
    public void Context_Absent_MarkupUsesZeroThresholdTag()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));

        var seg = Assert.Single(segments);
        var tag = ColorResolution.ResolveStandardThreshold(0);
        Assert.Equal($"ctx:[{tag}]0%[/]", seg.Markup);
    }

    [Fact]
    public void Context_WindowPresentPercentageAbsent_RendersZeroPercent()
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo();

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("ctx:0%", seg.Plain);
    }

    [Fact]
    public void Context_PercentageAbsentTokenCountsPresent_SuppressesParenthetical()
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo { TotalInputTokens = 150000, ContextWindowSize = 200000 };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("ctx:0%", seg.Plain);
        Assert.DoesNotContain("(", seg.Markup);
        Assert.DoesNotContain("150k", seg.Markup);
    }

    [Fact]
    public void ResolveContext_Absent_ReturnsZero()
    {
        Assert.Equal("0", SegmentBuilder.ResolveContext(null));
        Assert.Equal("0", SegmentBuilder.ResolveContext(new ContextWindowInfo()));
    }

    [Fact]
    public void ResolveContext_Absent_HasNoPercentSignAndParses()
    {
        var raw = SegmentBuilder.ResolveContext(null);
        Assert.DoesNotContain("%", raw);
        Assert.True(int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed));
        Assert.Equal(0, parsed);
    }

    [Fact]
    public void Context_PresentValue_UnchangedFromBeforeThisChange()
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo
        {
            UsedPercentage = 62.5,
            TotalInputTokens = 125000,
            ContextWindowSize = 200000,
        };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("ctx:62% (125k/200k)", seg.Plain);
        Assert.Equal("ctx:[olive]62%[/] [dim](125k/200k)[/]", seg.Markup);
    }

    [Fact]
    public void Context_DisplayTextAndBuildSegment_AgreeWhenAbsent()
    {
        var displayText = SegmentBuilder.ResolveContextDisplayText(null);
        Assert.Equal("ctx:0%", displayText);
        Assert.Equal(displayText, SegmentBuilder.BuildContext(null)!.Plain);
    }

    [Fact]
    public async Task Context_AbsentInStandalonePane_DoesNotCollapse()
    {
        var plainText = await RenderPaneWithSoleItem("context", Empty());
        Assert.Contains("ctx:0%", plainText);
    }

    [Fact]
    public async Task RateLimits_AbsentInStandalonePane_StillCollapses()
    {
        var plainText = await RenderPaneWithSoleItem("rate-limits", Empty());
        Assert.Equal(string.Empty, plainText.Trim());
    }

    /// <summary>
    /// SPEC context-zero-render §7 T11: a numeric "thresholds" colour rule must see the context
    /// item's default-zero value and evaluate against it, rather than treating an absent-context
    /// value as valueless and falling through to the rule's default branch.
    /// </summary>
    [Fact]
    public void Context_ThresholdRule_SeesDefaultZeroValue_NotSkippedAsValueless()
    {
        var rawValue = ItemRegistry.Find("context")!.ResolveValue(Ctx(Empty()));
        Assert.Equal("0", rawValue);

        var rule = new ColorResolution.ColorRule(
            Thresholds: new[] { new ColorResolution.ThresholdRule(0, new ColorResolution.ColorValue.Literal("red")) },
            Match: null,
            Default: new ColorResolution.ColorValue.Literal("green"),
            From: "context");
        var values = new Dictionary<string, string?> { ["context"] = rawValue };

        var resolvedColor = ColorResolution.Resolve(new ColorResolution.ColorExpr.Inline(rule), values, new Dictionary<string, ColorResolution.ColorRule>());

        Assert.Equal("red", resolvedColor);
    }

    private static async Task<string> RenderPaneWithSoleItem(string itemId, StatusInput input)
    {
        var json = $$"""
        {
          "surface": {
            "pane": { "border": { "enabled": false }, "items": [ { "item": "{{itemId}}" } ] }
          }
        }
        """;
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
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

        var ctx = Ctx(input);
        var values = (await ItemValueResolver.ResolveAsync(
            pane, ctx, topLevel.Colors, rawStdinJson: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null,
            new RenderNoteCollector())).Values;

        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values, new Dictionary<string, Segment>(), new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors, new Dictionary<string, Segment>(), new RenderNoteCollector());

        return string.Join('\n', rendered.Buffer.Rows.Select(r => Markup.Remove(r.Markup)));
    }

    // --- Segment 11: Rate limits ---

    [Theory]
    [InlineData(49.0, "green")]
    [InlineData(50.0, "olive")]
    [InlineData(79.0, "olive")]
    [InlineData(80.0, "maroon")]
    public void RateLimits_FiveHourOnly_ThresholdBoundaries(double pct, string expectedTag)
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo { FiveHour = new RateWindowInfo { UsedPercentage = pct } };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        var v = (int)pct;
        Assert.Equal($"5h:{v}%", seg.Plain);
        Assert.Equal($"5h:[{expectedTag}]{v}%[/]", seg.Markup);
    }

    [Fact]
    public void RateLimits_BothWindows_JoinedWithDimSlash()
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo
        {
            FiveHour = new RateWindowInfo { UsedPercentage = 30.0 },
            SevenDay = new RateWindowInfo { UsedPercentage = 85.0 },
        };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("5h:30% / 7d:85%", seg.Plain);
        Assert.Equal("5h:[green]30%[/] [dim]/[/] 7d:[maroon]85%[/]", seg.Markup);
    }

    [Fact]
    public void RateLimits_SevenDayOnly()
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo { SevenDay = new RateWindowInfo { UsedPercentage = 60.0 } };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("7d:60%", seg.Plain);
        Assert.Equal("7d:[olive]60%[/]", seg.Markup);
    }

    [Fact]
    public void RateLimits_NeitherWindow_NoSegment()
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo();

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(Others(segments));
    }

    // --- Segment 12: Agent ---

    [Fact]
    public void Agent_Present_Purple()
    {
        var input = Empty();
        input.Agent = new AgentInfo { Name = "implementor" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("agent:implementor", seg.Plain);
        Assert.Equal(Expect("purple", "agent:implementor"), seg.Markup);
    }

    // --- Segment 13: Engram ---

    [Fact]
    public void Engram_FactsOnly()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), engram: new EngramResult(42, null)));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("engram:42", seg.Plain);
        Assert.Equal("[dim]engram:42[/]", seg.Markup);
    }

    [Fact]
    public void Engram_VerbOnly()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), engram: new EngramResult(null, "✱ captured")));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("✱ captured", seg.Plain);
        Assert.Equal(Expect("purple", "✱ captured"), seg.Markup);
    }

    [Fact]
    public void Engram_FactsAndVerb_Combined()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), engram: new EngramResult(7, "◉ recalled")));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("engram:7 ◉ recalled", seg.Plain);
        Assert.Equal("[dim]engram:7[/] [purple]◉ recalled[/]", seg.Markup);
    }

    [Fact]
    public void Engram_Null_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));
        Assert.Empty(Others(segments));
    }

    // --- Segment 14: Vim mode ---

    [Fact]
    public void VimMode_Present_OliveBracketed()
    {
        var input = Empty();
        input.Vim = new VimInfo { Mode = "NORMAL" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(Others(segments));
        Assert.Equal("[NORMAL]", seg.Plain);
        Assert.Equal(Expect("olive", "[NORMAL]"), seg.Markup);
    }

    // --- Ordering across all 14 ---

    [Fact]
    public void AllSegmentsPresent_PreserveCaptureOrder()
    {
        var input = new StatusInput
        {
            Cwd = "/tmp/proj",
            Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "o", Name = "n" } },
            Worktree = new WorktreeInfo { Name = "wt" },
            Pr = new PrInfo { Number = 1 },
            Model = new ModelInfo { DisplayName = "M" },
            Effort = new EffortInfo { Level = "low" },
            Thinking = new ThinkingInfo { Enabled = true },
            OutputStyle = new OutputStyleInfo { Name = "custom" },
            ContextWindow = new ContextWindowInfo { UsedPercentage = 10 },
            RateLimits = new RateLimitsInfo { FiveHour = new RateWindowInfo { UsedPercentage = 10 } },
            Agent = new AgentInfo { Name = "a" },
        };

        var segments = SegmentBuilder.Build(Ctx(input, gitBranch: "main", engram: new EngramResult(1, "verb")));

        Assert.Equal(13, segments.Count); // all but vim mode (not set on this fixture)
        Assert.Equal("proj", segments[0].Plain);          // directory
        Assert.Equal("main", segments[1].Plain);           // git branch
        Assert.Equal("o/n", segments[2].Plain);             // repo
        Assert.Equal("worktree:wt", segments[3].Plain);     // worktree
        Assert.Equal("PR #1", segments[4].Plain);           // pr
        Assert.Equal("M", segments[5].Plain);                // model
        Assert.Equal("effort:low", segments[6].Plain);       // effort
        Assert.Equal("thinking", segments[7].Plain);         // thinking
        Assert.Equal("style:custom", segments[8].Plain);     // output style
        Assert.StartsWith("ctx:", segments[9].Plain);         // context
        Assert.StartsWith("5h:", segments[10].Plain);         // rate limits
        Assert.Equal("agent:a", segments[11].Plain);          // agent
        Assert.StartsWith("engram:1", segments[12].Plain);    // engram
        // no vim mode in this fixture, so 14th slot (vim) is simply absent
    }

    [Theory]
    [InlineData("250000", "250K")]
    [InlineData("200", "200K")]
    [InlineData("\"500k\"", "500K")]
    [InlineData("\"500K\"", "500K")]
    [InlineData("\"1M\"", "1M")]
    [InlineData("1000000", "1M")]
    [InlineData("\"auto\"", "auto")]
    [InlineData("\"AUTO\"", "auto")]
    public void ResolveAutocompact_NormalizesWindowValues(string windowJson, string expected)
    {
        var dir = NewTempDir();
        WriteSettings(dir, "settings.json", "{\"autoCompactWindow\": " + windowJson + "}");

        Assert.Equal(expected, SegmentBuilder.ResolveAutocompact(dir, homeDir: null, envWindow: null));
    }

    [Theory]
    [InlineData("50")]
    [InlineData("99999")]
    [InlineData("2000000")]
    [InlineData("\"banana\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void ResolveAutocompact_OutOfRangeOrJunkWindow_FallsBackToAuto(string windowJson)
    {
        var dir = NewTempDir();
        WriteSettings(dir, "settings.json", "{\"autoCompactWindow\": " + windowJson + "}");

        Assert.Equal("auto", SegmentBuilder.ResolveAutocompact(dir, homeDir: null, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_DisabledWinsOverWindow()
    {
        var dir = NewTempDir();
        WriteSettings(dir, "settings.json", "{\"autoCompactEnabled\": false, \"autoCompactWindow\": 250000}");

        Assert.Equal("off", SegmentBuilder.ResolveAutocompact(dir, homeDir: null, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_NoKeysAnywhere_ReturnsAuto()
    {
        var dir = NewTempDir();

        Assert.Equal("auto", SegmentBuilder.ResolveAutocompact(dir, homeDir: null, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_EnabledPrecedence_LocalBeatsProjectBeatsHome()
    {
        var projectDir = NewTempDir();
        var homeDir = NewTempDir();
        WriteSettings(projectDir, "settings.local.json", "{\"autoCompactEnabled\": false}");
        WriteSettings(projectDir, "settings.json", "{\"autoCompactEnabled\": true}");
        WriteSettings(homeDir, "settings.json", "{\"autoCompactEnabled\": true}");

        Assert.Equal("off", SegmentBuilder.ResolveAutocompact(projectDir, homeDir, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_WindowPrecedence_LocalBeatsProjectBeatsHome()
    {
        var projectDir = NewTempDir();
        var homeDir = NewTempDir();
        WriteSettings(projectDir, "settings.local.json", "{\"autoCompactWindow\": 300000}");
        WriteSettings(projectDir, "settings.json", "{\"autoCompactWindow\": 400000}");
        WriteSettings(homeDir, "settings.json", "{\"autoCompactWindow\": 500000}");

        Assert.Equal("300K", SegmentBuilder.ResolveAutocompact(projectDir, homeDir, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_EnabledAndWindow_ResolveIndependentlyFromDifferentFiles()
    {
        var projectDir = NewTempDir();
        var homeDir = NewTempDir();
        WriteSettings(homeDir, "settings.json", "{\"autoCompactEnabled\": true}");
        WriteSettings(projectDir, "settings.json", "{\"autoCompactWindow\": 250000}");

        Assert.Equal("250K", SegmentBuilder.ResolveAutocompact(projectDir, homeDir, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_EnvWindow_OverridesSettingsFileWindow()
    {
        var dir = NewTempDir();
        WriteSettings(dir, "settings.json", "{\"autoCompactWindow\": 250000}");

        Assert.Equal("300K", SegmentBuilder.ResolveAutocompact(dir, homeDir: null, envWindow: "300000"));
    }

    [Fact]
    public void ResolveAutocompact_EnvWindowWithSuffix_IsRejected_FallsBackToSettingsFile()
    {
        var dir = NewTempDir();
        WriteSettings(dir, "settings.json", "{\"autoCompactWindow\": 250000}");

        Assert.Equal("250K", SegmentBuilder.ResolveAutocompact(dir, homeDir: null, envWindow: "500k"));
    }

    [Fact]
    public void ResolveAutocompact_MalformedSettingsFile_IsSkipped_NextFileConsulted()
    {
        var projectDir = NewTempDir();
        var homeDir = NewTempDir();
        WriteSettings(projectDir, "settings.json", "{ not valid json");
        WriteSettings(homeDir, "settings.json", "{\"autoCompactWindow\": 250000}");

        Assert.Equal("250K", SegmentBuilder.ResolveAutocompact(projectDir, homeDir, envWindow: null));
    }

    [Fact]
    public void ResolveAutocompact_MissingFiles_ResolvesWithoutThrowing()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "ctl-autocompact-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Equal("auto", SegmentBuilder.ResolveAutocompact(projectDir, homeDir: null, envWindow: null));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("auto")]
    [InlineData("250K")]
    public void Autocompact_ShowLabelFalse_ReturnsBareValue(string raw)
    {
        Assert.Equal(raw, LeafItems.ApplyFormat(SegmentBuilder.AutocompactFormat, raw, false));
    }

    [Fact]
    public void Registry_Autocompact_IsPresentButNotDefaultOn()
    {
        Assert.Contains(ItemRegistry.All, i => i.Id == "autocompact");
        Assert.DoesNotContain("autocompact", ItemRegistry.DefaultIds);
    }

    // --- SPEC-103-showlabel-fix-and-labelcolor.md §E.2-E.4: LabeledSegment ---

    [Fact]
    public void LabeledSegment_LabelColorUnset_ReturnsSingleColorShapeWithNoSpans()
    {
        var segment = SegmentBuilder.LabeledSegment(SegmentBuilder.OutputStyleFormat, "Explanatory", "yellow", new OutputStyleItemSettings());

        Assert.Null(segment.Spans);
        Assert.Equal("style:Explanatory", segment.Plain);
        Assert.Equal(Expect("yellow", "style:Explanatory"), segment.Markup);
    }

    [Fact]
    public void LabeledSegment_LabelColorSet_ProducesLabelAndValueSpans()
    {
        var settings = new OutputStyleItemSettings { LabelColor = "grey" };
        var segment = SegmentBuilder.LabeledSegment(SegmentBuilder.OutputStyleFormat, "Explanatory", "yellow", settings);

        Assert.NotNull(segment.Spans);
        Assert.Equal("style:Explanatory", string.Concat(segment.Spans!.Select(s => s.Plain)));
        Assert.Equal("style:Explanatory", segment.Plain);

        var prefixSpan = segment.Spans!.Single(s => s.Plain == "style:");
        var valueSpan = segment.Spans!.Single(s => s.Plain == "Explanatory");
        Assert.Equal(Expect("grey", "style:"), prefixSpan.Markup);
        Assert.Equal(Expect("yellow", "Explanatory"), valueSpan.Markup);
    }

    [Fact]
    public void LabeledSegment_ShowLabelFalseWithLabelColorSet_ReturnsBareValueNoSpans()
    {
        var settings = new OutputStyleItemSettings { ShowLabel = false, LabelColor = "grey" };
        var segment = SegmentBuilder.LabeledSegment(SegmentBuilder.OutputStyleFormat, "Explanatory", "yellow", settings);

        Assert.Null(segment.Spans);
        Assert.Equal("Explanatory", segment.Plain);
        Assert.Equal(Expect("yellow", "Explanatory"), segment.Markup);
    }

    // --- SPEC-103-showlabel-fix-and-labelcolor.md §E.5-E.6: BuildEngram fragment colors ---

    [Fact]
    public void BuildEngram_NoSettings_MatchesTodaysDefaultMarkup()
    {
        var segment = SegmentBuilder.BuildEngram(new EngramProbeResult(new EngramResult(3, "◉ recalled"), null, null, EngramState.Active, null));

        Assert.Equal("[dim]engram:3[/] [purple]◉ recalled[/]", segment!.Markup);
    }

    [Fact]
    public void BuildEngram_FactsColorSet_ChangesFactsTagOnlyLeavesVerbPurple()
    {
        var segment = SegmentBuilder.BuildEngram(new EngramProbeResult(new EngramResult(3, "◉ recalled"), null, null, EngramState.Active, null), new EngramItemSettings { FactsColor = "cyan" });

        Assert.Equal("[cyan]engram:3[/] [purple]◉ recalled[/]", segment!.Markup);
    }

    [Fact]
    public void BuildEngram_VerbColorSet_ChangesVerbTagOnlyLeavesFactsDim()
    {
        var segment = SegmentBuilder.BuildEngram(new EngramProbeResult(new EngramResult(3, "◉ recalled"), null, null, EngramState.Active, null), new EngramItemSettings { VerbColor = "red" });

        Assert.Equal("[dim]engram:3[/] [red]◉ recalled[/]", segment!.Markup);
    }

    // --- SPEC-104-engram-item-buildout.md §L.1/§G.12: lastEventAge and the no-inert-field guard ---

    [Fact]
    public void BuildEngram_LastEventAtSet_RendersHumanizedLastEventAge()
    {
        var probe = new EngramProbeResult(null, null, null, EngramState.Active, DateTimeOffset.UtcNow.AddSeconds(-90));
        var settings = new EngramItemSettings { Fields = new() { new EngramFieldJsonConfig { Field = "lastEventAge" } } };

        var segment = SegmentBuilder.BuildEngram(probe, settings);

        Assert.NotNull(segment);
        Assert.False(string.IsNullOrEmpty(segment!.Plain));
    }

    [Theory]
    [InlineData("facts")]
    [InlineData("verb")]
    [InlineData("lastEventAge")]
    [InlineData("server")]
    [InlineData("version")]
    [InlineData("uptime")]
    [InlineData("port")]
    [InlineData("pid")]
    [InlineData("lastKind")]
    [InlineData("lastAge")]
    [InlineData("windowCount")]
    [InlineData("kind:embedding")]
    public void BuildEngram_EveryAcceptedFieldName_RendersNonEmptyGivenSourceData(string field)
    {
        var probe = new EngramProbeResult(
            Telemetry: new EngramResult(3, "◉ recalled"),
            Status: new EngramStatusInfo(Initialised: true, Server: "Running", Pid: 30695, Port: 7433, Version: "1.0.0", UptimeSeconds: 220),
            Activity: new EngramActivityInfo(
                LastKind: "embedding",
                LastAgeSeconds: 105,
                WindowSeconds: 3600,
                WindowCount: 226,
                Kinds: new Dictionary<string, long> { ["embedding"] = 16 }),
            State: EngramState.Active,
            LastEventAt: DateTimeOffset.UtcNow.AddSeconds(-90));
        var settings = new EngramItemSettings { Fields = new() { new EngramFieldJsonConfig { Field = field } } };

        var segment = SegmentBuilder.BuildEngram(probe, settings);

        Assert.NotNull(segment);
        Assert.False(string.IsNullOrEmpty(segment!.Plain));
    }
}
