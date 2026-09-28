using System.Text.Json;
using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md's Phase 2 gate: the new pane pipeline (root Pane → PaneRenderer →
/// Compositor → Panel/MarkupLine) must be byte-identical to the pre-pane Phase 1 pipeline across
/// the full width sweep, border on and off. Any diff here is a compositor bug to fix — never a
/// reason to re-baseline the fixture. <c>fixtures/golden-phase1-baseline.json</c> was captured
/// once, from the pre-pane binary, before any Phase 2 rendering code was touched; the capture test
/// itself was deleted immediately afterward per the project's own §1 DRY rule (no permanently
/// duplicated "reference implementation" sitting alongside the real pipeline). Reusing
/// <see cref="RenderInvariantTests.NormalFixture"/> and
/// <see cref="RenderInvariantTests.FixtureWithOversizedSegment"/> here, rather than re-typing a
/// second copy of the same synthetic segments, is that same rule: they are the exact fixtures the
/// baseline was captured from.
/// </summary>
public class GoldenParityTests
{
    private sealed record GoldenEntry(string? ColumnsEnv, bool BorderRequested, string Fixture, string RawOutput);

    private static IReadOnlyList<GoldenEntry> LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "golden-phase1-baseline.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<GoldenEntry>>(json)
            ?? throw new InvalidOperationException("golden-phase1-baseline.json deserialized to null");
    }

    // Theory data is decomposed into plain primitives (not the GoldenEntry record itself) to stay
    // on xUnit's well-trodden MemberData path.
    public static IEnumerable<object?[]> GoldenEntries() =>
        LoadFixture().Select(e => new object?[] { e.ColumnsEnv, e.BorderRequested, e.Fixture, e.RawOutput });

    [Theory]
    [MemberData(nameof(GoldenEntries))]
    public void NewPipeline_MatchesPhase1Baseline_ByteForByte(string? columnsEnv, bool borderRequested, string fixtureName, string expectedRawOutput)
    {
        var segments = fixtureName switch
        {
            "normal" => RenderInvariantTests.NormalFixture(),
            "oversized" => RenderInvariantTests.FixtureWithOversizedSegment(),
            _ => throw new InvalidOperationException($"unknown fixture name in golden entry: {fixtureName}"),
        };

        var actual = RenderNewPipeline(columnsEnv, borderRequested, segments);

        Assert.Equal(expectedRawOutput, actual);
    }

    // Mirrors Program.cs's pipeline exactly (SurfaceLayout -> root Pane -> PaneRenderer ->
    // Compositor -> Panel/MarkupLine), redirected to a StringWriter instead of Console.Out, with
    // config-file reading replaced by a directly-constructed ResolvedConfig/root Pane. Program.cs
    // itself is top-level statements and cannot be called into directly, so this reproduces its
    // wiring the same way RenderInvariantTests' own RenderBorderless/RenderPanel helpers already
    // do for the pre-pane pipeline.
    private static string RenderNewPipeline(string? columnsEnv, bool borderRequested, IReadOnlyList<Segment> segments)
    {
        var topLevel = new ResolvedConfig(
            new ColorResolution.ColorExpr.Literal("grey"),
            borderRequested ? BoxBorder.Rounded : null,
            PaneBorderEdges.All,
            ChromeReserve: 3,
            ColorSystem: ColorSystemSupport.Standard,
            Colors: new Dictionary<string, ColorResolution.ColorRule>());
        var pane = ConfigLoader.ResolveRootPane(config: null, topLevel);

        var surfaceWidth = SurfaceLayout.ComputeWidth(columnsEnv, topLevel.ChromeReserve);
        var borderReserve = pane.Border.Style is not null ? 4 : 0;
        var contentWidth = surfaceWidth is int sw ? Math.Max(0, sw - borderReserve) : (int?)null;

        var overflow = pane.Overflow ?? OverflowMode.Overflow;
        var buffer = PaneRenderer.RenderLeaf(segments, contentWidth, overflow, pane.Ellipsis, new RenderNoteCollector());

        IReadOnlyList<PaneRow> rows = contentWidth is int width
            ? Compositor.ComposeRoot(new[] { new Compositor.PaneContribution(buffer, width, HasBackground: false) })
            : buffer.Rows;

        var fallback = RowLayout.IsFallbackWidth(contentWidth);
        var suppressBorder = pane.Border.Style is not null && fallback;
        var renderingPanel = pane.Border.Style is not null && !suppressBorder;

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        if (renderingPanel)
        {
            var boxBorder = pane.Border.Style!;
            var borderColor = ColorResolution.ResolveBorderColor(pane.Border.Color, new Dictionary<string, string?>(), topLevel.Colors);
            var panel = new Panel(new Markup(string.Join('\n', rows.Select(r => r.Markup))))
                .Padding(1, 0)
                .Border(boxBorder)
                .BorderStyle(borderColor);

            panel.Width = surfaceWidth!.Value;

            console.Write(panel);
        }
        else
        {
            foreach (var row in rows)
            {
                console.MarkupLine(row.Markup);
            }
        }

        return writer.ToString();
    }
}
