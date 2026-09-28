namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §12.6.6: every tool fails with <c>cli-not-found</c>, naming the paths
/// searched, when the CLI binary cannot be located — it never falls back to a remembered item
/// list. The candidates mirror <c>commands/setup.md</c>'s own install-location convention
/// (<c>${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin/claude-tui-line</c>), since that is
/// the one place <c>/claude-tui-line:setup</c> actually puts the binary; SPEC-12.6-mcp-tools.md
/// does not itself enumerate a search order, so this is derived from that existing convention
/// rather than a fresh choice.
/// </summary>
internal static class CliLocator
{
    public static CliLocation Locate()
    {
        var candidates = new List<string>();

        var pluginData = Environment.GetEnvironmentVariable("CLAUDE_PLUGIN_DATA");
        if (!string.IsNullOrEmpty(pluginData))
        {
            candidates.Add(Path.Combine(pluginData, "bin", "claude-tui-line"));
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Add(Path.Combine(home, ".claude", "claude-tui-line", "bin", "claude-tui-line"));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new CliLocation(candidate, candidates);
            }
        }

        return new CliLocation(null, candidates);
    }
}

internal sealed record CliLocation(string? Path, IReadOnlyList<string> SearchedPaths);
