namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4/§5: the one payload every item-resolution call site threads, instead of
/// its own growing (input, gitBranch, engram, ...) parameter list — the render's
/// <see cref="StatusInput"/>, its already-probed git branch and Engram telemetry, plus environment
/// values that cost a subprocess to learn (<see cref="RemoteUrl"/> today, whatever comes next).
/// Adding a new such value is a change to this class alone: <see cref="ItemRegistry.ItemDefinition"/>'s
/// delegate signature, and every call site between Program.cs and a leaf item, never changes again.
/// </summary>
public sealed class ItemContext
{
    public StatusInput Input { get; }
    public string? GitBranch { get; }
    public EngramProbeResult? Engram { get; }

    /// <summary>
    /// Per-item settings from the user's config, keyed by item id. Null when the user configured
    /// none. Item ids resolve one value per render (ItemValueResolver.Resolve), so these are
    /// per-item, never per-placement.
    /// </summary>
    public ItemSettingsJsonConfig? ItemSettings { get; }

    private readonly Lazy<string?> _remoteUrl;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §3.2: <c>git remote get-url origin</c> costs a subprocess, so this is
    /// probed at most once per render and only when something actually reads it — an item placed,
    /// a link template's <c>{remote-url}</c>, or a colors-table <c>from</c> naming it — rather than
    /// unconditionally on every render the way <see cref="GitBranch"/> already is.
    /// </summary>
    public string? RemoteUrl => _remoteUrl.Value;

    private readonly Lazy<HierarchyEntry?> _hierarchy;

    /// <summary>
    /// The agent-hierarchy status entry that is current now, read at most once per render and only
    /// when an ah item reads it. Null when no probe was supplied, so contexts built without one
    /// never touch the filesystem.
    /// </summary>
    public HierarchyEntry? Hierarchy => _hierarchy.Value;

    public ItemContext(StatusInput input, string? gitBranch, EngramProbeResult? engram, Func<string?> remoteUrlProbe, ItemSettingsJsonConfig? itemSettings = null, Func<HierarchyEntry?>? hierarchyProbe = null)
    {
        Input = input;
        GitBranch = gitBranch;
        Engram = engram;
        ItemSettings = itemSettings;
        _remoteUrl = new Lazy<string?>(remoteUrlProbe, System.Threading.LazyThreadSafetyMode.None);
        _hierarchy = new Lazy<HierarchyEntry?>(hierarchyProbe ?? (() => null), System.Threading.LazyThreadSafetyMode.None);
    }
}
