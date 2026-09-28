using System.Reflection;
using System.Text.RegularExpressions;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5/§6.5/§8: the single up-front resolution pass — one dictionary of every
/// item id's current value, built once per render before sizing begins, and shared by
/// <see cref="LeafItems"/>, <see cref="SizeResolver"/>, and <see cref="ColorResolution"/> alike so
/// nothing downstream re-fetches or re-derives a value. An id is collected — and therefore
/// resolved — whether or not a pane actually places it: a <c>colors</c>-table token's <c>from</c>
/// (§6.3), an inline rule's explicit <c>from</c>, a derived item's <see cref="PaneItem.From"/>, or
/// a link template's <c>{other-id}</c> placeholder (§3.2) may name a builtin no pane displays. A
/// builtin resolves through <see cref="ItemRegistry"/> regardless of placement; a <c>command</c>
/// item resolves only when some placed <see cref="PaneItem"/> actually carries it, since a
/// command has no registry entry to drive it independent of placement. A derived item (§8:
/// <see cref="PaneItem.From"/>/<see cref="PaneItem.Extract"/>/<see cref="PaneItem.Case"/>)
/// resolves in a final pass, once every builtin/command value above is settled.
///
/// §5: "any future construct that names an item by id is incomplete until it is added to this
/// list" — <see cref="ReferenceExtractors"/> is that list, kept as data rather than as inline
/// <c>foreach</c> bodies, so a new reference form (§4.2's argv placeholders, §3.3's compound-item
/// parts) is appended to the array instead of edited into <see cref="CollectIds"/>'s body. Defect
/// 11 was exactly this shape gone wrong: an enumeration that looked exhaustive until the config
/// surface grew past it.
/// </summary>
public static class ItemValueResolver
{
    /// <summary>
    /// Builtins-only, synchronous counterpart to <see cref="ResolveAsync"/> — same id collection
    /// and the same <see cref="ItemRegistry"/> resolution, minus command-item execution. For
    /// callers (tests, the fixpoint sizing harness) that need a values dictionary without paying
    /// for async command spawns.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Resolve(
        Pane root,
        ItemContext ctx,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens = null)
    {
        var items = new List<ScanEntry>();
        var colorExprs = new List<(ColorResolution.ColorExpr Expr, string Path)>();
        var colorValues = new List<(ColorResolution.ColorValue Value, string Path)>();
        Walk(root, "", items, colorExprs, colorValues);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in CollectIds(items, colorExprs, tokens))
        {
            if (ItemRegistry.Find(id) is { } def)
            {
                values[id] = def.ResolveValue(ctx);
            }
        }

        ResolveDerived(items, values);
        return values;
    }

    /// <summary>
    /// The production resolver: builtins as above, plus every placed <c>command</c> item, spawned
    /// concurrently (§5 gives each its own TTL/timeout — nothing serializes them against each
    /// other). <paramref name="tokens"/> is the parsed <c>colors</c> table, needed here only to
    /// widen id collection (§6.3), not to resolve any colour itself (§6.5 resolves colour
    /// separately, from this method's returned values).
    /// </summary>
    public static async Task<Resolution> ResolveAsync(
        Pane root,
        ItemContext ctx,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens,
        string? rawStdinJson,
        string cacheDir,
        string widthsDir,
        int? surfaceWidth,
        RenderNoteCollector notes)
    {
        var items = new List<ScanEntry>();
        var colorExprs = new List<(ColorResolution.ColorExpr Expr, string Path)>();
        var colorValues = new List<(ColorResolution.ColorValue Value, string Path)>();
        Walk(root, "", items, colorExprs, colorValues);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var pendingCommands = new List<ScanEntry>();

        foreach (var id in CollectIds(items, colorExprs, tokens))
        {
            if (ItemRegistry.Find(id) is { } def)
            {
                values[id] = def.ResolveValue(ctx);
                continue;
            }

            var placed = items.FirstOrDefault(e => e.Item.Id == id && e.Item.Command is { Count: > 0 });
            if (placed.Item is not null)
            {
                pendingCommands.Add(placed);
            }
        }

        // §4.2: an argv placeholder may only name a registry id, never another `command` item, so
        // every id a command's placeholders can legally reference is already settled in `values`
        // above, before any command spawns — one snapshot, handed to every command task alike,
        // rather than the mutating dictionary those spawns would otherwise race against.
        var placeholderValues = new Dictionary<string, string?>(values, StringComparer.Ordinal);
        var commandTasks = pendingCommands
            .Select(placed => (Id: placed.Item.Id!, Task: CommandProvider.ResolveAsync(
                placed.Item, rawStdinJson, ctx.Input.Cwd, cacheDir, widthsDir, surfaceWidth, placed.Eligible, placeholderValues, Array.Empty<string>(), notes)))
            .ToList();

        await Task.WhenAll(commandTasks.Select(t => t.Task)).ConfigureAwait(false);
        var unavailableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, task) in commandTasks)
        {
            values[id] = task.Result.Value;
            if (task.Result.Unavailable)
            {
                unavailableIds.Add(id);
            }
        }

        ResolveDerived(items, values);
        return new Resolution(values, unavailableIds);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.11.2: <see cref="ResolveAsync"/>'s full result — <see cref="Values"/>
    /// as the method always returned, plus <see cref="UnavailableIds"/>, the subset of command item
    /// ids that did not answer this render (§7's 150ms budget expired, the process exited nonzero,
    /// or it could not start, with no cached value to fall back on) rather than legitimately
    /// resolving to nothing. <see cref="PaneCollapse"/> is the one caller that reads
    /// <see cref="UnavailableIds"/>; every other caller of <see cref="ResolveAsync"/> just wants
    /// <see cref="Values"/>, the same as before this type existed.
    /// </summary>
    public readonly record struct Resolution(IReadOnlyDictionary<string, string?> Values, IReadOnlyCollection<string> UnavailableIds);

    // §9.4/§9.5: the JSON-Pointer path each entry was found at, alongside the same (Item, Eligible)
    // pair Walk always tracked — production ignores Path (every call site here passes "" as the
    // root and never reads it back), but it lets ScanReferences below reuse this exact walk instead
    // of a second traversal of the config tree.
    internal readonly record struct ScanEntry(PaneItem Item, bool Eligible, string Path);

    private static void Walk(
        Pane pane,
        string path,
        List<ScanEntry> items,
        List<(ColorResolution.ColorExpr Expr, string Path)> colorExprs,
        List<(ColorResolution.ColorValue Value, string Path)> colorValues)
    {
        var borderColorPath = path + "/border/color";
        colorExprs.Add((pane.Border.Color, borderColorPath));
        if (pane.Border.Color is ColorResolution.ColorExpr.Inline borderInline)
        {
            CollectRuleBranches(borderInline.Rule, borderColorPath, colorValues);
        }

        // §2.5.1 rule 2: a content-sized pane's width is derived from measuring its own content, so
        // its command items must be resolved with CLAUDE_TUI_LINE_PANE_WIDTH unset rather than
        // exporting a value that depends on what the export itself would return. Every other pane
        // kind is eligible and gets its prior render's stamped width (rule 1: spawned once per
        // render, reusing the first-pass grant rather than re-fetching across sizing passes).
        var eligible = !SizeResolver.IsContentSized(pane);
        for (var i = 0; i < pane.Items.Count; i++)
        {
            var item = pane.Items[i];
            var itemPath = $"{path}/items/{i}";
            items.Add(new ScanEntry(item, eligible, itemPath));
            if (item.Color is { } color)
            {
                var itemColorPath = itemPath + "/color";
                colorExprs.Add((color, itemColorPath));
                if (color is ColorResolution.ColorExpr.Inline itemInline)
                {
                    CollectRuleBranches(itemInline.Rule, itemColorPath, colorValues);
                }
            }
        }

        if (pane.Title is { } title)
        {
            var titlePath = path + "/title";
            items.Add(new ScanEntry(title, eligible, titlePath));
            if (title.Color is { } titleColor)
            {
                var titleColorPath = titlePath + "/color";
                colorExprs.Add((titleColor, titleColorPath));
                if (titleColor is ColorResolution.ColorExpr.Inline titleInline)
                {
                    CollectRuleBranches(titleInline.Rule, titleColorPath, colorValues);
                }
            }
        }

        for (var i = 0; i < pane.Children.Count; i++)
        {
            Walk(pane.Children[i], $"{path}/children/{i}", items, colorExprs, colorValues);
        }
    }

    // SPEC-44-color-token-in-rule-branches.md §4.3: a rule's branch colours (a threshold's/match's
    // `color`, a rule's `default`) are ColorValue, not ColorExpr — reachable from an inline rule
    // here, and separately from every `colors`-table token in ScanReferences, since a token's own
    // rule lives outside the pane tree this method walks. Path convention matches
    // ConfigCheck.CheckColorRuleLiterals's existing `{path}/thresholds/{i}/color` etc.
    private static void CollectRuleBranches(
        ColorResolution.ColorRule rule,
        string path,
        List<(ColorResolution.ColorValue Value, string Path)> colorValues)
    {
        if (rule.Thresholds is { } thresholds)
        {
            for (var i = 0; i < thresholds.Count; i++)
            {
                colorValues.Add((thresholds[i].Color, $"{path}/thresholds/{i}/color"));
            }
        }

        if (rule.Match is { } match)
        {
            for (var i = 0; i < match.Count; i++)
            {
                colorValues.Add((match[i].Color, $"{path}/match/{i}/color"));
            }
        }

        if (rule.Default is { } def)
        {
            colorValues.Add((def, path + "/default"));
        }
    }

    internal readonly record struct ScanContext(
        List<ScanEntry> Items,
        List<(ColorResolution.ColorExpr Expr, string Path)> ColorExprs,
        List<(ColorResolution.ColorValue Value, string Path)> ColorValues,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? Tokens);

    // Each row carries the MemberInfo(s) its Extract delegate reads alongside the delegate itself,
    // authored together in one literal, so SPEC-V2-FRAMEWORK.md §9.5.1's coverage test can verify
    // "this member is handled" structurally against Members instead of trusting a separately
    // hand-authored, drift-prone member-to-extractor map.
    internal readonly record struct ReferenceExtractor(IReadOnlyList<MemberInfo> Members, Func<ScanContext, IEnumerable<IdCandidate>> Extract);

    internal readonly record struct ColorTokenExtractor(IReadOnlyList<MemberInfo> Members, Func<ScanContext, IEnumerable<ColorTokenReference>> Extract);

    // §5's reference forms, one extractor per form. Each yields every id-occurrence that form's
    // config keys produce, self-tagged as a declaration or a reference (§9.5.1's IdCandidate) —
    // CollectIds below reads only the id and discards the rest; ScanReferences reads all of it.
    // Adding a form (§4.2's argv placeholders, §3.3's compound-item parts) means appending here,
    // not editing either consumer.
    internal static readonly IReadOnlyList<ReferenceExtractor> ReferenceExtractors = new[]
    {
        // A placed entry either declares its own id or names another one via an `item` selector —
        // §3 makes the two mutually exclusive, so this yields exactly one candidate or none.
        new ReferenceExtractor(
            new[] { typeof(PaneItem).GetProperty(nameof(PaneItem.Id))!, typeof(PaneItem).GetProperty(nameof(PaneItem.Item))! },
            ctx => ctx.Items.SelectMany(entry =>
                entry.Item.Id is { Length: > 0 } ownId
                    ? new[] { new IdCandidate(ownId, entry.Path + "/id", ReferenceKind.Declaration, null) }
                    : entry.Item.Item is { Length: > 0 } selector
                        ? new[] { new IdCandidate(selector, entry.Path + "/item", ReferenceKind.Reference, ReferenceForm.ItemSelector) }
                        : Array.Empty<IdCandidate>())),

        // A derived item's `from` (§8).
        new ReferenceExtractor(
            new[] { typeof(PaneItem).GetProperty(nameof(PaneItem.From))! },
            ctx => ctx.Items
                .Where(entry => entry.Item.From is { Length: > 0 })
                .Select(entry => new IdCandidate(entry.Item.From!, entry.Path + "/from", ReferenceKind.Reference, ReferenceForm.DerivedFrom))),

        // A link template's `{other-id}` placeholders (§3.2); `{}` is the item's own value, not a
        // reference, and is already excluded by LeafContent.LinkPlaceholderIds.
        new ReferenceExtractor(
            new[] { typeof(PaneItem).GetProperty(nameof(PaneItem.Link))! },
            ctx => ctx.Items
                .Where(entry => entry.Item.Link is { Length: > 0 })
                .SelectMany(entry => LeafContent.LinkPlaceholderIds(entry.Item.Link!)
                    .Select(id => new IdCandidate(id, entry.Path + "/link", ReferenceKind.Reference, ReferenceForm.LinkPlaceholder)))),

        // An inline colour rule's explicit `from` (§6.4).
        new ReferenceExtractor(
            new[] { typeof(ColorResolution.ColorRule).GetProperty(nameof(ColorResolution.ColorRule.From))! },
            ctx => ctx.ColorExprs
                .Select(t => (t.Path, Inline: t.Expr as ColorResolution.ColorExpr.Inline))
                .Where(t => t.Inline?.Rule.From is { Length: > 0 })
                .Select(t => new IdCandidate(t.Inline!.Rule.From!, t.Path + "/from", ReferenceKind.Reference, ReferenceForm.ColorFrom))),

        // A `colors`-table token's `from` (§6.3) — required on every token, so this widens id
        // collection regardless of whether the token is ever referenced via `@name`.
        new ReferenceExtractor(
            new[] { typeof(ColorResolution.ColorRule).GetProperty(nameof(ColorResolution.ColorRule.From))! },
            ctx => (ctx.Tokens?
                .Where(kv => kv.Value.From is { Length: > 0 })
                .Select(kv => new IdCandidate(kv.Value.From!, $"/colors/{kv.Key}/from", ReferenceKind.Reference, ReferenceForm.ColorFrom))
                ?? Enumerable.Empty<IdCandidate>())),

        // A `command` item's argv `{other-id}` placeholders (§4.2), sharing LinkPlaceholder's
        // vocabulary via ArgvPlaceholders.ReferencedIds. `{}` (self-reference) is excluded the same
        // way a link template's own-value placeholder is.
        new ReferenceExtractor(
            new[] { typeof(PaneItem).GetProperty(nameof(PaneItem.Command))! },
            ctx => ctx.Items
                .Where(entry => entry.Item.Command is { Count: > 0 })
                .SelectMany(entry => ArgvPlaceholders.ReferencedIds(entry.Item.Command!)
                    .Select(id => new IdCandidate(id, entry.Path + "/command", ReferenceKind.Reference, ReferenceForm.ArgvPlaceholder, entry.Item.Id)))),

        // §3.3: a compound part's `item` selector.
        new ReferenceExtractor(
            new[] { typeof(PaneItemPart).GetProperty(nameof(PaneItemPart.Item))! },
            ctx => ctx.Items
                .Where(entry => entry.Item.Parts is not null)
                .SelectMany(entry => entry.Item.Parts!
                    .Select((part, i) => (part, i))
                    .Where(t => t.part.Item is { Length: > 0 })
                    .Select(t => new IdCandidate(t.part.Item!, $"{entry.Path}/parts/{t.i}/item",
                        ReferenceKind.Reference, ReferenceForm.PartItemSelector)))),

        // §3.3: a compound part's `from`.
        new ReferenceExtractor(
            new[] { typeof(PaneItemPart).GetProperty(nameof(PaneItemPart.From))! },
            ctx => ctx.Items
                .Where(entry => entry.Item.Parts is not null)
                .SelectMany(entry => entry.Item.Parts!
                    .Select((part, i) => (part, i))
                    .Where(t => t.part.From is { Length: > 0 })
                    .Select(t => new IdCandidate(t.part.From!, $"{entry.Path}/parts/{t.i}/from",
                        ReferenceKind.Reference, ReferenceForm.DerivedFrom)))),
    };

    // An `@name` colour reference (§6.3) validates against the `colors` table's own keys, not
    // against an item id — a separate table from ReferenceExtractors, rather than a Kind on
    // IdCandidate, so that type never has to mean "an id or a colour-token name depending on
    // which Kind this is."
    internal static readonly IReadOnlyList<ColorTokenExtractor> ColorTokenExtractors = new[]
    {
        new ColorTokenExtractor(
            new[] { typeof(ColorResolution.ColorExpr.TokenRef).GetProperty(nameof(ColorResolution.ColorExpr.TokenRef.Name))! },
            ctx => ctx.ColorExprs
                .Select(t => (t.Path, TokenRef: t.Expr as ColorResolution.ColorExpr.TokenRef))
                .Where(t => t.TokenRef is not null)
                .Select(t => new ColorTokenReference(t.TokenRef!.Name, t.Path, InRuleBranch: false))),

        // SPEC-44-color-token-in-rule-branches.md §3.2/§4.3: a rule-branch colour's own @-token,
        // flagged InRuleBranch so ConfigCheck can layer the constant-token restriction on top of
        // the existence check the two extractors otherwise share.
        new ColorTokenExtractor(
            new[] { typeof(ColorResolution.ColorValue.TokenRef).GetProperty(nameof(ColorResolution.ColorValue.TokenRef.Name))! },
            ctx => ctx.ColorValues
                .Select(t => (t.Path, TokenRef: t.Value as ColorResolution.ColorValue.TokenRef))
                .Where(t => t.TokenRef is not null)
                .Select(t => new ColorTokenReference(t.TokenRef!.Name, t.Path, InRuleBranch: true))),
    };

    private static IReadOnlyList<string> CollectIds(
        List<ScanEntry> items,
        List<(ColorResolution.ColorExpr Expr, string Path)> colorExprs,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens)
    {
        var ctx = new ScanContext(items, colorExprs, new List<(ColorResolution.ColorValue Value, string Path)>(), tokens);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var extractor in ReferenceExtractors)
        {
            foreach (var candidate in extractor.Extract(ctx))
            {
                ids.Add(candidate.Id);
            }
        }

        return ids.ToList();
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4: every placed item, path-tagged, reusing <see cref="Walk"/> instead
    /// of a second traversal of the pane tree. <c>--check</c>'s command-shape diagnostics (§4.1) walk
    /// items directly rather than ids, so they do not belong on <see cref="ReferenceScan"/>.
    /// </summary>
    internal static IReadOnlyList<(PaneItem Item, string Path)> WalkItems(Pane root, string rootPath)
    {
        var items = new List<ScanEntry>();
        var colorExprs = new List<(ColorResolution.ColorExpr Expr, string Path)>();
        var colorValues = new List<(ColorResolution.ColorValue Value, string Path)>();
        Walk(root, rootPath, items, colorExprs, colorValues);
        return items.Select(e => (e.Item, e.Path)).ToList();
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4/§9.5: <c>--check</c>'s id diagnostics, derived from the exact same
    /// reference forms as <see cref="ReferenceExtractors"/> — a form is taught to one place, not two
    /// — but path-tagged instead of flattened, since a diagnostic needs to say where the bad
    /// reference lives, not just that one exists somewhere. A placed entry's own declared
    /// <see cref="PaneItem.Id"/> (command or derived) is never itself a reference to validate —
    /// nothing looks it up, it defines it — so it is reported back separately as
    /// <see cref="ReferenceScan.SelfDeclaredIds"/> rather than as an <see cref="IdReference"/>.
    /// <paramref name="rootPath"/> is <c>/surface/pane</c> when a surface pane tree is configured,
    /// or <c>""</c> for the implicit single-root-pane-from-top-level-items case (§8), whose items
    /// live at <c>/items/N</c> instead.
    /// </summary>
    internal static ReferenceScan ScanReferences(Pane root, string rootPath, IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens)
    {
        var items = new List<ScanEntry>();
        var colorExprs = new List<(ColorResolution.ColorExpr Expr, string Path)>();
        var colorValues = new List<(ColorResolution.ColorValue Value, string Path)>();
        Walk(root, rootPath, items, colorExprs, colorValues);

        // §4.3: the `colors`-table tokens' own rule branches live outside the pane tree Walk
        // traverses, so they're collected here, once per token, using the same path convention
        // ConfigCheck.CheckColorLiterals already uses for `/colors/{name}` diagnostics.
        if (tokens is not null)
        {
            foreach (var (name, rule) in tokens)
            {
                CollectRuleBranches(rule, $"/colors/{name}", colorValues);
            }
        }

        var ctx = new ScanContext(items, colorExprs, colorValues, tokens);

        var references = new List<IdReference>();
        var selfDeclared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extractor in ReferenceExtractors)
        {
            foreach (var candidate in extractor.Extract(ctx))
            {
                if (candidate.Kind == ReferenceKind.Declaration)
                {
                    selfDeclared.Add(candidate.Id);
                }
                else
                {
                    references.Add(new IdReference(candidate.Id, candidate.Path, candidate.Form!.Value, candidate.OwnerId));
                }
            }
        }

        var derivedItemIds = new HashSet<string>(StringComparer.Ordinal);
        var commandItemIds = new HashSet<string>(StringComparer.Ordinal);
        var compoundItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in items)
        {
            if (entry.Item.Id is not { Length: > 0 } ownId)
            {
                continue;
            }

            if (entry.Item.From is { Length: > 0 })
            {
                derivedItemIds.Add(ownId);
            }

            if (entry.Item.Command is { Count: > 0 })
            {
                commandItemIds.Add(ownId);
            }

            // §3.3/§12.8: a compound declares an id but produces no scalar value, so nothing may
            // source `from` it. Deliberately NOT folded into derivedItemIds — §6.2.
            if (entry.Item.Parts is not null)
            {
                compoundItemIds.Add(ownId);
            }
        }

        var colorTokenReferences = new List<ColorTokenReference>();
        foreach (var extractor in ColorTokenExtractors)
        {
            colorTokenReferences.AddRange(extractor.Extract(ctx));
        }

        return new ReferenceScan(references, selfDeclared, derivedItemIds, commandItemIds, compoundItemIds, colorTokenReferences, colorExprs);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §8: computes every derived item's (<see cref="PaneItem.From"/>) value
    /// from a snapshot of <paramref name="values"/> taken before any derived result is written
    /// back, then merges all of them in afterward. This makes chaining — a derived item's
    /// <see cref="PaneItem.From"/> naming another derived item — structurally impossible rather
    /// than merely discouraged: regardless of declaration order, every derived item can only ever
    /// see a builtin/command value.
    /// </summary>
    private static void ResolveDerived(List<ScanEntry> items, Dictionary<string, string?> values)
    {
        var snapshot = new Dictionary<string, string?>(values, StringComparer.Ordinal);
        var derived = new List<(string Id, string? Value)>();

        foreach (var entry in items)
        {
            var item = entry.Item;
            if (item.From is not { Length: > 0 } from || item.Id is not { Length: > 0 } id)
            {
                continue;
            }

            var value = snapshot.GetValueOrDefault(from);
            if (value is not null && item.Extract is { Length: > 0 } pattern)
            {
                value = ExtractValue(value, pattern);
            }

            if (value is not null)
            {
                value = ApplyCase(value, item.Case);
            }

            derived.Add((id, value));
        }

        foreach (var (id, value) in derived)
        {
            values[id] = value;
        }
    }

    // §8: the first capture group when the pattern has one, otherwise the whole match; no match
    // suppresses the derived item entirely (null), the same convention as an absent field (§3).
    // internal: SPEC-85 §5.3 step 1 reuses this for a compound part's `from`/`extract`, rather
    // than re-implementing the extract/case rule a second time.
    internal static string? ExtractValue(string source, string pattern)
    {
        var match = Regex.Match(source, pattern);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private enum CaseMode { Upper, Lower }

    private static readonly (string Token, CaseMode Value)[] CaseAccepted =
    {
        ("upper", CaseMode.Upper),
        ("lower", CaseMode.Lower),
    };

    internal static IReadOnlyList<string> CaseAcceptedTokens { get; } = CaseAccepted.Select(a => a.Token).ToArray();

    private static CaseMode? ParseCaseMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in CaseAccepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    // §8: any case value other than "upper"/"lower" passes the text through unchanged.
    // internal: SPEC-85 §5.3 step 1 reuses this for a compound part's `case`, same reason as
    // ExtractValue above.
    internal static string ApplyCase(string value, string? caseMode) => ParseCaseMode(caseMode) switch
    {
        CaseMode.Upper => value.ToUpperInvariant(),
        CaseMode.Lower => value.ToLowerInvariant(),
        _ => value,
    };

    /// <summary>
    /// True when <paramref name="caseMode"/> was present but is neither "upper" nor "lower" —
    /// distinct from an absent value, both of which <see cref="ApplyCase"/> passes through
    /// unchanged. §9.4's config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    internal static bool IsUnrecognizedCase(string? caseMode) => !string.IsNullOrWhiteSpace(caseMode) && ParseCaseMode(caseMode) is null;
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.4.1: which of §5's reference forms an <see cref="IdReference"/> came
/// from — severity groups by what a dangling reference costs the config, not by syntax:
/// <see cref="ItemSelector"/> and <see cref="DerivedFrom"/> delete the item outright (error,
/// <c>unknown-item-id</c>); <see cref="LinkPlaceholder"/> and <see cref="ColorFrom"/> (an inline
/// rule's or a <c>colors</c>-table token's <c>from</c> alike) degrade to plain text or a fallback
/// colour (warning). <see cref="ArgvPlaceholder"/> (§4.2) is data handed to another process with no
/// defined meaning when unmet, so it errors like <see cref="ItemSelector"/>/<see cref="DerivedFrom"/>
/// despite sharing its syntax with <see cref="LinkPlaceholder"/> — same walk, different severity
/// (§4.2's own ruling on why). An <c>@name</c> colour reference is a distinct namespace — a
/// <c>colors</c>-table key, not an item id — and is therefore not a
/// <see cref="ReferenceForm"/>/<see cref="IdReference"/> at all; see <see cref="ColorTokenReference"/>.
/// </summary>
internal enum ReferenceForm
{
    ItemSelector,
    DerivedFrom,
    LinkPlaceholder,
    ColorFrom,
    ArgvPlaceholder,
    PartItemSelector,
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.5.1: which of the two things an <see cref="IdCandidate"/> is — the
/// record's own discriminator rather than a nullable <see cref="ReferenceForm"/>, so a consumer
/// must branch on it explicitly instead of a null check quietly standing in for "this declares an
/// id rather than referencing one."
/// </summary>
internal enum ReferenceKind
{
    Declaration,
    Reference,
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.5.1: one id occurrence yielded by
/// <see cref="ItemValueResolver.ReferenceExtractors"/> — either a placed entry declaring
/// <see cref="Id"/> as its own, or some construct naming <see cref="Id"/> as a reference to
/// something else, tagged with the JSON Pointer to where it was found. Value resolution
/// (<see cref="ItemValueResolver.Resolve"/>/<see cref="ItemValueResolver.ResolveAsync"/>) reads
/// only <see cref="Id"/> and discards the rest — a declaration and a reference both name an id that
/// needs a resolved value, so resolution doesn't care which. <c>--check</c> (§9.4) is the consumer
/// that does: a declaration is never itself invalid, while a reference needs validating and, when
/// dangling, needs <see cref="Form"/> to pick a severity (§9.4.1). <see cref="Form"/> is non-null
/// exactly when <see cref="Kind"/> is <see cref="ReferenceKind.Reference"/>, enforced in the
/// constructor rather than left to callers to keep straight.
/// </summary>
internal readonly record struct IdCandidate
{
    public string Id { get; }
    public string Path { get; }
    public ReferenceKind Kind { get; }
    public ReferenceForm? Form { get; }

    /// <summary>
    /// The id of the item this reference lives inside, when the extractor has one in scope —
    /// null for extractors with no owning item (e.g. a <c>colors</c>-table token's <c>from</c>).
    /// Lets a self-naming reference (§4.2's named self-reference: a command item's argv
    /// placeholder naming its own id) be told apart from one naming a different item of the
    /// same kind.
    /// </summary>
    public string? OwnerId { get; }

    public IdCandidate(string id, string path, ReferenceKind kind, ReferenceForm? form, string? ownerId = null)
    {
        if ((kind == ReferenceKind.Reference) != (form is not null))
        {
            throw new ArgumentException(
                $"{nameof(form)} must be non-null exactly when {nameof(kind)} is {nameof(ReferenceKind.Reference)}.",
                nameof(form));
        }

        Id = id;
        Path = path;
        Kind = kind;
        Form = form;
        OwnerId = ownerId;
    }
}

/// <summary>
/// One item-id reference found by <see cref="ItemValueResolver.ScanReferences"/> — always an item
/// id, tagged with where it lives (a JSON Pointer) and which config construct it came from.
/// <c>--check</c> (§9.4) needs both to place a diagnostic and to pick its severity, since §9.4.1
/// makes severity a per-form question, not a single verdict for "unknown id". A <c>colors</c>-table
/// key (<c>@name</c>) is never one of these — see <see cref="ColorTokenReference"/> — so a lookup
/// against <see cref="ReferenceScan.SelfDeclaredIds"/>/the item registry can never be handed the
/// wrong namespace by mistake.
/// </summary>
internal readonly record struct IdReference(string Id, string Path, ReferenceForm Form, string? OwnerId = null);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6.3: one <c>@name</c> colour reference found by
/// <see cref="ItemValueResolver.ScanReferences"/>. <see cref="Name"/> is a <c>colors</c>-table key,
/// not an item id — a structurally different type from <see cref="IdReference"/> so a dangling-name
/// check can never be pointed at <see cref="ReferenceScan.SelfDeclaredIds"/>/the item registry by
/// mistake; it validates against the parsed <c>colors</c> table's own keys instead.
/// <see cref="InRuleBranch"/> is true for a <see cref="ColorResolution.ColorValue.TokenRef"/> (a
/// rule branch's <c>color</c>, or a rule's <c>default</c>) and false for a top-position
/// <see cref="ColorResolution.ColorExpr.TokenRef"/> — SPEC-44-color-token-in-rule-branches.md §3.2
/// restricts only the former to naming a constant rule.
/// </summary>
internal readonly record struct ColorTokenReference(string Name, string Path, bool InRuleBranch);

/// <summary>
/// The result of one <see cref="ItemValueResolver.ScanReferences"/> pass: every item-id reference
/// that needs validating (<see cref="References"/>); every <c>@name</c> colour-token reference,
/// kept separate because it validates against a different namespace
/// (<see cref="ColorTokenReferences"/>); every id a placed entry declares as its own (never itself
/// invalid); which of those self-declared ids belong to a <em>derived</em> item specifically, so a
/// <see cref="ReferenceForm.DerivedFrom"/> reference landing on one of them is
/// <c>from-derived-source</c> (§9.4.1) rather than <c>unknown-item-id</c> — the id isn't unknown,
/// §3.2.1 just forbids naming it as a source; <see cref="CommandItemIds"/>, the same idea for a
/// <see cref="ReferenceForm.ArgvPlaceholder"/> landing on a <c>command</c> item
/// (<c>placeholder-command-source</c>, §4.2); and the raw, path-tagged colour expressions the same
/// walk collected — <c>--check</c>'s colour-literal diagnostics reuse <see cref="ColorExprs"/>
/// rather than re-walking.
/// </summary>
internal readonly record struct ReferenceScan(
    IReadOnlyList<IdReference> References,
    IReadOnlyCollection<string> SelfDeclaredIds,
    IReadOnlyCollection<string> DerivedItemIds,
    IReadOnlyCollection<string> CommandItemIds,
    IReadOnlyCollection<string> CompoundItemIds,
    IReadOnlyList<ColorTokenReference> ColorTokenReferences,
    IReadOnlyList<(ColorResolution.ColorExpr Expr, string Path)> ColorExprs);
