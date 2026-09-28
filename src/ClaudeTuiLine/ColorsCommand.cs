using System.Text.Json.Serialization;
using Spectre.Console;

namespace ClaudeTuiLine;

public sealed record ColorEntryJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("themeMapped")] bool ThemeMapped);

public sealed record PaletteEntryJson(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("themeMapped")] bool ThemeMapped);

public sealed record ColorsResultJson(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("recommended")] IReadOnlyList<ColorEntryJson> Recommended,
    [property: JsonPropertyName("alsoAccepted")] string AlsoAccepted,
    [property: JsonPropertyName("palette")] IReadOnlyList<PaletteEntryJson> Palette);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(ColorsResultJson))]
public partial class ColorsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.6.3/§9.6.3.1: <c>--colors</c> cannot enumerate the colors this tool
/// accepts — that set is Spectre.Console's own (roughly 256 names, plus arbitrary hex), a table
/// this codebase does not own and must not duplicate. What it prints instead is a curated
/// recommendation: <see cref="ColorResolution.StandardColorNames"/>'s sixteen ANSI theme colors —
/// reused here as their second consumer, not re-declared — plus the three decoration keywords
/// <c>default</c>/<c>dim</c>/<c>bold</c>, which are not theme-mapped colors and marked as such.
/// </summary>
public static class ColorsCommand
{
    // §9.6.3.1: these three are not colors — "default" names Color.Default outright, and "bold"/
    // "dim" parse as decorations that leave the foreground at Color.Default. Appended after the
    // sixteen so `themeMapped` splits cleanly along a HashSet-membership check.
    private static readonly IReadOnlyList<string> NonColorKeywords = new[] { "default", "dim", "bold" };

    private const string AlsoAcceptedText =
        "Any Spectre.Console color name (256-palette, e.g. deepskyblue1) or #rrggbb hex. These parse everywhere a name is accepted; how faithfully they render depends on colorSystem (§6.2), which defaults to standard and approximates them to the nearest of the sixteen. config-check flags this against your actual colorSystem with its color-down-converted diagnostic.";

    public static ColorsResultJson Build()
    {
        var recommended = ColorResolution.StandardColorNames
            .Select(name => new ColorEntryJson(name, ThemeMapped: true))
            .Concat(NonColorKeywords.Select(name => new ColorEntryJson(name, ThemeMapped: false)))
            .ToList();

        // SPEC-93 §3.2: enumerated from Spectre by index, never hardcoded or reflected — see
        // §3.2.1 for why reflection over Color's static properties (291 aliased entries) is
        // rejected. Index order alone gives the sixteen standard names first for free (§3.3).
        var palette = Enumerable.Range(0, 256)
            .Select(i =>
            {
                var name = Color.FromInt32(i).ToString();
                return new PaletteEntryJson(i, name, ColorResolution.StandardColorNames.Contains(name));
            })
            .ToList();

        return new ColorsResultJson(AssemblyVersionInfo.InformationalVersion, recommended, AlsoAcceptedText, palette);
    }

    // §9.6.3.1: the deliberate exception to §9.6.2.2's plain-only rule — stripping the colour
    // from a swatch leaves nothing but the name to guess from, so bare `--colors` renders each
    // entry in its own style. Spectre parses "default"/"dim"/"bold" as style tokens the same way
    // it parses a colour name, so a name doubling as its own markup tag needs no special-casing.
    //
    // SPEC-93 §3.5: renders the full palette (256 entries), not just `recommended` — the sixteen
    // still come first because `palette` is in index order. The index is included alongside the
    // name since a bare numeric spec is itself a usable colour (E3).
    public static IReadOnlyList<string> RenderMarkupLines(ColorsResultJson result) =>
        result.Palette.Select(c => $"{c.Number,3}  [{c.Name}]{c.Name}[/]").ToList();
}
