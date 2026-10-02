using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2: OSC 8 hyperlinks. Rule 1 (Plain carries link text only, Markup
/// carries the OSC 8 bytes) is exercised indirectly by every test below asserting on
/// <c>Segment.Plain</c>/<c>Markup</c> separately. Rule 2 (OSC scanned separately from CSI) is
/// covered by the raw-command-passthrough tests. Rule 3 (a wrapped link reopens per continuation
/// row; a truncated link closes itself before the ellipsis) and ruling d ("clicking '…' must never
/// navigate") are covered directly.
/// </summary>
public class HyperlinkTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);

    [Fact]
    public void WrapOfLinkedSegment_EveryContinuationRow_ReopensAndClosesTheLink()
    {
        const string url = "https://example.com/path";
        var text = new string('A', 75);
        var markup = OscHyperlink.Wrap(url, Markup.Escape(text));
        var segment = new Segment(markup, text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 30, OverflowMode.Wrap, "…", new RenderNoteCollector());

        Assert.Equal(3, buffer.Rows.Count);
        foreach (var row in buffer.Rows)
        {
            Assert.True(OscHyperlink.TryUnwrap(row.Markup, out var rowUrl, out _), $"row '{row.Markup}' is not a self-contained link");
            Assert.Equal(url, rowUrl);
        }
    }

    // SPEC-2.6-vertical-marker-splice.md §7 test 5: the vertical marker splice must go through
    // the same SegmentTruncation.Truncate the horizontal axis uses, so a colored OSC 8 link
    // spliced with the marker still closes the link before the ellipsis (ruling d) and keeps the
    // color on both the retained content and the ellipsis itself.
    [Fact]
    public void RowLayoutWrap_MarkerSpliceOnLinkedColoredSegment_ClosesLinkBeforeEllipsis_KeepsColor()
    {
        const string url = "https://example.com/path";
        var text = new string('A', 25);
        var coloredMarkup = $"[green]{Markup.Escape(text)}[/]";
        var segment = new Segment(OscHyperlink.Wrap(url, coloredMarkup), text);

        var buffer = PaneRenderer.RenderLeaf(
            new[] { segment }, 30, OverflowMode.Truncate, "…", new RenderNoteCollector(),
            allowFallback: false, rowBudget: 1, markerRequired: true);

        var row = Assert.Single(buffer.Rows);
        // Markup.Remove chokes on raw OSC 8 bytes (unescaped ']' outside a "[...]" span — see
        // OscHyperlink.EscapeForRender's own doc comment), so the ellipsis is confirmed as a
        // literal character present directly rather than through Spectre's tag stripper — its
        // ordering relative to the link's close sequence is checked below.
        Assert.Contains('…', row.Markup);

        // The link must be explicitly closed before the ellipsis appears — "clicking '…' must
        // never navigate" — so the OSC 8 close sequence precedes the marker's own position, not
        // the reverse (which would leave the marker inside the still-open link).
        var closeIndex = row.Markup.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);
        var markerIndex = row.Markup.LastIndexOf('…');
        Assert.True(closeIndex >= 0, $"expected a closed OSC 8 link in '{row.Markup}'");
        Assert.True(markerIndex > closeIndex, "marker must appear after the link's own close sequence");
        Assert.Contains("[green]", row.Markup);
    }

    [Fact]
    public void WrapOfLinkedSegment_AtFallbackWidth_CollapsesToOneRow_LinkIntactAndUnsplit()
    {
        const string url = "https://example.com/path";
        var text = new string('A', 25);
        var markup = OscHyperlink.Wrap(url, Markup.Escape(text));
        var segment = new Segment(markup, text);

        // width=10 is below RowLayout.MinUsableWidth(20), so RenderLeaf's per-chunk wrap
        // pre-pass still runs (producing 3 self-contained link chunks, as above at width=30),
        // but RowLayout.Wrap's own fallback then collapses them onto one overwide row.
        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 10, OverflowMode.Wrap, "…", new RenderNoteCollector());

        Assert.Single(buffer.Rows);
        var row = buffer.Rows[0].Markup;

        // OscHyperlink.OpenPrefix is private, so the introducer is derived from the public
        // Close constant (Close = OpenPrefix + ST, and ST is 2 bytes) rather than duplicated.
        var introducer = OscHyperlink.Close[..^2];
        var opens = new List<int>();
        var closes = new List<int>();
        for (var i = row.IndexOf(introducer, StringComparison.Ordinal); i >= 0; i = row.IndexOf(introducer, i + 1, StringComparison.Ordinal))
        {
            var isClose = i + OscHyperlink.Close.Length <= row.Length
                && string.CompareOrdinal(row.Substring(i, OscHyperlink.Close.Length), OscHyperlink.Close) == 0;
            (isClose ? closes : opens).Add(i);
        }

        Assert.NotEmpty(opens);
        Assert.Equal(opens.Count, closes.Count);

        for (var k = 0; k < opens.Count; k++)
        {
            var closeEnd = closes[k] + OscHyperlink.Close.Length;
            var slice = row[opens[k]..closeEnd];
            Assert.True(OscHyperlink.TryUnwrap(slice, out var rowUrl, out _), $"OSC 8 wrap #{k} is not well-formed at the fallback join: '{slice}'");
            Assert.Equal(url, rowUrl);
        }
    }

    [Fact]
    public void WrapOfLinkedColoredSegment_EveryContinuationRow_KeepsBothColorAndLink()
    {
        const string url = "https://example.com/path";
        var text = new string('B', 65);
        var coloredMarkup = $"[green]{Markup.Escape(text)}[/]";
        var segment = new Segment(OscHyperlink.Wrap(url, coloredMarkup), text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 30, OverflowMode.Wrap, "…", new RenderNoteCollector());

        Assert.True(buffer.Rows.Count > 1, "fixture must actually wrap across multiple rows to exercise reopening");
        foreach (var row in buffer.Rows)
        {
            Assert.True(OscHyperlink.TryUnwrap(row.Markup, out var rowUrl, out var inner));
            Assert.Equal(url, rowUrl);
            Assert.StartsWith("[green]", inner);
            Assert.EndsWith("[/]", inner);
        }
    }

    [Fact]
    public void TruncateOfLinkedSegment_ClosesLinkBeforeAppendingUnlinkedEllipsis()
    {
        const string url = "https://example.com/path";
        var text = new string('C', 20);
        var segment = new Segment(OscHyperlink.Wrap(url, Markup.Escape(text)), text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 10, OverflowMode.Truncate, "…", new RenderNoteCollector());

        Assert.Single(buffer.Rows);
        var row = buffer.Rows[0].Markup;

        var closeIndex = row.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);
        Assert.True(closeIndex >= 0, "expected the link to be closed within the truncated row");

        // Ruling d: clicking "…" must never navigate — nothing after the close reopens a link.
        var afterClose = row[(closeIndex + OscHyperlink.Close.Length)..];
        Assert.Contains("…", afterClose);
        Assert.DoesNotContain("]8;;", afterClose);
    }

    [Fact]
    public void LeafContent_LinkTemplateReferencingMissingOtherId_SuppressesLinkButKeepsItemText()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var item = new PaneItem("model-short", null, null, null, Link: "https://x/{missing-id}");
        var values = new Dictionary<string, string?> { ["model-short"] = ItemRegistry.Find("model-short")!.ResolveValue(ctx) };

        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
        Assert.Equal("Opus 4.5", decision.Text);
    }

    [Fact]
    public void LeafContent_LinkTemplateReferencingPresentOtherId_WrapsMarkupInLink()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var item = new PaneItem("model-short", null, null, null, Link: "https://x/{other}");
        var values = new Dictionary<string, string?>
        {
            ["model-short"] = ItemRegistry.Find("model-short")!.ResolveValue(ctx),
            ["other"] = "resolved-value",
        };

        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://x/resolved-value", url);
    }

    [Fact]
    public void DerivedItem_ExtractAndCaseApply_AndChainingToAnotherDerivedItemIsSuppressed()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var derived1 = new PaneItem(null, null, null, null, Id: "derived1", From: "model", Extract: "^(\\w+)", Case: "upper");
        // §8: ResolveDerived snapshots values before any derived result is written back, so a
        // derived item naming another derived item's id can never see that id's value.
        var derived2 = new PaneItem(null, null, null, null, Id: "derived2", From: "derived1");
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { derived1, derived2 });

        var values = ItemValueResolver.Resolve(pane, ctx);

        Assert.Equal("CLAUDE", values["derived1"]);
        Assert.Null(values["derived2"]);
    }

    [Fact]
    public void TicketRecipe_ExtractCaseAndLink_BuildsUrlFromPostExtractPostCaseValue()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: "fix/eng-1234-thing", engram: null, remoteUrlProbe: () => null);

        var ticket = new PaneItem(null, null, null, null, Id: "ticket", From: "git-branch",
            Extract: "[A-Za-z]{2,}-[0-9]+", Case: "upper", Link: "https://linear.app/acme-corp/issue/{}");
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { ticket });

        var values = ItemValueResolver.Resolve(pane, ctx);
        var resolved = LeafItems.Resolve(new[] { ticket }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("ENG-1234", decision.Text);
        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://linear.app/acme-corp/issue/ENG-1234", url);
    }

    [Fact]
    public void TicketRecipe_BranchWithNoMatch_ItemDoesNotRender()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: "main", engram: null, remoteUrlProbe: () => null);

        var ticket = new PaneItem(null, null, null, null, Id: "ticket", From: "git-branch",
            Extract: "[A-Za-z]{2,}-[0-9]+", Case: "upper", Link: "https://linear.app/acme-corp/issue/{}");
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { ticket });

        var values = ItemValueResolver.Resolve(pane, ctx);

        Assert.Null(values["ticket"]);
    }

    [Fact]
    public void TicketRecipe_NoLinkTemplate_PlainTextWithNoHyperlink()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: "fix/eng-1234-thing", engram: null, remoteUrlProbe: () => null);

        var ticket = new PaneItem(null, null, null, null, Id: "ticket", From: "git-branch",
            Extract: "[A-Za-z]{2,}-[0-9]+", Case: "upper");
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { ticket });

        var values = ItemValueResolver.Resolve(pane, ctx);
        var resolved = LeafItems.Resolve(new[] { ticket }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("ENG-1234", decision.Text);
        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
    }

    // item-specific-config.md §12.9 T11-T16, T18: the `linear` builtin.

    [Fact]
    public void Linear_IsNotInDefaultIds()
    {
        Assert.DoesNotContain("linear", ItemRegistry.DefaultIds);
    }

    [Fact]
    public void Linear_BranchWithTicket_ExtractsAndUppercases()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: "fix/eng-1234-thing", engram: null, remoteUrlProbe: () => null);

        Assert.Equal("ENG-1234", ItemRegistry.Find("linear")!.ResolveValue(ctx));
    }

    [Fact]
    public void Linear_BranchWithNoMatch_Suppressed()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: "main", engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("linear", null, null, null);
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { item });

        var values = ItemValueResolver.Resolve(pane, ctx);
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();

        Assert.Null(values["linear"]);
        Assert.Null(resolved.Value);
        Assert.Null(resolved.Display);
    }

    [Fact]
    public void Linear_WorkspaceConfigured_WrapsInDefaultLinkTemplate_FromPostExtractPostCaseValue()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var itemSettings = new ItemSettingsJsonConfig { Linear = new LinearItemSettings { Workspace = "acme-corp" } };
        var ctx = new ItemContext(input, gitBranch: "fix/eng-1234-thing", engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("linear", null, null, null);
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { item });

        var values = ItemValueResolver.Resolve(pane, ctx);
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://linear.app/acme-corp/issue/ENG-1234", url);
    }

    [Fact]
    public void Linear_WorkspaceAbsent_PlainTextNoHyperlink()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: "fix/eng-1234-thing", engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("linear", null, null, null);
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { item });

        var values = ItemValueResolver.Resolve(pane, ctx);
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("ENG-1234", decision.Text);
        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
    }

    [Fact]
    public void Linear_PlacementLinkWinsOverDefaultTemplate_AndWrapsExactlyOnce()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var itemSettings = new ItemSettingsJsonConfig { Linear = new LinearItemSettings { Workspace = "acme-corp" } };
        var ctx = new ItemContext(input, gitBranch: "fix/eng-1234-thing", engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("linear", null, null, null, Link: "https://other-tracker.example/{}");
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { item });

        var values = ItemValueResolver.Resolve(pane, ctx);
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://other-tracker.example/ENG-1234", url);

        var introducer = OscHyperlink.Close[..^2];
        var wrapCount = 0;
        for (var i = decision.Markup.IndexOf(introducer, StringComparison.Ordinal); i >= 0;
             i = decision.Markup.IndexOf(introducer, i + 1, StringComparison.Ordinal))
        {
            wrapCount++;
        }
        Assert.Equal(2, wrapCount); // one open + one close = one wrap
    }

    [Fact]
    public void Linear_NullOrEmptyBranch_SuppressedWithoutException()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var nullBranchCtx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var emptyBranchCtx = new ItemContext(input, gitBranch: "", engram: null, remoteUrlProbe: () => null);

        Assert.Null(ItemRegistry.Find("linear")!.ResolveValue(nullBranchCtx));
        Assert.Null(ItemRegistry.Find("linear")!.ResolveValue(emptyBranchCtx));
    }

    [Fact]
    public void RepoLinkedViaRepoHost_WrapsMarkupInLink_WithoutInvokingRemoteUrlProbe()
    {
        var probeInvoked = false;
        var input = new StatusInput
        {
            Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
            Workspace = new WorkspaceInfo { Repo = new RepoInfo { Host = "github.com", Owner = "JimCline", Name = "claude-tui-line" } },
        };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => { probeInvoked = true; return null; });
        var item = new PaneItem("repo", null, null, null, Link: "https://{repo-host}/{}");
        var values = new Dictionary<string, string?>
        {
            ["repo"] = ItemRegistry.Find("repo")!.ResolveValue(ctx),
            ["repo-host"] = ItemRegistry.Find("repo-host")!.ResolveValue(ctx),
        };

        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://github.com/JimCline/claude-tui-line", url);
        Assert.False(probeInvoked, "repo-host must resolve from the session payload, never the git-remote probe");
    }

    [Fact]
    public void BuildItemSegment_RawUnterminatedAnsiColor_PlainIsStrippedAndMarkupSelfResets()
    {
        var segment = SegmentBuilder.BuildItemSegment("\x1b[31mHello", null);

        Assert.Equal("Hello", segment.Plain);

        // Round-trip through Spectre's own markup parser rather than inspecting the raw markup
        // string directly: Segment.Markup is markup *source*, escaped so it parses without
        // throwing, and it is what actually reaches the terminal (via a real console render)
        // that must carry the real ESC[0m reset — not the source's own escaped bytes.
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        console.Markup(segment.Markup);

        Assert.EndsWith("\x1b[0m", writer.ToString());
    }

    [Fact]
    public void DisplayWidth_MeasuresLinkedRow_StripsBothOsc8AndMarkupTagsToPlainText()
    {
        const string url = "https://example.com/path";
        const string text = "click";
        var markup = OscHyperlink.Wrap(url, $"[green]{Markup.Escape(text)}[/]");

        Assert.Equal(text, DisplayWidth.Strip(markup));
        Assert.Equal(text.Length, DisplayWidth.Measure(markup));
    }

    [Fact]
    public void EndToEnd_ConfiguredLink_RealPipelineRowReachesConsoleMarkupLineAsOsc8()
    {
        // Full pipeline, not a unit slice: ConfigLoader -> ItemValueResolver -> SizeResolver ->
        // PaneTreeRenderer -> the same OscHyperlink.EscapeForRender + console.MarkupLine(row.Markup)
        // calls Program.cs makes for every row of the split pipeline (Program.cs:104). Top-level
        // Main isn't reachable from a test project, so this reproduces its exact statements
        // in-process instead.
        const string url = "https://example.com/org/repo";
        const string configJson = """
        {
          "surface": {
            "pane": {
              "items": [ { "item": "remote-url", "link": "{}" } ]
            }
          }
        }
        """;

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

        var ctx = new ItemContext(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => url);
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        foreach (var row in rendered.Buffer.Rows)
        {
            console.MarkupLine(OscHyperlink.EscapeForRender(row.Markup));
        }

        var raw = writer.ToString();

        // Not just "]8;;" appears somewhere: the literal OSC 8 open sequence with the URL embedded
        // contiguously, and the literal close sequence, in that order — the exact bytes a terminal's
        // hyperlink support keys off, not a loose substring.
        var openPrefix = OscHyperlink.Close[..^2];
        var st = OscHyperlink.Close[^2..];
        var expectedOpen = $"{openPrefix}{url}{st}";

        var openIndex = raw.IndexOf(expectedOpen, StringComparison.Ordinal);
        var closeIndex = raw.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);

        Assert.True(openIndex >= 0, $"expected the exact OSC 8 open sequence for '{url}' in rendered output: {raw}");
        Assert.True(closeIndex > openIndex, $"expected the OSC 8 close sequence after the open sequence in rendered output: {raw}");
    }

    [Fact]
    public void EndToEnd_LinkTemplateReferencesUnplacedItem_RealPipelineStillResolvesIt()
    {
        // Defect 11 (§5): CollectIds only ever swept a colour token's `from` and a pane's own
        // placed items, so a link template's `{other-id}` resolved only when the referenced item
        // also happened to be placed somewhere else — the configuration nobody writes. remote-url
        // here is referenced ONLY through the link template and never appears in `items`; a
        // config that also places it would pass over the unfixed CollectIds just as easily, so
        // this deliberately does not.
        const string remoteUrl = "https://github.com/example/repo";
        const string configJson = """
        {
          "surface": {
            "pane": {
              "items": [ { "item": "git-branch", "link": "{remote-url}/tree/{}" } ]
            }
          }
        }
        """;

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

        var ctx = new ItemContext(new StatusInput(), gitBranch: "feature/thing", engram: null, remoteUrlProbe: () => remoteUrl);
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, ctx, topLevel.Colors);

        Assert.Equal(remoteUrl, values.GetValueOrDefault("remote-url"));

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        foreach (var row in rendered.Buffer.Rows)
        {
            console.MarkupLine(OscHyperlink.EscapeForRender(row.Markup));
        }

        var raw = writer.ToString();
        var expectedUrl = $"{remoteUrl}/tree/{values["git-branch"]}";
        var openPrefix = OscHyperlink.Close[..^2];
        var st = OscHyperlink.Close[^2..];
        var expectedOpen = $"{openPrefix}{expectedUrl}{st}";

        var openIndex = raw.IndexOf(expectedOpen, StringComparison.Ordinal);
        var closeIndex = raw.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);

        Assert.True(openIndex >= 0, $"expected the exact OSC 8 open sequence for '{expectedUrl}' (remote-url resolved though never placed) in rendered output: {raw}");
        Assert.True(closeIndex > openIndex, $"expected the OSC 8 close sequence after the open sequence in rendered output: {raw}");
    }

    [Fact]
    public void ColorToken_FromNamesUnplacedItem_StillResolves()
    {
        // §6.3: a colors-table token's `from` is fetched "even when it is never displayed in any
        // pane." agent here is referenced only via the token's `from`, never placed as an item —
        // confirming CollectIds' colour-token extractor (unchanged by the defect-11 refactor)
        // still covers this after CollectIds was restructured into ReferenceExtractors.
        const string configJson = """
        {
          "colors": {
            "agent-accent": {
              "from": "agent",
              "match": [ { "contains": "cdtui", "color": "aqua" } ],
              "default": "red"
            }
          },
          "surface": {
            "pane": {
              "items": [ { "item": "context", "color": "@agent-accent" } ]
            }
          }
        }
        """;

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

        var input = new StatusInput { Agent = new AgentInfo { Name = "cdtui-implementor" }, ContextWindow = new ContextWindowInfo { UsedPercentage = 42 } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var values = ItemValueResolver.Resolve(pane, ctx, topLevel.Colors);

        Assert.Equal("cdtui-implementor", values.GetValueOrDefault("agent"));

        var color = ColorResolution.Resolve(new ColorResolution.ColorExpr.TokenRef("agent-accent"), values, topLevel.Colors);
        Assert.Equal("aqua", color);
    }

    [Fact]
    public void CommandItemSegment_UnterminatedRawAnsiColor_ResetPrecedesNextSegmentInRenderedOutput()
    {
        var redSegment = SegmentBuilder.BuildItemSegment("[31mHello", null);
        var plainSegment = SegmentBuilder.BuildItemSegment("World", null);

        var rows = RowLayout.Wrap(new[] { redSegment, plainSegment }, 1000);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        foreach (var row in rows)
        {
            console.MarkupLine(row.Markup);
        }

        var raw = writer.ToString();
        var resetIndex = raw.IndexOf("[0m", StringComparison.Ordinal);
        var worldIndex = raw.IndexOf("World", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0, "expected the raw SGR reset to appear in rendered output");
        Assert.True(worldIndex >= 0, "expected the neighboring segment's text to appear in rendered output");
        Assert.True(resetIndex < worldIndex, "the reset must close the unterminated red before the next segment's text");
    }

    // SPEC-87 §12.1/§12.4: a `link` template naming a compound id declared in a different pane
    // resolves via the same whole-tree compound map LeafItems.Resolve consults — only the
    // compound's Plain substitutes, with no markup/ANSI bytes carried into the URL.
    [Fact]
    public void LinkTemplate_NamingCompoundDeclaredInAnotherPane_SubstitutesPlainTextOnly()
    {
        var compoundPart = new PaneItemPart(Text: null, Item: null, From: "agent", Extract: "[^:]+$", Case: "upper", Format: null,
            Color: new ColorResolution.ColorExpr.Literal("aqua"));
        var declaringItem = new PaneItem(Item: null, Format: null, Color: null, Overflow: null, Id: "agent-badge", Parts: new[] { compoundPart });
        var declaringPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto", NoBorder, OverflowMode.Truncate, "…", null, new[] { declaringItem });
        var root = new Pane(PaneSplit.Horizontal, new[] { declaringPane }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());

        var values = new Dictionary<string, string?> { ["agent"] = "team:worker-7" };
        var ctx = new ItemContext(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var compounds = LeafItems.BuildCompoundMap(root, values, ctx, null);

        var linkItem = new PaneItem(Item: null, Format: null, Color: null, Overflow: null, Id: "anchor", Link: "https://x/{agent-badge}");
        var resolved = new LeafItems.ResolvedItem(linkItem, "click", new Segment(Markup.Escape("click"), "click"));

        var decision = LeafContent.Decide(resolved, values, compounds);

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://x/WORKER-7", url);
        Assert.DoesNotContain('', url);
    }

    // SPEC-87 §12.3/§6.8: a compound suppressed by BuildCompoundMap (every value-part empty) has
    // no map entry, so a `link` naming it hits the ordinary missing-placeholder path — the link
    // is dropped entirely rather than substituting an empty string.
    [Fact]
    public void LinkTemplate_NamingSuppressedCompound_DropsTheLinkEntirely()
    {
        var emptyPart = new PaneItemPart(Text: null, Item: null, From: "missing-source", Extract: null, Case: null, Format: null, Color: null);
        var declaringItem = new PaneItem(Item: null, Format: null, Color: null, Overflow: null, Id: "empty-badge", Parts: new[] { emptyPart });
        var declaringPane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto", NoBorder, OverflowMode.Truncate, "…", null, new[] { declaringItem });
        var root = new Pane(PaneSplit.Horizontal, new[] { declaringPane }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());

        var values = new Dictionary<string, string?>();
        var ctx = new ItemContext(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var compounds = LeafItems.BuildCompoundMap(root, values, ctx, null);
        Assert.Empty(compounds);

        var linkItem = new PaneItem(Item: null, Format: null, Color: null, Overflow: null, Id: "anchor", Link: "https://x/{empty-badge}");
        var ownMarkup = Markup.Escape("click");
        var resolved = new LeafItems.ResolvedItem(linkItem, "click", new Segment(ownMarkup, "click"));

        var decision = LeafContent.Decide(resolved, values, compounds);

        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
        Assert.Equal(ownMarkup, decision.Markup);
    }

    // default-links-branch-directory.md §3/§7.1: `git-branch`'s DefaultLinkTemplate.

    [Fact]
    public void GitBranch_RemoteConfigured_DefaultLinksToTreePath()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: "main", engram: null, remoteUrlProbe: () => "https://github.com/o/r");

        var item = new PaneItem("git-branch", null, null, null);
        var values = new Dictionary<string, string?> { ["git-branch"] = ItemRegistry.Find("git-branch")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://github.com/o/r/tree/main", url);
    }

    [Fact]
    public void Repo_RemoteConfigured_DefaultLinksToRemote()
    {
        var input = new StatusInput
        {
            Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "o", Name = "r" } },
        };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => "https://gitlab.example.com/o/r");

        var item = new PaneItem("repo", null, null, null);
        var values = new Dictionary<string, string?> { ["repo"] = ItemRegistry.Find("repo")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("o/r", decision.Text);
        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://gitlab.example.com/o/r", url);
    }

    [Fact]
    public void Repo_NoRemote_PlainTextNoHyperlink()
    {
        var input = new StatusInput
        {
            Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "o", Name = "r" } },
        };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("repo", null, null, null);
        var values = new Dictionary<string, string?> { ["repo"] = ItemRegistry.Find("repo")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("o/r", decision.Text);
        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
    }

    [Fact]
    public void GitBranch_NoRemote_PlainTextNoHyperlink()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: "main", engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("git-branch", null, null, null);
        var values = new Dictionary<string, string?> { ["git-branch"] = ItemRegistry.Find("git-branch")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("main", decision.Text);
        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
    }

    [Fact]
    public void GitBranch_OutsideRepo_Suppressed()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => "https://github.com/o/r");

        var item = new PaneItem("git-branch", null, null, null);
        var values = new Dictionary<string, string?> { ["git-branch"] = ItemRegistry.Find("git-branch")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();

        Assert.Null(resolved.Value);
        Assert.Null(resolved.Display);
    }

    [Fact]
    public void GitBranch_SlashInBranchName_SlashSurvivesUnescaped()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: "feature/foo", engram: null, remoteUrlProbe: () => "https://github.com/o/r");

        var item = new PaneItem("git-branch", null, null, null);
        var values = new Dictionary<string, string?> { ["git-branch"] = ItemRegistry.Find("git-branch")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://github.com/o/r/tree/feature/foo", url);
    }

    [Fact]
    public void GitBranch_HashInBranchName_IsPercentEncoded()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: "fix#12", engram: null, remoteUrlProbe: () => "https://github.com/o/r");

        var item = new PaneItem("git-branch", null, null, null);
        var values = new Dictionary<string, string?> { ["git-branch"] = ItemRegistry.Find("git-branch")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://github.com/o/r/tree/fix%2312", url);
    }

    [Fact]
    public void GitBranch_PlacementLinkWinsOverDefaultTemplate()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: "main", engram: null, remoteUrlProbe: () => "https://github.com/o/r");

        var item = new PaneItem("git-branch", null, null, null, Link: "https://other-host.example/{}");
        var values = new Dictionary<string, string?> { ["git-branch"] = ItemRegistry.Find("git-branch")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://other-host.example/main", url);
    }

    [Fact]
    public void GitBranch_DefaultTemplate_DoesNotDependOnRemoteUrlPlaceholder()
    {
        var input = new StatusInput();
        var ctx = new ItemContext(input, gitBranch: "main", engram: null, remoteUrlProbe: () => "https://github.com/o/r");

        var template = ItemRegistry.Find("git-branch")!.DefaultLinkTemplate!(ctx);

        Assert.NotNull(template);
        Assert.DoesNotContain('{', template);
    }

    // default-links-branch-directory.md §4/§7.2: `directory`'s DefaultLinkTemplate.

    [Fact]
    public void Directory_DefaultLinksToAbsoluteFileUri()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_DepthOne_LinkUsesFullPathNotBasename()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("proj", decision.Text);
        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_DepthTwo_LinkUnchangedByDepth()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { Depth = 2 } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("me/proj", decision.Text);
        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_SpaceInPath_IsPercentEncoded()
    {
        var input = new StatusInput { Cwd = "/Users/me/my proj" };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/my%20proj/", url);
    }

    [Fact]
    public void Directory_BraceInPath_LinkIsNotSuppressed()
    {
        var input = new StatusInput { Cwd = "/Users/me/{build}" };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Contains("%7Bbuild%7D", url);
    }

    [Fact]
    public void Directory_NullCwd_Suppressed()
    {
        var input = new StatusInput { Cwd = null };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();

        Assert.Null(resolved.Value);
        Assert.Null(resolved.Display);
    }

    [Fact]
    public void Directory_PlacementLinkWinsOverDefaultTemplate()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null, Link: "https://other.example/{}");
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://other.example/proj", url);
    }

    // default-links-branch-directory.md §4.2/§4.3: FileUri.ForDirectory's construction directly,
    // including the Windows shape — pure string transform, does not need a Windows host to run.

    [Fact]
    public void FileUri_ForDirectory_WindowsStylePath_ProducesFileUriWithDriveLetter()
    {
        var uri = FileUri.ForDirectory(@"C:\Users\me\my proj");

        Assert.Equal("file:///C:/Users/me/my%20proj/", uri);
    }

    // directory-openwith.md §9.1: the `vscode` target.

    [Fact]
    public void Directory_OpenWithVsCode_LinksToVsCodeUri()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("vscode://file/Users/me/proj", url);
    }

    [Fact]
    public void Directory_OpenWithVsCode_DepthDoesNotAffectLink()
    {
        var input = new StatusInput { Cwd = "/Users/me/deep/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { Depth = 1, OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.Equal("proj", decision.Text);
        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("vscode://file/Users/me/deep/proj", url);
    }

    [Fact]
    public void Directory_OpenWithVsCode_SpaceInPath_IsPercentEncoded()
    {
        var input = new StatusInput { Cwd = "/Users/me/my proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("vscode://file/Users/me/my%20proj", url);
    }

    [Fact]
    public void VsCode_BraceInPath_LinkIsNotSuppressed()
    {
        var input = new StatusInput { Cwd = "/tmp/{build}" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.NotNull(url);
        Assert.Contains("%7Bbuild%7D", url);
    }

    [Fact]
    public void VsCode_ColonInDirectoryName_StaysEncoded()
    {
        var input = new StatusInput { Cwd = "/tmp/build:12" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.NotNull(url);
        Assert.Contains("build%3A12", url);
        Assert.DoesNotContain("build:12", url);
    }

    [Fact]
    public void VsCode_WindowsStylePath_KeepsDriveLetterColon()
    {
        var uri = DirectoryLink.ForVsCode(@"C:\Users\me\proj");

        Assert.Equal("vscode://file/C:/Users/me/proj", uri);
    }

    [Fact]
    public void VsCode_NullCwd_Suppressed()
    {
        var input = new StatusInput { Cwd = null };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();

        Assert.Null(resolved.Value);
        Assert.Null(resolved.Display);
    }

    [Fact]
    public void VsCode_EmptyCwd_Suppressed()
    {
        var input = new StatusInput { Cwd = "" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();

        Assert.Null(resolved.Value);
        Assert.Null(resolved.Display);
    }

    // directory-openwith.md §9.2: the dispatcher.

    [Fact]
    public void Directory_OpenWithAbsent_UsesFileUri()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_OpenWithFiles_UsesFileUri()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "files" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_OpenWithUnknownToken_FallsBackToFileUri()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "sublime" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_OpenWithWrongCase_FallsBackToFileUri()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "VSCode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null);
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("file:///Users/me/proj/", url);
    }

    [Fact]
    public void Directory_OpenWithVsCode_PlacementLinkStillWins()
    {
        var input = new StatusInput { Cwd = "/Users/me/proj" };
        var itemSettings = new ItemSettingsJsonConfig { Directory = new DirectoryItemSettings { OpenWith = "vscode" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);

        var item = new PaneItem("directory", null, null, null, Link: "https://other.example/{}");
        var values = new Dictionary<string, string?> { ["directory"] = ItemRegistry.Find("directory")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://other.example/proj", url);
    }

    // SPEC-105-pr-item-hyperlink.md §G: `pr`'s DefaultLinkTemplate.

    private static ItemContext PrContext(long number = 128, string? reviewState = "draft",
        string? host = "github.com", string? owner = "acme", string? name = "acme-web",
        bool nullWorkspace = false, bool nullRepo = false, ItemSettingsJsonConfig? itemSettings = null)
    {
        var input = new StatusInput
        {
            Pr = new PrInfo { Number = number, ReviewState = reviewState },
            Workspace = nullWorkspace ? null
                : new WorkspaceInfo { Repo = nullRepo ? null : new RepoInfo { Host = host, Owner = owner, Name = name } },
        };
        return new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null, itemSettings);
    }

    private static (string? Text, string? Url) RenderPr(ItemContext ctx)
    {
        var item = new PaneItem("pr", null, null, null);
        var values = new Dictionary<string, string?> { ["pr"] = ItemRegistry.Find("pr")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());
        return (decision.Text, OscHyperlink.TryUnwrap(decision.Markup, out var url, out _) ? url : null);
    }

    [Fact]
    public void Pr_DraftReviewState_DefaultLinksToPullRequestPage()
    {
        var ctx = PrContext(reviewState: "draft");

        var (text, url) = RenderPr(ctx);

        Assert.Equal("PR #128 [draft]", text);
        Assert.Equal("https://github.com/acme/acme-web/pull/128", url);
    }

    [Fact]
    public void Pr_NoReviewState_DefaultLinksToPullRequestPage()
    {
        var ctx = PrContext(reviewState: null);

        var (text, url) = RenderPr(ctx);

        Assert.Equal("PR #128", text);
        Assert.Equal("https://github.com/acme/acme-web/pull/128", url);
    }

    [Fact]
    public void Pr_ReviewStateLabelSuffix_DoesNotLeakIntoUrl()
    {
        var itemSettings = new ItemSettingsJsonConfig { Pr = new PrItemSettings { ReviewStateLabels = new() { ["draft"] = " ~WIP~" } } };
        var ctx = PrContext(reviewState: "draft", itemSettings: itemSettings);

        var (text, url) = RenderPr(ctx);

        Assert.Equal("PR #128 ~WIP~", text);
        Assert.Equal("https://github.com/acme/acme-web/pull/128", url);
    }

    [Fact]
    public void Pr_WorkspaceNull_PlainTextNoHyperlink()
    {
        var ctx = PrContext(nullWorkspace: true);

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_RepoNull_PlainTextNoHyperlink()
    {
        var ctx = PrContext(nullRepo: true);

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostNull_PlainTextNoHyperlink()
    {
        var ctx = PrContext(host: null);

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostEmpty_PlainTextNoHyperlink()
    {
        var ctx = PrContext(host: "");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_OwnerNull_PlainTextNoHyperlink()
    {
        var ctx = PrContext(owner: null);

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_NameNull_PlainTextNoHyperlink()
    {
        var ctx = PrContext(name: null);

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_OwnerContainsSlash_RejectedNoHyperlink()
    {
        var ctx = PrContext(owner: "ac/me");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_NameContainsHash_RejectedNoHyperlink()
    {
        var ctx = PrContext(name: "acmeweb#1");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_NameContainsBrace_SurvivesEscapingAndAppearsLiterally()
    {
        var ctx = PrContext(name: "acme{web}");

        var (_, url) = RenderPr(ctx);

        Assert.Equal("https://github.com/acme/acme%7Bweb%7D/pull/128", url);
    }

    [Fact]
    public void Pr_PlacementLinkWinsOverDefaultTemplate_AndWrapsExactlyOnce()
    {
        var ctx = PrContext();
        var item = new PaneItem("pr", null, null, null, Link: "https://example.test/x");
        var values = new Dictionary<string, string?> { ["pr"] = ItemRegistry.Find("pr")!.ResolveValue(ctx) };
        var resolved = LeafItems.Resolve(new[] { item }, values, ctx, new Dictionary<string, Segment>()).Single();
        var decision = LeafContent.Decide(resolved, values, new Dictionary<string, Segment>());

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://example.test/x", url);

        var introducer = OscHyperlink.Close[..^2];
        var wrapCount = 0;
        for (var i = decision.Markup.IndexOf(introducer, StringComparison.Ordinal); i >= 0;
             i = decision.Markup.IndexOf(introducer, i + 1, StringComparison.Ordinal))
        {
            wrapCount++;
        }
        Assert.Equal(2, wrapCount); // one open + one close = one wrap
    }

    [Fact]
    public void Pr_NonGitHubHost_UsedVerbatimNoHardcodedDomain()
    {
        var ctx = PrContext(host: "ghe.corp.example");

        var (_, url) = RenderPr(ctx);

        Assert.Equal("https://ghe.corp.example/acme/acme-web/pull/128", url);
    }

    // SPEC-105-pr-item-hyperlink.md §K.1/§G.11-13: `Host` moves to a whitelist so `@` can never
    // reach the authority position, while a legitimate trailing numeric port still survives.

    [Fact]
    public void Pr_HostWithAtSign_SpoofRejectedNoHyperlink()
    {
        var ctx = PrContext(host: "github.com@evil.test");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostWithNumericPort_SurvivesAndRendersWorkingLink()
    {
        var ctx = PrContext(host: "github.corp.example:8443");

        var (_, url) = RenderPr(ctx);

        Assert.Equal("https://github.corp.example:8443/acme/acme-web/pull/128", url);
    }

    [Fact]
    public void Pr_HostWithNonNumericPort_RejectedNoHyperlink()
    {
        var ctx = PrContext(host: "github.com:evil.test");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostWithOutOfRangePort_RejectedNoHyperlink()
    {
        var ctx = PrContext(host: "github.com:99999");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostWithZeroPort_RejectedNoHyperlink()
    {
        var ctx = PrContext(host: "github.com:0");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostWithTrailingNewline_RejectedNoHyperlink()
    {
        var ctx = PrContext(host: "github.com\n");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }

    [Fact]
    public void Pr_HostWithBrace_RejectedNoHyperlink()
    {
        var ctx = PrContext(host: "git{hub}.com");

        var (text, url) = RenderPr(ctx);

        Assert.StartsWith("PR #128", text);
        Assert.Null(url);
    }
}
