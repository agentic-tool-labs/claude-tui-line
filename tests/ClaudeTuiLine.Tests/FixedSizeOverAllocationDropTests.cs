using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// #67a: AllocateOnePass step 2 (fixed-size panes) is unclamped, and AllocateWithDrop's tooSmall
// check explicitly exempts SizeKind.Fixed — so two fixed-size panes whose declared sizes alone
// exceed the split's budget were both granted their full declared size (sum over budget) with no
// drop and no note. This pins the greedy-path analogue of the min-rows over-allocation bug #64/#25
// fixed on ResolveVerticalMinRows: two size:30 panes in a 46-column split (gutter 0) sum to 60,
// which cannot fit — one must be dropped and reported, not both over-granted.
public class FixedSizeOverAllocationDropTests
{
    private const string ConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 0,
          "children": [
            { "size": "30", "overflow": "wrap", "border": { "enabled": false }, "items": [] },
            { "size": "30", "overflow": "wrap", "border": { "enabled": false }, "items": [] }
          ]
        }
      }
    }
    """;

    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private readonly ITestOutputHelper _output;

    public FixedSizeOverAllocationDropTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ConfigJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TwoFixedSizePanesSummingOverBudget_DropsOneRatherThanOverGrantingBoth()
    {
        var (_, pane) = LoadConfig();
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 46, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: children need 60 columns at 46 columns");
        Assert.Single(resolved.Children);
        Assert.Equal(30, resolved.Children[0].OuterWidth);
    }
}
