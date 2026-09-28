using Spectre.Console;

namespace ClaudeTuiLine.Tests;

public class ColorsCommandTests
{
    [Fact]
    public void Build_RecommendsTheSixteenThemeColorsAsThemeMappedPlusThreeDecorationKeywords()
    {
        var result = ColorsCommand.Build();

        var themeMapped = result.Recommended.Where(c => c.ThemeMapped).Select(c => c.Name).ToList();
        var notThemeMapped = result.Recommended.Where(c => !c.ThemeMapped).Select(c => c.Name).ToList();

        Assert.Equal(19, result.Recommended.Count);
        Assert.Equal(ColorResolution.StandardColorNames.Count, themeMapped.Count);
        Assert.All(themeMapped, name => Assert.Contains(name, ColorResolution.StandardColorNames));
        Assert.Equal(new[] { "default", "dim", "bold" }, notThemeMapped);
    }

    [Fact]
    public void Build_TheSixteenThemeColorsRoundTripToAForegroundOtherThanColorDefault()
    {
        var result = ColorsCommand.Build();

        foreach (var entry in result.Recommended.Where(c => c.ThemeMapped))
        {
            var resolved = ColorResolution.ResolveLiteral(entry.Name);
            Assert.NotNull(resolved);
            Assert.NotEqual(Color.Default, resolved!.Value);
        }
    }

    // §9.6.3.1: "default"/"dim"/"bold" parse successfully but aren't colors, so a bare non-null
    // check would pass for a real color that got misclassified. Pinning the exact set that
    // resolves to Color.Default is what would catch that.
    [Fact]
    public void Build_ExactlyDefaultDimAndBoldRoundTripToColorDefault()
    {
        var result = ColorsCommand.Build();

        var resolvedToDefault = result.Recommended
            .Where(c => ColorResolution.ResolveLiteral(c.Name) == Color.Default)
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "bold", "default", "dim" }, resolvedToDefault);
    }

    [Fact]
    public void Build_CarriesTheSameVersionAsItemsCommand()
    {
        Assert.Equal(AssemblyVersionInfo.InformationalVersion, ColorsCommand.Build().Version);
    }

    [Fact]
    public void Build_AlsoAcceptedExplainsTheWiderPaletteAndColorSystemFallback()
    {
        var result = ColorsCommand.Build();

        Assert.False(string.IsNullOrWhiteSpace(result.AlsoAccepted));
        Assert.Contains("#rrggbb", result.AlsoAccepted);
        Assert.Contains("colorSystem", result.AlsoAccepted);
    }

    // SPEC-93 §3.4: the degradation rule itself lives only in ConfigCheck; --colors just points at
    // it by name so a second, less accurate copy never has to be kept in sync.
    [Fact]
    public void Build_AlsoAcceptedNamesTheConfigCheckDegradationDiagnostic()
    {
        var result = ColorsCommand.Build();

        Assert.Contains("config-check", result.AlsoAccepted);
        Assert.Contains("color-down-converted", result.AlsoAccepted);
    }

    [Fact]
    public void RenderMarkupLines_TagsEachPaletteNameWithItselfAsTheMarkupStyle()
    {
        var result = ColorsCommand.Build();

        var lines = ColorsCommand.RenderMarkupLines(result);

        Assert.Equal(result.Palette.Select(c => $"{c.Number,3}  [{c.Name}]{c.Name}[/]"), lines);
    }

    [Fact]
    public void RenderMarkupLines_EveryLineParsesAsValidSpectreMarkup()
    {
        var lines = ColorsCommand.RenderMarkupLines(ColorsCommand.Build());

        Assert.All(lines, line => Markup.Remove(line));
    }

    [Fact]
    public void Build_SerializesToJsonWithTheSpecifiedPropertyNames()
    {
        var result = ColorsCommand.Build();
        var json = System.Text.Json.JsonSerializer.Serialize(result, ColorsJsonContext.Default.ColorsResultJson);

        Assert.Contains("\"version\":", json);
        Assert.Contains("\"recommended\":", json);
        Assert.Contains("\"name\":", json);
        Assert.Contains("\"themeMapped\":", json);
        Assert.Contains("\"alsoAccepted\":", json);
    }

    // SPEC-93 §5 test 1: palette has exactly 256 entries.
    [Fact]
    public void Build_PaletteHasExactly256Entries()
    {
        Assert.Equal(256, ColorsCommand.Build().Palette.Count);
    }

    // SPEC-93 §5 test 2: palette numbers are exactly 0..255, each once, in ascending order.
    [Fact]
    public void Build_PaletteNumbersAreZeroTo255InAscendingOrderEachOnce()
    {
        var numbers = ColorsCommand.Build().Palette.Select(p => p.Number);

        Assert.Equal(Enumerable.Range(0, 256), numbers);
    }

    // SPEC-93 §5 test 3 / §3.3: themeMapped by index position, which is a consequence of the
    // membership test (Build_ThemeMappedNamesInPaletteEqualStandardColorNames) rather than a
    // second independent rule.
    [Fact]
    public void Build_PaletteEntries0To15AreThemeMappedAndTheRestAreNot()
    {
        var palette = ColorsCommand.Build().Palette;

        Assert.All(palette.Where(p => p.Number <= 15), p => Assert.True(p.ThemeMapped));
        Assert.All(palette.Where(p => p.Number >= 16), p => Assert.False(p.ThemeMapped));
    }

    // SPEC-93 §5 test 4 / §5.1: compared as sets, never as a sequence — StandardColorNames is a
    // HashSet and its enumeration order is not part of its contract.
    [Fact]
    public void Build_ThemeMappedNamesInPaletteEqualStandardColorNamesAsASet()
    {
        var themeMappedNames = ColorsCommand.Build().Palette
            .Where(p => p.ThemeMapped)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(themeMappedNames.SetEquals(ColorResolution.StandardColorNames));
    }

    // SPEC-93 §5 test 6: every one of the 256, not a sample — this is what guarantees --colors
    // never advertises a name config-check would reject as UnknownColor.
    [Fact]
    public void Build_EveryPaletteNameRoundTripsThroughResolveLiteralToANonNullColor()
    {
        var palette = ColorsCommand.Build().Palette;

        Assert.All(palette, p => Assert.NotNull(ColorResolution.ResolveLiteral(p.Name)));
    }

    // SPEC-93 §5 test 7 / §3.2.1: guards against ever swapping the enumeration for reflection,
    // which would surface as duplicate names (Spectre's alias properties).
    [Fact]
    public void Build_EveryPaletteNameIsNonEmptyAndUniqueAcrossAll256Entries()
    {
        var names = ColorsCommand.Build().Palette.Select(p => p.Name).ToList();

        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // SPEC-93 §5 test 8.
    [Fact]
    public void Build_JsonOutputContainsPalette()
    {
        var result = ColorsCommand.Build();
        var json = System.Text.Json.JsonSerializer.Serialize(result, ColorsJsonContext.Default.ColorsResultJson);

        Assert.Contains("\"palette\":", json);
    }
}
