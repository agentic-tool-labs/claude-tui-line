namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4.2/§4.2.1: <see cref="PlaceholderTemplate"/>'s shared grammar and
/// <see cref="ArgvPlaceholders"/>'s expansion of it — non-shell substitutes directly into argv,
/// shell:true exports only-referenced values into the environment, and both preserve arity for a
/// known-but-empty source.
/// </summary>
public class ArgvPlaceholderTests
{
    [Fact]
    public void NonShell_SubstitutesKnownIdIntoArgv()
    {
        var values = new Dictionary<string, string?> { ["agent"] = "opus", ["model"] = "claude" };

        var expansion = ArgvPlaceholders.Expand(new[] { "tool", "{agent}", "{model}" }, shell: false, values);

        Assert.Equal(new[] { "tool", "opus", "claude" }, expansion.Argv);
        Assert.Empty(expansion.ExportedEnv);
        Assert.Equal(new[] { "agent", "model" }, expansion.ReferencedIds.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void NonShell_KnownButEmptyId_SubstitutesEmptyStringAndPreservesArity()
    {
        var values = new Dictionary<string, string?> { ["git-branch"] = null };

        var expansion = ArgvPlaceholders.Expand(new[] { "mytool", "--branch", "{git-branch}", "--format", "json" }, shell: false, values);

        Assert.Equal(new[] { "mytool", "--branch", "", "--format", "json" }, expansion.Argv);
    }

    [Fact]
    public void NonShell_UnknownId_SubstitutesEmptyStringRatherThanDroppingTheArgvEntry()
    {
        var expansion = ArgvPlaceholders.Expand(new[] { "tool", "{unknown-id}" }, shell: false, new Dictionary<string, string?>());

        Assert.Equal(new[] { "tool", "" }, expansion.Argv);
    }

    [Fact]
    public void NonShell_MixedLiteralAndPlaceholderInOneArgvElement_ConcatenatesIntoOneElement()
    {
        var values = new Dictionary<string, string?> { ["git-branch"] = "main" };

        var expansion = ArgvPlaceholders.Expand(new[] { "--branch={git-branch}" }, shell: false, values);

        Assert.Equal(new[] { "--branch=main" }, expansion.Argv);
    }

    [Fact]
    public void NonShell_BareSelfReference_SubstitutesEmptyAndIsNotAReferencedId()
    {
        var expansion = ArgvPlaceholders.Expand(new[] { "tool", "{}" }, shell: false, new Dictionary<string, string?>());

        Assert.Equal(new[] { "tool", "" }, expansion.Argv);
        Assert.Empty(expansion.ReferencedIds);
    }

    [Fact]
    public void NonShell_LiteralBracesThatAreNotAnIdCharset_PassThroughUnchanged()
    {
        var expansion = ArgvPlaceholders.Expand(new[] { "{name: .name}" }, shell: false, new Dictionary<string, string?>());

        Assert.Equal(new[] { "{name: .name}" }, expansion.Argv);
        Assert.Empty(expansion.ReferencedIds);
    }

    [Fact]
    public void NonShell_EscapedBraces_ProduceLiteralBraces()
    {
        var expansion = ArgvPlaceholders.Expand(new[] { "{{a}}" }, shell: false, new Dictionary<string, string?>());

        Assert.Equal(new[] { "{a}" }, expansion.Argv);
    }

    [Fact]
    public void Shell_DoesNotSubstituteIntoTheCommandString()
    {
        var values = new Dictionary<string, string?> { ["git-branch"] = "; rm -rf ~" };

        var expansion = ArgvPlaceholders.Expand(new[] { "echo {git-branch}" }, shell: true, values);

        Assert.Equal(new[] { "echo {git-branch}" }, expansion.Argv);
    }

    [Fact]
    public void Shell_ExportsOnlyReferencedIds()
    {
        var values = new Dictionary<string, string?> { ["agent"] = "opus", ["model"] = "claude", ["remote-url"] = "https://example.com" };

        var expansion = ArgvPlaceholders.Expand(new[] { "echo \"$CLAUDE_TUI_LINE_VAL_AGENT\"", "{agent}" }, shell: true, values);

        Assert.Equal(new[] { "CLAUDE_TUI_LINE_VAL_AGENT" }, expansion.ExportedEnv.Keys);
        Assert.Equal("opus", expansion.ExportedEnv["CLAUDE_TUI_LINE_VAL_AGENT"]);
    }

    [Fact]
    public void Shell_KnownButEmptyId_ExportsEmptyRatherThanLeavingItUnset()
    {
        var values = new Dictionary<string, string?> { ["git-branch"] = null };

        var expansion = ArgvPlaceholders.Expand(new[] { "{git-branch}" }, shell: true, values);

        Assert.True(expansion.ExportedEnv.ContainsKey("CLAUDE_TUI_LINE_VAL_GIT_BRANCH"));
        Assert.Equal("", expansion.ExportedEnv["CLAUDE_TUI_LINE_VAL_GIT_BRANCH"]);
    }

    [Theory]
    [InlineData("agent", "CLAUDE_TUI_LINE_VAL_AGENT")]
    [InlineData("agent-short", "CLAUDE_TUI_LINE_VAL_AGENT_SHORT")]
    [InlineData("agent.short", "CLAUDE_TUI_LINE_VAL_AGENT_SHORT")]
    public void EnvVarNameFor_UppercasesAndMangles(string id, string expected)
    {
        Assert.Equal(expected, ArgvPlaceholders.EnvVarNameFor(id));
    }

    [Fact]
    public void ReferencedIds_IsDistinctAcrossMultipleArgvElements()
    {
        var ids = ArgvPlaceholders.ReferencedIds(new[] { "{agent}", "prefix-{agent}-suffix", "{model}" });

        Assert.Equal(new[] { "agent", "model" }, ids.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void HasSelfReference_DetectsBareBraces()
    {
        Assert.True(ArgvPlaceholders.HasSelfReference(new[] { "tool", "{}" }));
        Assert.False(ArgvPlaceholders.HasSelfReference(new[] { "tool", "{other-id}" }));
    }
}

public class PlaceholderTemplateTests
{
    [Fact]
    public void Tokenize_PlainLiteral_YieldsOneLiteralToken()
    {
        var tokens = PlaceholderTemplate.Tokenize("plain text").ToList();

        var token = Assert.Single(tokens);
        Assert.False(token.IsPlaceholder);
        Assert.Equal("plain text", token.Text);
    }

    [Fact]
    public void Tokenize_NamedPlaceholder_YieldsPlaceholderToken()
    {
        var tokens = PlaceholderTemplate.Tokenize("{other-id}").ToList();

        var token = Assert.Single(tokens);
        Assert.True(token.IsPlaceholder);
        Assert.Equal("other-id", token.Text);
    }

    [Fact]
    public void Tokenize_BareBraces_YieldsEmptyPlaceholderToken()
    {
        var tokens = PlaceholderTemplate.Tokenize("{}").ToList();

        var token = Assert.Single(tokens);
        Assert.True(token.IsPlaceholder);
        Assert.Equal("", token.Text);
    }

    [Fact]
    public void Tokenize_NonIdCharsetBody_IsLiteral()
    {
        var tokens = PlaceholderTemplate.Tokenize("jq '{name: .name}'").ToList();

        Assert.All(tokens, t => Assert.False(t.IsPlaceholder));
        Assert.Equal("jq '{name: .name}'", string.Concat(tokens.Select(t => t.Text)));
    }

    [Fact]
    public void Tokenize_DoubleBraces_AreLiteralSingleBraces()
    {
        var tokens = PlaceholderTemplate.Tokenize("{{a}}").ToList();

        Assert.All(tokens, t => Assert.False(t.IsPlaceholder));
        Assert.Equal("{a}", string.Concat(tokens.Select(t => t.Text)));
    }
}
