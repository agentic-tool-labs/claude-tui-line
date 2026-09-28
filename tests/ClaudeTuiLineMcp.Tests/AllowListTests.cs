using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>
/// SPEC-12.6-mcp-tools.md §10 V4 / §7.1: SPEC-83 removed the MCP server's project reference to
/// the core entirely (config path resolution now lives in the shared dependency-free
/// <c>ClaudeTuiLineShared</c> library), so this regex now finds zero matches by construction —
/// that vacuity is the point, not a gap. This test asserts no reference to
/// <c>ClaudeTuiLine.*</c> has crept back in by any route other than the `ProjectReference`
/// itself, which V4b (below) asserts is absent.
/// </summary>
public sealed class AllowListTests
{
    private static readonly Regex CoreReference = new(@"\bClaudeTuiLine\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    [Fact]
    public void V4_OnlyResolveConfigPathIsReferencedFromTheCore()
    {
        var srcDir = FindSrcClaudeTuiLineMcp();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in CoreReference.Matches(text))
            {
                var member = match.Groups[1].Value;
                if (member != "ConfigLoader")
                {
                    offenders.Add($"{file}: ClaudeTuiLine.{member}");
                    continue;
                }

                // Must be followed by ".ResolveConfigPath(" — no other ConfigLoader member allowed.
                var afterMatch = text[(match.Index + match.Length)..];
                if (!afterMatch.TrimStart().StartsWith(".ResolveConfigPath("))
                {
                    offenders.Add($"{file}: ClaudeTuiLine.ConfigLoader.<something other than ResolveConfigPath>");
                }
            }
        }

        Assert.True(offenders.Count == 0, "§7.1 allow-list violated:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// SPEC-83 §5.2(b): the MCP csproj must carry no <c>ProjectReference</c> to
    /// <c>ClaudeTuiLine.csproj</c> and exactly one to <c>ClaudeTuiLineShared.csproj</c>. This is
    /// the test that makes the NETSDK1151 fix durable — it fails the moment someone re-adds the
    /// reference that caused #83.
    /// </summary>
    [Fact]
    public void V4b_McpDoesNotReferenceTheCoreProject()
    {
        var srcDir = FindSrcClaudeTuiLineMcp();
        var csprojPath = Path.Combine(srcDir, "ClaudeTuiLineMcp.csproj");
        var text = File.ReadAllText(csprojPath);

        var includes = Regex.Matches(text, @"<ProjectReference\s+Include=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        var coreReferences = includes.Where(i => Path.GetFileName(i) == "ClaudeTuiLine.csproj").ToList();
        Assert.True(coreReferences.Count == 0, "ClaudeTuiLineMcp.csproj must not reference ClaudeTuiLine.csproj: " + string.Join(", ", coreReferences));

        var sharedReferences = includes.Where(i => Path.GetFileName(i) == "ClaudeTuiLineShared.csproj").ToList();
        Assert.True(sharedReferences.Count == 1, "ClaudeTuiLineMcp.csproj must reference ClaudeTuiLineShared.csproj exactly once, found " + sharedReferences.Count);
    }

    private static string FindSrcClaudeTuiLineMcp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ClaudeTuiLineMcp")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("could not locate src/ClaudeTuiLineMcp from the test output directory");
        }

        return Path.Combine(dir.FullName, "src", "ClaudeTuiLineMcp");
    }
}
