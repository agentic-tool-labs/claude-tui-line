using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-V2-FRAMEWORK.md §9.8.2: pane-dropping is silent instrumentation debt for any caller, not
// just --preview --json. ResolveVerticalMinRows (SizeResolver.cs) has its own drop-retry loop,
// structurally separate from AllocateWithDrop's, and previously had no RenderNoteCollector to
// report through at all — this exercises that the `distribute: "min-rows"` path now emits the
// same "pane {n} dropped" note AllocateWithDrop already does, with the same message format and
// position convention (1-based, counted before the truncation that drops it).
public class MinRowsDropNoteTests
{
    // maxSize: 1 pins each candidate's upper bound in SolveMinRows well below what a single
    // real "model" value needs to render on any row count small enough to be in scope, so the
    // min-rows solve is infeasible at every T it tries and must fall back to (and then drop)
    // every candidate, regardless of surfaceWidth.
    private const string ConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "distribute": "min-rows",
          "children": [
            { "size": "content", "maxSize": 1, "overflow": "wrap",
              "border": { "enabled": false }, "items": [ { "item": "model" } ] },
            { "size": "content", "maxSize": 1, "overflow": "wrap",
              "border": { "enabled": false }, "items": [ { "item": "model" } ] },
            { "size": "content", "maxSize": 1, "overflow": "wrap",
              "border": { "enabled": false }, "items": [ { "item": "model" } ] }
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

    public MinRowsDropNoteTests(ITestOutputHelper output)
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
    public void MinRows_OverConstrainedThreeChildSplit_EmitsPaneDroppedNotesAndDropsToOneChild()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values,new Dictionary<string, Segment>(),  notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Contains(notes.Notes, n => n.Message.StartsWith("pane 3 dropped: 0 columns is under its 1-column floor at", StringComparison.Ordinal));
        Assert.Contains(notes.Notes, n => n.Message.StartsWith("pane 2 dropped: 0 columns is under its 1-column floor at", StringComparison.Ordinal));
        Assert.Single(resolved.Children);
    }
}
