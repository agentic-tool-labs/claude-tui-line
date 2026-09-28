namespace ClaudeTuiLine.Tests;

public class AcceptedCommandTests
{
    [Fact]
    public void Build_ReturnsExactlyTwelveKeysForTheElevenEnumerableKindsPlusSize()
    {
        var result = AcceptedCommand.Build();

        Assert.Equal(
            new[] { "border.style", "colorSystem", "split", "valign", "align", "selfAlign", "titleAlign", "distribute", "overflow", "case", "height", "size" },
            result.Keys.Select(k => k.Key));
    }

    [Fact]
    public void Build_CarriesTheSameVersionAsItemsCommand()
    {
        Assert.Equal(AssemblyVersionInfo.InformationalVersion, AcceptedCommand.Build().Version);
    }

    [Fact]
    public void Build_EveryRowSatisfiesTheAcceptedOrAlsoAcceptedInvariant()
    {
        var result = AcceptedCommand.Build();

        Assert.All(result.Keys, key => Assert.True(
            (key.Accepted is { Count: > 0 }) || !string.IsNullOrEmpty(key.AlsoAccepted),
            $"key '{key.Key}' has neither accepted nor alsoAccepted"));
    }

    [Fact]
    public void Build_SizeRowHasNoAcceptedListAndAByteIdenticalAlsoAcceptedString()
    {
        var result = AcceptedCommand.Build();
        var size = result.Keys.Single(k => k.Key == "size");

        Assert.Null(size.Accepted);
        Assert.Equal(ConfigChecker.FormatAccepted(ConfigChecker.SizeValues), size.AlsoAccepted);
    }

    // §1.1.3 verification 6/what-must-not-change item 2: every non-size row must read the exact
    // registry object its parser exposes, not a hand-copied list — Assert.Same, not SequenceEqual,
    // is what proves that, since AcceptedTokens is a get-only property computed once at type load.
    [Fact]
    public void Build_EveryNonSizeRowIsTheSameObjectAsItsParsersAcceptedTokens()
    {
        var result = AcceptedCommand.Build();
        var byKey = result.Keys.ToDictionary(k => k.Key, k => k.Accepted);

        Assert.Same(BorderStyleParsing.AcceptedTokens, byKey["border.style"]);
        Assert.Same(ConfigLoader.ColorSystemAcceptedTokens, byKey["colorSystem"]);
        Assert.Same(ConfigLoader.SplitAcceptedTokens, byKey["split"]);
        Assert.Same(PaneValignParsing.AcceptedTokens, byKey["valign"]);
        Assert.Same(PaneAlignParsing.AcceptedTokens, byKey["align"]);
        Assert.Same(PaneDistributeParsing.AcceptedTokens, byKey["distribute"]);
        Assert.Same(OverflowModeParsing.AcceptedTokens, byKey["overflow"]);
        Assert.Same(ItemValueResolver.CaseAcceptedTokens, byKey["case"]);
        Assert.Same(PaneHeightParsing.AcceptedTokens, byKey["height"]);
    }

    [Fact]
    public void ValidateInvariant_ThrowsWhenARowHasNeitherAcceptedNorAlsoAccepted()
    {
        var keys = new[]
        {
            new AcceptedKeyJson("overflow", OverflowModeParsing.AcceptedTokens, null),
            new AcceptedKeyJson("broken", null, null),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => AcceptedCommand.ValidateInvariant(keys));
        Assert.Contains("broken", ex.Message);
    }

    [Fact]
    public void ValidateInvariant_ThrowsWhenAcceptedIsAnEmptyListAndAlsoAcceptedIsAnEmptyString()
    {
        var keys = new[] { new AcceptedKeyJson("broken", Array.Empty<string>(), "") };

        Assert.Throws<InvalidOperationException>(() => AcceptedCommand.ValidateInvariant(keys));
    }

    [Fact]
    public void Build_SerializesToJsonWithTheSpecifiedPropertyNames()
    {
        var result = AcceptedCommand.Build();
        var json = System.Text.Json.JsonSerializer.Serialize(result, AcceptedJsonContext.Default.AcceptedResultJson);

        Assert.Contains("\"version\":", json);
        Assert.Contains("\"keys\":", json);
        Assert.Contains("\"key\":", json);
        Assert.Contains("\"accepted\":", json);
        Assert.Contains("\"alsoAccepted\":", json);
    }
}
