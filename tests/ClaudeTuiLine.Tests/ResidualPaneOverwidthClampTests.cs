using System.Reflection;
using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-2.3-residual-pane-overwidth.md §8.2: verification items 1-10 for the ClampToAvail fix.
// §8.1: every config below is borderless on the split with gutter:0 and collapse:false (the
// default), so avail == splitOuterWidth by inspection at every child count — the expectations
// are derived from that arithmetic, not read off a run, except item 10 which deliberately gives
// the split a border so avail != splitOuterWidth.
public class ResidualPaneOverwidthClampTests
{
    private static readonly StatusInput Input = new() { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private readonly ITestOutputHelper _output;

    public ResidualPaneOverwidthClampTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (ResolvedConfig TopLevel, Pane RootPane, IReadOnlyDictionary<string, string?> Values) LoadResolved(string json)
    {
        var (topLevel, pane) = LoadConfig(json);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        return (topLevel, pane, values);
    }

    // Item 1: single fixed child wider than the split. Borderless split, gutter:0, one child
    // size:50, split outer 20 => avail = 20 (§8.1). Fails on main today; the headline test.
    [Fact]
    public void Item1_SingleFixedChildWiderThanSplit_ClampsToAvailAndNotes()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "50", "border": { "enabled": false }, "items": [ { "item": "model" } ] }
              ]
            }
          }
        }
        """;
        var (topLevel, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(20, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 1: 50 columns requested, clamped to 20 at 20 columns");

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        Assert.NotEmpty(rendered.Buffer.Rows);
        Assert.All(rendered.Buffer.Rows, row => Assert.Equal(20, row.Width));
    }

    // Item 2: single fixed child inside the split. size:15, outer 20 => grant 15, no note. Pins
    // that the clamp is inert when it should be.
    [Fact]
    public void Item2_SingleFixedChildInsideSplit_NoClamp()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "15", "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (_, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(15, resolved.Children[0].OuterWidth);
        Assert.Empty(notes.Notes);
    }

    // Item 3 (§2.2): clamping the survivor pushes it under its floor, but the clamp must not
    // retroactively cause a drop — the drop decision was already made against the unclamped
    // result. A content pane with minSize:50 forces Floor(pane) == 50 (Floor checks MinSize
    // first), so AllocateOnePass step 4 clamps its grant to exactly 50 regardless of request,
    // which is not below its own floor (tooSmall stays false) yet is over avail (overAllocated
    // true) — so the single-child exit clamps 50 down to 20, landing below the 50-column floor.
    [Fact]
    public void Item3_ClampNeverCausesADrop()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "content", "minSize": 50, "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (_, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Single(resolved.Children);
        Assert.Equal(20, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 1: 50 columns requested, clamped to 20 at 20 columns");
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
    }

    // Item 4: drop cascade then clamp. Three fixed children of size:50, outer 20 — avail stays 20
    // through every iteration (§8.1, gutter:0 borderless). Two drops, then one clamp, in order.
    [Fact]
    public void Item4_DropCascadeThenClamp()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "50", "border": { "enabled": false }, "items": [] },
                { "size": "50", "border": { "enabled": false }, "items": [] },
                { "size": "50", "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (_, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Equal(20, resolved.Children[0].OuterWidth);

        var messages = notes.Notes.Select(n => n.Message).ToList();
        Assert.Equal(3, messages.Count);
        Assert.Equal("pane 3 dropped: children need 150 columns at 20 columns", messages[0]);
        Assert.Equal("pane 2 dropped: children need 100 columns at 20 columns", messages[1]);
        Assert.Equal("pane 1: 50 columns requested, clamped to 20 at 20 columns", messages[2]);
    }

    // Item 5: zero-width surface. Outer 0, one fixed child => avail = 0. No exception, grant 0.
    [Fact]
    public void Item5_ZeroWidthSurface_ClampsToZeroWithoutThrowing()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "50", "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (topLevel, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 0, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(0, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 1: 50 columns requested, clamped to 0 at 0 columns");

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        Assert.All(rendered.Buffer.Rows, row => Assert.Equal(0, row.Width));
    }

    // Item 6 (§4.1): the empty-child-list guard. `current.Count <= 1` admits `Count == 0`, which
    // the public Resolve API can never actually reach (ResolveNode short-circuits a
    // zero-children split before either drop loop runs), so this calls the private helper
    // directly via reflection — the only way to exercise the guard the spec calls out.
    [Fact]
    public void Item6_EmptyChildList_NoExceptionNoNote()
    {
        var allocResultType = typeof(SizeResolver).GetNestedType("AllocResult", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AllocResult nested type not found");
        var emptyResult = Activator.CreateInstance(
            allocResultType,
            new object[] { Array.Empty<Pane>(), Array.Empty<int>() });

        var clampMethod = typeof(SizeResolver).GetMethod("ClampToAvail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ClampToAvail method not found");

        var notes = new RenderNoteCollector();
        var result = clampMethod.Invoke(null, new[] { emptyResult, 20, 20, notes });

        Assert.NotNull(result);
        Assert.Empty(notes.Notes);
    }

    // Item 7: the min-rows path (:587) clamps too — the analogue of item 1 driven through
    // ResolveVerticalMinRows, where SolveMinRows's `return lo` fallback is the producer. A
    // content candidate with minSize:50 forces Floor == 50 == lo, which alone exceeds r == 20 at
    // every T, so no T is ever feasible and SolveMinRows falls back to lo == 50 — the same
    // numbers as item 1.
    [Fact]
    public void Item7_MinRowsPathClampsToo()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "distribute": "min-rows",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "content", "minSize": 50, "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (_, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(20, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 1: 50 columns requested, clamped to 20 at 20 columns");
        Assert.DoesNotContain(notes.Notes, n => n.Message.Contains("dropped"));
    }

    // Item 8 (§4.2, medium confidence): the item-7 config rendered end to end must still produce
    // a valid rectangle — every row exactly the surface width — even though the row count was
    // solved at 50 columns and the grant was then clamped to 20. If this fails, §4.2's ruling
    // (clamp the grant, do not re-solve) is a spec-defect, not an implementation bug to work
    // around.
    [Fact]
    public void Item8_ClampedMinRowsStillRendersAValidRectangle()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "distribute": "min-rows",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "content", "minSize": 50, "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (topLevel, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);
        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors,new Dictionary<string, Segment>(),  new RenderNoteCollector());

        Assert.All(rendered.Buffer.Rows, row => Assert.Equal(20, row.Width));
    }

    // Item 9 (§2.3): monotonicity spot-check. Item 1's config at outer 19-22 — the grant must be
    // non-decreasing as outer width grows.
    [Theory]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    public void Item9_Monotonicity_GrantNonDecreasingWithOuterWidth(int outerWidth)
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": false },
              "children": [
                { "size": "50", "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (_, pane, values) = LoadResolved(json);

        var grants = new[] { 19, 20, 21, 22 }
            .Select(w => SizeResolver.Resolve(pane, w, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector()).Children[0].OuterWidth)
            .ToArray();

        for (var i = 1; i < grants.Length; i++)
        {
            Assert.True(grants[i] >= grants[i - 1], $"grant at outer {19 + i} ({grants[i]}) must be >= grant at outer {18 + i} ({grants[i - 1]})");
        }

        Assert.Equal(outerWidth, SizeResolver.Resolve(pane, outerWidth, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector()).Children[0].OuterWidth);
    }

    // Item 10: a split with a border still clamps to avail, not to splitOuterWidth. Item 1's
    // config, but the split itself has border.style, reserving 4 columns (padding 2 + left/right
    // 1 each) => avail = 20 - 4 = 16. Deliberately the one item where avail != splitOuterWidth.
    [Fact]
    public void Item10_SplitWithBorderClampsToAvailNotOuterWidth()
    {
        const string json = """
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "border": { "enabled": true },
              "children": [
                { "size": "50", "border": { "enabled": false }, "items": [] }
              ]
            }
          }
        }
        """;
        var (_, pane, values) = LoadResolved(json);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 20, Ctx, values,new Dictionary<string, Segment>(),  notes);

        Assert.Equal(16, resolved.Children[0].OuterWidth);
        Assert.Contains(notes.Notes, n => n.Message == "pane 1: 50 columns requested, clamped to 16 at 20 columns");
    }
}
