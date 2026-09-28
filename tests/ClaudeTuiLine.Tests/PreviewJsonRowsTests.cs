using System.Text.Json;

namespace ClaudeTuiLine.Tests;

// SPEC-V2-FRAMEWORK.md §9.3.4/§9.6: rows[].text is ANSI-stripped, rows[].width always equals
// text.Length, and rows[].contentWidth is present only on content rows of a single-pane (bordered)
// render and absent — not null, omitted from the JSON entirely — on that pane's top/bottom border
// lines. Unlike ItemsCommand/ColorsCommand, --preview's JSON is built inside RunPreview, a
// top-level local function in Program.cs with no public, directly callable entry point (top-level
// statements compile local functions in a way reflection can't cleanly address even with
// InternalsVisibleTo), so these tests exercise the built CLI as a subprocess — the same mechanism
// tools/check-examples.sh already uses to check this document's JSON shapes against real output.
[Collection("PreviewCli")]
public class PreviewJsonRowsTests
{
    [Fact]
    public void Preview_SingleBorderedPane_TopAndBottomRowsOmitContentWidthAndContentRowsIncludeIt()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            using var doc = PreviewCliRunner.ParseJsonOrFail((exitCode, stdout, stderr));
            var root = doc.RootElement;

            Assert.Equal(60, root.GetProperty("columns").GetInt32());

            var rowList = root.GetProperty("rows").EnumerateArray().ToList();
            Assert.True(rowList.Count >= 3, $"expected a bordered pane to report a top row, at least one content row, and a bottom row; got {rowList.Count}. stdout: {stdout}");

            foreach (var row in rowList)
            {
                var text = row.GetProperty("text").GetString();
                Assert.Equal(text!.Length, row.GetProperty("width").GetInt32());
            }

            var topRow = rowList[0];
            var bottomRow = rowList[^1];
            Assert.False(topRow.TryGetProperty("contentWidth", out _), "a border row must omit contentWidth entirely, not carry it as null");
            Assert.False(bottomRow.TryGetProperty("contentWidth", out _), "a border row must omit contentWidth entirely, not carry it as null");

            foreach (var contentRow in rowList.Skip(1).SkipLast(1))
            {
                Assert.True(contentRow.TryGetProperty("contentWidth", out var contentWidth), "a content row between the top and bottom border must carry contentWidth");
                Assert.True(contentWidth.GetInt32() < contentRow.GetProperty("width").GetInt32(), "a content row's pre-border width must be smaller than the bordered line's own width");
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_SplitPaneConfig_EveryRowCarriesItsOwnWidthMatchingItsText()
    {
        var configPath = WriteTempConfig("""
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "children": [
                { "border": { "enabled": true }, "items": [ { "item": "model-short" } ] },
                { "border": { "enabled": true }, "items": [ { "item": "git-branch" } ] }
              ]
            }
          }
        }
        """);
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            using var doc = PreviewCliRunner.ParseJsonOrFail((exitCode, stdout, stderr));
            var rowList = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();

            Assert.NotEmpty(rowList);
            foreach (var row in rowList)
            {
                var text = row.GetProperty("text").GetString();
                Assert.Equal(text!.Length, row.GetProperty("width").GetInt32());
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    // Two side-by-side bordered children (split pipeline, PaneBorderRenderer-drawn borders) rather
    // than the single Spectre-Panel-drawn pane the other tests in this file exercise. #49 fixed
    // rows[].width in this pipeline to reuse PaneRow.Width instead of recomputing it; contentWidth
    // is set from that same PaneRow.Width, so the two must always agree here.
    [Fact]
    public void Preview_SplitPaneConfig_WidthAndContentWidthAgreeSinceBothComeFromTheSameLayoutValue()
    {
        var configPath = WriteTempConfig("""
        {
          "surface": {
            "pane": {
              "split": "vertical",
              "children": [
                { "border": { "enabled": true }, "items": [ { "item": "model-short" } ] },
                { "border": { "enabled": true }, "items": [ { "item": "git-branch" } ] }
              ]
            }
          }
        }
        """);
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            using var doc = PreviewCliRunner.ParseJsonOrFail((exitCode, stdout, stderr));
            var rowList = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();

            Assert.NotEmpty(rowList);

            // Two side-by-side bordered panes at columns 60: confirms this fixture actually reaches
            // the split pipeline rather than silently falling back to a single default pane.
            Assert.Contains(rowList, row => row.GetProperty("text").GetString()!.Count(c => c == '╭') >= 2);

            foreach (var row in rowList)
            {
                Assert.True(row.TryGetProperty("contentWidth", out var contentWidth), "a split-pipeline row must carry contentWidth");
                Assert.Equal(row.GetProperty("width").GetInt32(), contentWidth.GetInt32());
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_Json_SerializesRowsWithTheSpecifiedPropertyNames()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, stdout, stderr) = PreviewCliRunner.Run("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("\"columns\":", stdout);
            Assert.Contains("\"usableColumns\":", stdout);
            Assert.Contains("\"rows\":", stdout);
            Assert.Contains("\"text\":", stdout);
            Assert.Contains("\"width\":", stdout);
            Assert.Contains("\"contentWidth\":", stdout);
            Assert.Contains("\"notes\":", stdout);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"preview-json-rows-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

}
