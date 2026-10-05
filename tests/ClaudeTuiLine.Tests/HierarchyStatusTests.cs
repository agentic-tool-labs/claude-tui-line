using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

public sealed class HierarchyStatusTests : IDisposable
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T12:00:00.000Z");
    private static readonly string[] Fixtures = { "bad", "hidden", "idle", "member-session", "pipeline", "warn", "work" };

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cdtui-ah-" + Guid.NewGuid().ToString("N"));

    public HierarchyStatusTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private static DateTimeOffset At(string hhmmss) => DateTimeOffset.Parse($"2026-01-01T{hhmmss}Z");

    private static string FixtureText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ah", name + ".json"));

    private static JsonObject FixtureNode(string name) => JsonNode.Parse(FixtureText(name))!.AsObject();

    private string StageText(string json, string? dir = null)
    {
        var repo = dir ?? Path.Combine(_tmp, "repo-" + Guid.NewGuid().ToString("N"));
        if (!File.Exists(Path.Combine(repo, ".git")))
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
        }

        Directory.CreateDirectory(Path.Combine(repo, ".claude", "hierarchy"));
        var path = Path.Combine(repo, ".claude", "hierarchy", "status.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string Stage(JsonNode node) => StageText(node.ToJsonString());

    private string StageFixture(string name) => StageText(FixtureText(name));

    private static HierarchyEntry? Read(string path, DateTimeOffset now, string? session = "sess-orch") =>
        HierarchyStatus.ReadAndSelect(path, session, now);

    private static JsonObject Work(Action<JsonObject> edit)
    {
        var n = FixtureNode("work");
        edit(n);
        return n;
    }

    private static JsonObject FirstEntry(JsonObject n) => n["timeline"]!.AsArray()[0]!.AsObject();

    private static ItemContext Ctx(string path, DateTimeOffset now, string? session, ItemSettingsJsonConfig? settings = null, Func<HierarchyEntry?>? counting = null) =>
        new(new StatusInput { SessionId = session }, null, null, () => null, settings,
            counting ?? (() => HierarchyStatus.ReadAndSelect(path, session, now)));

    private static string RenderPlain(string id, ItemContext ctx, int width, out string markup)
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { new PaneItem(id, null, null, null) });
        var values = ItemValueResolver.Resolve(pane, ctx);
        var rows = PaneAssembler.RenderLeafRows(pane, width, ctx, values, new Dictionary<string, ColorResolution.ColorRule>(), new Dictionary<string, Segment>(), new RenderNoteCollector());
        markup = string.Concat(rows.Select(r => r.Markup));
        return string.Concat(rows.Select(r => Markup.Remove(r.Markup))).Trim();
    }

    private static string Seg(string id, string tone = "warn", ItemSettingsJsonConfig? settings = null)
    {
        var ctx = new ItemContext(new StatusInput(), null, null, () => null, settings, () => new HierarchyEntry(tone, "T", "S"));
        return ItemRegistry.Find(id)!.BuildDefaultSegment(ctx)!.Markup;
    }

    // ---- shared vectors ----

    [Fact]
    public void CopiedFixtures_AreAllSchemaOne()
    {
        foreach (var f in Fixtures)
        {
            Assert.Equal(1, FixtureNode(f)["schema"]!.GetValue<int>());
        }
    }

    [Theory]
    [InlineData("idle", "sess-orch", "ah: 2 live · 0 out", "ah: 2/0", "grey")]
    [InlineData("work", "sess-orch", "ah: 1 live · 1 out", "ah: 1/1", "blue")]
    [InlineData("warn", "sess-orch", "ah: 2 live · 1 out · 1 blocked", "ah: 2/1 1b", "yellow")]
    [InlineData("bad", "sess-orch", "ah: 2 live · 2 out · 1 overdue · 1 stalled", "ah: 2/2 1o 1s", "red")]
    [InlineData("pipeline", "sess-orch", "ah: 2 live · 1 out", "ah: 2/1", "blue")]
    [InlineData("member-session", "sess-orch", "ah: 1 live · 1 out", "ah: 1/1", "blue")]
    [InlineData("member-session", null, "ah: 1 live · 1 out", "ah: 1/1", "blue")]
    public void Vectors_RenderAtT0(string fixture, string? session, string full, string shortForm, string colour)
    {
        var path = StageFixture(fixture);
        var ctx = Ctx(path, T0, session);

        Assert.Equal(full, RenderPlain("ah", ctx, 120, out var m1));
        Assert.Contains($"[{colour}]", m1);
        Assert.Equal(shortForm, RenderPlain("ah-short", Ctx(path, T0, session), 120, out var m2));
        Assert.Contains($"[{colour}]", m2);
        Assert.Equal(shortForm, RenderPlain("ah-short", Ctx(path, T0, session), 13, out _));
    }

    [Theory]
    [InlineData("hidden", "sess-orch")]
    [InlineData("member-session", "sess-demo-reviewer")]
    public void Vectors_HiddenAtEveryWidth(string fixture, string session)
    {
        var path = StageFixture(fixture);
        foreach (var id in new[] { "ah", "ah-short" })
        {
            foreach (var w in new[] { 120, 13 })
            {
                Assert.Equal("", RenderPlain(id, Ctx(path, T0, session), w, out _));
            }
        }

        Assert.Null(Read(path, At("13:00:00.000"), session));
    }

    [Fact]
    public void Ah_AtNarrowWidth_GoesThroughTheNormalTruncation()
    {
        var path = StageFixture("bad");
        var plain = RenderPlain("ah", Ctx(path, T0, "sess-orch"), 13, out var markup);

        Assert.True(plain.Length <= 13);
        Assert.StartsWith("ah: 2 live ", plain);
        Assert.EndsWith("…", plain);
        Assert.Contains("[red]", markup);
    }

    // ---- reader: absent ----

    [Fact]
    public void Absent_NoFileDirectoryAndMalformed()
    {
        var repo = Path.Combine(_tmp, "nofile");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Assert.Null(Read(Path.Combine(repo, ".claude", "hierarchy", "status.json"), T0));

        var dirPath = Path.Combine(_tmp, "dirrepo", ".claude", "hierarchy", "status.json");
        Directory.CreateDirectory(dirPath);
        Assert.Null(Read(dirPath, T0));

        Assert.Null(Read(StageText("{"), T0));
        Assert.Null(Read(StageText("[]"), T0));
    }

    public static IEnumerable<object[]> AbsentEdits()
    {
        yield return new object[] { "schema 2", (Action<JsonObject>)(n => n["schema"] = 2) };
        yield return new object[] { "schema missing", (Action<JsonObject>)(n => n.Remove("schema")) };
        yield return new object[] { "schema string", (Action<JsonObject>)(n => n["schema"] = "1") };
        yield return new object[] { "expires missing", (Action<JsonObject>)(n => n.Remove("expires_at")) };
        yield return new object[] { "expires number", (Action<JsonObject>)(n => n["expires_at"] = 5) };
        yield return new object[] { "expires tomorrow", (Action<JsonObject>)(n => n["expires_at"] = "tomorrow") };
        yield return new object[] { "expires equal now", (Action<JsonObject>)(n => n["expires_at"] = "2026-01-01T12:00:00.000Z") };
        yield return new object[] { "members missing", (Action<JsonObject>)(n => n.Remove("member_sessions")) };
        yield return new object[] { "members null", (Action<JsonObject>)(n => n["member_sessions"] = null) };
        yield return new object[] { "members string", (Action<JsonObject>)(n => n["member_sessions"] = "sess-x") };
        yield return new object[] { "members number elem", (Action<JsonObject>)(n => n["member_sessions"] = new JsonArray(1)) };
        yield return new object[] { "members null elem", (Action<JsonObject>)(n => n["member_sessions"] = new JsonArray("a", null)) };
        yield return new object[] { "timeline empty", (Action<JsonObject>)(n => n["timeline"] = new JsonArray()) };
        yield return new object[] { "timeline object", (Action<JsonObject>)(n => n["timeline"] = new JsonObject()) };
        yield return new object[] { "timeline missing", (Action<JsonObject>)(n => n.Remove("timeline")) };
        yield return new object[] { "last at bad", (Action<JsonObject>)(n => n["timeline"]!.AsArray().Last()!["at"] = "not-a-time") };
        yield return new object[] { "last at missing", (Action<JsonObject>)(n => n["timeline"]!.AsArray().Last()!.AsObject().Remove("at")) };
        yield return new object[] { "picked visible missing", (Action<JsonObject>)(n => FirstEntry(n).Remove("visible")) };
        yield return new object[] { "picked visible string", (Action<JsonObject>)(n => FirstEntry(n)["visible"] = "true") };
        yield return new object[] { "picked tone missing", (Action<JsonObject>)(n => FirstEntry(n).Remove("tone")) };
        yield return new object[] { "picked tone number", (Action<JsonObject>)(n => FirstEntry(n)["tone"] = 5) };
        yield return new object[] { "picked text missing", (Action<JsonObject>)(n => FirstEntry(n).Remove("text")) };
        yield return new object[] { "picked short null", (Action<JsonObject>)(n => FirstEntry(n)["short"] = null) };
    }

    [Theory]
    [MemberData(nameof(AbsentEdits))]
    public void Absent_Cases(string name, Action<JsonObject> edit)
    {
        _ = name;
        var path = Stage(Work(edit));

        Assert.Null(Read(path, T0));
        Assert.Null(Read(path, T0, session: null));
        var ctx = Ctx(path, T0, "sess-orch");
        Assert.Equal("", RenderPlain("ah", ctx, 120, out _));
        Assert.Equal("", RenderPlain("ah-short", Ctx(path, T0, "sess-orch"), 120, out _));
    }

    [Fact]
    public void Size_Boundary()
    {
        var json = FixtureText("work");
        var at = Encoding.UTF8.GetByteCount(json);
        Assert.True(at < HierarchyStatus.MaxBytes);

        Assert.NotNull(Read(StageText(json + new string(' ', HierarchyStatus.MaxBytes - at)), T0));
        Assert.Null(Read(StageText(json + new string(' ', HierarchyStatus.MaxBytes + 1 - at)), T0));
    }

    // ---- reader: present ----

    [Fact]
    public void Present_NonPickedEntryWrongTypes_AndPickedOneAbsent()
    {
        var path = Stage(Work(n =>
        {
            FirstEntry(n)["tone"] = 5;
            FirstEntry(n)["visible"] = "yes";
        }));

        var e = Read(path, At("12:05:00.000"))!;
        Assert.Equal(("bad", "1 live · 1 out · 1 overdue", "1/1 1o"), (e.Tone, e.Text, e.Short));
        Assert.Null(Read(path, T0));
    }

    [Fact]
    public void Present_UnreadFieldsWrongTypes_AndUnknownFields()
    {
        var path = Stage(Work(n =>
        {
            n["enabled"] = "yes";
            n["teams"] = 7;
            n["written_at"] = "garbage";
            n["brand_new"] = new JsonObject();
            FirstEntry(n)["live"] = "x";
            FirstEntry(n)["brand_new"] = 1;
        }));

        Assert.Equal("1 live · 1 out", Read(path, T0)!.Text);
    }

    [Fact]
    public void Present_EmptyMembers_AndExpiryBoundary()
    {
        var path = StageFixture("work");

        Assert.Equal("1 live · 1 out", Read(path, T0)!.Text);
        Assert.Equal("1/1 1s", Read(path, DateTimeOffset.Parse("2026-01-02T11:59:59.999Z"))!.Short);
    }

    [Fact]
    public void EmptyField_HidesOnlyThatItem()
    {
        var path = Stage(Work(n => FirstEntry(n)["text"] = ""));

        Assert.Equal("", RenderPlain("ah", Ctx(path, T0, "sess-orch"), 120, out _));
        Assert.Equal("ah: 1/1", RenderPlain("ah-short", Ctx(path, T0, "sess-orch"), 120, out _));
    }

    [Theory]
    [InlineData("11:59:59.999", "1 live · 1 out", "1/1", "work")]
    [InlineData("12:00:00.000", "1 live · 1 out", "1/1", "work")]
    [InlineData("12:04:00.000", "1 live · 1 out · 1 overdue", "1/1 1o", "bad")]
    [InlineData("12:05:00.000", "1 live · 1 out · 1 overdue", "1/1 1o", "bad")]
    [InlineData("12:06:30.000", "1 live · 1 out · 1 stalled", "1/1 1s", "bad")]
    [InlineData("23:00:00.000", "1 live · 1 out · 1 stalled", "1/1 1s", "bad")]
    public void TimelinePick_Work(string now, string text, string shortForm, string tone)
    {
        var e = Read(StageFixture("work"), At(now))!;

        Assert.Equal((tone, text, shortForm), (e.Tone, e.Text, e.Short));
    }

    [Fact]
    public void TimelinePick_OtherFixtures()
    {
        var bad = Read(StageFixture("bad"), At("12:01:30.000"))!;
        Assert.Equal(("2 live · 2 out · 2 stalled", "2/2 2s"), (bad.Text, bad.Short));

        var pipeline = Read(StageFixture("pipeline"), At("12:10:00.000"))!;
        Assert.Equal(("2 live · 1 out · 1 overdue", "2/1 1o"), (pipeline.Text, pipeline.Short));
    }

    // ---- tones and settings ----

    private static ItemSettingsJsonConfig Settings(AhItemSettings? ah = null, AhItemSettings? ahShort = null) =>
        new() { Ah = ah, AhShort = ahShort };

    [Theory]
    [InlineData("ah")]
    [InlineData("ah-short")]
    public void Tones_DefaultAndConfiguredAndInvalidAndUnknown(string id)
    {
        foreach (var (tone, colour) in new[] { ("work", "blue"), ("warn", "yellow"), ("bad", "red"), ("idle", "grey"), ("mystery", "grey"), ("Work", "grey") })
        {
            Assert.StartsWith($"[{colour}]", Seg(id, tone));
        }

        AhItemSettings Block(AhStateColorsJsonConfig c) => new() { StateColors = c };
        var key = id == "ah" ? (Func<AhItemSettings, ItemSettingsJsonConfig>)(b => Settings(ah: b)) : b => Settings(ahShort: b);

        var all = key(Block(new() { Work = "green", Warn = "purple", Bad = "magenta", Idle = "white" }));
        Assert.StartsWith("[green]", Seg(id, "work", all));
        Assert.StartsWith("[purple]", Seg(id, "warn", all));
        Assert.StartsWith("[magenta]", Seg(id, "bad", all));
        Assert.StartsWith("[white]", Seg(id, "idle", all));
        Assert.StartsWith("[white]", Seg(id, "mystery", all));
        Assert.StartsWith("[white]", Seg(id, "Work", all));

        var invalid = key(Block(new() { Work = "notacolour", Idle = "notacolour" }));
        Assert.StartsWith("[blue]", Seg(id, "work", invalid));
        Assert.StartsWith("[grey]", Seg(id, "mystery", invalid));
        _ = new Markup(Seg(id, "work", invalid));
    }

    [Fact]
    public void SeparateBlocks_DoNotLeak()
    {
        var both = Settings(
            ah: new() { StateColors = new() { Bad = "magenta" } },
            ahShort: new() { StateColors = new() { Bad = "green" } });
        Assert.StartsWith("[magenta]", Seg("ah", "bad", both));
        Assert.StartsWith("[green]", Seg("ah-short", "bad", both));

        var onlyAh = Settings(ah: new() { StateColors = new() { Bad = "magenta" } });
        Assert.StartsWith("[red]", Seg("ah-short", "bad", onlyAh));
    }

    [Fact]
    public void Labels_PerItemBlocks()
    {
        var path = StageFixture("warn");
        string Plain(string id, ItemSettingsJsonConfig? s) => RenderPlain(id, Ctx(path, T0, "sess-orch", s), 120, out _);

        Assert.Equal("ah: 2 live · 1 out · 1 blocked", Plain("ah", null));
        Assert.Equal("ah: 2/1 1b", Plain("ah-short", null));

        var shortOff = Settings(ahShort: new() { ShowLabel = false });
        Assert.Equal("2/1 1b", Plain("ah-short", shortOff));
        Assert.Equal("ah: 2 live · 1 out · 1 blocked", Plain("ah", shortOff));

        var ahOff = Settings(ah: new() { ShowLabel = false });
        Assert.Equal("2 live · 1 out · 1 blocked", Plain("ah", ahOff));
        Assert.Equal("ah: 2/1 1b", Plain("ah-short", ahOff));

        var labelColor = Settings(ahShort: new() { LabelColor = "grey" });
        RenderPlain("ah-short", Ctx(path, T0, "sess-orch", labelColor), 120, out var m);
        Assert.Contains("[grey]ah: [/]", m);
        Assert.Contains("[yellow]2/1 1b[/]", m);
        RenderPlain("ah", Ctx(path, T0, "sess-orch", labelColor), 120, out var m2);
        Assert.DoesNotContain("[grey]", m2);
    }

    [Fact]
    public void MarkupInjection_IsEscapedAndDoesNotThrow()
    {
        var path = Stage(Work(n =>
        {
            FirstEntry(n)["text"] = "[red]x[/]";
            FirstEntry(n)["short"] = "[bold]y";
        }));

        Assert.Equal("ah: [red]x[/]", RenderPlain("ah", Ctx(path, T0, "sess-orch"), 120, out var m1));
        Assert.Equal("ah: [bold]y", RenderPlain("ah-short", Ctx(path, T0, "sess-orch"), 120, out var m2));
        _ = new Markup(m1);
        _ = new Markup(m2);
    }

    [Fact]
    public void RawValue_IsTheTextWithoutLabel()
    {
        var ctx = Ctx(StageFixture("warn"), T0, "sess-orch");

        Assert.Equal("2 live · 1 out · 1 blocked", ItemRegistry.Find("ah")!.ResolveValue(ctx));
        Assert.Equal("2/1 1b", ItemRegistry.Find("ah-short")!.ResolveValue(ctx));
    }

    // ---- laziness ----

    [Fact]
    public void Probe_RunsZeroTimesWithoutAh_AndOnceWithBoth()
    {
        var calls = 0;
        HierarchyEntry? Counting() { calls++; return new HierarchyEntry("warn", "T", "S"); }

        RenderPlain("model", Ctx("", T0, null, counting: Counting), 120, out _);
        Assert.Equal(0, calls);

        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null,
            new[] { new PaneItem("ah", null, null, null), new PaneItem("ah-short", null, null, null) });
        var ctx = Ctx("", T0, null, counting: Counting);
        var values = ItemValueResolver.Resolve(pane, ctx);
        PaneAssembler.RenderLeafRows(pane, 120, ctx, values, new Dictionary<string, ColorResolution.ColorRule>(), new Dictionary<string, Segment>(), new RenderNoteCollector());
        Assert.Equal(1, calls);
    }

    // ---- locate ----

    [Fact]
    public void Locate_WalksUpToTheFirstGitEntry()
    {
        var repo = Path.Combine(_tmp, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var deep = Path.Combine(repo, "a", "b", "c");
        Assert.Equal(Path.Combine(repo, ".claude", "hierarchy", "status.json"), HierarchyStatus.Locate(deep));
    }

    [Fact]
    public void Locate_GitFileStopsTheWalk()
    {
        var outer = Path.Combine(_tmp, "outer");
        var inner = Path.Combine(outer, "inner");
        Directory.CreateDirectory(Path.Combine(outer, ".git"));
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, ".git"), "gitdir: elsewhere");
        StageText(FixtureText("work"), outer);

        var path = HierarchyStatus.Locate(Path.Combine(inner, "x"))!;
        Assert.Equal(Path.Combine(inner, ".claude", "hierarchy", "status.json"), path);
        Assert.Null(Read(path, T0));

        StageText(FixtureText("work"), inner);
        Assert.NotNull(Read(path, T0));
    }

    [Fact]
    public void Locate_NoGitAnywhere_AndBadCwd()
    {
        for (var d = new DirectoryInfo(_tmp); d is not null; d = d.Parent)
        {
            Assert.False(Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")),
                $"an ancestor of the temp dir has .git ({d.FullName}); this test needs a git-free temp tree");
        }

        Assert.Null(HierarchyStatus.Locate(Path.Combine(_tmp, "nogit", "a")));
        Assert.Null(HierarchyStatus.Locate(null));
        Assert.Null(HierarchyStatus.Locate(""));
        Assert.Null(HierarchyStatus.Locate("relative/dir"));
    }

    // ---- config check ----

    public static IEnumerable<object[]> Keys() => new[] { new object[] { "ah" }, new object[] { "ahShort" } };

    private static IReadOnlyList<Diagnostic> Check(string key, string blockJson)
    {
        var config = JsonSerializer.Deserialize($"{{\"itemSettings\":{{\"{key}\":{blockJson}}}}}", ConfigJsonContext.Default.UserConfig)!;
        return ConfigChecker.Check(config).ToList();
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Check_StateColorLabelColorUnknownKeyAndValid(string key)
    {
        Assert.Contains(Check(key, """{"stateColors":{"work":"notacolour"}}"""),
            d => d.Code == "unknown-color" && d.Path == $"/itemSettings/{key}/stateColors/work");
        Assert.Contains(Check(key, """{"labelColor":"notacolour"}"""),
            d => d.Code == "unknown-color" && d.Path == $"/itemSettings/{key}/labelColor");
        Assert.Contains(Check(key, """{"bogus":1}"""),
            d => d.Code == "unknown-key" && d.Path == $"/itemSettings/{key}/bogus");
        Assert.DoesNotContain(Check(key, """{"showLabel":false,"labelColor":"grey","stateColors":{"work":"blue","warn":"yellow","bad":"red","idle":"grey"}}"""), d => d.Path.StartsWith("/itemSettings", StringComparison.Ordinal));
    }
}
