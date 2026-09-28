using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-47: the config-resolution catch-all's diagnostic channel. §5.1 tests 3, 7, and 8 — the
/// reachable tier now that ConfigResolution is a proper internal class (§5.0.2). Tests 1 and 2
/// (forcing ResolveTopLevel/ResolveRootPane to throw) remain struck per §5.2 — no seam exists to
/// stage that throw, and none is being added (§5.0.0/§7).
/// </summary>
public class ConfigResolutionTests
{
    // §5.1 test 3 — the highest-value test in the spec: guards a defect a real exception message
    // can cause today, since .NET does not promise Exception.Message is newline-free.
    [Fact]
    public void ComposeResolutionFailureReason_ProducesNoNewline()
    {
        var ex = new InvalidOperationException("first line\nsecond line\r\nthird line");

        var reason = ConfigResolution.ComposeResolutionFailureReason(ex);

        Assert.DoesNotContain('\n', reason);
        Assert.DoesNotContain('\r', reason);
    }

    [Fact]
    public void ComposeResolutionFailureReason_NamesTheFailureClassAndCarriesTheMessageVerbatim()
    {
        var ex = new InvalidOperationException("something specific broke");

        var reason = ConfigResolution.ComposeResolutionFailureReason(ex);

        Assert.Equal("config could not be resolved: something specific broke", reason);
    }

    // §5.1 test 7 — the two reachable failure paths (parse error, asserted missing file) each
    // produce a non-null UnreadableReason with the expected protected length. The third
    // (resolution throw) is unreachable per §1.3 and stays in §5.2.
    [Fact]
    public void LoadRenderConfig_ParseError_ReturnsNonNullReasonWithLineProtectedLength()
    {
        var path = WriteTempFile("{ this is not valid json");
        try
        {
            var (_, _, configPath, unreadableReason, protectedLength) = ConfigResolution.LoadRenderConfig(path);

            Assert.Equal(path, configPath);
            Assert.NotNull(unreadableReason);
            // A JSON parse error carries a "line N" prefix (§9.2.2) protected at that prefix's
            // length, not zero — this pins that ComposeUnreadableReason still runs unmodified
            // through the new call site.
            Assert.StartsWith("line ", unreadableReason);
            Assert.True(protectedLength > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadRenderConfig_AssertedMissingFile_ReturnsNoSuchFileReasonAtProtectedLengthZero()
    {
        var path = Path.Combine(Path.GetTempPath(), $"config-resolution-missing-{Guid.NewGuid():N}.json");

        var (_, _, configPath, unreadableReason, protectedLength) = ConfigResolution.LoadRenderConfig(path);

        Assert.Equal(path, configPath);
        Assert.Equal("no such file", unreadableReason);
        Assert.Equal(0, protectedLength);
    }

    // §5.1 test 8 — the one test Revision 3 struck that the refactor genuinely restores: this was
    // previously a code-review-only claim, now assertable directly via FallbackResult.
    [Fact]
    public void FallbackResult_ResolvedConfigAndPaneMatchMainsUnchangedOutput()
    {
        var (topLevel, rootPane, _, unreadableReason, protectedLength) =
            ConfigResolution.FallbackResult("/some/path.json", "some reason", 3);

        Assert.Equal(new ColorResolution.ColorExpr.Literal("grey"), topLevel.BorderColor);
        Assert.Same(BoxBorder.Rounded, topLevel.Style);
        Assert.Equal(PaneBorderEdges.All, topLevel.Edges);
        Assert.Equal(ConfigLoader.DefaultChromeReserve, topLevel.ChromeReserve);
        Assert.Equal(ColorSystemSupport.Standard, topLevel.ColorSystem);
        Assert.Empty(topLevel.Colors);

        Assert.Equal(PaneSplit.None, rootPane.Split);
        Assert.Empty(rootPane.Children);
        Assert.Equal("auto", rootPane.Size);
        Assert.Null(rootPane.MinSize);
        Assert.Equal(ConfigLoader.DefaultEllipsis, rootPane.Ellipsis);
        Assert.Empty(rootPane.Items);

        Assert.Equal("some reason", unreadableReason);
        Assert.Equal(3, protectedLength);
    }

    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"config-resolution-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
