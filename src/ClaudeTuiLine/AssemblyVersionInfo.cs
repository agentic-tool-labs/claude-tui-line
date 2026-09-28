using System.Reflection;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.7: the .csproj's <c>&lt;Version&gt;</c> is the one source of truth for
/// the plugin's version — this reads what MSBuild derived from it rather than transcribing the
/// number a second time in source. Shared by <c>--version</c>'s own output and `--items --json`'s
/// <c>version</c> field, so the two can never disagree about what build produced them.
/// </summary>
public static class AssemblyVersionInfo
{
    public static string InformationalVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
