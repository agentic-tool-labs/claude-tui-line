using ClaudeTuiLine;
using ClaudeTuiLineShared;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

// Shares CalibrationEnvVarCollection with CalibrateTests/CalibrationPromptTests: all three mutate
// the process-wide CLAUDE_TUI_LINE_CONFIG/HOME env vars, so they must never run concurrently.
[Collection(nameof(CalibrationEnvVarCollection))]
public class ConfigTests
{
    private static string WriteTempConfig(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void MissingFile_UsesDefaults()
    {
        var resolved = ConfigLoader.LoadBorderConfig("/nonexistent/claude-tui-line.json");

        Assert.Equal(new ColorResolution.ColorExpr.Literal("grey"), resolved.BorderColor);
        Assert.Same(BoxBorder.Rounded, resolved.Style);
    }

    [Fact]
    public void InvalidJson_UsesDefaults()
    {
        var path = WriteTempConfig("{ not valid json");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);

            Assert.Equal(new ColorResolution.ColorExpr.Literal("grey"), resolved.BorderColor);
            Assert.Same(BoxBorder.Rounded, resolved.Style);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PartialObject_OnlyColorSet_OtherKeysDefault()
    {
        var path = WriteTempConfig("""{ "border": { "color": "red" } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);

            Assert.Equal(new ColorResolution.ColorExpr.Literal("red"), resolved.BorderColor);
            Assert.Same(BoxBorder.Rounded, resolved.Style); // style/enabled both defaulted
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BadColorString_FallsBackToGrey()
    {
        var path = WriteTempConfig("""{ "border": { "color": "not-a-real-color-xyz" } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            var style = ColorResolution.ResolveBorderColor(resolved.BorderColor, new Dictionary<string, string?>(), new Dictionary<string, ColorResolution.ColorRule>());
            Assert.Equal(Color.Grey, style.Foreground);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownStyle_FallsBackToRounded()
    {
        var path = WriteTempConfig("""{ "border": { "style": "triangles" } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            Assert.Same(BoxBorder.Rounded, resolved.Style);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledFalse_BorderlessRegardlessOfStyle()
    {
        var path = WriteTempConfig("""{ "border": { "enabled": false, "style": "double" } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            Assert.Null(resolved.Style);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StyleNone_BorderlessEvenWhenEnabledTrue()
    {
        var path = WriteTempConfig("""{ "border": { "enabled": true, "style": "none" } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            Assert.Null(resolved.Style);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FullySpecified_AllValuesHonored()
    {
        var path = WriteTempConfig("""{ "border": { "enabled": true, "color": "blue", "style": "heavy" } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            Assert.Equal(new ColorResolution.ColorExpr.Literal("blue"), resolved.BorderColor);
            Assert.Same(BoxBorder.Heavy, resolved.Style);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // SPEC-98: layout.chromeReserve, default 4.

    [Fact]
    public void ChromeReserve_Absent_DefaultsToFour()
    {
        var path = WriteTempConfig("""{ "border": { "enabled": false } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            Assert.Equal(4, resolved.ChromeReserve);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChromeReserve_ExplicitValue_IsHonored()
    {
        var path = WriteTempConfig("""{ "layout": { "chromeReserve": 1 } }""");
        try
        {
            var resolved = ConfigLoader.LoadBorderConfig(path);
            Assert.Equal(1, resolved.ChromeReserve);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChromeReserve_MissingFile_DefaultsToFour()
    {
        var resolved = ConfigLoader.LoadBorderConfig("/nonexistent/claude-tui-line.json");
        Assert.Equal(4, resolved.ChromeReserve);
    }

    // SPEC.md §6b "Config path resolution": ConfigLoader.ResolveConfigPath owns the rule for
    // finding the config file. These test the rule itself via its pure (override, home) overload
    // — no process-env mutation needed — rather than re-exercising LoadBorderConfig's file-
    // reading mechanics, which is already covered above.

    [Fact]
    public void ResolveConfigPath_OverrideSet_ReturnsOverrideVerbatim()
    {
        var resolved = ConfigPath.ResolveConfigPath(configPathOverride: "/some/explicit/path.json", home: "/irrelevant/home");
        Assert.Equal("/some/explicit/path.json", resolved);
    }

    [Fact]
    public void ResolveConfigPath_OverrideEmpty_IsTreatedAsUnset()
    {
        var resolved = ConfigPath.ResolveConfigPath(configPathOverride: "", home: "/tmp/some-home");
        Assert.Equal(Path.Combine("/tmp/some-home", ".claude", "claude-tui-line.json"), resolved);
    }

    [Fact]
    public void ResolveConfigPath_OverrideNull_FallsBackToHomePath()
    {
        var resolved = ConfigPath.ResolveConfigPath(configPathOverride: null, home: "/tmp/some-home");
        Assert.Equal(Path.Combine("/tmp/some-home", ".claude", "claude-tui-line.json"), resolved);
    }

    [Fact]
    public void ConfigPathOverride_Unset_FallsBackToHomePath()
    {
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var originalOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");
        var tempHome = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tempHome, ".claude"));
            File.WriteAllText(
                Path.Combine(tempHome, ".claude", "claude-tui-line.json"),
                """{ "border": { "enabled": true, "color": "blue", "style": "heavy" } }""");
            Environment.SetEnvironmentVariable("HOME", tempHome);
            Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", null);

            var resolved = ConfigLoader.LoadBorderConfig(ConfigPath.ResolveConfigPath());

            Assert.Equal(new ColorResolution.ColorExpr.Literal("blue"), resolved.BorderColor);
            Assert.Same(BoxBorder.Heavy, resolved.Style);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", originalHome);
            Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", originalOverride);
            Directory.Delete(tempHome, recursive: true);
        }
    }
}
