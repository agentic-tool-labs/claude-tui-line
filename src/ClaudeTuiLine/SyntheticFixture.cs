namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.3/§9.3.1: the one synthetic <see cref="StatusInput"/> shared by every
/// command that renders an item without a real stdin payload — today <c>--items</c>' <c>example</c>
/// field, later <c>--preview</c>. §9.3.1 specifies this fixture in prose rather than leaving it to
/// whoever writes it first, because "a fixed synthetic payload" is not itself a value and two
/// independent implementations of that sentence would drift apart; every field, value, and
/// cross-reference below is a literal transcription of that section, not an invention of this file.
/// Explicitly not a test fixture — tests keep constructing whatever <see cref="StatusInput"/> they
/// need; this one is user-facing output.
/// </summary>
public static class SyntheticFixture
{
    public static readonly StatusInput Input = new()
    {
        Cwd = "/home/you/code/acme-web",
        Workspace = new WorkspaceInfo { Repo = new RepoInfo { Host = "github.com", Owner = "acme", Name = "acme-web" } },
        Worktree = new WorktreeInfo { Name = "acme-web", Branch = "feat/eng-1234" },
        Pr = new PrInfo { Number = 128, ReviewState = "APPROVED" },
        Model = new ModelInfo { DisplayName = "Claude Sonnet 5" },
        Effort = new EffortInfo { Level = "medium" },
        Thinking = new ThinkingInfo { Enabled = true },
        OutputStyle = new OutputStyleInfo { Name = "Explanatory" },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 34.0, TotalInputTokens = 68000, ContextWindowSize = 200000 },
        RateLimits = new RateLimitsInfo
        {
            FiveHour = new RateWindowInfo { UsedPercentage = 22.0 },
            SevenDay = new RateWindowInfo { UsedPercentage = 41.0 },
        },
        Agent = new AgentInfo { Name = "acme-reviewer" },
        Vim = new VimInfo { Mode = "NORMAL" },
        SessionId = "00000000-0000-4000-8000-000000000000",
    };

    // §9.3.1: the three ItemContext fields beyond the payload are canned because they come from
    // probing the machine rather than stdin, which this fixture must not do. GitBranch and
    // RemoteUrl agree with Input.Worktree.Branch and Input.Workspace.Repo per §9.3.1's redundant-
    // fields-must-agree rule; EngramResult(Facts: 3, Verb: "◉ recalled") is §9.3.1's own normative
    // fixture, not a value this file invented — BuildEngram's rendered string additionally
    // prefixes "engram:" and the fact count ahead of the verb, so if that rendered form and this
    // fixture ever disagree, the builder is the fact and the spec clause is the finding.
    // SPEC-104 §E.6: Status/Activity/State are canned the same way — never spawned or resolved.
    private static readonly EngramProbeResult SyntheticEngram = new(
        Telemetry: new EngramResult(3, "◉ recalled"),
        Status: new EngramStatusInfo(Initialised: true, Server: "Running", Pid: 30695, Port: 7433, Version: "1.0.0", UptimeSeconds: 220),
        Activity: new EngramActivityInfo(
            LastKind: "embedding",
            LastAgeSeconds: 105,
            WindowSeconds: 3600,
            WindowCount: 226,
            Kinds: new Dictionary<string, long> { ["index"] = 134, ["file-touched"] = 38, ["embedding"] = 16 }),
        State: EngramState.Active,
        LastEventAt: new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.Zero));

    public static ItemContext CreateItemContext(ItemSettingsJsonConfig? itemSettings = null) =>
        new(Input, gitBranch: "feat/eng-1234", engram: SyntheticEngram,
            remoteUrlProbe: () => "https://github.com/acme/acme-web", itemSettings);

    // §12.3.1/§12.7.1/§12.7.2: the payload the --fixture flag emits. Every field of Input, except
    // Cwd, which is replaced by the process's real working directory — piping this through
    // --preview takes the non-empty-stdin (real-probe) branch, and an invented cwd paired with
    // real git/remote probes would be an incoherent render rather than a merely minimal one. This
    // is not a second authored fixture: exactly one field is derived from the environment, and
    // §9.3.1's pinned Input is otherwise unchanged and still what --items and the empty-stdin path
    // use verbatim.
    public static StatusInput WithRealCwd(string cwd) => new()
    {
        Cwd = cwd,
        Workspace = Input.Workspace,
        Worktree = Input.Worktree,
        Pr = Input.Pr,
        Model = Input.Model,
        Effort = Input.Effort,
        Thinking = Input.Thinking,
        OutputStyle = Input.OutputStyle,
        ContextWindow = Input.ContextWindow,
        RateLimits = Input.RateLimits,
        Agent = Input.Agent,
        Vim = Input.Vim,
        SessionId = Input.SessionId,
    };
}
