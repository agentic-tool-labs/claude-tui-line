namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §12.6.11 rules 1-2: <c>commands/edit.md</c> re-reads the config
/// immediately before writing, compares it against the earlier read, and on a difference
/// re-derives the pending edit against the new content and tells the user what moved rather
/// than applying a position resolved against stale content. Task #39 scope (confirmed:
/// test-only, no production code) — this is a content-presence regression guard for that
/// procedure, mirroring the AllowListTests.V4 pattern of scanning known text rather than
/// executing it, since <c>edit.md</c> is a procedure for the model to follow, not compiled code.
/// </summary>
public sealed class EditCommandCompareAndBranchTests
{
    [Fact]
    public void EditMd_ReReadsBeforeWritingAndComparesAgainstTheEarlierRead()
    {
        var text = ReadEditMd();

        Assert.Contains("Re-read the config file immediately before you edit it", text);
        Assert.Contains("compare it against what you read at step 2, and branch", text);
    }

    [Fact]
    public void EditMd_OnADifferenceReDerivesAndTellsTheUserRatherThanApplyingTheStalePosition()
    {
        var text = ReadEditMd();

        Assert.Contains("Do **not** apply the edit you already worked out", text);
        Assert.Contains("Re-derive it against the new content", text);
        Assert.Contains("tell the user the file changed underneath you and what moved", text);
    }

    private static string ReadEditMd()
    {
        var path = Path.Combine(FindRepoRoot(), "commands", "edit.md");
        // Markdown hard-wraps prose at a column width, so a phrase spanning a wrap point would
        // otherwise contain a newline the source text doesn't logically have. Collapse all
        // whitespace runs to a single space before matching against fixed phrases.
        var raw = File.ReadAllText(path);
        return System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "commands")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("could not locate the repo's commands/ directory from the test output directory");
        }

        return dir.FullName;
    }
}
