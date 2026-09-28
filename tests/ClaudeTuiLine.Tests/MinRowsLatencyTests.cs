using System.Diagnostics;
using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3.1 acceptance condition 2: "p90 re-measured against the budget (§5)
/// with min-rows active across widths 100-240. A regression here fails the feature, per the
/// paragraph above" — the paragraph in question is the worked example's packer-call-count
/// argument, i.e. this is about the cost of <c>SizeResolver.Resolve</c>'s own min-rows search, not
/// a full process-spawn render. That reading is what is measured here, in-process; the acceptance
/// text names no harness for this specific condition, unlike §10 item 10's separate "existing
/// bench harness" criterion (full end-to-end render latency with zero command items, measured by
/// <c>bench/bench.sh</c>).
/// </summary>
public class MinRowsLatencyTests
{
    // §5: "the 12.6ms budget that justified Native AOT in the first place."
    private const double BudgetMs = 12.6;

    private const string ConfigJson = """
    {
      "colors": { "model-accent": { "default": "blue" } },
      "surface": {
        "maxRows": 8,
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "distribute": "min-rows",
          "children": [
            { "size": "fill", "overflow": "wrap",
              "border": { "enabled": true, "color": "grey" } },
            { "size": "content", "overflow": "wrap",
              "border": { "enabled": true, "color": "@model-accent" },
              "items": [ { "item": "model", "color": "@model-accent" },
                         { "item": "effort" }, { "item": "thinking" }, { "item": "context" } ] }
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

    public MinRowsLatencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MinRows_P90LatencyAcrossWidths100To240_StaysUnderTheRenderBudget()
    {
        var path = Path.GetTempFileName();
        UserConfigLoadResult loaded;
        try
        {
            File.WriteAllText(path, ConfigJson);
            var (topLevel, pane) = ConfigLoader.LoadAll(path);
            loaded = new UserConfigLoadResult(topLevel, pane);
        }
        finally
        {
            File.Delete(path);
        }

        var values = ItemValueResolver.Resolve(loaded.Pane, Ctx, loaded.TopLevel.Colors);

        // Warm-up: JIT tiering must not leak into the measured samples.
        for (var w = 100; w <= 240; w++)
        {
            SizeResolver.Resolve(loaded.Pane, w, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
        }

        var samples = new List<double>();
        var stopwatch = new Stopwatch();
        for (var w = 100; w <= 240; w++)
        {
            stopwatch.Restart();
            SizeResolver.Resolve(loaded.Pane, w, Ctx, values,new Dictionary<string, Segment>(),  new RenderNoteCollector());
            stopwatch.Stop();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p90Index = Math.Clamp((int)Math.Ceiling(0.90 * samples.Count) - 1, 0, samples.Count - 1);
        var p90 = samples[p90Index];

        _output.WriteLine($"min-rows p90 latency across widths 100-240: {p90:F3}ms over {samples.Count} samples (max {samples[^1]:F3}ms)");

        Assert.True(p90 < BudgetMs, $"p90 latency {p90:F3}ms must stay under the {BudgetMs}ms budget (§5)");
    }

    private readonly record struct UserConfigLoadResult(ResolvedConfig TopLevel, Pane Pane);
}
