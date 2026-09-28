using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-colors-terminal-fidelity.md §7.1: the regression guard for the ColorSystem-pinning fix.
/// Renders through <see cref="ColorsConsole"/>'s production factory — not a test-local console —
/// because a test that builds its own <c>Standard</c> console would assert Spectre's behaviour
/// rather than ours, and would stay green even if production reverted to the global
/// auto-detecting <c>AnsiConsole</c>.
/// </summary>
public class ColorsConsoleTests
{
    // tools/colors.sh:20-23 — the fixed, binary-verified SGR code per standard colour name.
    private static readonly IReadOnlyDictionary<string, int> ExpectedSgrCode = new Dictionary<string, int>
    {
        ["black"] = 30,
        ["maroon"] = 31,
        ["green"] = 32,
        ["olive"] = 33,
        ["navy"] = 34,
        ["purple"] = 35,
        ["teal"] = 36,
        ["silver"] = 37,
        ["grey"] = 90,
        ["red"] = 91,
        ["lime"] = 92,
        ["yellow"] = 93,
        ["blue"] = 94,
        ["fuchsia"] = 95,
        ["aqua"] = 96,
        ["white"] = 97,
    };

    [Fact]
    public void Create_PinnedToStandard_MatchesToolsColorsShForEveryStandardColorName()
    {
        Assert.Equal(ColorResolution.StandardColorNames.Count, ExpectedSgrCode.Count);
        Assert.All(ColorResolution.StandardColorNames, name => Assert.Contains(name, ExpectedSgrCode.Keys));

        var result = ColorsCommand.Build();
        var lines = ColorsCommand.RenderMarkupLines(result);

        foreach (var (entry, line) in result.Recommended.Zip(lines))
        {
            if (!entry.ThemeMapped)
            {
                continue;
            }

            var writer = new StringWriter();
            var console = ColorsConsole.Create(writer, AnsiSupport.Yes);

            console.MarkupLine(line);

            var output = writer.ToString();
            var expectedCode = ExpectedSgrCode[entry.Name];
            var expectedEscape = "[" + expectedCode + "m";

            Assert.Contains(expectedEscape, output);
            Assert.DoesNotContain("[38;2;", output);
        }
    }
}
