using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.7: the .csproj's &lt;Version&gt; is the source of truth for what the
/// binary reports, and .claude-plugin/plugin.json is a hand-written manifest that cannot be
/// generated from it — a second home for the same number that "will drift, silently, because
/// nothing reads both." This is the mitigation §9.7 calls for: a test that reads both and fails
/// the build the moment they disagree, rather than a user reporting a version that does not
/// correspond to anything. Reads the real .claude-plugin/plugin.json rather than a copy under
/// fixtures/, since a fixture copy would itself be the "third home" §9.7 rules out.
/// </summary>
public class AssemblyVersionInfoTests
{
    [Fact]
    public void AssemblyVersion_MatchesPluginJsonVersion()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PluginJsonPath()));
        var pluginJsonVersion = document.RootElement.GetProperty("version").GetString();

        Assert.Equal(pluginJsonVersion, AssemblyVersionInfo.InformationalVersion);
    }

    private static string PluginJsonPath([CallerFilePath] string testFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFilePath)!, "..", "..", ".claude-plugin", "plugin.json"));
}
