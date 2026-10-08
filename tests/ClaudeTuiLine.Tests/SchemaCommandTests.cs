using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTuiLine.Tests;

public class SchemaCommandTests
{
    // §7 V4: colorExpr is exempt from the record-vs-declared-keys reflection check — it's a
    // converter-driven union with no attributed properties of its own. compoundPart now has a
    // real backing record (PaneItemPartJsonConfig, task #85) and is no longer exempt.
    private static readonly HashSet<string> RecordCheckExemptEntries = new(StringComparer.Ordinal)
    {
        "colorExpr",
    };

    private static readonly HashSet<string> RequiredStructureNames = new(StringComparer.Ordinal)
    {
        "config", "border", "borderEdges", "layout", "surface", "pane", "item",
        "colorRule", "threshold", "match", "colorExpr", "compoundPart",
        "itemSettings", "directoryItemSettings", "contextItemSettings",
        "worktreeItemSettings", "rateLimitsItemSettings", "prItemSettings", "linearItemSettings",
        "outputStyleItemSettings", "autocompactItemSettings", "engramItemSettings",
        "engramField", "engramStateColors", "ahItemSettings", "ahStateColors",
    };

    [Fact]
    public void Build_EmbedsItemsColorsAndAcceptedByteIdenticalToTheirOwnSoloBuilds()
    {
        var schema = SchemaCommand.Build();

        var itemsJson = JsonSerializer.Serialize(schema.Items, ItemsJsonContext.Default.ItemsResultJson);
        var soloItemsJson = JsonSerializer.Serialize(ItemsCommand.Build(), ItemsJsonContext.Default.ItemsResultJson);
        Assert.Equal(soloItemsJson, itemsJson);

        var colorsJson = JsonSerializer.Serialize(schema.Colors, ColorsJsonContext.Default.ColorsResultJson);
        var soloColorsJson = JsonSerializer.Serialize(ColorsCommand.Build(), ColorsJsonContext.Default.ColorsResultJson);
        Assert.Equal(soloColorsJson, colorsJson);

        var acceptedJson = JsonSerializer.Serialize(schema.Accepted, AcceptedJsonContext.Default.AcceptedResultJson);
        var soloAcceptedJson = JsonSerializer.Serialize(AcceptedCommand.Build(), AcceptedJsonContext.Default.AcceptedResultJson);
        Assert.Equal(soloAcceptedJson, acceptedJson);
    }

    [Fact]
    public void Build_KindSupportHasExactlyOneEntryPerItemsKindsKey()
    {
        var schema = SchemaCommand.Build();

        Assert.NotNull(schema.KindSupport.Builtin);
        Assert.NotNull(schema.KindSupport.Derived);
        Assert.NotNull(schema.KindSupport.Command);
        Assert.NotNull(schema.KindSupport.Compound);
    }

    [Fact]
    public void ModelItemKeys_MatchesPaneItemJsonConfigsRealJsonPropertyNamesExactly()
    {
        var declared = (IReadOnlyList<string>)typeof(SchemaCommand)
            .GetField("ModelItemKeys", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var real = typeof(PaneItemJsonConfig)
            .GetProperties()
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(real, declared.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void BuildStructures_EntryNamesAreExactlyTheRequiredNames()
    {
        var names = SchemaCommand.Build().Structures.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(RequiredStructureNames, names);
    }

    [Fact]
    public void BuildStructures_EveryNonExemptEntrysRecordTypeMatchesItsRequiredAndOptionalKeysExactly()
    {
        var structures = SchemaCommand.Build().Structures;
        var assembly = typeof(SchemaCommand).Assembly;

        foreach (var entry in structures)
        {
            if (entry.Record is null || RecordCheckExemptEntries.Contains(entry.Name))
            {
                continue;
            }

            var type = assembly.GetType($"ClaudeTuiLine.{entry.Record}");
            Assert.True(type is not null, $"structures entry '{entry.Name}' names record '{entry.Record}', which does not exist");

            var realKeys = type!
                .GetProperties()
                .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>())
                .Where(a => a is not null)
                .Select(a => a!.Name)
                .ToHashSet(StringComparer.Ordinal);

            var declaredKeys = entry.Required.Concat(entry.Optional).ToHashSet(StringComparer.Ordinal);

            Assert.True(
                realKeys.SetEquals(declaredKeys),
                $"structures entry '{entry.Name}' (record {entry.Record}) declares {{{string.Join(",", declaredKeys)}}} but the type's real wire keys are {{{string.Join(",", realKeys)}}}");
        }
    }

    [Fact]
    public void BuildStructures_EveryNonNullAcceptedKeyExistsInAcceptedKeysKey()
    {
        var schema = SchemaCommand.Build();
        var acceptedKeys = schema.Accepted.Keys.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in schema.Structures)
        {
            foreach (var field in entry.Fields)
            {
                if (field.AcceptedKey is not null)
                {
                    Assert.Contains(field.AcceptedKey, acceptedKeys);
                }
            }
        }
    }

    // §7 V6: written to pass both before and after #85 lands — it asserts the computed
    // relationship (unsupported iff the kind's keys aren't a subset of ModelItemKeys), not a
    // hardcoded `false`, so #85 flips this from red to green with no edit here.
    [Fact]
    public void KindSupport_CompoundReflectsWhetherItsKeysAreCurrentlyModeled()
    {
        var schema = SchemaCommand.Build();
        var compoundKeys = schema.Items.Kinds.Compound.Required.Concat(schema.Items.Kinds.Compound.Optional);
        var modelKeys = (IReadOnlyList<string>)typeof(SchemaCommand)
            .GetField("ModelItemKeys", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var expectedSupported = compoundKeys.All(k => modelKeys.Contains(k, StringComparer.Ordinal));

        Assert.Equal(expectedSupported, schema.KindSupport.Compound.Supported);
    }
}
