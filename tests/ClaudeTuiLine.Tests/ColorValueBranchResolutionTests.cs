namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-44-color-token-in-rule-branches.md §10 items 1-3: the runtime-resolution side of a rule
/// branch's <c>@token</c> colour. §4.2's <c>ColorResolution.Resolve</c> is the single entry point
/// exercised here directly, mirroring <see cref="BorderColorResolutionTests"/>'s convention of
/// calling into <see cref="ColorResolution"/> rather than round-tripping through JSON config
/// parsing. The diagnostic-emission side (does <c>--check</c> warn) lives in
/// <see cref="ConfigCheckTests"/>.
/// </summary>
public class ColorValueBranchResolutionTests
{
    [Fact]
    public void ConstantTokenInThresholdBranch_ResolvesToItsLiteralColour()
    {
        var tokens = new Dictionary<string, ColorResolution.ColorRule>
        {
            ["accent"] = new ColorResolution.ColorRule(null, null, new ColorResolution.ColorValue.Literal("red")),
        };
        var rule = new ColorResolution.ColorRule(
            Thresholds: new[] { new ColorResolution.ThresholdRule(50, new ColorResolution.ColorValue.TokenRef("accent")) },
            Match: null,
            Default: new ColorResolution.ColorValue.Literal("grey"),
            From: "cwd");
        var values = new Dictionary<string, string?> { ["cwd"] = "75" };

        var result = ColorResolution.Resolve(new ColorResolution.ColorExpr.Inline(rule), values, tokens);

        Assert.Equal("red", result);
    }

    [Fact]
    public void NonConstantTokenInThresholdBranch_ResolvesToNoColour()
    {
        var tokens = new Dictionary<string, ColorResolution.ColorRule>
        {
            ["busy"] = new ColorResolution.ColorRule(
                Thresholds: new[] { new ColorResolution.ThresholdRule(50, new ColorResolution.ColorValue.Literal("olive")) },
                Match: null,
                Default: null,
                From: "cwd"),
        };
        var rule = new ColorResolution.ColorRule(
            Thresholds: new[] { new ColorResolution.ThresholdRule(50, new ColorResolution.ColorValue.TokenRef("busy")) },
            Match: null,
            Default: new ColorResolution.ColorValue.Literal("grey"),
            From: "cwd");
        var values = new Dictionary<string, string?> { ["cwd"] = "75" };

        var result = ColorResolution.Resolve(new ColorResolution.ColorExpr.Inline(rule), values, tokens);

        Assert.Null(result);
    }

    [Fact]
    public void ChainedTokenInThresholdBranch_DoesNotFollowTheSecondHop()
    {
        var tokens = new Dictionary<string, ColorResolution.ColorRule>
        {
            ["a"] = new ColorResolution.ColorRule(null, null, new ColorResolution.ColorValue.TokenRef("b")),
            ["b"] = new ColorResolution.ColorRule(null, null, new ColorResolution.ColorValue.Literal("red")),
        };
        var rule = new ColorResolution.ColorRule(
            Thresholds: new[] { new ColorResolution.ThresholdRule(50, new ColorResolution.ColorValue.TokenRef("a")) },
            Match: null,
            Default: new ColorResolution.ColorValue.Literal("grey"),
            From: "cwd");
        var values = new Dictionary<string, string?> { ["cwd"] = "75" };

        var result = ColorResolution.Resolve(new ColorResolution.ColorExpr.Inline(rule), values, tokens);

        Assert.Null(result);
    }
}
