namespace ClaudeTuiLine.Tests;

public class ItemsCommandTests
{
    [Fact]
    public void ItemRegistryAll_ContainsTwentyOneUniquelyIdentifiedItemsWithNonEmptyReports()
    {
        Assert.Equal(21, ItemRegistry.All.Count);
        Assert.Equal(ItemRegistry.All.Count, ItemRegistry.All.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ItemRegistry.All, i => Assert.False(string.IsNullOrWhiteSpace(i.Reports)));
    }

    [Fact]
    public void Build_ReturnsOneRowPerRegistryItemWithVersionAndFixedKinds()
    {
        var result = ItemsCommand.Build();

        Assert.Equal(AssemblyVersionInfo.InformationalVersion, result.Version);
        Assert.Equal(ItemRegistry.All.Count, result.Items.Count);
        Assert.Equal(ItemRegistry.All.Select(i => i.Id), result.Items.Select(i => i.Id));

        Assert.Equal(new[] { "item" }, result.Kinds.Builtin.Required);
        Assert.Equal(new[] { "format", "color", "overflow", "link" }, result.Kinds.Builtin.Optional);
        Assert.Equal(new[] { "id", "from" }, result.Kinds.Derived.Required);
        Assert.Equal(new[] { "extract", "case", "format", "color", "overflow", "link" }, result.Kinds.Derived.Optional);
        Assert.Equal(new[] { "id", "command" }, result.Kinds.Command.Required);
        Assert.Equal(new[] { "shell", "ttlSeconds", "timeoutMs", "format", "color", "overflow", "link" }, result.Kinds.Command.Optional);
        Assert.Equal(new[] { "id", "parts" }, result.Kinds.Compound.Required);
        Assert.Equal(new[] { "color", "overflow", "link" }, result.Kinds.Compound.Optional);
    }

    [Fact]
    public void Build_MarksOnlyTheOptInItemsAsNonDefault()
    {
        var result = ItemsCommand.Build();

        var nonDefault = result.Items.Where(i => !i.Default).Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(new[] { "ah", "ah-short", "autocompact", "linear", "model-short", "remote-url", "repo-host" }, nonDefault);
    }

    [Fact]
    public void Build_MarksContextRateLimitsEngramAndTheAhItemsAsSemanticAndEverythingElseDecorative()
    {
        var result = ItemsCommand.Build();

        var semantic = result.Items.Where(i => i.Color == "semantic").Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(new[] { "ah", "ah-short", "context", "engram", "rate-limits" }, semantic);
        Assert.All(result.Items.Where(i => i.Color != "semantic"), i => Assert.Equal("decorative", i.Color));
    }

    [Fact]
    public void Build_RendersTheAhItemsFromTheCannedWarnEntry()
    {
        var result = ItemsCommand.Build();

        Assert.Equal("ah: 2 live · 1 out · 1 blocked", result.Items.Single(i => i.Id == "ah").Example);
        Assert.Equal("ah: 2/1 1b", result.Items.Single(i => i.Id == "ah-short").Example);
    }

    [Fact]
    public void Build_RendersANonEmptyExampleForEveryItem()
    {
        var result = ItemsCommand.Build();

        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.Example)));
    }

    [Fact]
    public void Build_GitBranchExampleIsThePlainBranchName()
    {
        var result = ItemsCommand.Build();

        // BuildGitBranch's default segment carries no glyph — Plain is exactly the branch name.
        var gitBranch = result.Items.Single(i => i.Id == "git-branch");
        Assert.Equal("feat/eng-1234", gitBranch.Example);
    }

    [Fact]
    public void Build_LinearExampleIsTheExtractedUppercasedTicketId()
    {
        // item-specific-config.md §13.6 T19.
        var result = ItemsCommand.Build();

        var linear = result.Items.Single(i => i.Id == "linear");
        Assert.Equal("ENG-1234", linear.Example);
    }

    [Fact]
    public void Build_RepoHostExampleIsTheFixtureHost()
    {
        var result = ItemsCommand.Build();

        var repoHost = result.Items.Single(i => i.Id == "repo-host");
        Assert.Equal("the host the workspace repo lives on, from the session payload rather than a git probe", repoHost.Reports);
        Assert.Equal("github.com", repoHost.Example);
    }

    [Fact]
    public void Build_SerializesToJsonWithTheSpecifiedPropertyNames()
    {
        var result = ItemsCommand.Build();

        var json = System.Text.Json.JsonSerializer.Serialize(result, ItemsJsonContext.Default.ItemsResultJson);

        Assert.Contains("\"version\":", json);
        Assert.Contains("\"items\":", json);
        Assert.Contains("\"kinds\":", json);
        Assert.Contains("\"reports\":", json);
        Assert.Contains("\"color\":", json);
        Assert.Contains("\"default\":", json);
        Assert.Contains("\"example\":", json);
        Assert.Contains("\"builtin\":", json);
        Assert.Contains("\"derived\":", json);
        Assert.Contains("\"command\":", json);
        Assert.Contains("\"compound\":", json);
        Assert.Contains("\"required\":", json);
        Assert.Contains("\"optional\":", json);
    }

    [Fact]
    public void RenderPlainText_FormatsEachRowWithColumnsPaddedToTheWidestValueAcrossAllItems()
    {
        var result = ItemsCommand.Build();
        var idWidth = result.Items.Max(i => i.Id.Length);
        var exampleWidth = result.Items.Max(i => i.Example.Length);

        var text = ItemsCommand.RenderPlainText(result);

        foreach (var item in result.Items)
        {
            var expectedRow = $"  {item.Id.PadRight(idWidth)}  {item.Example.PadRight(exampleWidth)}  {item.Reports}";
            Assert.Contains(expectedRow, text);
        }
    }

    [Fact]
    public void RenderPlainText_ListsDefaultItemsBeforeOptInItemsUnderLabeledHeaders()
    {
        var result = ItemsCommand.Build();
        var idWidth = result.Items.Max(i => i.Id.Length);
        var exampleWidth = result.Items.Max(i => i.Example.Length);
        var text = ItemsCommand.RenderPlainText(result);

        var defaultHeaderIndex = text.IndexOf("Default items — rendered unless you remove them:", StringComparison.Ordinal);
        var optInHeaderIndex = text.IndexOf("Opt-in items — rendered only where you place them:", StringComparison.Ordinal);
        var pointerLineIndex = text.IndexOf("Item kinds: builtin, command, derived, compound. Run with --json for the schema of each.", StringComparison.Ordinal);

        Assert.True(defaultHeaderIndex >= 0);
        Assert.True(optInHeaderIndex > defaultHeaderIndex);
        Assert.True(pointerLineIndex > optInHeaderIndex);

        foreach (var item in result.Items)
        {
            var row = $"  {item.Id.PadRight(idWidth)}  {item.Example.PadRight(exampleWidth)}  {item.Reports}";
            var rowIndex = text.IndexOf(row, StringComparison.Ordinal);
            var (lower, upper) = item.Default ? (defaultHeaderIndex, optInHeaderIndex) : (optInHeaderIndex, pointerLineIndex);
            Assert.InRange(rowIndex, lower, upper);
        }
    }

    [Fact]
    public void RenderPlainText_EndsWithTheItemKindsPointerLine()
    {
        var text = ItemsCommand.RenderPlainText(ItemsCommand.Build());
        Assert.EndsWith("Item kinds: builtin, command, derived, compound. Run with --json for the schema of each.", text, StringComparison.Ordinal);
    }
}
