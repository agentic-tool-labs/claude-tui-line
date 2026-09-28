using System.Text.Json;

namespace ClaudeTuiLine.Tests;

// SPEC-V2-FRAMEWORK.md §9.4/§9.3: RunPreview (Program.cs) has no public entry point reflection can
// address (see PreviewJsonRowsTests.cs's header comment for why), so — same as that file — these
// tests exercise the built CLI as a subprocess. PreviewJsonRowsTests.cs only covers the --json
// happy path against a valid/empty config with synthetic stdin. This file closes the remaining
// RunPreview paths that had no test entry point at all: the config-unreadable exit-3 gate (both
// --json and bare, both parse-error and missing-file), the bare (non-JSON) render+diagnostics
// output, and the --columns/COLUMNS/default-100 fallback chain.
[Collection("PreviewCli")]
public class PreviewCliTests
{
    [Fact]
    public void Preview_ConfigParseError_Json_ReturnsExit3WithConfigUnreadablePayload()
    {
        var configPath = WriteTempConfig("{ this is not valid json");
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--json", "--config", configPath);

            Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stderr: {stderr}");
            using var doc = PreviewCliRunner.ParseJsonOrFail((exitCode, stdout, stderr));
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("config-unreadable", root.GetProperty("code").GetString());
            Assert.Equal(configPath, root.GetProperty("path").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ConfigParseError_Bare_ReturnsExit3WithStderrMessage()
    {
        var configPath = WriteTempConfig("{ this is not valid json");
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--config", configPath);

            Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stdout: {stdout}");
            Assert.Empty(stdout);
            Assert.Contains(configPath, stderr);
            Assert.StartsWith("claude-tui-line:", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ExplicitConfigMissingFile_Json_ReturnsExit3WithNoSuchFileMessage()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"preview-cli-missing-{Guid.NewGuid():N}.json");

        var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--json", "--config", configPath);

        Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stderr: {stderr}");
        using var doc = PreviewCliRunner.ParseJsonOrFail((exitCode, stdout, stderr));
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("config-unreadable", root.GetProperty("code").GetString());
        Assert.Equal(configPath, root.GetProperty("path").GetString());
        Assert.Contains("no such file", root.GetProperty("message").GetString());
    }

    [Fact]
    public void Preview_ExplicitConfigMissingFile_Bare_ReturnsExit3WithStderrMessage()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"preview-cli-missing-{Guid.NewGuid():N}.json");

        var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--config", configPath);

        Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stdout: {stdout}");
        Assert.Empty(stdout);
        Assert.Contains(configPath, stderr);
        Assert.Contains("no such file", stderr);
    }

    [Fact]
    public void Preview_Bare_WritesRenderedRowsToStdoutAndDiagnosticsToStderr()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.False(string.IsNullOrWhiteSpace(stdout));
            Assert.Contains("preview at 60 columns (from --columns)", stderr);
            Assert.Contains("no stdin given", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ColumnsOmitted_FallsBackToColumnsEnvVarWhenSet()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, _, stderr) = PreviewCliRunner.Run(new Dictionary<string, string?> { ["COLUMNS"] = "45" }, "--preview", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("preview at 45 columns (from COLUMNS)", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ColumnsOmitted_FallsBackToDefaultOf100WhenNoColumnsEnvVar()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, _, stderr) = PreviewCliRunner.Run(new Dictionary<string, string?> { ["COLUMNS"] = null }, "--preview", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("preview at 100 columns (from the default of 100)", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    // §9.3: real stdin, not the synthetic fixture — PreviewCliRunner.Run above always closes stdin
    // immediately, so it can only ever exercise the usedSynthetic branch. ParseInput/StatusInput (Program.cs)
    // had no test entry point at all before this.
    [Fact]
    public void Preview_RealStdinJson_IsParsedAndDrivesRendering()
    {
        var configPath = WriteTempConfig("""
        {
          "surface": { "pane": { "items": [ { "item": "model" } ] } }
        }
        """);
        try
        {
            var stdin = """{ "model": { "display_name": "Test-Model-XYZ" } }""";
            var (exitCode, stdout, stderr) = PreviewCliRunner.RunWithStdin(stdin, "--preview", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("Test-Model-XYZ", stdout);
            Assert.DoesNotContain("no stdin given", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    // ParseInput (Program.cs) swallows a JsonException and returns an empty StatusInput rather
    // than throwing or falling back to the synthetic fixture — malformed stdin is still "real"
    // stdin (usedSynthetic is decided by whether raw stdin was blank, not by parse success).
    [Fact]
    public void Preview_RealStdinMalformedJson_ParsesToEmptyStatusInputRatherThanSynthetic()
    {
        var configPath = WriteTempConfig("""
        {
          "surface": { "pane": { "items": [ { "item": "model" } ] } }
        }
        """);
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.RunWithStdin("{ this is not valid json", "--preview", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.DoesNotContain("no stdin given", stderr);
            Assert.DoesNotContain("Claude Sonnet 5", stdout);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    // SPEC-103-showlabel-fix-and-labelcolor.md §E.1: the regression guard for the actual bug —
    // itemSettings.outputStyle.showLabel was silently dropped at the synthetic-fixture call site
    // (SyntheticFixture.CreateItemContext()), so the segment-builder-level tests (which inject
    // settings directly) passed while the real --preview surface stayed broken.
    [Fact]
    public void Preview_Synthetic_ShowLabelFalse_OmitsLabelPrefix()
    {
        var configPath = WriteTempConfig("""{"itemSettings":{"outputStyle":{"showLabel":false}}}""");
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("Explanatory", stdout);
            Assert.DoesNotContain("style:", stdout);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_Synthetic_ShowLabelOmittedOrTrue_KeepsLabelPrefix()
    {
        var omittedPath = WriteTempConfig("{}");
        var truePath = WriteTempConfig("""{"itemSettings":{"outputStyle":{"showLabel":true}}}""");
        try
        {
            var omitted = PreviewCliRunner.Run("--preview", "--columns", "60", "--config", omittedPath);
            var trueVal = PreviewCliRunner.Run("--preview", "--columns", "60", "--config", truePath);

            Assert.True(omitted.ExitCode == 0, $"expected exit 0, got {omitted.ExitCode}. stderr: {omitted.StdErr}");
            Assert.True(trueVal.ExitCode == 0, $"expected exit 0, got {trueVal.ExitCode}. stderr: {trueVal.StdErr}");
            Assert.Contains("style:Explanatory", omitted.StdOut);
            Assert.Equal(omitted.StdOut, trueVal.StdOut);
        }
        finally
        {
            File.Delete(omittedPath);
            File.Delete(truePath);
        }
    }

    static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"preview-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

}
