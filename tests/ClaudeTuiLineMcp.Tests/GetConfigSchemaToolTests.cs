using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>SPEC-84-mcp-schema-explorer.md §7 V7-V10.</summary>
public sealed class GetConfigSchemaToolTests : IDisposable
{
    private readonly TestCliFixture _cli = new();

    public void Dispose() => _cli.Dispose();

    private static JsonElement AsJson(object result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

    // §7 V7: AllowListTests V4/V4b (no ProjectReference to ClaudeTuiLine.csproj, no
    // ClaudeTuiLine.* reference outside ConfigLoader.ResolveConfigPath) are asserted by that
    // file directly and run unmodified as part of the same test project; this test exists only
    // to document that get_config_schema's CLI-spawn design was chosen specifically to keep
    // those static checks passing, not to re-implement them.
    [Fact]
    public async Task V7_GetConfigSchemaSpawnsTheCliRatherThanLinkingTheCoreInProcess()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());

        Assert.True(result.GetProperty("version").GetString() == "test");
    }

    [Fact]
    public async Task V8_NoCliFailsWithCliNotFoundAndSearchedPaths()
    {
        _cli.RemoveCli();

        var result = AsJson(await ConfigTools.GetConfigSchema());

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("cli-not-found", result.GetProperty("code").GetString());
        Assert.True(result.GetProperty("searchedPaths").GetArrayLength() > 0);
    }

    [Fact]
    public async Task V9_SectionsFilterReturnsOnlyVersionAndTheRequestedSections()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "accepted" }));

        Assert.Equal("test", result.GetProperty("version").GetString());
        Assert.True(result.TryGetProperty("accepted", out _));
        Assert.False(result.TryGetProperty("items", out _));
        Assert.False(result.TryGetProperty("colors", out _));
        Assert.False(result.TryGetProperty("structures", out _));
        Assert.False(result.TryGetProperty("kindSupport", out _));
    }

    [Fact]
    public async Task V10_AnUnrecognizedSectionNameFailsWithTheValidNamesListed()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "colours" }));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown-section", result.GetProperty("code").GetString());
        var message = result.GetProperty("message").GetString()!;
        Assert.Contains("colours", message);
        Assert.Contains("items", message);
        Assert.Contains("colors", message);
        Assert.Contains("accepted", message);
        Assert.Contains("structures", message);
        Assert.Contains("kindSupport", message);
    }

    // schema-mcp-query.md §7 T1-T20 (amendment-1).

    [Fact]
    public async Task T1_DefaultHasIndexModeAndAllFiveSectionsPlusVersion()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());

        Assert.Equal("index", result.GetProperty("mode").GetString());
        Assert.True(result.TryGetProperty("version", out _));
        foreach (var section in new[] { "items", "colors", "accepted", "structures", "kindSupport" })
        {
            Assert.True(result.TryGetProperty(section, out _), $"missing section {section}");
        }
    }

    [Fact]
    public async Task T2_DefaultHasNoProseKeyAtAnyDepth()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());

        AssertNoProseKeys(result);
    }

    private static void AssertNoProseKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    Assert.False(prop.NameEquals("description"), "found a description key");
                    Assert.False(prop.NameEquals("notes"), "found a notes key");
                    Assert.False(prop.NameEquals("example"), "found an example key");
                    AssertNoProseKeys(prop.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoProseKeys(item);
                }

                break;
        }
    }

    [Fact]
    public async Task T3_DefaultStructuresHasExactlyTwelveEntriesWithTheExpectedKeys()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());
        var structures = result.GetProperty("structures");

        Assert.Equal(12, structures.GetArrayLength());
        foreach (var entry in structures.EnumerateArray())
        {
            Assert.True(entry.TryGetProperty("id", out _));
            Assert.True(entry.TryGetProperty("name", out _));
            Assert.True(entry.TryGetProperty("required", out _));
            Assert.True(entry.TryGetProperty("optional", out _));
            Assert.True(entry.TryGetProperty("fields", out _));
        }
    }

    [Fact]
    public async Task T4_DefaultBorderEdgesCarriesAllFourFieldNamesAndTypes()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());
        var borderEdges = result.GetProperty("structures").EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "borderEdges");

        var fields = borderEdges.GetProperty("fields").EnumerateArray().ToList();
        var names = fields.Select(f => f.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "top", "right", "bottom", "left" }, names);
        Assert.All(fields, f => Assert.Equal("boolean", f.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task T5_EveryFieldEntryHasNameAndTypeNeverDescription()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());

        foreach (var entry in result.GetProperty("structures").EnumerateArray())
        {
            foreach (var field in entry.GetProperty("fields").EnumerateArray())
            {
                Assert.True(field.TryGetProperty("name", out _));
                Assert.True(field.TryGetProperty("type", out _));
                Assert.False(field.TryGetProperty("description", out _));
            }
        }
    }

    [Fact]
    public async Task T6_AcceptedKeySurvivesWherePresentAndIsOmittedNotNullWhereAbsent()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());
        var border = result.GetProperty("structures").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "border");
        var fields = border.GetProperty("fields").EnumerateArray().ToList();

        var styleField = fields.Single(f => f.GetProperty("name").GetString() == "style");
        Assert.Equal("border.style", styleField.GetProperty("acceptedKey").GetString());

        var enabledField = fields.Single(f => f.GetProperty("name").GetString() == "enabled");
        Assert.False(enabledField.TryGetProperty("acceptedKey", out _));
    }

    [Fact]
    public async Task T7_SelectStructuresPaneDeepEqualsTheUnfilteredSectionsEntry()
    {
        var detail = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "structures.pane" }));
        Assert.Equal("detail", detail.GetProperty("mode").GetString());
        var entries = detail.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("structures.pane", entries[0].GetProperty("id").GetString());

        var full = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "structures" }));
        var expected = full.GetProperty("structures").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "pane");

        Assert.Equal(expected.GetRawText(), entries[0].GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task T8_SelectAcrossDifferentSectionsReturnsEntriesInRequestOrder()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "colors.recommended.color0", "structures.pane" }));

        var entries = result.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("colors.recommended.color0", entries[0].GetProperty("id").GetString());
        Assert.Equal("structures.pane", entries[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task T9_BareNameResolvesUnambiguouslyOrReportsAmbiguousEntry()
    {
        var unambiguous = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "pane" }));
        var entries = unambiguous.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("structures.pane", entries[0].GetProperty("id").GetString());

        var ambiguous = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "border" }));
        Assert.False(ambiguous.GetProperty("ok").GetBoolean());
        Assert.Equal("ambiguous-entry", ambiguous.GetProperty("code").GetString());
        var candidates = ambiguous.GetProperty("candidates").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains("structures.border", candidates);
        Assert.Contains("accepted.keys.border", candidates);
    }

    [Fact]
    public async Task T10_UnknownEntryReportsCandidatesAndReturnsNoEntries()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "structures.pain" }));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown-entry", result.GetProperty("code").GetString());
        var candidates = result.GetProperty("candidates").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains("structures.pane", candidates);
        Assert.False(result.TryGetProperty("entries", out _));
    }

    [Fact]
    public async Task T11_DriftGuardPinsTheTopLevelKeySetAndEveryStructureNameIsInTheDefault()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());
        var topLevelKeys = result.EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Equal(
            new HashSet<string> { "version", "mode", "hint", "items", "colors", "accepted", "kindSupport", "structures" },
            topLevelKeys);

        var full = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "structures" }));
        var expectedNames = full.GetProperty("structures").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToHashSet();
        var defaultNames = result.GetProperty("structures").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToHashSet();
        Assert.Equal(expectedNames, defaultNames);
    }

    [Fact]
    public async Task T12_UnknownSectionPassesThroughWholeInTheDefault()
    {
        var envelope = _cli.DefaultSchemaEnvelope;
        envelope["futureSection"] = new JsonObject { ["foo"] = "bar", ["description"] = "should survive untouched" };
        _cli.SetSchemaJson(envelope);

        var result = AsJson(await ConfigTools.GetConfigSchema());

        Assert.True(result.TryGetProperty("futureSection", out var future));
        Assert.Equal("bar", future.GetProperty("foo").GetString());
        Assert.Equal("should survive untouched", future.GetProperty("description").GetString());
    }

    [Fact]
    public async Task T13_SelectAndSectionsTogetherFailWithConflictingArgs()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "pane" }, sections: new[] { "structures" }));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("conflicting-args", result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task T14_SectionsBehavesExactlyAsBeforeAndStillRejectsUnknownSections()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "items" }));
        Assert.Equal("test", result.GetProperty("version").GetString());
        Assert.True(result.TryGetProperty("items", out _));
        Assert.False(result.TryGetProperty("colors", out _));

        var unknown = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "nope" }));
        Assert.Equal("unknown-section", unknown.GetProperty("code").GetString());
    }

    [Fact]
    public async Task T15_CliNotFoundAndSchemaUnavailableStillFireForAllThreeCallForms()
    {
        _cli.RemoveCli();

        var defaultResult = AsJson(await ConfigTools.GetConfigSchema());
        Assert.Equal("cli-not-found", defaultResult.GetProperty("code").GetString());

        var selectResult = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "pane" }));
        Assert.Equal("cli-not-found", selectResult.GetProperty("code").GetString());

        var sectionsResult = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "items" }));
        Assert.Equal("cli-not-found", sectionsResult.GetProperty("code").GetString());
    }

    [Fact]
    public async Task T16_DefaultSerializesUnderTheSizeCeiling()
    {
        var result = await ConfigTools.GetConfigSchema();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result);

        // schema-mcp-query.md §8 E5: ceiling set to ~1.5x the measured real-binary default
        // (see the response reported to the orchestrator for the measured figure). This fixture's
        // synthetic envelope is smaller than the real schema, so the ceiling here is generous —
        // it exists to catch gross re-inflation, not to pin an exact byte count.
        // schema-mcp-query.md §8 E5: ceiling set to ~1.5x the measured real-binary default
        // (11,332 B measured against the installed CLI -> ceiling ~17,000 B). This fixture's
        // synthetic envelope serializes smaller (~6,047 B) than the real schema, so the ceiling
        // here has headroom above the fixture's own baseline — it exists to catch gross
        // re-inflation (e.g. the palette omission or prose elision silently regressing), not to
        // pin an exact byte count against this synthetic data.
        Assert.True(bytes.Length < 17_000, $"default index serialized to {bytes.Length} bytes, expected under 17,000");
    }

    [Fact]
    public async Task T17_ColorsDefaultHasAllNineteenRecommendedEntriesAndAlsoAccepted()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());
        var colors = result.GetProperty("colors");

        Assert.Equal(19, colors.GetProperty("recommended").GetArrayLength());
        var names = colors.GetProperty("recommended").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains("default", names);
        Assert.Contains("dim", names);
        Assert.Contains("bold", names);
        Assert.True(colors.TryGetProperty("alsoAccepted", out _));
    }

    [Fact]
    public async Task T18_PaletteMarkerIsAnObjectWithDerivedCount()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());
        var palette = result.GetProperty("colors").GetProperty("palette");

        Assert.Equal(JsonValueKind.Object, palette.ValueKind);
        Assert.True(palette.GetProperty("omitted").GetBoolean());

        var full = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "colors" }));
        var fullPaletteLength = full.GetProperty("colors").GetProperty("palette").GetArrayLength();

        Assert.Equal(fullPaletteLength, palette.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task T19_PaletteAndPaletteEntryAreRetrievableViaSelect()
    {
        var wholePalette = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "colors.palette" }));
        var full = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "colors" }));

        var entries = wholePalette.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal(full.GetProperty("colors").GetProperty("palette").GetRawText(), entries[0].GetProperty("value").GetRawText());

        var singleEntry = AsJson(await ConfigTools.GetConfigSchema(select: new[] { "colors.palette.196" }));
        var singleEntries = singleEntry.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(singleEntries);
        Assert.Equal(196, singleEntries[0].GetProperty("value").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task T20_SectionsColorsReturnsThePaletteInFull()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(sections: new[] { "colors" }));

        Assert.Equal(256, result.GetProperty("colors").GetProperty("palette").GetArrayLength());
    }
}
