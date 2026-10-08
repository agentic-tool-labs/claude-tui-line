using System.Text.Json;
using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>
/// Per-id construction logic for every builtin item: for each id, a composite default-segment
/// builder (color/format baked in, used by <see cref="Build"/> for the no-<c>items</c>-configured
/// pipeline) and a raw-value resolver (used when an id is explicitly selected into a pane's
/// <c>items</c> list — see <see cref="LeafItems"/>). <see cref="ItemRegistry"/> is what enumerates
/// these ids and their default order; this class only knows how to build one id's output once
/// asked.
/// </summary>
public static class SegmentBuilder
{
    // The raw ANSI SGR reset (ESC [ 0 m) appended after a command provider's own escaped text —
    //  rather than an embedded raw byte, so this stays a plain, diff-legible ASCII source
    // line instead of an invisible control character between quotes.
    private const string RawSgrReset = "[0m";

    public static IReadOnlyList<Segment> Build(ItemContext ctx)
    {
        var segments = new List<Segment>();
        foreach (var id in ItemRegistry.DefaultIds)
        {
            var segment = ItemRegistry.Find(id)!.BuildDefaultSegment(ctx);
            if (segment is not null)
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    private static Segment SingleColor(string tag, string plain) =>
        new($"[{tag}]{Markup.Escape(plain)}[/]", plain);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4: <c>model-short</c> is a new registry row, not a format string —
    /// it gives a shorter form of the model name than the full <c>model</c> item. Strips a leading "Claude "
    /// prefix when present ("Claude Opus 4.5" → "Opus 4.5"); otherwise passes the display name
    /// through unchanged. Mirrors <see cref="BuildModel"/>'s suppress-on-absent behavior: an
    /// absent/empty display name resolves to null (suppressed), never an empty string.
    /// </summary>
    public static string? ResolveModelShort(ModelInfo? model)
    {
        if (model is null || string.IsNullOrEmpty(model.DisplayName))
        {
            return null;
        }

        const string prefix = "Claude ";
        return model.DisplayName.StartsWith(prefix, StringComparison.Ordinal)
            ? model.DisplayName[prefix.Length..]
            : model.DisplayName;
    }

    /// <summary>
    /// Builds one plain-text item segment with an optional single color tag — the same
    /// markup convention as <see cref="SingleColor"/>, exposed for §3's per-item leaf
    /// rendering path where a color comes from item config rather than a fixed provider tag. This
    /// is the one place a <c>command</c> item's raw stdout (or a derived item's value pulled from
    /// one) turns into a <see cref="Segment"/>, so it is also the one place that text is
    /// sanitized (SPEC-V2-FRAMEWORK.md §3.2 rule 2): <see cref="Segment.Plain"/> is the sole width
    /// metric and must be escape-free, so it is stripped with <see cref="AnsiStrip.Strip"/>
    /// unconditionally, never left to carry a script's raw ANSI bytes into a width calculation.
    /// <see cref="Segment.Markup"/> instead best-effort *preserves* the script's own raw bytes —
    /// <see cref="Markup.Escape"/> only neutralizes Spectre's own <c>[</c> tag syntax, so an
    /// unescaped ESC byte passes through untouched and the script's own colours/links still render
    /// — with a trailing raw SGR reset appended so an unterminated colour (e.g. a bare
    /// <c>\e[31m</c>) cannot bleed into the next segment the way an unterminated OSC 8 link would.
    /// </summary>
    public static Segment BuildItemSegment(string plain, string? color)
    {
        var strippedPlain = AnsiStrip.Strip(plain);
        var rawMarkup = Markup.Escape(plain) + Markup.Escape(RawSgrReset);

        return string.IsNullOrEmpty(color)
            ? new Segment(rawMarkup, strippedPlain)
            : new Segment($"[{color}]{rawMarkup}[/]", strippedPlain);
    }

    /// <summary>
    /// SPEC-85-ADDENDUM-spans-threading.md §12/D-F: one compound part's <see cref="StyledSpan"/>
    /// markup as a clean <c>"[color]text[/]"</c> (or unstyled) wrap — unlike
    /// <see cref="BuildItemSegment(string,string?)"/>, no trailing raw SGR reset is baked in.
    /// That reset exists to stop a command's raw stdout bleeding into an adjacent segment, which
    /// does not apply here (a compound part's text is never raw script output); its escaped-bracket
    /// form also breaks <see cref="SegmentTruncation.TryGetSimpleWrap"/>'s exact-suffix match, so a
    /// span truncated mid-part would silently lose its colour.
    /// </summary>
    internal static string BuildSpanMarkup(string plain, string? color)
    {
        var escaped = Markup.Escape(plain);
        return string.IsNullOrEmpty(color) ? escaped : $"[{color}]{escaped}[/]";
    }

    /// <summary>
    /// Builds one item segment from its own already-tagged markup, with an optional outer colour
    /// wrapped around it. SPEC-V2-FRAMEWORK.md §6: a config <c>color</c> nests around an item's
    /// internal markup rather than replacing it — Spectre gives the inner tags their own span and
    /// leaves the outer colour to claim whatever text they don't.
    /// </summary>
    public static Segment BuildItemSegment(string plain, string markup, string? color, IReadOnlyList<StyledSpan>? spans = null) =>
        string.IsNullOrEmpty(color)
            ? new Segment(markup, plain, spans)
            : new Segment($"[{color}]{markup}[/]", plain, null);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §3.3: assembles a compound item's per-part spans into one
    /// <see cref="Segment"/> — the sole place that establishes the <see cref="Segment.Spans"/>
    /// invariant (§5.1: concatenated span <c>Plain</c>/<c>Markup</c> equal the segment's own).
    /// No separator is inserted between spans.
    /// </summary>
    public static Segment BuildCompoundSegment(IReadOnlyList<StyledSpan> spans) =>
        new(string.Concat(spans.Select(s => s.Markup)), string.Concat(spans.Select(s => s.Plain)), spans);

    /// <summary>
    /// SPEC-87 §12.9: applies a selecting item's colour as a floor onto a compound's spans — only
    /// where a span carries no colour of its own (its markup is exactly its escaped plain text,
    /// the shape <see cref="BuildSpanMarkup"/> produces for a null colour). A part's own colour,
    /// explicit or value-derived, is never overridden.
    /// </summary>
    internal static Segment ApplyColorFloor(Segment compound, string? floorColor)
    {
        if (string.IsNullOrEmpty(floorColor) || compound.Spans is not { Count: > 0 } spans)
        {
            return compound;
        }

        var changed = false;
        var merged = new List<StyledSpan>(spans.Count);
        foreach (var span in spans)
        {
            if (span.Markup == Markup.Escape(span.Plain))
            {
                merged.Add(new StyledSpan(span.Plain, BuildSpanMarkup(span.Plain, floorColor)));
                changed = true;
            }
            else
            {
                merged.Add(span);
            }
        }

        return changed ? BuildCompoundSegment(merged) : compound;
    }

    internal static Segment? BuildDirectory(string? cwd, DirectoryItemSettings? settings = null) =>
        string.IsNullOrEmpty(cwd) ? null : SingleColor("teal", TrailingSegments(cwd, settings?.Depth ?? 1));

    internal static string? ResolveDirectory(string? cwd, DirectoryItemSettings? settings = null) =>
        string.IsNullOrEmpty(cwd) ? null : TrailingSegments(cwd, settings?.Depth ?? 1);

    /// <summary>The last <paramref name="depth"/> path segments of <paramref name="path"/>, joined by '/'. depth &lt;= 1 is the plain basename.</summary>
    private static string TrailingSegments(string path, int depth)
    {
        if (depth <= 1)
        {
            return Basename(path);
        }

        var trimmed = path.TrimEnd('/');
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return "/";
        }

        var take = Math.Min(depth, segments.Length);
        return string.Join('/', segments[^take..]);
    }

    internal static Segment? BuildGitBranch(string? branch) =>
        string.IsNullOrEmpty(branch) ? null : SingleColor("green", branch);

    internal static string? ResolveGitBranch(string? branch) =>
        string.IsNullOrEmpty(branch) ? null : branch;

    internal static Segment? BuildRepo(RepoInfo? repo) =>
        repo is null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name)
            ? null
            : SingleColor("dim", $"{repo.Owner}/{repo.Name}");

    internal static string? ResolveRepo(RepoInfo? repo) =>
        repo is null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name)
            ? null
            : $"{repo.Owner}/{repo.Name}";

    internal static Segment? BuildRepoHost(RepoInfo? repo) =>
        ResolveRepoHost(repo) is { } raw ? SingleColor("dim", raw) : null;

    internal static string? ResolveRepoHost(RepoInfo? repo) =>
        string.IsNullOrEmpty(repo?.Host) ? null : repo!.Host;

    internal const string TicketPattern = "[A-Za-z]{2,}-[0-9]+";

    internal static string? ResolveLinear(string? gitBranch) =>
        string.IsNullOrEmpty(gitBranch)
            ? null
            : ItemValueResolver.ExtractValue(gitBranch, TicketPattern) is { } id
                ? ItemValueResolver.ApplyCase(id, "upper")
                : null;

    internal static Segment? BuildLinear(string? gitBranch) =>
        ResolveLinear(gitBranch) is { } raw ? SingleColor("blue", raw) : null;

    internal const string WorktreeFormat = "worktree:{}";

    internal static Segment? BuildWorktree(WorktreeInfo? worktree, WorktreeItemSettings? settings = null) =>
        ResolveWorktree(worktree, settings) is { } raw ? SingleColor("purple", LeafItems.ApplyFormat(WorktreeFormat, raw)) : null;

    internal static string? ResolveWorktree(WorktreeInfo? worktree, WorktreeItemSettings? settings = null)
    {
        if (worktree is null || string.IsNullOrEmpty(worktree.Name))
        {
            return null;
        }

        var showBranch = settings?.ShowBranch ?? false;

        return !showBranch || string.IsNullOrEmpty(worktree.Branch)
            ? worktree.Name
            : $"{worktree.Name}({worktree.Branch})";
    }

    internal const string PullRequestFormat = "PR {}";

    internal static Segment? BuildPullRequest(PrInfo? pr, PrItemSettings? settings = null) =>
        ResolvePullRequest(pr, settings) is { } raw ? SingleColor("olive", LeafItems.ApplyFormat(PullRequestFormat, raw)) : null;

    internal static string? ResolvePullRequest(PrInfo? pr, PrItemSettings? settings = null)
    {
        if (pr?.Number is not { } number)
        {
            return null;
        }

        return $"#{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}{ReviewStateSuffix(pr.ReviewState, settings)}";
    }

    private static string ReviewStateSuffix(string? reviewState, PrItemSettings? settings) =>
        string.IsNullOrEmpty(reviewState)
            ? string.Empty
            : settings?.ReviewStateLabels?.GetValueOrDefault(reviewState) is { } overridden
                ? overridden
                : reviewState switch
                {
                    "approved" => " [approved]",
                    "changes_requested" => " [changes]",
                    "draft" => " [draft]",
                    _ => $" [{reviewState}]",
                };

    internal static Segment? BuildModel(ModelInfo? model) =>
        model is null || string.IsNullOrEmpty(model.DisplayName) ? null : SingleColor("navy", model.DisplayName);

    internal static string? ResolveModel(ModelInfo? model) =>
        string.IsNullOrEmpty(model?.DisplayName) ? null : model!.DisplayName;

    internal static Segment? BuildModelShort(ModelInfo? model) =>
        ResolveModelShort(model) is { } shortName ? SingleColor("navy", shortName) : null;

    internal static Segment? BuildRemoteUrl(string? remoteUrl) =>
        string.IsNullOrEmpty(remoteUrl) ? null : SingleColor("cyan", remoteUrl);

    internal static string? ResolveRemoteUrl(string? remoteUrl) =>
        string.IsNullOrEmpty(remoteUrl) ? null : remoteUrl;

    internal const string EffortFormat = "effort:{}";

    internal static Segment? BuildEffort(EffortInfo? effort) =>
        ResolveEffort(effort) is { } raw ? SingleColor("dim", LeafItems.ApplyFormat(EffortFormat, raw)) : null;

    internal static string? ResolveEffort(EffortInfo? effort) =>
        string.IsNullOrEmpty(effort?.Level) ? null : effort!.Level;

    internal static Segment? BuildThinking(ThinkingInfo? thinking) =>
        thinking?.Enabled == true ? SingleColor("purple", "thinking") : null;

    internal static string? ResolveThinking(ThinkingInfo? thinking) =>
        thinking?.Enabled == true ? "thinking" : null;

    /// <summary>
    /// Renders "&lt;label&gt;&lt;value&gt;&lt;suffix&gt;" honoring both LabeledItemSettings knobs.
    /// Falls back to the exact single-color segment this produced before labelColor
    /// existed whenever no label color is in play, so default output is unchanged.
    /// </summary>
    internal static Segment LabeledSegment(string format, string value, string color, LabeledItemSettings? settings)
    {
        if (settings?.ShowLabel == false)
        {
            return SingleColor(color, value);
        }

        if (settings?.LabelColor is not { Length: > 0 } labelColor)
        {
            return SingleColor(color, LeafItems.ApplyFormat(format, value));
        }

        var marker = format.IndexOf("{}", StringComparison.Ordinal);
        if (marker < 0)
        {
            return SingleColor(color, LeafItems.ApplyFormat(format, value));
        }

        var prefix = format[..marker];
        var suffix = format[(marker + 2)..];

        var spans = new List<StyledSpan>(3);
        if (prefix.Length > 0)
        {
            spans.Add(new StyledSpan(prefix, BuildSpanMarkup(prefix, labelColor)));
        }
        spans.Add(new StyledSpan(value, BuildSpanMarkup(value, color)));
        if (suffix.Length > 0)
        {
            spans.Add(new StyledSpan(suffix, BuildSpanMarkup(suffix, labelColor)));
        }

        return BuildCompoundSegment(spans);
    }

    internal const string OutputStyleFormat = "style:{}";

    internal static Segment? BuildOutputStyle(OutputStyleInfo? style, OutputStyleItemSettings? settings = null) =>
        ResolveOutputStyle(style) is { } raw ? LabeledSegment(OutputStyleFormat, raw, "yellow", settings) : null;

    internal static string? ResolveOutputStyle(OutputStyleInfo? style) =>
        IsDefaultOrEmptyStyle(style) ? null : style!.Name;

    private static bool IsDefaultOrEmptyStyle(OutputStyleInfo? style) =>
        style is null || string.IsNullOrEmpty(style.Name) || string.Equals(style.Name, "default", StringComparison.OrdinalIgnoreCase);

    internal const string AhFormat = "ah: {}";

    internal static string? ResolveAh(ItemContext ctx, bool useShort) =>
        ctx.Hierarchy is { } entry && (useShort ? entry.Short : entry.Text) is { } v && !string.IsNullOrWhiteSpace(v) ? v : null;

    internal static Segment? BuildAh(ItemContext ctx, bool useShort, AhItemSettings? settings) =>
        ResolveAh(ctx, useShort) is { } raw
            ? LabeledSegment(AhFormat, raw, AhToneColor(ctx.Hierarchy!.Tone, settings?.StateColors), settings)
            : null;

    // An unknown tone renders as idle. A configured colour that does not parse falls back to the
    // default, because the colour is interpolated into markup unvalidated.
    private static string AhToneColor(string tone, AhStateColorsJsonConfig? colors)
    {
        var (configured, fallback) = tone switch
        {
            "work" => (colors?.Work, "blue"),
            "warn" => (colors?.Warn, "yellow"),
            "bad" => (colors?.Bad, "red"),
            _ => (colors?.Idle, "grey"),
        };
        return configured is { Length: > 0 } && ColorResolution.ResolveLiteral(configured) is not null ? configured : fallback;
    }

    internal const string AutocompactFormat = "autocompact:{}";

    // ponytail: no per-render cache — up to 3 small file reads per render; add a cache
    // (mirroring the existing item-cache mechanism) if statusline latency regresses.
    internal static Segment? BuildAutocompact(ItemContext ctx, AutocompactItemSettings? settings = null) =>
        ResolveAutocompact(ctx) is { } raw ? LabeledSegment(AutocompactFormat, raw, "yellow", settings) : null;

    internal static string? ResolveAutocompact(ItemContext ctx) =>
        ResolveAutocompact(
            ctx.Input.Cwd,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("CLAUDE_CODE_AUTO_COMPACT_WINDOW"));

    /// <summary>
    /// SPEC-102-showlabel-and-autocompact.md §B.2: <paramref name="projectDir"/>/<paramref name="homeDir"/>/
    /// <paramref name="envWindow"/> are parameters, not ambient lookups, so tests can point them at temp
    /// directories and a fake env value without ever touching the developer's real
    /// <c>~/.claude/settings.json</c>. The two dimensions (enabled, window) resolve independently and may
    /// come from different files, matching how Claude Code's own settings merge works.
    /// </summary>
    internal static string? ResolveAutocompact(string? projectDir, string? homeDir, string? envWindow)
    {
        var settingsPaths = AutocompactSettingsPaths(projectDir, homeDir);

        var enabled = FindBoolSetting(settingsPaths, "autoCompactEnabled") ?? true;
        if (!enabled)
        {
            return "off";
        }

        if (envWindow is { Length: > 0 } && TryParsePlainTokenCount(envWindow, out var envTokens))
        {
            return FormatAutocompactTokens(envTokens);
        }

        var windowElement = FindJsonSetting(settingsPaths, "autoCompactWindow");
        return windowElement is { } el && TryNormalizeAutocompactWindow(el, out var tokens)
            ? FormatAutocompactTokens(tokens)
            : "auto";
    }

    private static IReadOnlyList<string> AutocompactSettingsPaths(string? projectDir, string? homeDir)
    {
        var paths = new List<string>();
        if (!string.IsNullOrEmpty(projectDir))
        {
            paths.Add(Path.Combine(projectDir, ".claude", "settings.local.json"));
            paths.Add(Path.Combine(projectDir, ".claude", "settings.json"));
        }

        if (!string.IsNullOrEmpty(homeDir))
        {
            paths.Add(Path.Combine(homeDir, ".claude", "settings.json"));
        }

        return paths;
    }

    // A present-but-wrong-typed value (e.g. autoCompactEnabled: "yes") is treated as not defined —
    // TryGetProperty succeeds but the ValueKind guard below rejects it, so the scan continues to
    // the next file exactly as if the key were absent there.
    private static bool? FindBoolSetting(IReadOnlyList<string> paths, string key)
    {
        foreach (var path in paths)
        {
            if (TryReadSettingsRoot(path) is { } root &&
                root.TryGetProperty(key, out var prop) &&
                prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return prop.GetBoolean();
            }
        }

        return null;
    }

    private static JsonElement? FindJsonSetting(IReadOnlyList<string> paths, string key)
    {
        foreach (var path in paths)
        {
            if (TryReadSettingsRoot(path) is { } root && root.TryGetProperty(key, out var prop))
            {
                return prop;
            }
        }

        return null;
    }

    // Missing, unreadable, or malformed JSON must never throw or blank the statusline (§B.2) — every
    // failure mode collapses to "this file has nothing to say" and the scan moves to the next one.
    private static JsonElement? TryReadSettingsRoot(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch
        {
            return null;
        }
    }

    // CLAUDE_CODE_AUTO_COMPACT_WINDOW accepts a plain token count only — no k/M suffix, no bare
    // hundreds shorthand — per §B.1's documented env-var shape.
    private static bool TryParsePlainTokenCount(string value, out int tokens)
    {
        if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out tokens) &&
            tokens is >= 100_000 and <= 1_000_000)
        {
            return true;
        }

        tokens = 0;
        return false;
    }

    /// <summary>SPEC-102-showlabel-and-autocompact.md §B.2's window-normalization table.</summary>
    private static bool TryNormalizeAutocompactWindow(JsonElement el, out int tokens)
    {
        tokens = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetInt64(out var n) && TryNormalizeAutocompactNumeric(n, out tokens);

            case JsonValueKind.String:
                var s = el.GetString();
                if (string.IsNullOrEmpty(s) || string.Equals(s, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    tokens = 0;
                    return false;
                }

                if (TryParseSuffixedWindow(s, out tokens))
                {
                    return true;
                }

                return long.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var digits)
                    && TryNormalizeAutocompactNumeric(digits, out tokens);

            default:
                tokens = 0;
                return false;
        }
    }

    private static bool TryNormalizeAutocompactNumeric(long n, out int tokens)
    {
        if (n is >= 100_000 and <= 1_000_000)
        {
            tokens = (int)n;
            return true;
        }

        if (n is >= 100 and <= 1000)
        {
            tokens = (int)(n * 1000);
            return true;
        }

        tokens = 0;
        return false;
    }

    private static bool TryParseSuffixedWindow(string s, out int tokens)
    {
        tokens = 0;
        var suffix = s[^1];
        if (suffix is not ('k' or 'K' or 'm' or 'M'))
        {
            return false;
        }

        if (!double.TryParse(s[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var multiplied = (long)(value * (suffix is 'k' or 'K' ? 1_000 : 1_000_000));
        return TryNormalizeAutocompactNumeric(multiplied, out tokens);
    }

    // Never the raw token count, alone or alongside the abbreviation (§B.2 "Display formatting" —
    // confirmed by the user; the statusline is width-constrained and this item is decorative).
    private static string FormatAutocompactTokens(int tokens) =>
        tokens == 1_000_000 ? "1M" : $"{tokens / 1000}K";

    internal static Segment? BuildContext(ContextWindowInfo? ctx, ContextItemSettings? settings = null)
    {
        var pctInt = RoundHalfToEven(EffectiveContextPercentage(ctx));
        var tag = ColorResolution.ResolveStandardThreshold(pctInt);
        var plain = ResolveContextDisplayText(ctx, settings)!;
        var showDetail = settings?.ShowDetail ?? true;

        if (showDetail && ctx?.UsedPercentage is not null && ctx.TotalInputTokens is { } totalInput && ctx.ContextWindowSize is { } size)
        {
            var markup = $"ctx:[{tag}]{pctInt}%[/] [dim]({totalInput / 1000}k/{size / 1000}k)[/]";
            return new Segment(markup, plain);
        }
        else
        {
            var markup = $"ctx:[{tag}]{pctInt}%[/]";
            return new Segment(markup, plain);
        }
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4: context's registry-row text function — the composite display text
    /// (the "ctx:" label plus the token-count parenthetical when both fields are present), shared
    /// by <see cref="BuildContext"/>'s markup and the configured-<c>items</c> path via
    /// <see cref="ItemRegistry"/>. Can't be a format string: the parenthetical needs
    /// <see cref="ContextWindowInfo.TotalInputTokens"/>/<see cref="ContextWindowInfo.ContextWindowSize"/>,
    /// neither of which is present in <see cref="ResolveContext"/>'s raw value.
    /// </summary>
    internal static string? ResolveContextDisplayText(ContextWindowInfo? ctx, ContextItemSettings? settings = null)
    {
        var pctInt = RoundHalfToEven(EffectiveContextPercentage(ctx));
        var showDetail = settings?.ShowDetail ?? true;
        return showDetail && ctx?.UsedPercentage is not null && ctx.TotalInputTokens is { } totalInput && ctx.ContextWindowSize is { } size
            ? $"ctx:{pctInt}% ({totalInput / 1000}k/{size / 1000}k)"
            : $"ctx:{pctInt}%";
    }

    /// <summary>
    /// The context-window percentage to render when the harness has reported none.
    /// A session that has sent nothing has genuinely used none of its window, so an
    /// absent percentage is zero rather than unknown — see SPEC context-zero-render §2.5.
    /// </summary>
    internal static double EffectiveContextPercentage(ContextWindowInfo? ctx) =>
        ctx?.UsedPercentage ?? 0.0;

    /// <summary>
    /// The bare percentage, with no "ctx:" label and no token-count detail — unlike
    /// <see cref="ResolveContextDisplayText"/>'s composite, this is meant to also parse as a number
    /// so a numeric §6 "thresholds" rule can be applied to it when it's selected into a pane's
    /// <c>items</c> list.
    /// </summary>
    internal static string? ResolveContext(ContextWindowInfo? ctx) =>
        RoundHalfToEven(EffectiveContextPercentage(ctx))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static Segment? BuildRateLimits(RateLimitsInfo? rateLimits, RateLimitsItemSettings? settings = null)
    {
        var windows = settings?.Windows ?? "both";

        string? fivePlain = null, fiveMarkup = null;
        if (windows is "5h" or "both" && rateLimits?.FiveHour?.UsedPercentage is { } fivePct)
        {
            var v = RoundHalfToEven(fivePct);
            var tag = ColorResolution.ResolveStandardThreshold(v);
            fivePlain = $"5h:{v}%";
            fiveMarkup = $"5h:[{tag}]{v}%[/]";
        }

        string? sevenPlain = null, sevenMarkup = null;
        if (windows is "7d" or "both" && rateLimits?.SevenDay?.UsedPercentage is { } sevenPct)
        {
            var v = RoundHalfToEven(sevenPct);
            var tag = ColorResolution.ResolveStandardThreshold(v);
            sevenPlain = $"7d:{v}%";
            sevenMarkup = $"7d:[{tag}]{v}%[/]";
        }

        if (fivePlain is null && sevenPlain is null)
        {
            return null;
        }

        var plain = fivePlain is not null && sevenPlain is not null
            ? $"{fivePlain} / {sevenPlain}"
            : fivePlain ?? sevenPlain!;
        var markup = fiveMarkup is not null && sevenMarkup is not null
            ? $"{fiveMarkup} [dim]/[/] {sevenMarkup}"
            : fiveMarkup ?? sevenMarkup!;

        return new Segment(markup, plain);
    }

    /// <summary>
    /// The maximum used-percentage across the two windows, as a bare parseable number — unlike
    /// <see cref="BuildRateLimits"/>'s composite label text, this is what a numeric §6 "thresholds"
    /// rule sourced from rate-limits evaluates against, mirroring how <see cref="ResolveContext"/>
    /// exposes context's bare percentage for the same purpose.
    /// </summary>
    internal static string? ResolveRateLimits(RateLimitsInfo? rateLimits)
    {
        var five = rateLimits?.FiveHour?.UsedPercentage;
        var seven = rateLimits?.SevenDay?.UsedPercentage;

        if (five is null && seven is null)
        {
            return null;
        }

        var max = Math.Max(five ?? double.NegativeInfinity, seven ?? double.NegativeInfinity);
        return RoundHalfToEven(max).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal const string AgentFormat = "agent:{}";

    internal static Segment? BuildAgent(AgentInfo? agent) =>
        ResolveAgent(agent) is { } raw ? SingleColor("purple", LeafItems.ApplyFormat(AgentFormat, raw)) : null;

    internal static string? ResolveAgent(AgentInfo? agent) =>
        string.IsNullOrEmpty(agent?.Name) ? null : agent!.Name;

    private static readonly EngramFieldJsonConfig[] DefaultEngramFields =
    {
        new() { Field = "facts" },
        new() { Field = "verb" },
    };

    /// <summary>SPEC-104 §C.4: one closed-vocabulary field's rendered plain text and built-in default colour, or null if absent.</summary>
    private static (string Plain, string DefaultColor)? RenderEngramField(string field, EngramProbeResult engram)
    {
        if (field.StartsWith("kind:", StringComparison.Ordinal))
        {
            if (engram.Activity is null)
            {
                return null;
            }

            var count = engram.Activity.Kinds.TryGetValue(field["kind:".Length..], out var c) ? c : 0L;
            return (count.ToString(System.Globalization.CultureInfo.InvariantCulture), "dim");
        }

        return field switch
        {
            "facts" => engram.Telemetry?.Facts is { } facts ? ($"engram:{facts}", "dim") : null,
            "verb" => engram.Telemetry?.Verb is { } verb ? (verb, "purple") : null,
            "lastEventAge" => engram.LastEventAt is { } lastEventAt
                ? (EngramCli.HumanizeDuration((long)Math.Max(0, (DateTimeOffset.UtcNow - lastEventAt).TotalSeconds)), "dim")
                : null,
            "server" => engram.Status?.Server is { Length: > 0 } server ? (server, "dim") : null,
            "version" => engram.Status?.Version is { Length: > 0 } version ? (version, "dim") : null,
            "uptime" => engram.Status?.UptimeSeconds is { } uptime ? (EngramCli.HumanizeDuration(uptime), "dim") : null,
            "port" => engram.Status?.Port is { } port ? (port.ToString(System.Globalization.CultureInfo.InvariantCulture), "dim") : null,
            "pid" => engram.Status?.Pid is { } pid ? (pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "dim") : null,
            "lastKind" => engram.Activity?.LastKind is { Length: > 0 } lastKind ? (lastKind, "purple") : null,
            "lastAge" => engram.Activity?.LastAgeSeconds is { } lastAge ? (EngramCli.HumanizeDuration(lastAge), "dim") : null,
            "windowCount" => engram.Activity?.WindowCount is { } windowCount ? (windowCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "dim") : null,
            _ => null,
        };
    }

    private static string? ResolveEngramStateColor(EngramState state, EngramStateColorsJsonConfig? colors) => state switch
    {
        EngramState.Active => colors?.Active,
        EngramState.Idle => colors?.Idle,
        EngramState.Unreachable => colors?.Unreachable,
        EngramState.Unavailable => colors?.Unavailable,
        _ => null,
    };

    private static string? ResolveEngramFieldColor(string field, EngramFieldJsonConfig fieldConfig, EngramItemSettings? settings)
    {
        if (fieldConfig.Color is { Length: > 0 } explicitColor)
        {
            return explicitColor;
        }

        return field switch
        {
            "facts" => settings?.FactsColor is { Length: > 0 } fc ? fc : null,
            "verb" => settings?.VerbColor is { Length: > 0 } vc ? vc : null,
            _ => null,
        };
    }

    internal static Segment? BuildEngram(EngramProbeResult? engram, EngramItemSettings? settings = null)
    {
        if (engram is null)
        {
            return null;
        }

        IReadOnlyList<EngramFieldJsonConfig> fieldConfigs = settings?.Fields is { Count: > 0 } configured ? configured : DefaultEngramFields;
        var stateColor = ResolveEngramStateColor(engram.State, settings?.StateColors);

        var spans = new List<StyledSpan>();
        foreach (var fieldConfig in fieldConfigs)
        {
            if (fieldConfig.Field is not { Length: > 0 } field)
            {
                continue;
            }

            if (RenderEngramField(field, engram) is not { } rendered)
            {
                continue;
            }

            var plain = fieldConfig.Format is { Length: > 0 } format ? LeafItems.ApplyFormat(format, rendered.Plain) : rendered.Plain;
            var color = stateColor ?? ResolveEngramFieldColor(field, fieldConfig, settings) ?? rendered.DefaultColor;

            if (spans.Count > 0)
            {
                spans.Add(new StyledSpan(" ", BuildSpanMarkup(" ", null)));
            }

            spans.Add(new StyledSpan(plain, BuildSpanMarkup(plain, color)));
        }

        return spans.Count == 0 ? null : BuildCompoundSegment(spans);
    }

    // Facts + verb are two independent optional fields, same "two labels disambiguate two
    // values" reasoning as ResolveRateLimits — reuses the composite's plain text rather than
    // inventing a reduced scalar.
    internal static string? ResolveEngram(EngramProbeResult? engram) => BuildEngram(engram)?.Plain;

    internal const string VimFormat = "[{}]";

    internal static Segment? BuildVimMode(VimInfo? vim) =>
        ResolveVim(vim) is { } raw ? SingleColor("olive", LeafItems.ApplyFormat(VimFormat, raw)) : null;

    internal static string? ResolveVim(VimInfo? vim) =>
        string.IsNullOrEmpty(vim?.Mode) ? null : vim!.Mode;

    private static int RoundHalfToEven(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

    private static string Basename(string path)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/";
        }

        var idx = trimmed.LastIndexOf('/');
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
    }
}
