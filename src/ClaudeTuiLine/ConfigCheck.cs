using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>SPEC-V2-FRAMEWORK.md §9.4/§9.6: a config problem's severity — see §9.6.1's code registry.</summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.6: one entry of <c>--check</c>'s <c>diagnostics</c> array. <see cref="Code"/>
/// is a permanent surface (§9.6.1's registry) — never invented here ad hoc.
/// </summary>
public sealed record Diagnostic(string Path, DiagnosticSeverity Severity, string Code, string Message);

/// <summary>SPEC-V2-FRAMEWORK.md §9.6: the JSON projection of one <see cref="Diagnostic"/>.</summary>
public sealed record DiagnosticJson(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>SPEC-V2-FRAMEWORK.md §9.6: <c>--check --json</c>'s success/checked shape.</summary>
public sealed record CheckResultJson(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<DiagnosticJson> Diagnostics);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.6: the failure envelope for exit 2 (<c>"usage"</c>) and exit 3
/// (<c>"config-unreadable"</c>) — a distinct shape from <see cref="CheckResultJson"/> so that
/// <c>diagnostics</c> is structurally absent rather than present as an empty array.
/// </summary>
public sealed record CheckFailureJson(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path,
    [property: JsonPropertyName("message")] string Message);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CheckResultJson))]
[JsonSerializable(typeof(CheckFailureJson))]
[JsonSerializable(typeof(PaneItemPartJsonConfig))]
public partial class CheckJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.4: <c>--check</c>'s config diagnostics. §9.5 requires every id diagnostic
/// to come from <see cref="ItemValueResolver.ScanReferences"/> rather than a second walk; §9.8
/// requires the structural size diagnostics to call <see cref="SizeResolver"/>'s own boundary-cost
/// arithmetic rather than transcribe it. This never touches a width — no diagnostic here may depend
/// on <c>COLUMNS</c> or resolved sizes (§9.8).
/// </summary>
public static class ConfigChecker
{
    public static IReadOnlyList<Diagnostic> Check(UserConfig? config)
    {
        var topLevel = ConfigLoader.ResolveTopLevel(config);
        var root = ConfigLoader.ResolveRootPane(config, topLevel);
        var rootPath = config?.Surface?.Pane is not null ? "/surface/pane" : "";
        var scan = ItemValueResolver.ScanReferences(root, rootPath, topLevel.Colors);

        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(CheckReferences(scan, topLevel.Colors));
        diagnostics.AddRange(CheckColorLiterals(scan, topLevel.Colors, topLevel.ColorSystem));
        diagnostics.AddRange(CheckCommandShape(root, rootPath));
        diagnostics.AddRange(CheckArgvPlaceholders(root, rootPath));
        diagnostics.AddRange(CheckEnums(config));
        diagnostics.AddRange(CheckItemSettings(config));
        diagnostics.AddRange(CheckLeafOnlyKeysOnSplits(config));
        diagnostics.AddRange(CheckKeyNotApplicable(config));
        diagnostics.AddRange(CheckBorderInsideOnLeaf(config));
        diagnostics.AddRange(CheckCollapseNotSurfaceLevel(config));
        diagnostics.AddRange(CheckCollapsedEdgeConflicts(root, rootPath, topLevel.Collapse));
        diagnostics.AddRange(CheckStructuralSizes(root, rootPath, topLevel.Collapse));
        diagnostics.AddRange(CheckOverflowPosition(root, rootPath));
        diagnostics.AddRange(CheckEmptyPanes(root, rootPath));
        diagnostics.AddRange(CheckUnknownKeys(config));
        diagnostics.AddRange(CheckMaxLines(root, rootPath));
        diagnostics.AddRange(CheckCompoundParts(config));
        diagnostics.AddRange(CheckPaneIds(root, rootPath, scan.SelfDeclaredIds));
        diagnostics.AddRange(CheckTitleWithoutBorder(root, rootPath));
        diagnostics.AddRange(CheckSelfAlign(root, rootPath, topLevel.Collapse));
        return diagnostics;
    }

    // ---- §4.0.1: maxLines is an opt-in ceiling — zero or negative has no meaningful reading ----

    private static IEnumerable<Diagnostic> CheckMaxLines(Pane root, string rootPath)
    {
        foreach (var (item, path) in ItemValueResolver.WalkItems(root, rootPath))
        {
            if (item.MaxLines is int maxLines && maxLines <= 0)
            {
                yield return new Diagnostic(path + "/maxLines", DiagnosticSeverity.Error, "invalid-max-lines",
                    $"maxLines ({maxLines}) must be a positive integer");
            }
        }
    }

    // ---- §9.4.1/§9.5: id and colour-token references, via ItemValueResolver.ScanReferences ----

    private static IEnumerable<Diagnostic> CheckReferences(ReferenceScan scan, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        foreach (var reference in scan.References)
        {
            var known = scan.SelfDeclaredIds.Contains(reference.Id) || ItemRegistry.Find(reference.Id) is not null;
            if (!known)
            {
                var (severity, code) = reference.Form switch
                {
                    ReferenceForm.ItemSelector => (DiagnosticSeverity.Error, "unknown-item-id"),
                    ReferenceForm.DerivedFrom => (DiagnosticSeverity.Error, "unknown-item-id"),
                    ReferenceForm.LinkPlaceholder => (DiagnosticSeverity.Warning, "unknown-link-target"),
                    ReferenceForm.ColorFrom => (DiagnosticSeverity.Warning, "unknown-color-source"),
                    ReferenceForm.ArgvPlaceholder => (DiagnosticSeverity.Error, "unknown-item-id"),
                    ReferenceForm.PartItemSelector => (DiagnosticSeverity.Error, "unknown-item-id"),
                    _ => (DiagnosticSeverity.Error, "unknown-item-id"),
                };
                yield return new Diagnostic(reference.Path, severity, code, $"no item named '{reference.Id}'");
            }
            else if (reference.Form == ReferenceForm.DerivedFrom && scan.CompoundItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "from-compound-source",
                    $"'{reference.Id}' is a compound item; a compound item has no single value for 'from' to read");
            }
            else if (reference.Form == ReferenceForm.DerivedFrom && scan.DerivedItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "from-derived-source",
                    $"'{reference.Id}' is itself a derived item; a derived item cannot source from another derived item");
            }
            else if (reference.Form == ReferenceForm.ArgvPlaceholder && scan.DerivedItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "placeholder-derived-source",
                    $"'{reference.Id}' is a derived item; an argv placeholder may not name a derived item");
            }
            else if (reference.Form == ReferenceForm.ArgvPlaceholder && scan.CompoundItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "placeholder-compound-source",
                    $"'{reference.Id}' is a compound item; an argv placeholder may not name a compound item");
            }
            else if (reference.Form == ReferenceForm.PartItemSelector && scan.CompoundItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "part-compound-source",
                    $"'{reference.Id}' is a compound item; a part may not name a compound, because one compound inside another is the nesting §3.3 forbids");
            }
            else if (reference.Form == ReferenceForm.ColorFrom && scan.CompoundItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Warning, "color-from-compound-source",
                    $"'{reference.Id}' is a compound item; a compound has a colour per part and no single value for a colour rule to read");
            }
            else if (reference.Form == ReferenceForm.ArgvPlaceholder && string.Equals(reference.Id, reference.OwnerId, StringComparison.Ordinal))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "placeholder-self-reference",
                    $"'{reference.Id}' names this command item's own id, but its output does not exist until it has run");
            }
            else if (reference.Form == ReferenceForm.ArgvPlaceholder && scan.CommandItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "placeholder-command-source",
                    $"'{reference.Id}' is a command item; a command item's argv placeholder may not name another command item");
            }
        }

        foreach (var tokenRef in scan.ColorTokenReferences)
        {
            if (!tokens.TryGetValue(tokenRef.Name, out var rule))
            {
                yield return new Diagnostic(tokenRef.Path, DiagnosticSeverity.Warning, "unknown-color-token",
                    $"no color named '{tokenRef.Name}' in the colors table");
                continue;
            }

            // SPEC-44-color-token-in-rule-branches.md §3.2: a rule-branch token is legal only when
            // it names a constant rule — separate from the existence check above, and only run for
            // candidates that originated in ColorValue (branch) position.
            if (tokenRef.InRuleBranch && !IsConstantColorRule(rule))
            {
                yield return new Diagnostic(tokenRef.Path, DiagnosticSeverity.Warning, "non-constant-color-token",
                    $"colour token '@{tokenRef.Name}' is used in a rule branch at {tokenRef.Path}, so it must be a constant colour (a 'default' literal with no 'from', 'thresholds', or 'match')");
            }
        }
    }

    private static bool IsConstantColorRule(ColorResolution.ColorRule rule) =>
        string.IsNullOrEmpty(rule.From) &&
        rule.Thresholds is not { Count: > 0 } &&
        rule.Match is not { Count: > 0 } &&
        rule.Default is ColorResolution.ColorValue.Literal;

    // ---- §9.4.1/§6/§6.2: literal colour values, reusing the ColorExprs the same walk already collected ----

    private static IEnumerable<Diagnostic> CheckColorLiterals(ReferenceScan scan, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens, ColorSystemSupport colorSystem)
    {
        foreach (var (expr, path) in scan.ColorExprs)
        {
            foreach (var d in CheckColorExprLiterals(expr, path, colorSystem))
            {
                yield return d;
            }
        }

        foreach (var (name, rule) in tokens)
        {
            foreach (var d in CheckColorRuleLiterals(rule, $"/colors/{name}", colorSystem))
            {
                yield return d;
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckColorExprLiterals(ColorResolution.ColorExpr expr, string path, ColorSystemSupport colorSystem)
    {
        switch (expr)
        {
            case ColorResolution.ColorExpr.Literal lit:
                foreach (var d in CheckLiteralSpec(lit.Spec, path, colorSystem))
                {
                    yield return d;
                }

                break;
            case ColorResolution.ColorExpr.Inline inline:
                foreach (var d in CheckColorRuleLiterals(inline.Rule, path, colorSystem))
                {
                    yield return d;
                }

                break;
        }
    }

    private static IEnumerable<Diagnostic> CheckColorRuleLiterals(ColorResolution.ColorRule rule, string path, ColorSystemSupport colorSystem)
    {
        // A ColorValue.TokenRef branch has no literal spec of its own to check against the color
        // system — its own colour system exposure is checked wherever the constant rule it points
        // at is walked. Only the Literal case has a spec string here.
        if (rule.Default is ColorResolution.ColorValue.Literal defLit && defLit.Spec.Length > 0)
        {
            foreach (var d in CheckLiteralSpec(defLit.Spec, path + "/default", colorSystem))
            {
                yield return d;
            }
        }

        if (rule.Thresholds is { } thresholds)
        {
            for (var i = 0; i < thresholds.Count; i++)
            {
                if (thresholds[i].Color is ColorResolution.ColorValue.Literal thresholdLit && thresholdLit.Spec.Length > 0)
                {
                    foreach (var d in CheckLiteralSpec(thresholdLit.Spec, $"{path}/thresholds/{i}/color", colorSystem))
                    {
                        yield return d;
                    }
                }
            }
        }

        if (rule.Match is { } match)
        {
            for (var i = 0; i < match.Count; i++)
            {
                if (match[i].Color is ColorResolution.ColorValue.Literal matchLit && matchLit.Spec.Length > 0)
                {
                    foreach (var d in CheckLiteralSpec(matchLit.Spec, $"{path}/match/{i}/color", colorSystem))
                    {
                        yield return d;
                    }
                }
            }
        }
    }

    // §6.2.1: every literal form names a minimum color system it needs — hex is truecolor; a bare
    // palette index ≤15 is standard, ≥16 is 256; a name is standard only if it's one of the sixteen
    // ANSI standard names (STATUS.md), else 256, since anything outside that closed set is only
    // "verified to parse", not verified to be part of the safe standard-16 palette. A spec that
    // resolves to Color.Default (e.g. "default"/"dim"/"bold" — decoration, not a palette index) has
    // no palette dependency and so never needs more than standard.
    private static IEnumerable<Diagnostic> CheckLiteralSpec(string spec, string path, ColorSystemSupport colorSystem)
    {
        var resolved = ColorResolution.ResolveLiteral(spec);
        if (resolved is null)
        {
            yield return UnknownColor(path, spec);
            yield break;
        }

        var minimum = MinimumColorSystem(spec.Trim(), resolved.Value);
        if (ColorSystemRank(colorSystem) < ColorSystemRank(minimum))
        {
            yield return new Diagnostic(path, DiagnosticSeverity.Warning, "color-down-converted",
                $"'{spec}' is a {LiteralTierLabel(minimum)} literal; this terminal's color system ({PaletteLabel(colorSystem)}) will approximate it to the nearest supported color");
        }
    }

    private static ColorSystemSupport MinimumColorSystem(string trimmed, Color resolved)
    {
        if (resolved == Color.Default)
        {
            return ColorSystemSupport.Standard;
        }

        if (trimmed.StartsWith('#'))
        {
            return ColorSystemSupport.TrueColor;
        }

        if (int.TryParse(trimmed, out var index))
        {
            return index <= 15 ? ColorSystemSupport.Standard : ColorSystemSupport.EightBit;
        }

        return ColorResolution.StandardColorNames.Contains(trimmed) ? ColorSystemSupport.Standard : ColorSystemSupport.EightBit;
    }

    // Spectre's own ColorSystemSupport ordinal order is an implementation detail of a library we
    // don't control; this ranking is ours, so a comparison here can't silently break under an
    // upstream reorder.
    private static int ColorSystemRank(ColorSystemSupport system) => system switch
    {
        ColorSystemSupport.Standard => 0,
        ColorSystemSupport.EightBit => 1,
        ColorSystemSupport.TrueColor => 2,
        _ => 0,
    };

    private static string LiteralTierLabel(ColorSystemSupport minimum) => minimum switch
    {
        ColorSystemSupport.TrueColor => "truecolor",
        ColorSystemSupport.EightBit => "256-color",
        _ => "standard",
    };

    private static string PaletteLabel(ColorSystemSupport system) => system switch
    {
        ColorSystemSupport.Standard => "16 standard colors",
        ColorSystemSupport.EightBit => "256-color palette",
        ColorSystemSupport.TrueColor => "truecolor",
        _ => system.ToString(),
    };

    private static Diagnostic UnknownColor(string path, string spec) =>
        new(path, DiagnosticSeverity.Error, "unknown-color", $"'{spec}' is not a recognized color");

    // ---- §4.1: command shape / shell argv, via a plain item walk (not an id reference) ----

    private static IEnumerable<Diagnostic> CheckCommandShape(Pane root, string rootPath)
    {
        foreach (var (item, path) in ItemValueResolver.WalkItems(root, rootPath))
        {
            if (item.Command is not { Count: > 0 } command)
            {
                continue;
            }

            if (item.Shell)
            {
                if (command.Count > 1)
                {
                    yield return new Diagnostic(path + "/command", DiagnosticSeverity.Error, "command-shell-argv",
                        "shell:true only runs the first argv element; the remaining elements would be silently dropped");
                }
            }
            else if (command.Count == 1 && command[0].Any(char.IsWhiteSpace))
            {
                yield return new Diagnostic(path + "/command", DiagnosticSeverity.Error, "command-shape",
                    $"'{command[0]}' contains whitespace but shell is not true; it will run as a single binary name, not split into arguments");
            }
        }
    }

    // ---- §4.2.1/§4.2: argv-placeholder declaration faults not covered by the generic id-reference
    // pipeline above — self-reference has no id to validate, and env-collision is a relationship
    // between two references rather than a property of either one alone ----

    private static IEnumerable<Diagnostic> CheckArgvPlaceholders(Pane root, string rootPath)
    {
        foreach (var (item, path) in ItemValueResolver.WalkItems(root, rootPath))
        {
            if (item.Command is not { Count: > 0 } command)
            {
                continue;
            }

            if (ArgvPlaceholders.HasSelfReference(command))
            {
                yield return new Diagnostic(path + "/command", DiagnosticSeverity.Error, "placeholder-self-reference",
                    "'{}' names this command item's own output, which does not exist until it has run");
            }

            // §4.2: only shell:true exports referenced values into the environment, where two ids
            // mangling to the same CLAUDE_TUI_LINE_VAL_<ID> name would silently overwrite each other.
            if (!item.Shell)
            {
                continue;
            }

            var collisions = ArgvPlaceholders.ReferencedIds(command)
                .GroupBy(ArgvPlaceholders.EnvVarNameFor, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);

            foreach (var group in collisions)
            {
                var ids = string.Join(", ", group.Select(id => $"'{id}'"));
                yield return new Diagnostic(path + "/command", DiagnosticSeverity.Error, "placeholder-env-collision",
                    $"{ids} all mangle to the environment variable {group.Key}; the script would only ever see one of them");
            }
        }
    }

    // ---- §9.4.1: unknown enum values — any key with a closed value set, one shared code across all of them ----

    // §1.1.1: every enumerable kind's accepted-token list now lives with its parser (see e.g.
    // BorderStyleParsing.AcceptedTokens) so the diagnostic reads the same object the parser looks
    // up, instead of a second hand-copied list. `size` has no closed token set — a mix of literals
    // and form descriptions — so it stays a plain array here, per §1.1.1.
    internal static readonly string[] SizeValues = { "an integer", "a percentage", "content", "fill", "auto" };

    private static IEnumerable<Diagnostic> CheckEnums(UserConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(config?.Border?.Style) && !BorderStyleParsing.TryParse(config.Border.Style!, out _))
        {
            yield return UnknownEnumValue("/border/style", config.Border.Style, "style", BorderStyleParsing.AcceptedTokens);
        }

        if (ConfigLoader.IsUnrecognizedColorSystem(config?.ColorSystem))
        {
            yield return UnknownEnumValue("/colorSystem", config?.ColorSystem, "colorSystem", ConfigLoader.ColorSystemAcceptedTokens);
        }

        if (config?.Surface?.Pane is { } surfacePane)
        {
            foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
            {
                foreach (var d in CheckPaneEnums(pane, path))
                {
                    yield return d;
                }
            }
        }
        else if (config?.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var d in CheckItemEnums(items[i], $"/items/{i}"))
                {
                    yield return d;
                }
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckPaneEnums(PaneConfig pane, string path)
    {
        if (SizeResolver.IsUnrecognizedSize(pane.Size))
        {
            yield return UnknownEnumValue(path + "/size", pane.Size, "size", SizeValues);
        }

        if (SizeResolver.IsDeprecatedSizeAlias(pane.Size))
        {
            yield return new Diagnostic(path + "/size", DiagnosticSeverity.Warning, "deprecated-size-alias",
                "\"auto\" resolves to \"fill\"; \"content\" is the separate value that sizes a pane to its content");
        }

        if (PaneValignParsing.IsUnrecognized(pane.Valign))
        {
            yield return UnknownEnumValue(path + "/valign", pane.Valign, "valign", PaneValignParsing.AcceptedTokens);
        }

        if (PaneAlignParsing.IsUnrecognized(pane.Align))
        {
            yield return UnknownEnumValue(path + "/align", pane.Align, "align", PaneAlignParsing.AcceptedTokens);
        }

        if (OverflowModeParsing.IsUnrecognized(pane.Overflow))
        {
            yield return UnknownEnumValue(path + "/overflow", pane.Overflow, "overflow", OverflowModeParsing.AcceptedTokens);
        }

        if (!string.IsNullOrWhiteSpace(pane.Border?.Style) && !BorderStyleParsing.TryParse(pane.Border.Style!, out _))
        {
            yield return UnknownEnumValue(path + "/border/style", pane.Border.Style, "style", BorderStyleParsing.AcceptedTokens);
        }

        if (ConfigLoader.IsUnrecognizedSplit(pane.Split))
        {
            yield return UnknownEnumValue(path + "/split", pane.Split, "split", ConfigLoader.SplitAcceptedTokens);
        }

        if (PaneDistributeParsing.IsUnrecognized(pane.Distribute))
        {
            yield return UnknownEnumValue(path + "/distribute", pane.Distribute, "distribute", PaneDistributeParsing.AcceptedTokens);
        }

        if (PaneHeightParsing.IsUnrecognized(pane.Height))
        {
            yield return UnknownEnumValue(path + "/height", pane.Height, "height", PaneHeightParsing.AcceptedTokens);
        }

        if (pane.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var d in CheckItemEnums(items[i], $"{path}/items/{i}"))
                {
                    yield return d;
                }
            }
        }

        if (PaneSelfAlignParsing.IsUnrecognized(pane.SelfAlign))
        {
            yield return UnknownEnumValue(path + "/selfAlign", pane.SelfAlign, "selfAlign", PaneSelfAlignParsing.AcceptedTokens);
        }

        if (PaneTitleAlignParsing.IsUnrecognized(pane.TitleAlign))
        {
            yield return UnknownEnumValue(path + "/titleAlign", pane.TitleAlign, "titleAlign", PaneTitleAlignParsing.AcceptedTokens);
        }

        if (!string.IsNullOrWhiteSpace(pane.TitleAlign) && pane.Title is null)
        {
            yield return new Diagnostic(path + "/titleAlign", DiagnosticSeverity.Warning, "title-align-without-title",
                "'titleAlign' has no effect without a 'title' on this pane");
        }

        if (pane.Title is { } title)
        {
            foreach (var d in CheckItemEnums(title, path + "/title"))
            {
                yield return d;
            }

            if (!string.IsNullOrWhiteSpace(title.Overflow))
            {
                yield return new Diagnostic(path + "/title/overflow", DiagnosticSeverity.Warning, "title-overflow-ignored",
                    "a title is a single border line and always truncates; 'overflow' is ignored here");
            }

            if (title.MaxLines is int maxLines && maxLines != 1)
            {
                yield return new Diagnostic(path + "/title/maxLines", DiagnosticSeverity.Warning, "title-max-lines-ignored",
                    "a title is a single line; only the first line is drawn");
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckItemEnums(PaneItemJsonConfig item, string path)
    {
        if (OverflowModeParsing.IsUnrecognized(item.Overflow))
        {
            yield return UnknownEnumValue(path + "/overflow", item.Overflow, "overflow", OverflowModeParsing.AcceptedTokens);
        }

        if (ItemValueResolver.IsUnrecognizedCase(item.Case))
        {
            yield return UnknownEnumValue(path + "/case", item.Case, "case", ItemValueResolver.CaseAcceptedTokens);
        }

        if (item.Parts is { } parts)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (ItemValueResolver.IsUnrecognizedCase(parts[i].Case))
                {
                    yield return UnknownEnumValue($"{path}/parts/{i}/case", parts[i].Case, "case", ItemValueResolver.CaseAcceptedTokens);
                }
            }
        }
    }

    private static readonly string[] RateLimitsWindowsAcceptedTokens = { "5h", "7d", "both" };

    private static readonly string[] DirectoryOpenWithAcceptedTokens = { "files", "vscode" };

    // §3.3 of item-specific-config.md: enum/range validation for itemSettings values that
    // WalkRawObjects' unknown-key pass cannot catch, since a legal-but-unrecognized string is a
    // known key with a bad value, not an unknown key.
    private static IEnumerable<Diagnostic> CheckItemSettings(UserConfig? config)
    {
        if (config?.ItemSettings?.RateLimits?.Windows is { } windows
            && !RateLimitsWindowsAcceptedTokens.Contains(windows, StringComparer.Ordinal))
        {
            yield return UnknownEnumValue("/itemSettings/rateLimits/windows", windows, "windows", RateLimitsWindowsAcceptedTokens);
        }

        if (config?.ItemSettings?.Directory?.Depth is int depth && depth <= 0)
        {
            yield return new Diagnostic("/itemSettings/directory/depth", DiagnosticSeverity.Error, "invalid-depth",
                $"depth ({depth}) must be a positive integer");
        }

        if (config?.ItemSettings?.Directory?.OpenWith is { } openWith
            && !DirectoryOpenWithAcceptedTokens.Contains(openWith, StringComparer.Ordinal))
        {
            yield return UnknownEnumValue("/itemSettings/directory/openWith", openWith, "openWith", DirectoryOpenWithAcceptedTokens);
        }

        // SPEC-103-showlabel-fix-and-labelcolor.md §B.7: an unrecognized labelColor/factsColor/
        // verbColor would silently render nothing useful, so it gets the same unknown-color
        // diagnostic CheckLiteralSpec uses for ColorExpr literals — routed through ResolveLiteral
        // directly rather than CheckLiteralSpec itself, since these are plain strings with no
        // color-system-down-conversion concern for a decorative label.
        if (config?.ItemSettings?.OutputStyle?.LabelColor is { Length: > 0 } outputStyleLabelColor && ColorResolution.ResolveLiteral(outputStyleLabelColor) is null)
        {
            yield return UnknownColor("/itemSettings/outputStyle/labelColor", outputStyleLabelColor);
        }

        if (config?.ItemSettings?.Autocompact?.LabelColor is { Length: > 0 } autocompactLabelColor && ColorResolution.ResolveLiteral(autocompactLabelColor) is null)
        {
            yield return UnknownColor("/itemSettings/autocompact/labelColor", autocompactLabelColor);
        }

        foreach (var d in CheckAhSettings(config?.ItemSettings?.Ah, "/itemSettings/ah")) yield return d;
        foreach (var d in CheckAhSettings(config?.ItemSettings?.AhShort, "/itemSettings/ahShort")) yield return d;

        if (config?.ItemSettings?.Engram?.FactsColor is { Length: > 0 } factsColor && ColorResolution.ResolveLiteral(factsColor) is null)
        {
            yield return UnknownColor("/itemSettings/engram/factsColor", factsColor);
        }

        if (config?.ItemSettings?.Engram?.VerbColor is { Length: > 0 } verbColor && ColorResolution.ResolveLiteral(verbColor) is null)
        {
            yield return UnknownColor("/itemSettings/engram/verbColor", verbColor);
        }

        foreach (var diagnostic in CheckEngramItemSettings(config?.ItemSettings?.Engram))
        {
            yield return diagnostic;
        }
    }

    // SPEC-104 §F.3: EngramItemSettings' own field-vocabulary/range/binaryPath rules — the
    // WalkRawObjects unknown-key pass doesn't walk itemSettings.engram at all (§B), so a bad
    // "field" name inside fields[] needs its own check here, same as the enum checks above.
    private static IEnumerable<Diagnostic> CheckEngramItemSettings(EngramItemSettings? engram)
    {
        if (engram is null)
        {
            yield break;
        }

        if (engram.Fields is { } fields)
        {
            if (fields.Count == 0)
            {
                yield return new Diagnostic("/itemSettings/engram/fields", DiagnosticSeverity.Error, "empty-engram-fields",
                    "fields must not be an empty array — omit it to use the default facts+verb pair");
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var path = $"/itemSettings/engram/fields/{i}";

                if (field.Field is not { Length: > 0 } fieldName)
                {
                    yield return new Diagnostic($"{path}/field", DiagnosticSeverity.Error, "empty-engram-field",
                        "field must be a non-empty string");
                }
                else if (!EngramProbe.IsKnownField(fieldName))
                {
                    var suggestions = KeySuggestion.Suggest(fieldName, EngramFieldVocabulary);
                    var tail = suggestions.Count == 0 ? "" : $" — did you mean {FormatAccepted(suggestions.Select(s => $"'{s}'").ToArray())}?";
                    yield return new Diagnostic($"{path}/field", DiagnosticSeverity.Error, "unknown-engram-field",
                        $"'{fieldName}' is not a known engram field{tail}");
                }

                if (field.Color is { Length: > 0 } fieldColor && ColorResolution.ResolveLiteral(fieldColor) is null)
                {
                    yield return UnknownColor($"{path}/color", fieldColor);
                }
            }
        }

        if (engram.TtlSeconds is < 0)
        {
            yield return new Diagnostic("/itemSettings/engram/ttlSeconds", DiagnosticSeverity.Error, "invalid-ttl-seconds",
                $"ttlSeconds ({engram.TtlSeconds}) must not be negative");
        }

        if (engram.TimeoutMs is <= 0)
        {
            yield return new Diagnostic("/itemSettings/engram/timeoutMs", DiagnosticSeverity.Error, "invalid-timeout-ms",
                $"timeoutMs ({engram.TimeoutMs}) must be a positive integer");
        }

        if (engram.IdleAfterSeconds is <= 0)
        {
            yield return new Diagnostic("/itemSettings/engram/idleAfterSeconds", DiagnosticSeverity.Error, "invalid-idle-after-seconds",
                $"idleAfterSeconds ({engram.IdleAfterSeconds}) must be a positive integer");
        }

        if (engram.BinaryPath is { } binaryPath)
        {
            if (binaryPath.Length == 0)
            {
                yield return new Diagnostic("/itemSettings/engram/binaryPath", DiagnosticSeverity.Error, "invalid-binary-path",
                    "binaryPath must not be empty");
            }
            else if (binaryPath.IndexOfAny(new[] { '/', '\\' }) >= 0 && !Path.IsPathRooted(binaryPath))
            {
                yield return new Diagnostic("/itemSettings/engram/binaryPath", DiagnosticSeverity.Error, "invalid-binary-path",
                    $"'{binaryPath}' has a directory separator but is not an absolute path — a bare name (no separator) is looked up on PATH, and a path is resolved as given, never relative to the current directory");
            }
            else
            {
                // §F.3: missing is a warning, not an error — the config may be checked on a
                // different machine than it runs on. A bare name is checked via PATH (never cwd),
                // matching EngramProbe's own runtime resolution.
                var found = binaryPath.IndexOfAny(new[] { '/', '\\' }) < 0
                    ? EngramProbe.TryFindOnPath(binaryPath) is not null
                    : File.Exists(binaryPath);

                if (!found)
                {
                    yield return new Diagnostic("/itemSettings/engram/binaryPath", DiagnosticSeverity.Warning, "binary-path-not-found",
                        $"'{binaryPath}' does not exist on this machine");
                }
            }
        }

        if (engram.StateColors is { } stateColors)
        {
            if (stateColors.Active is { Length: > 0 } active && ColorResolution.ResolveLiteral(active) is null)
            {
                yield return UnknownColor("/itemSettings/engram/stateColors/active", active);
            }

            if (stateColors.Idle is { Length: > 0 } idle && ColorResolution.ResolveLiteral(idle) is null)
            {
                yield return UnknownColor("/itemSettings/engram/stateColors/idle", idle);
            }

            if (stateColors.Unreachable is { Length: > 0 } unreachable && ColorResolution.ResolveLiteral(unreachable) is null)
            {
                yield return UnknownColor("/itemSettings/engram/stateColors/unreachable", unreachable);
            }

            if (stateColors.Unavailable is { Length: > 0 } unavailable && ColorResolution.ResolveLiteral(unavailable) is null)
            {
                yield return UnknownColor("/itemSettings/engram/stateColors/unavailable", unavailable);
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckAhSettings(AhItemSettings? settings, string basePath)
    {
        if (settings is null)
        {
            yield break;
        }

        if (settings.LabelColor is { Length: > 0 } labelColor && ColorResolution.ResolveLiteral(labelColor) is null)
        {
            yield return UnknownColor($"{basePath}/labelColor", labelColor);
        }

        if (settings.StateColors is not { } colors)
        {
            yield break;
        }

        foreach (var (tone, value) in new[] { ("work", colors.Work), ("warn", colors.Warn), ("bad", colors.Bad), ("idle", colors.Idle) })
        {
            if (value is { Length: > 0 } && ColorResolution.ResolveLiteral(value) is null)
            {
                yield return UnknownColor($"{basePath}/stateColors/{tone}", value);
            }
        }
    }

    private static readonly string[] EngramFieldVocabulary =
    {
        "facts", "verb", "lastEventAge", "server", "version", "uptime", "port", "pid",
        "lastKind", "lastAge", "windowCount",
    };

    private static Diagnostic UnknownEnumValue(string path, string? value, string fieldName, IReadOnlyList<string> accepted) =>
        new(path, DiagnosticSeverity.Error, "unknown-enum-value", $"'{value}' is not a {fieldName} — expected {FormatAccepted(accepted)}");

    internal static string FormatAccepted(IReadOnlyList<string> accepted) => accepted.Count switch
    {
        0 => "",
        1 => accepted[0],
        2 => $"{accepted[0]} or {accepted[1]}",
        _ => string.Join(", ", accepted.Take(accepted.Count - 1)) + ", or " + accepted[^1],
    };

    // ---- §2.6/§7.1: "overflow"/"ellipsis" are leaf-only keys; a split node's own value is unconsulted ----

    private static IEnumerable<Diagnostic> CheckLeafOnlyKeysOnSplits(UserConfig? config)
    {
        if (config?.Surface?.Pane is not { } surfacePane)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
        {
            if (pane.Children is not { Count: > 0 })
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pane.Overflow))
            {
                yield return new Diagnostic(path + "/overflow", DiagnosticSeverity.Warning, "leaf-only-key-on-split",
                    "\"overflow\" has no effect on a split; only a leaf pane has content that can overflow");
            }

            if (!string.IsNullOrWhiteSpace(pane.Ellipsis))
            {
                yield return new Diagnostic(path + "/ellipsis", DiagnosticSeverity.Warning, "leaf-only-key-on-split",
                    "\"ellipsis\" has no effect on a split; only a leaf pane's own overflow marker uses it");
            }
        }
    }

    // ---- §2.3.2: a known key with a legal value on a node that never reads it. "distribute" and
    // "gutter" divide/consume extent among side-by-side or stacked siblings, so a horizontal split
    // — whose children each span the full width and stack downward — has nothing for either to act
    // on; "items" on a pane that also declares "children" is unread because only a leaf pane's
    // items are ever resolved. Values already flagged unknown-enum-value are excluded here so one
    // bad value doesn't also read as "legal but misplaced" (§9.4.1: one condition, one code). ----

    private static IEnumerable<Diagnostic> CheckKeyNotApplicable(UserConfig? config)
    {
        // SPEC-85 §4.4: an item carrying `parts` alongside a key from another kind (`from`,
        // `command`, `item`) — or `format`/`maxLines`, neither of which a compound reads — has no
        // defined meaning; `parts` wins, and the conflicting key is reported here instead.
        foreach (var (item, path) in WalkRawItems(config))
        {
            if (item.Parts is null)
            {
                continue;
            }

            if (item.From is not null)
            {
                yield return new Diagnostic(path + "/from", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"from\" has no effect alongside \"parts\"; a compound item's text comes from its parts, and \"parts\" wins");
            }

            if (item.Command is not null)
            {
                yield return new Diagnostic(path + "/command", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"command\" has no effect alongside \"parts\"; a compound item's text comes from its parts, and \"parts\" wins");
            }

            if (item.Item is not null)
            {
                yield return new Diagnostic(path + "/item", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"item\" has no effect alongside \"parts\"; a compound item's text comes from its parts, and \"parts\" wins");
            }

            if (item.Format is not null)
            {
                yield return new Diagnostic(path + "/format", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"format\" has no effect alongside \"parts\"; a compound item's text comes from its parts, not a provider value");
            }

            if (item.MaxLines is not null)
            {
                yield return new Diagnostic(path + "/maxLines", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"maxLines\" has no effect alongside \"parts\"; it caps a provider's output and a compound item has no provider");
            }
        }

        if (config?.Surface?.Pane is not { } surfacePane)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
        {
            var isHorizontal = ConfigLoader.ParseSplitCore(pane.Split) == PaneSplit.Horizontal;

            if (isHorizontal && !string.IsNullOrWhiteSpace(pane.Distribute) && !PaneDistributeParsing.IsUnrecognized(pane.Distribute))
            {
                yield return new Diagnostic(path + "/distribute", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"distribute\" has no effect on a horizontal split; it divides extent among side-by-side children");
            }

            if (isHorizontal && pane.Gutter is not null)
            {
                yield return new Diagnostic(path + "/gutter", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"gutter\" has no effect on a horizontal split; it is blank cells between siblings in a vertical split");
            }

            if (pane.Children is { Count: > 0 } && pane.Items is { Count: > 0 })
            {
                yield return new Diagnostic(path + "/items", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"items\" has no effect on a pane that also declares \"children\"; only a leaf pane's items are read");
            }

            if (pane.Children is { Count: 0 })
            {
                yield return new Diagnostic(path + "/children", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"children\" has no effect on a leaf; a pane is a split only when its children list has at least one entry");
            }

            if (ConfigLoader.ParseSplitCore(pane.Split) is PaneSplit.Vertical or PaneSplit.Horizontal or PaneSplit.Flex
                && pane.Children is not { Count: > 0 })
            {
                yield return new Diagnostic(path + "/split", DiagnosticSeverity.Warning, "key-not-applicable",
                    "\"split\" has no effect on a leaf; a pane is a split only when its children list has at least one entry");
            }
        }
    }

    // ---- §2.10.1: "inside" computes interior-divider edges for a split's children; a leaf has no
    // interior for that to divide, so the shorthand silences its border entirely instead ----

    private static IEnumerable<Diagnostic> CheckBorderInsideOnLeaf(UserConfig? config)
    {
        if (config?.Surface?.Pane is not { } surfacePane)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
        {
            if (pane.Children is { Count: > 0 })
            {
                continue;
            }

            if (string.Equals(pane.Border?.Shorthand, "inside", StringComparison.OrdinalIgnoreCase))
            {
                yield return new Diagnostic(path + "/border", DiagnosticSeverity.Warning, "border-inside-on-leaf",
                    "a leaf has no interior, so this silences its border entirely");
            }
        }
    }

    // ---- §2.10.1 rule 2: "collapse" is legal only at surface.border.collapse — declared on the
    // top-level border or any per-pane border, it would be silently dropped by a converter that
    // never binds it there, so this flags it as an Error rather than let it vanish unremarked ----

    private static IEnumerable<Diagnostic> CheckCollapseNotSurfaceLevel(UserConfig? config)
    {
        if (config?.Border?.Collapse is not null)
        {
            yield return new Diagnostic("/border/collapse", DiagnosticSeverity.Error, "collapse-not-surface-level",
                "\"collapse\" is only legal at surface.border.collapse, not the top-level border");
        }

        if (config?.Surface?.Pane is not { } surfacePane)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
        {
            if (pane.Border?.Collapse is not null)
            {
                yield return new Diagnostic(path + "/border/collapse", DiagnosticSeverity.Error, "collapse-not-surface-level",
                    "\"collapse\" is only legal at surface.border.collapse, not a pane's own border");
            }
        }
    }

    // ---- §2.10.1 rule 4/§9.6.1 item 7: under collapse:true, adjacent panes disagreeing on a
    // shared boundary's style/color is reported once per boundary, not once per row — first
    // requester in tree-declaration order wins, so the earlier child's path anchors the warning ----

    private static IEnumerable<Diagnostic> CheckCollapsedEdgeConflicts(Pane root, string rootPath, bool collapse)
    {
        if (!collapse)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Split is not (PaneSplit.Vertical or PaneSplit.Horizontal) || pane.Children.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < pane.Children.Count - 1; i++)
            {
                var a = pane.Children[i];
                var b = pane.Children[i + 1];
                var (edgeA, edgeB) = pane.Split == PaneSplit.Vertical
                    ? (a.Border.Edges.Right, b.Border.Edges.Left)
                    : (a.Border.Edges.Bottom, b.Border.Edges.Top);

                if (!edgeA || !edgeB || a.Border.Style is null || b.Border.Style is null)
                {
                    continue;
                }

                if (!Equals(a.Border.Style, b.Border.Style) || !Equals(a.Border.Color, b.Border.Color))
                {
                    yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Warning, "collapsed-edge-conflict",
                        $"children {i} and {i + 1} declare different border style/color on their shared edge; the earlier child in tree order wins");
                }
            }
        }
    }

    // SPEC-85 §4.2/§4.3: every raw item, across both the surface.pane tree and the top-level
    // items shorthand — the same two-shape source CheckEnums/CheckItemEnums already reads item
    // config from, reused here rather than a second traversal.
    private static IEnumerable<(PaneItemJsonConfig Item, string Path)> WalkRawItems(UserConfig? config)
    {
        if (config?.Surface?.Pane is { } surfacePane)
        {
            foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
            {
                if (pane.Items is { } items)
                {
                    for (var i = 0; i < items.Count; i++)
                    {
                        yield return (items[i], $"{path}/items/{i}");
                    }
                }

                if (pane.Title is { } title)
                {
                    yield return (title, $"{path}/title");
                }
            }
        }
        else if (config?.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                yield return (items[i], $"/items/{i}");
            }
        }
    }

    // ---- §4.2: compound-part diagnostics — part-source-count / part-forbidden-key ----

    private static IEnumerable<Diagnostic> CheckCompoundParts(UserConfig? config)
    {
        foreach (var (item, path) in WalkRawItems(config))
        {
            if (item.Parts is not { } parts)
            {
                continue;
            }

            if (parts.Count == 0)
            {
                yield return new Diagnostic(path + "/parts", DiagnosticSeverity.Error, "part-source-count",
                    "a compound item must declare at least one part");
                continue;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var partPath = $"{path}/parts/{i}";

                var sourceCount = (part.Text is not null ? 1 : 0) + (part.Item is not null ? 1 : 0) + (part.From is not null ? 1 : 0);
                if (sourceCount == 0)
                {
                    yield return new Diagnostic(partPath, DiagnosticSeverity.Error, "part-source-count",
                        "a compound part must name exactly one source — 'text', 'item', or 'from'");
                }
                else if (sourceCount > 1)
                {
                    var present = new[] { (part.Text is not null, "text"), (part.Item is not null, "item"), (part.From is not null, "from") }
                        .Where(t => t.Item1).Select(t => $"'{t.Item2}'");
                    yield return new Diagnostic(partPath, DiagnosticSeverity.Error, "part-source-count",
                        $"a compound part must name exactly one source, not {string.Join(" and ", present)}");
                }

                if (part.Parts is not null)
                {
                    yield return new Diagnostic(partPath, DiagnosticSeverity.Error, "part-forbidden-key",
                        "'parts' may not appear inside a compound part — compound items are one level deep");
                }

                if (part.Link is not null)
                {
                    yield return new Diagnostic(partPath, DiagnosticSeverity.Error, "part-forbidden-key",
                        "'link' belongs on the item, not on a part — it wraps the whole compound");
                }
            }
        }
    }

    private static IEnumerable<(PaneConfig Pane, string Path)> WalkRawPanes(PaneConfig pane, string path)
    {
        yield return (pane, path);
        if (pane.Children is { } children)
        {
            for (var i = 0; i < children.Count; i++)
            {
                foreach (var entry in WalkRawPanes(children[i], $"{path}/children/{i}"))
                {
                    yield return entry;
                }
            }
        }
    }

    // ---- §9.8: structural size checks, width-independent, via SizeResolver's own arithmetic ----

    private static IEnumerable<Diagnostic> CheckStructuralSizes(Pane root, string rootPath, bool collapse)
    {
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.MinSize is int min && pane.MaxSize is int max && min > max)
            {
                yield return new Diagnostic(path, DiagnosticSeverity.Error, "min-exceeds-max",
                    $"minSize ({min}) exceeds maxSize ({max})");
            }

            if (pane.Children.Count == 0)
            {
                continue;
            }

            if (pane.Split == PaneSplit.Vertical)
            {
                foreach (var d in CheckSplitBounds(pane, path, collapse))
                {
                    yield return d;
                }
            }
            else if (pane.Split == PaneSplit.Horizontal)
            {
                foreach (var d in CheckHorizontalSplitChildren(pane, path))
                {
                    yield return d;
                }
            }
            else if (pane.Split == PaneSplit.Flex)
            {
                foreach (var d in CheckFlexSplitBounds(pane, path, collapse))
                {
                    yield return d;
                }
            }
        }
    }

    // SPEC-88 §4.5.3: a Flex pane is structurally impossible only if it is impossible in BOTH
    // arrangements it can adopt — the AND is the dual of §3.4's Floor(flex) = min(...), stated once
    // and reused here rather than restated (§1.1). Running both checks and reporting on EITHER
    // would reject flex's headline use case (side-by-side over-constrained, stacked fine) at error
    // severity, which is not shippable. Both underlying checks are lazy IEnumerables and must be
    // materialized before the AND can be evaluated.
    private static IEnumerable<Diagnostic> CheckFlexSplitBounds(Pane split, string path, bool collapse)
    {
        var sideBySide = CheckSplitBounds(split, path, collapse).ToList();
        var stacked = CheckHorizontalSplitChildren(split, path).ToList();

        if (sideBySide.Count == 0 || stacked.Count == 0)
        {
            yield break;
        }

        yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
            $"this flex split's children exceed its parent's bound in both arrangements: side by side ({sideBySide[0].Message}) and stacked ({stacked[0].Message})");
    }

    private static IEnumerable<Diagnostic> CheckSplitBounds(Pane split, string path, bool collapse)
    {
        var boundaryCost = SizeResolver.BoundaryCost(split, split.Children.Count, collapse);

        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is not int bound)
        {
            yield break;
        }

        var fixedSum = split.Children.Sum(c => SizeResolver.FixedSize(c) ?? 0);
        if (fixedSum + boundaryCost > bound)
        {
            yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"children's fixed sizes ({fixedSum}) plus boundary cost ({boundaryCost}) exceed this pane's own bound ({bound})");
        }

        var minSum = split.Children.Sum(c => c.MinSize ?? 0);
        if (minSum + boundaryCost > bound)
        {
            yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"children's minSize sum ({minSum}) plus boundary cost ({boundaryCost}) exceed this pane's own bound ({bound})");
        }
    }

    private static IEnumerable<Diagnostic> CheckHorizontalSplitChildren(Pane split, string path)
    {
        // A horizontal split's children stack and are sized independently against the parent's inner
        // width (§2.3.5) — they occupy different rows and never contend for a column, so their fixed
        // sizes and minSizes are compared against the parent individually and never summed. This is
        // unlike a vertical split, whose children genuinely divide one budget.
        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is not int bound)
        {
            yield break;
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            var child = split.Children[i];

            if (SizeResolver.FixedSize(child) is int childFixed && childFixed > bound)
            {
                yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"this pane's fixed size ({childFixed}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
            }
            else if (child.MinSize is int childMin && childMin > bound)
            {
                yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"this pane's minSize ({childMin}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
            }
        }
    }

    // ---- §2.6: "overflow" is only legal on the sole pane of a single-pane surface ----

    private static IEnumerable<Diagnostic> CheckOverflowPosition(Pane root, string rootPath)
    {
        if (root.Split == PaneSplit.None && root.Children.Count == 0)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Children.Count == 0 && pane.Overflow == OverflowMode.Overflow)
            {
                yield return new Diagnostic(path + "/overflow", DiagnosticSeverity.Error, "overflow-forbidden-position",
                    "\"overflow\" is only legal when the surface has exactly one pane");
            }
        }
    }

    // ---- §9.4/§2.11/§2.11.1: a content/fill leaf pane with no items and no explicit minSize collapses;
    // an explicit minSize suppresses collapse (§2.3's floor table: "author said so; always wins"), so
    // it's as legitimate a spacer as a fixed/percent pane ----

    private static IEnumerable<Diagnostic> CheckEmptyPanes(Pane root, string rootPath)
    {
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Children.Count == 0 && pane.Items.Count == 0 && pane.MinSize is null &&
                (SizeResolver.IsContentSized(pane) || SizeResolver.IsFillSized(pane)))
            {
                yield return new Diagnostic(path, DiagnosticSeverity.Warning, "pane-no-items",
                    "this pane declares no items and its size collapses to zero, so the declaration does nothing");
            }
        }
    }

    // ---- SPEC pane-id-title-align §2.4: pane id — a tree-wide walk, since CheckPaneEnums is
    // per-pane and cannot see siblings ----

    private static IEnumerable<Diagnostic> CheckPaneIds(Pane root, string rootPath, IReadOnlyCollection<string> selfDeclaredItemIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Id is not { } id)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                yield return new Diagnostic(path + "/id", DiagnosticSeverity.Warning, "empty-pane-id",
                    "'id' is present but empty; omit it instead");
                continue;
            }

            if (!seen.Add(id))
            {
                yield return new Diagnostic(path + "/id", DiagnosticSeverity.Error, "duplicate-pane-id",
                    $"another pane already declares id '{id}'");
            }

            if (selfDeclaredItemIds.Contains(id))
            {
                yield return new Diagnostic(path + "/id", DiagnosticSeverity.Warning, "pane-id-shadows-item-id",
                    $"'{id}' is also an item id; a pane id is a separate namespace and is not referencable by 'from', '{{{id}}}', or 'color.from'");
            }
        }
    }

    // ---- SPEC pane-id-title-align §3.9: title-without-border needs the resolved border (style +
    // top edge), which only exists on the resolved Pane tree, not the raw PaneConfig ----

    private static IEnumerable<Diagnostic> CheckTitleWithoutBorder(Pane root, string rootPath)
    {
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Title is null)
            {
                continue;
            }

            if (pane.Border.Style is null || !pane.Border.Edges.Top)
            {
                yield return new Diagnostic(path + "/title", DiagnosticSeverity.Error, "title-without-border",
                    "'title' draws into the pane's top border line, but this pane has no border (or its top edge is off)");
            }
        }
    }

    // ---- SPEC pane-id-title-align §4.5/§4.7: selfAlign's two "declared but inert" warnings ----

    private static IEnumerable<Diagnostic> CheckSelfAlign(Pane root, string rootPath, bool collapse)
    {
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.SelfAlign != PaneSelfAlign.Left && collapse)
            {
                yield return new Diagnostic(path + "/selfAlign", DiagnosticSeverity.Warning, "self-align-with-collapse",
                    "'selfAlign' is ignored while surface.border.collapse is true");
            }

            if (pane.Title is not null && collapse)
            {
                yield return new Diagnostic(path + "/title", DiagnosticSeverity.Warning, "title-with-collapse",
                    "'title' is ignored while surface.border.collapse is true");
            }

            if (pane.Children.Count == 0)
            {
                continue;
            }

            // Finding B (review round 1): the fill-sibling/distribute:even "no leftover space"
            // condition is only true for §4.2's mechanism (Vertical/side-by-side leftover-width
            // redistribution). §4.4's stacked mechanism (Horizontal split) aligns each child
            // independently within innerWidth regardless of siblings/distribute, so applying this
            // check there is a false positive. Flex/None splits have no statically-known orientation
            // at config-check time, so skip them too rather than guess.
            if (pane.Split != PaneSplit.Vertical)
            {
                continue;
            }

            var anyFillSibling = pane.Children.Any(SizeResolver.IsFillSized);
            var distributeEven = pane.Distribute == PaneDistribute.Even;
            if (!anyFillSibling && !distributeEven)
            {
                continue;
            }

            for (var i = 0; i < pane.Children.Count; i++)
            {
                if (pane.Children[i].SelfAlign == PaneSelfAlign.Left)
                {
                    continue;
                }

                yield return new Diagnostic($"{path}/children/{i}/selfAlign", DiagnosticSeverity.Warning, "self-align-no-effect",
                    "'selfAlign' has no effect here: a sibling is fill-sized (or distribute is \"even\"), so the row has no leftover space to position this pane in");
            }
        }
    }

    private static IEnumerable<(Pane Pane, string Path)> WalkPanes(Pane pane, string path)
    {
        yield return (pane, path);
        for (var i = 0; i < pane.Children.Count; i++)
        {
            foreach (var entry in WalkPanes(pane.Children[i], $"{path}/children/{i}"))
            {
                yield return entry;
            }
        }
    }

    // ---- §9.4.2: keys no config object defines. Captured by [JsonExtensionData] during binding
    // (so per-object scoping falls out of the deserializer rather than a second shape mirror) and
    // compared against ConfigJsonContext's own JsonTypeInfo — the same metadata the binder used ----

    // §9.4.2: the known-key set is the *same* metadata the deserializer binds with, so it cannot
    // disagree with what actually parses. Reflection is forbidden here — PublishAot trims it and
    // the resulting short/empty set only misfires in the shipped binary.
    //
    // TASK-21-SPEC.md §3 NEEDS-EVIDENCE fallback: [JsonIgnore] on BorderConfig.Shorthand does not
    // remove it from JsonTypeInfo.Properties on this runtime (confirmed by the §9.3 guard test), so
    // it is filtered out here by name rather than left to leak in as a false "known key" — which
    // would let a nearby typo get suggested toward a name that isn't part of the config language.
    private static string[] KnownKeys(JsonTypeInfo typeInfo) =>
        typeInfo.Properties
            .Where(p => !p.IsExtensionData && p.Name != nameof(BorderConfig.Shorthand))
            .Select(p => p.Name)
            .ToArray();

    private static IEnumerable<Diagnostic> CheckUnknownKeys(UserConfig? config)
    {
        if (config is null)
        {
            yield break;
        }

        foreach (var (extra, typeInfo, label, path) in WalkRawObjects(config))
        {
            if (extra is not { Count: > 0 })
            {
                continue;
            }

            var known = KnownKeys(typeInfo);
            foreach (var key in extra.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var suggestions = KeySuggestion.Suggest(key, known);
                var tail = suggestions.Count == 0
                    ? ""
                    : $" — did you mean {FormatAccepted(suggestions.Select(s => $"'{s}'").ToArray())}?";
                yield return new Diagnostic($"{path}/{key}", DiagnosticSeverity.Warning, "unknown-key",
                    $"unknown key '{key}' on {label}{tail}");
            }
        }
    }

    private static IEnumerable<(Dictionary<string, JsonElement>? Extra, JsonTypeInfo TypeInfo, string Label, string Path)>
        WalkRawObjects(UserConfig config)
    {
        yield return (config.Extra, ConfigJsonContext.Default.UserConfig, "the top-level config", "");

        if (config.Border is { } border)
        {
            foreach (var e in WalkBorder(border, "/border")) yield return e;
        }

        if (config.Layout is { } layout)
        {
            yield return (layout.Extra, ConfigJsonContext.Default.LayoutConfig, "layout", "/layout");
        }

        if (config.ItemSettings is { } itemSettings)
        {
            yield return (itemSettings.Extra, ConfigJsonContext.Default.ItemSettingsJsonConfig,
                "itemSettings", "/itemSettings");

            if (itemSettings.Directory is { } directorySettings)
            {
                yield return (directorySettings.Extra, ConfigJsonContext.Default.DirectoryItemSettings,
                    "the directory item's settings", "/itemSettings/directory");
            }

            if (itemSettings.Context is { } contextSettings)
            {
                yield return (contextSettings.Extra, ConfigJsonContext.Default.ContextItemSettings,
                    "the context item's settings", "/itemSettings/context");
            }

            if (itemSettings.RateLimits is { } rateLimitsSettings)
            {
                yield return (rateLimitsSettings.Extra, ConfigJsonContext.Default.RateLimitsItemSettings,
                    "the rate-limits item's settings", "/itemSettings/rateLimits");
            }

            if (itemSettings.Pr is { } prSettings)
            {
                yield return (prSettings.Extra, ConfigJsonContext.Default.PrItemSettings,
                    "the pr item's settings", "/itemSettings/pr");
            }

            if (itemSettings.Linear is { } linearSettings)
            {
                yield return (linearSettings.Extra, ConfigJsonContext.Default.LinearItemSettings,
                    "the linear item's settings", "/itemSettings/linear");
            }

            if (itemSettings.Worktree is { } worktreeSettings)
            {
                yield return (worktreeSettings.Extra, ConfigJsonContext.Default.WorktreeItemSettings,
                    "the worktree item's settings", "/itemSettings/worktree");
            }

            if (itemSettings.Ah is { } ahSettings)
            {
                yield return (ahSettings.Extra, ConfigJsonContext.Default.AhItemSettings,
                    "the ah item's settings", "/itemSettings/ah");
            }

            if (itemSettings.AhShort is { } ahShortSettings)
            {
                yield return (ahShortSettings.Extra, ConfigJsonContext.Default.AhItemSettings,
                    "the ah-short item's settings", "/itemSettings/ahShort");
            }
        }

        if (config.Surface is { } surface)
        {
            yield return (surface.Extra, ConfigJsonContext.Default.SurfaceConfig, "surface", "/surface");
            if (surface.Border is { } surfaceBorder)
            {
                foreach (var e in WalkBorder(surfaceBorder, "/surface/border")) yield return e;
            }

            if (surface.Pane is { } pane)
            {
                foreach (var e in WalkPaneObjects(pane, "/surface/pane")) yield return e;
            }
        }

        // §6.3 of TASK-21-SPEC.md: unlike CheckEnums, this walks both surface.pane and top-level
        // items unconditionally — an unknown key in an items array that resolution ignores (because
        // surface.pane is present) is still a typo worth reporting, and reporting it costs nothing.
        if (config.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var e in WalkItemObjects(items[i], $"/items/{i}")) yield return e;
            }
        }

        if (config.Colors is { } colors)
        {
            foreach (var (name, rule) in colors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (rule is not null)
                {
                    foreach (var e in WalkRuleObjects(rule, $"/colors/{name}")) yield return e;
                }
            }
        }
    }

    private static IEnumerable<(Dictionary<string, JsonElement>? Extra, JsonTypeInfo TypeInfo, string Label, string Path)>
        WalkBorder(BorderConfig border, string path)
    {
        yield return (border.Extra, ConfigJsonContext.Default.BorderConfig, "a border", path);

        if (border.Edges is { } edges)
        {
            yield return (edges.Extra, ConfigJsonContext.Default.BorderEdgesConfig, "border edges", path + "/edges");
        }

        if (border.Color?.Rule is { } rule)
        {
            foreach (var e in WalkRuleObjects(rule, path + "/color")) yield return e;
        }
    }

    private static IEnumerable<(Dictionary<string, JsonElement>? Extra, JsonTypeInfo TypeInfo, string Label, string Path)>
        WalkPaneObjects(PaneConfig pane, string path)
    {
        yield return (pane.Extra, ConfigJsonContext.Default.PaneConfig, "a pane", path);

        if (pane.Border is { } border)
        {
            foreach (var e in WalkBorder(border, path + "/border")) yield return e;
        }

        if (pane.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var e in WalkItemObjects(items[i], $"{path}/items/{i}")) yield return e;
            }
        }

        if (pane.Children is { } children)
        {
            for (var i = 0; i < children.Count; i++)
            {
                foreach (var e in WalkPaneObjects(children[i], $"{path}/children/{i}")) yield return e;
            }
        }
    }

    private static IEnumerable<(Dictionary<string, JsonElement>? Extra, JsonTypeInfo TypeInfo, string Label, string Path)>
        WalkItemObjects(PaneItemJsonConfig item, string path)
    {
        yield return (item.Extra, ConfigJsonContext.Default.PaneItemJsonConfig, "an item", path);

        if (item.Color?.Rule is { } rule)
        {
            foreach (var e in WalkRuleObjects(rule, path + "/color")) yield return e;
        }

        if (item.Parts is { } parts)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                yield return (parts[i].Extra, ConfigJsonContext.Default.PaneItemPartJsonConfig, "a compound part", $"{path}/parts/{i}");

                if (parts[i].Color?.Rule is { } partRule)
                {
                    foreach (var e in WalkRuleObjects(partRule, $"{path}/parts/{i}/color")) yield return e;
                }
            }
        }
    }

    private static IEnumerable<(Dictionary<string, JsonElement>? Extra, JsonTypeInfo TypeInfo, string Label, string Path)>
        WalkRuleObjects(ColorRuleJsonConfig rule, string path)
    {
        yield return (rule.Extra, ConfigJsonContext.Default.ColorRuleJsonConfig, "a color rule", path);

        if (rule.Thresholds is { } thresholds)
        {
            for (var i = 0; i < thresholds.Count; i++)
            {
                if (thresholds[i] is { } threshold)
                {
                    yield return (threshold.Extra, ConfigJsonContext.Default.ThresholdJsonConfig, "a color threshold", $"{path}/thresholds/{i}");
                }
            }
        }

        if (rule.Match is { } match)
        {
            for (var i = 0; i < match.Count; i++)
            {
                if (match[i] is { } m)
                {
                    yield return (m.Extra, ConfigJsonContext.Default.MatchJsonConfig, "a color match", $"{path}/match/{i}");
                }
            }
        }
    }
}
