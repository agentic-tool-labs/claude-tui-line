using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.6.2: the <c>--items --json</c> envelope — one row per
/// <see cref="ItemRegistry.ItemDefinition"/>, plus the fixed <c>kinds</c> schema table (§9.6.2.1
/// explains why that table is a section rather than a per-row column). <c>example</c> is rendered
/// live via <see cref="ItemRegistry.ItemDefinition.BuildDefaultSegment"/> against the one shared
/// synthetic fixture (§9.3.1, <see cref="SyntheticFixture"/>), never a stored string.
/// </summary>
public sealed record ItemJson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reports")] string Reports,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("default")] bool Default,
    [property: JsonPropertyName("example")] string Example);

public sealed record ItemKindJson(
    [property: JsonPropertyName("required")] IReadOnlyList<string> Required,
    [property: JsonPropertyName("optional")] IReadOnlyList<string> Optional);

public sealed record ItemKindsJson(
    [property: JsonPropertyName("builtin")] ItemKindJson Builtin,
    [property: JsonPropertyName("derived")] ItemKindJson Derived,
    [property: JsonPropertyName("command")] ItemKindJson Command,
    [property: JsonPropertyName("compound")] ItemKindJson Compound);

public sealed record ItemsResultJson(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("items")] IReadOnlyList<ItemJson> Items,
    [property: JsonPropertyName("kinds")] ItemKindsJson Kinds);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(ItemsResultJson))]
public partial class ItemsJsonContext : JsonSerializerContext
{
}

public static class ItemsCommand
{
    // §9.6.2's shape: the accepted keys per item kind do not vary by item id (§9.6.2.1 — every
    // builtin takes the same four, and so on for the other three), so this is one fixed table,
    // not sixteen copies of a per-kind fact.
    private static readonly ItemKindsJson Kinds = new(
        Builtin: new ItemKindJson(new[] { "item" }, new[] { "format", "color", "overflow", "link" }),
        Derived: new ItemKindJson(new[] { "id", "from" }, new[] { "extract", "case", "format", "color", "overflow", "link" }),
        Command: new ItemKindJson(new[] { "id", "command" }, new[] { "shell", "ttlSeconds", "timeoutMs", "format", "color", "overflow", "link" }),
        Compound: new ItemKindJson(new[] { "id", "parts" }, new[] { "color", "overflow", "link" }));

    public static ItemsResultJson Build()
    {
        var ctx = SyntheticFixture.CreateItemContext();

        var items = ItemRegistry.All
            .Select(def => new ItemJson(
                def.Id,
                def.Reports,
                def.ColorKind == ItemRegistry.ItemColorKind.Decorative ? "decorative" : "semantic",
                ItemRegistry.DefaultIds.Contains(def.Id, StringComparer.OrdinalIgnoreCase),
                def.BuildDefaultSegment(ctx)?.Plain ?? string.Empty))
            .ToList();

        return new ItemsResultJson(AssemblyVersionInfo.InformationalVersion, items, Kinds);
    }

    // §9.6.2.2: the plain form is a view of this same result, not a second registry walk — two
    // groups by what `default` means (spelled out, since a JSON `default: true` needs no
    // explaining but a bare table does), id/example columns padded to their widest value across
    // every item so both groups share one gutter, `reports` left unpadded for the terminal to
    // wrap. Columns may change without notice; --json is the frozen contract, this isn't.
    public static string RenderPlainText(ItemsResultJson result)
    {
        var idWidth = result.Items.Max(i => i.Id.Length);
        var exampleWidth = result.Items.Max(i => i.Example.Length);

        var lines = new List<string>
        {
            "Default items — rendered unless you remove them:",
        };
        lines.AddRange(result.Items.Where(i => i.Default).Select(i => FormatRow(i, idWidth, exampleWidth)));
        lines.Add(string.Empty);
        lines.Add("Opt-in items — rendered only where you place them:");
        lines.AddRange(result.Items.Where(i => !i.Default).Select(i => FormatRow(i, idWidth, exampleWidth)));
        lines.Add(string.Empty);
        lines.Add("Item kinds: builtin, command, derived, compound. Run with --json for the schema of each.");

        return string.Join('\n', lines);
    }

    private static string FormatRow(ItemJson item, int idWidth, int exampleWidth) =>
        $"  {item.Id.PadRight(idWidth)}  {item.Example.PadRight(exampleWidth)}  {item.Reports}";
}
