using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-2.3-even-split-parity.md (#78) §7: the three-way mirroring between AllocateWithDrop,
// ResolveVerticalMinRows, and ResolveVerticalEven is asserted in comments and, before this file,
// tested nowhere -- five fixes in a row landed in two of three loops and silently skipped the
// third. This is the tripwire: one config per resolver that forces a drop, asserting all three
// emit a note with the shared wording. It fails the moment a fourth loop is added without notes,
// or a note string drifts in one of the three.
public class DistributionLoopsShareDropNoteWordingTests
{
    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private readonly ITestOutputHelper _output;

    public DistributionLoopsShareDropNoteWordingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig(string configJson)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, configJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ConfigFor(string? distribute) => $$"""
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 0,
          {{(distribute is null ? "" : $"\"distribute\": \"{distribute}\",")}}
          "children": [
            { "size": "30", "border": { "enabled": false } },
            { "size": "30", "border": { "enabled": false } }
          ]
        }
      }
    }
    """;

    [Theory]
    [InlineData(null)]
    [InlineData("min-rows")]
    [InlineData("even")]
    public void EveryDistributionLoop_EmitsADropNoteWithTheSharedWording(string? distribute)
    {
        var (_, pane) = LoadConfig(ConfigFor(distribute));
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        SizeResolver.Resolve(pane, 46, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: children need 60 columns at 46 columns");
    }
}
