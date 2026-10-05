namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4: the single enumeration point for every builtin item id — 14 default
/// segments plus <c>model-short</c> and <c>remote-url</c>. Nothing else in the codebase enumerates
/// builtin ids: the default pipeline (<see cref="SegmentBuilder.Build"/>) iterates
/// <see cref="DefaultIds"/>, and a pane's <c>items</c>/color-token config (<see cref="LeafItems"/>)
/// looks an id up here directly. Per-id construction logic itself lives in
/// <see cref="SegmentBuilder"/>; this table only says which ids exist, what order they render in
/// by default, and how to reach each one's two distinct outputs — the raw value used for §6
/// color-threshold rules (<see cref="ItemDefinition.ResolveValue"/>), and the rendered segment —
/// both <c>Plain</c> (the sole width metric, §2.4) and <c>Markup</c> (colour, including any
/// internal per-fragment colouring an item applies to itself) — shared byte-for-byte by the
/// default segment and an explicit <c>items</c> selection alike
/// (<see cref="ItemDefinition.BuildDefaultSegment"/>). Every row reads through one
/// <see cref="ItemContext"/> instead of its own (input, gitBranch, engram, ...) parameter list, so
/// adding a new environment value (§3.2's <c>remote-url</c> today) never touches this delegate
/// signature again.
/// </summary>
public static class ItemRegistry
{
    /// <summary>
    /// Whether a row's own internal colour is a fixed provider identity with no information
    /// content (<see cref="Decorative"/> — an item-level <c>color</c> config replaces it entirely)
    /// or value-derived, where recolouring would destroy meaning (<see cref="Semantic"/> — an
    /// item-level <c>color</c> nests around it instead, claiming only text the row left unclaimed).
    /// </summary>
    public enum ItemColorKind
    {
        Decorative,
        Semantic,
    }

    public sealed record ItemDefinition(
        string Id,
        string Reports,
        Func<ItemContext, string?> ResolveValue,
        Func<ItemContext, Segment?> BuildDefaultSegment,
        ItemColorKind ColorKind,
        // default-links-branch-directory.md §2.2: `values` (what a link template's `{other-id}`
        // placeholders read) is built before any DefaultLinkTemplate is invoked, from the user's
        // own configured links only — a default template can therefore reference only `{}` (its
        // own value) and literal text; any other placeholder resolves to null and silently drops
        // the link.
        Func<ItemContext, string?>? DefaultLinkTemplate = null);

    // Declaration order is also the default rendering order (SegmentBuilder.Build iterates
    // DefaultIds in this order) — kept as one list rather than a separate ordering table so the
    // two can never drift apart. Reports strings are SPEC-V2-FRAMEWORK.md §9.6.2.1's table,
    // transcribed verbatim (its markdown code-span backticks are the document's own styling, not
    // literal characters) — required here rather than in a lookup table beside it, so a row added
    // without a description fails to compile instead of shipping as a bare id.
    private static readonly ItemDefinition[] Items =
    {
        new("directory", "the working directory",
            ctx => SegmentBuilder.ResolveDirectory(ctx.Input.Cwd, ctx.ItemSettings?.Directory),
            ctx => SegmentBuilder.BuildDirectory(ctx.Input.Cwd, ctx.ItemSettings?.Directory),
            ItemColorKind.Decorative,
            DefaultLinkTemplate: ctx => DirectoryLink.Build(
                ctx.Input.Cwd,
                ctx.ItemSettings?.Directory?.OpenWith)),
        new("git-branch", "the current branch, or nothing outside a repo",
            ctx => SegmentBuilder.ResolveGitBranch(ctx.GitBranch),
            ctx => SegmentBuilder.BuildGitBranch(ctx.GitBranch),
            ItemColorKind.Decorative,
            DefaultLinkTemplate: GitBranchDefaultLink),
        new("repo", "the workspace repo as owner/name", ctx => SegmentBuilder.ResolveRepo(ctx.Input.Workspace?.Repo), ctx => SegmentBuilder.BuildRepo(ctx.Input.Workspace?.Repo), ItemColorKind.Decorative,
            DefaultLinkTemplate: RepoDefaultLink),
        new("worktree", "the worktree's name when the session is in one, plus its branch when showBranch is enabled", ctx => SegmentBuilder.ResolveWorktree(ctx.Input.Worktree, ctx.ItemSettings?.Worktree), ctx => SegmentBuilder.BuildWorktree(ctx.Input.Worktree, ctx.ItemSettings?.Worktree), ItemColorKind.Decorative),
        new("pr", "the pull request number and its review state; links to the pull request on the repo host", ctx => SegmentBuilder.ResolvePullRequest(ctx.Input.Pr, ctx.ItemSettings?.Pr), ctx => SegmentBuilder.BuildPullRequest(ctx.Input.Pr, ctx.ItemSettings?.Pr), ItemColorKind.Decorative,
            DefaultLinkTemplate: PrDefaultLink),
        new("model", "the model's display name", ctx => SegmentBuilder.ResolveModel(ctx.Input.Model), ctx => SegmentBuilder.BuildModel(ctx.Input.Model), ItemColorKind.Decorative),
        new("effort", "the reasoning effort level", ctx => SegmentBuilder.ResolveEffort(ctx.Input.Effort), ctx => SegmentBuilder.BuildEffort(ctx.Input.Effort), ItemColorKind.Decorative),
        new("thinking", "whether extended thinking is on", ctx => SegmentBuilder.ResolveThinking(ctx.Input.Thinking), ctx => SegmentBuilder.BuildThinking(ctx.Input.Thinking), ItemColorKind.Decorative),
        new("output-style", "the active output style", ctx => SegmentBuilder.ResolveOutputStyle(ctx.Input.OutputStyle), ctx => SegmentBuilder.BuildOutputStyle(ctx.Input.OutputStyle, ctx.ItemSettings?.OutputStyle), ItemColorKind.Decorative),
        new("autocompact", "Auto-compaction state and window size (autoCompactEnabled / autoCompactWindow from the visible settings files and CLAUDE_CODE_AUTO_COMPACT_WINDOW; /autocompact, --autocompact and managed settings are not visible)",
            ctx => SegmentBuilder.ResolveAutocompact(ctx),
            ctx => SegmentBuilder.BuildAutocompact(ctx, ctx.ItemSettings?.Autocompact),
            ItemColorKind.Decorative),
        new("context", "how much of the context window is in use. Its colour follows that percentage through the configured thresholds, so it warms as the window fills. Renders 0% when the harness has reported no usage yet, so it never disappears from a fresh session.", ctx => SegmentBuilder.ResolveContext(ctx.Input.ContextWindow), ctx => SegmentBuilder.BuildContext(ctx.Input.ContextWindow, ctx.ItemSettings?.Context), ItemColorKind.Semantic),
        new("rate-limits", "usage against the five-hour and seven-day limits. Its colour follows the higher of the two through the thresholds, since the nearer limit is the one that will stop you", ctx => SegmentBuilder.ResolveRateLimits(ctx.Input.RateLimits), ctx => SegmentBuilder.BuildRateLimits(ctx.Input.RateLimits, ctx.ItemSettings?.RateLimits), ItemColorKind.Semantic),
        new("agent", "the name of the active agent, when the session is running one", ctx => SegmentBuilder.ResolveAgent(ctx.Input.Agent), ctx => SegmentBuilder.BuildAgent(ctx.Input.Agent), ItemColorKind.Decorative),
        new("engram", "recent Engram memory activity. Defaults to the free telemetry.jsonl-derived facts/verb pair; itemSettings.engram.fields can pull in richer values from `engram status`/`activity --json` (opt-in, each triggers its own subprocess), and itemSettings.engram.stateColors can recolour the whole item by reachability state instead of a magnitude", ctx => SegmentBuilder.ResolveEngram(ctx.Engram), ctx => SegmentBuilder.BuildEngram(ctx.Engram, ctx.ItemSettings?.Engram), ItemColorKind.Semantic),
        new("vim", "the current vim mode, when vim mode is enabled", ctx => SegmentBuilder.ResolveVim(ctx.Input.Vim), ctx => SegmentBuilder.BuildVimMode(ctx.Input.Vim), ItemColorKind.Decorative),
        new("model-short", "an abbreviated model name, for panes too narrow for the full one", ctx => SegmentBuilder.ResolveModelShort(ctx.Input.Model), ctx => SegmentBuilder.BuildModelShort(ctx.Input.Model), ItemColorKind.Decorative),
        new("remote-url", "the git remote's URL. Opt-in rather than default because resolving it shells out to git", ctx => SegmentBuilder.ResolveRemoteUrl(ctx.RemoteUrl), ctx => SegmentBuilder.BuildRemoteUrl(ctx.RemoteUrl), ItemColorKind.Decorative),
        new("repo-host", "the host the workspace repo lives on, from the session payload rather than a git probe", ctx => SegmentBuilder.ResolveRepoHost(ctx.Input.Workspace?.Repo), ctx => SegmentBuilder.BuildRepoHost(ctx.Input.Workspace?.Repo), ItemColorKind.Decorative),
        new("linear", "the Linear ticket id extracted from the current git branch, uppercased; links to the issue when itemSettings.linear.workspace is set",
            ctx => SegmentBuilder.ResolveLinear(ctx.GitBranch),
            ctx => SegmentBuilder.BuildLinear(ctx.GitBranch),
            ItemColorKind.Decorative,
            DefaultLinkTemplate: ctx => LinearDefaultLink(ctx)),
        new("ah", "agent-hierarchy status for this repo's live teams — live members, work out, and blocked/overdue/stalled counts — read from .claude/hierarchy/status.json, which agent-hierarchy keeps current; hidden in the sessions of live team members",
            ctx => SegmentBuilder.ResolveAh(ctx, useShort: false),
            ctx => SegmentBuilder.BuildAh(ctx, useShort: false, ctx.ItemSettings?.Ah),
            ItemColorKind.Semantic),
        new("ah-short", "an abbreviated ah status, for panes too narrow for the full one",
            ctx => SegmentBuilder.ResolveAh(ctx, useShort: true),
            ctx => SegmentBuilder.BuildAh(ctx, useShort: true, ctx.ItemSettings?.AhShort),
            ItemColorKind.Semantic),
    };

    // The number comes straight from PrInfo.Number, never from ResolvePullRequest's rendered value,
    // so review_state and itemSettings.pr.reviewStateLabels can never leak into the URL (SPEC-105
    // §C.1). Host/owner/name are escaped for the template layer (EscapeTemplateLiteral, shared with
    // GitBranchDefaultLink) and additionally rejected outright if they contain anything that could
    // break out of a URL path segment or an OSC-8 escape sequence (SPEC-105 §C.4).
    private static string? PrDefaultLink(ItemContext ctx)
    {
        if (ctx.Input.Pr?.Number is not { } number
            || ctx.Input.Workspace?.Repo is not { } repo
            || repo.Host is not { Length: > 0 } host
            || repo.Owner is not { Length: > 0 } owner
            || repo.Name is not { Length: > 0 } name
            || !IsSafeHost(host)
            || !IsSafeUrlComponent(owner)
            || !IsSafeUrlComponent(name))
        {
            return null;
        }

        return $"https://{host}/{EscapeTemplateLiteral(owner)}/{EscapeTemplateLiteral(name)}/pull/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    // SPEC-105 §C.4b.1 (rev 2): a whitelist, not a blacklist — the authority position, where an
    // unrejected `@` would let a spoof host claim a different real authority than what the
    // statusline displays. `[A-Za-z0-9.-]+` plus an optional `:port` (1-5 digits, numerically
    // 1..65535) rejects `@` by construction, along with everything else a blacklist could miss.
    private static readonly System.Text.RegularExpressions.Regex HostPattern =
        new(@"\A[A-Za-z0-9.-]+(?::([0-9]{1,5}))?\z", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsSafeHost(string s)
    {
        var match = HostPattern.Match(s);
        if (!match.Success)
        {
            return false;
        }

        if (!match.Groups[1].Success)
        {
            return true;
        }

        return int.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var port)
            && port is >= 1 and <= 65535;
    }

    // SPEC-105 §C.4b.2 (rev 2): `Owner`/`Name` deliberately stay on this blacklist rather than
    // moving to IsSafeHost's whitelist — neither can reach the authority position (`/` is already
    // rejected here), and whitelisting them would make EscapeTemplateLiteral unreachable for them.
    private static bool IsSafeUrlComponent(string s)
    {
        foreach (var c in s)
        {
            if (c < 0x20 || c == 0x7F || char.IsWhiteSpace(c) || c is '/' or '?' or '#' or '\\')
            {
                return false;
            }
        }

        return true;
    }

    private static string? LinearDefaultLink(ItemContext ctx) =>
        ctx.ItemSettings?.Linear?.Workspace is { Length: > 0 } ws
            ? $"https://linear.app/{ws}/issue/{{}}"
            : null;

    // Links to the repo's web page using the git remote, the same source git-branch uses, so the
    // host is never taken from the payload.
    private static string? RepoDefaultLink(ItemContext ctx) =>
        ctx.RemoteUrl is { Length: > 0 } remote ? EscapeTemplateLiteral(remote) : null;

    // The value substituted for `{}` is not URL-escaped (LeafContent.cs:123 strips ANSI and nothing
    // else), and a branch name may legally contain `#` and `%` — so the branch is escaped here and the
    // template returned with no placeholders. Braces are escaped because the returned string is
    // re-tokenized by PlaceholderTemplate (see the default-links spec §2.3).
    private static string? GitBranchDefaultLink(ItemContext ctx)
    {
        if (ctx.RemoteUrl is not { Length: > 0 } remote || ctx.GitBranch is not { Length: > 0 } branch)
        {
            return null;
        }

        return $"{EscapeTemplateLiteral(remote)}/tree/{EscapeBranchForPath(branch)}";
    }

    // Only braces: the remote URL is already a URL and may legitimately contain percent-escapes,
    // which re-escaping would double.
    private static string EscapeTemplateLiteral(string s) =>
        s.Replace("{", "%7B", StringComparison.Ordinal).Replace("}", "%7D", StringComparison.Ordinal);

    // A path segment, not a whole path: `/` is deliberately preserved so `feature/foo` keeps its shape
    // (GitHub serves /tree/feature/foo; %2F breaks it). git already forbids space, `~^:?*[` and `\` in
    // ref names, so `%` and `#` are the only escapes needed. `%` MUST come first.
    private static string EscapeBranchForPath(string s) => s
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace("#", "%23", StringComparison.Ordinal)
        .Replace("{", "%7B", StringComparison.Ordinal)
        .Replace("}", "%7D", StringComparison.Ordinal);

    private static readonly IReadOnlyDictionary<string, ItemDefinition> ById =
        Items.ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase);

    // §9.6.2: `--items` enumerates every row, unlike DefaultIds below which excludes the two
    // opt-in-only ones — exposed in the same declaration order for the same reason DefaultIds is.
    public static readonly IReadOnlyList<ItemDefinition> All = Items;

    // model-short, remote-url, repo-host, linear, and autocompact are all opt-in-only (never part
    // of the default 14-segment pipeline): remote-url because ItemContext.RemoteUrl's probe is lazy
    // and must stay unfired for a render that never references it (§3.2) — including it here would
    // probe on every render regardless of placement. repo-host is excluded for a different reason
    // — it fires no subprocess, but a bare hostname is noise in a rendered statusline; its purpose
    // is to be referenced by a link template, not displayed on its own. linear is excluded for a
    // third reason distinct from both: most branches carry no ticket id, so a default placement
    // would render nothing on the majority of renders, and adding a default segment moves the ~28
    // whole-statusline assertions in SegmentBuilderTests.cs for no benefit. autocompact is excluded
    // per SPEC-102-showlabel-and-autocompact.md §B.3: a new item must not silently appear in every
    // existing user's statusline.
    // ah and ah-short are opt-in for the same reason as autocompact, and because placing one costs a
    // file read.
    public static readonly IReadOnlyList<string> DefaultIds =
        Items.Where(i => i.Id is not ("model-short" or "remote-url" or "repo-host" or "linear" or "autocompact" or "ah" or "ah-short"))
            .Select(i => i.Id)
            .ToList();

    public static ItemDefinition? Find(string id) => ById.TryGetValue(id, out var def) ? def : null;
}
