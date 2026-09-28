using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC.md §6 "MEASURED": Claude Code truncates any statusline row wider than COLUMNS - 3, not
/// COLUMNS - 1 as originally assumed — and nothing caught this for three rounds because every
/// prior check compared the binary to bash through a pipe, where both used the same wrong
/// constant and agreed perfectly. These tests render through the same non-TTY Spectre pipeline
/// Program.cs uses and check ANSI-stripped row width, so they do not depend on a real terminal.
///
/// Two concerns are kept deliberately separate, matching the surface-sizing seam in
/// SurfaceLayout/RowLayout: <see cref="Borderless_RowsNeverExceedTheGivenAvailableWidth"/> and
/// <see cref="Panel_RowsIncludingOversizedContent_NeverExceedTheGivenSurfaceWidth"/> check that
/// rendering respects whatever width it is given, regardless of where that width came from —
/// true even under the old, wrong chromeReserve. Only
/// <see cref="AtLegacyChromeReserve1_TheRealTerminalBudgetCanBeViolated_ProvingTheFixMatters"/>
/// and <see cref="AtDefaultChromeReserve3_TheRealTerminalBudgetIsNeverViolated"/> anchor
/// chromeReserve's value to the measured real-world truncation boundary — a check that would
/// have caught this defect, since it does not move with whatever chromeReserve the code under
/// test used to produce the row.
/// </summary>
public class RenderInvariantTests
{
    private const int MeasuredTrueChromeBudget = 3;

    private static readonly int[] SweptWidths =
    {
        20, 21, 22, 23, 24, 25, 26, 27, 30, 40, 60, 80, 100, 112, 120, 160, 200,
    };

    // Every segment here is short enough to fit within avail at every non-fallback swept width,
    // so every row it produces comes from normal greedy packing — none can trigger RowLayout's
    // "oversized segment gets its own overwide row" exception, which is a documented, accepted
    // deviation (SPEC.md §3) unrelated to this defect.
    internal static IReadOnlyList<Segment> NormalFixture() => new[]
    {
        new Segment("claude-tui-line", "claude-tui-line"),
        new Segment("[dim]main[/]", "main"),
        new Segment("[purple]feature-x[/]", "feature-x"),
        new Segment("[yellow]PR #42[/]", "PR #42"),
        new Segment("[blue]Opus 4.5[/]", "Opus 4.5"),
    };

    // Deliberately includes a segment far wider than any swept avail, to exercise Spectre's
    // reflow of RowLayout's never-split oversized-segment row inside an actual rendered panel.
    internal static IReadOnlyList<Segment> FixtureWithOversizedSegment()
    {
        var oversized = new string('x', 49);
        return NormalFixture().Append(new Segment($"[dim]{oversized}[/]", oversized)).ToList();
    }

    private static List<string> RenderBorderless(IReadOnlyList<Segment> segments, int? availableWidth)
    {
        var rows = RowLayout.Wrap(segments, availableWidth);

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

        return Strip(writer.ToString());
    }

    private static List<string> RenderPanel(IReadOnlyList<Segment> segments, int surfaceWidth, int borderReserve)
    {
        var contentWidth = Math.Max(0, surfaceWidth - borderReserve);
        var rows = RowLayout.Wrap(segments, contentWidth);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = Math.Max(1, surfaceWidth);

        var panel = new Panel(new Markup(string.Join('\n', rows.Select(r => r.Markup))))
            .Padding(1, 0)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Grey));
        panel.Width = surfaceWidth;

        console.Write(panel);

        return Strip(writer.ToString());
    }

    private static List<string> Strip(string raw) =>
        AnsiStrip.Strip(raw).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    public static IEnumerable<object[]> SweptWidthsData() => SweptWidths.Select(w => new object[] { w });

    [Theory]
    [MemberData(nameof(SweptWidthsData))]
    public void Borderless_RowsNeverExceedTheGivenAvailableWidth(int availableWidth)
    {
        if (RowLayout.IsFallbackWidth(availableWidth))
        {
            return; // single-line fallback: deliberately overwide by design (SPEC.md §3), not this check's concern.
        }

        foreach (var line in RenderBorderless(NormalFixture(), availableWidth))
        {
            Assert.True(line.Length <= availableWidth, $"avail={availableWidth}: row {line.Length} wide: \"{line}\"");
        }
    }

    [Theory]
    [MemberData(nameof(SweptWidthsData))]
    public void Panel_RowsIncludingOversizedContent_NeverExceedTheGivenSurfaceWidth(int surfaceWidth)
    {
        if (RowLayout.IsFallbackWidth(Math.Max(0, surfaceWidth - 4)))
        {
            return; // §6b suppression: no panel renders below this threshold.
        }

        foreach (var line in RenderPanel(FixtureWithOversizedSegment(), surfaceWidth, borderReserve: 4))
        {
            Assert.True(line.Length <= surfaceWidth, $"surface={surfaceWidth}: panel row {line.Length} wide: \"{line}\"");
        }
    }

    [Fact]
    public void AtLegacyChromeReserve1_TheRealTerminalBudgetCanBeViolated_ProvingTheFixMatters()
    {
        var violations = new List<string>();

        foreach (var columns in SweptWidths)
        {
            var oldSurfaceWidth = SurfaceLayout.ComputeWidth(columns.ToString(), chromeReserve: 1)!.Value;
            if (RowLayout.IsFallbackWidth(Math.Max(0, oldSurfaceWidth - 4)))
            {
                continue;
            }

            var trueBudget = columns - MeasuredTrueChromeBudget;
            violations.AddRange(RenderPanel(FixtureWithOversizedSegment(), oldSurfaceWidth, borderReserve: 4)
                .Where(l => l.Length > trueBudget)
                .Select(l => $"COLUMNS={columns} (old surfaceWidth={oldSurfaceWidth}): row {l.Length} wide exceeds real budget {trueBudget}"));
        }

        Assert.True(violations.Count > 0, "Expected chromeReserve=1 to violate the measured true budget somewhere in the swept range — none did, which would mean this invariant cannot fail.");
    }

    [Fact]
    public void AtDefaultChromeReserve3_TheRealTerminalBudgetIsNeverViolated()
    {
        foreach (var columns in SweptWidths)
        {
            var surfaceWidth = SurfaceLayout.ComputeWidth(columns.ToString(), chromeReserve: 3)!.Value;
            if (RowLayout.IsFallbackWidth(Math.Max(0, surfaceWidth - 4)))
            {
                continue;
            }

            var trueBudget = columns - MeasuredTrueChromeBudget;
            foreach (var line in RenderPanel(FixtureWithOversizedSegment(), surfaceWidth, borderReserve: 4))
            {
                Assert.True(line.Length <= trueBudget, $"COLUMNS={columns}: panel row {line.Length} wide exceeds real budget {trueBudget}: \"{line}\"");
            }
        }
    }
}
