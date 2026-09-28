using System.Globalization;
using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6: one resolution path for a "color" config value, covering every form —
/// literal name (16-color or Spectre's 256-color set), hex, numeric thresholds, and string match.
/// Thresholds and match are the same mechanism selecting on different value types, not two parallel
/// resolvers. This is also the one place the existing context/rate-limits 50/80 rule lives, so a
/// command item's own "thresholds" config and the two legacy builtins share one ladder instead of
/// each carrying a copy of it (§1).
/// </summary>
public static class ColorResolution
{
    public readonly record struct ThresholdRule(double Min, ColorValue Color);

    public readonly record struct MatchRule(string? Contains, string? EqualsValue, ColorValue Color);

    /// <summary>
    /// A parsed "color" config value once it is a rule rather than a literal string: an unordered
    /// set of thresholds (numeric, evaluated highest-<see cref="ThresholdRule.Min"/>-first) or an
    /// ordered list of match rules (string, evaluated first-declared-first), plus a fallback
    /// default. At most one of <see cref="Thresholds"/>/<see cref="Match"/> is populated for a
    /// given rule — which one is selected by whether the value being resolved is numeric or string.
    /// <see cref="From"/> (§6.3/§6.4) is the item id whose value drives the rule: required for a
    /// <c>colors</c>-table token (a border has no value of its own to default to) and defaulting to
    /// the owning item for an inline rule — resolved to a concrete id by the config binder in
    /// either case, so it is only ever null here for an inline rule with no owning item (a
    /// border's inline rule with no explicit <c>from</c>), which resolves to no colour.
    /// </summary>
    public sealed record ColorRule(IReadOnlyList<ThresholdRule>? Thresholds, IReadOnlyList<MatchRule>? Match, ColorValue? Default, string? From = null);

    /// <summary>
    /// §6: a colour expression — the one grammar valid anywhere this spec accepts a colour. A
    /// plain literal (§6.1), a <c>colors</c>-table token reference (§6.3), or an inline rule
    /// (§6.4). <see cref="Resolve"/> is the single entry point that evaluates any of the three.
    /// </summary>
    public abstract record ColorExpr
    {
        public sealed record Literal(string Spec) : ColorExpr;

        public sealed record TokenRef(string Name) : ColorExpr;

        public sealed record Inline(ColorRule Rule) : ColorExpr;
    }

    /// <summary>
    /// SPEC-44-color-token-in-rule-branches.md §3.1: a colour-valued leaf — a rule branch's
    /// <c>color</c>, or a rule's <c>default</c>. Deliberately has no <c>Inline</c> case, unlike
    /// <see cref="ColorExpr"/>: a rule nested inside a rule branch must be unrepresentable, not
    /// merely forbidden. <see cref="TokenRef"/> here is further restricted at reference-validation
    /// time (§3.2) to naming only a constant rule — this type does not encode that restriction
    /// itself, since encoding it in the type would require knowing the token table at parse time.
    /// </summary>
    public abstract record ColorValue
    {
        public sealed record Literal(string Spec) : ColorValue;

        public sealed record TokenRef(string Name) : ColorValue;
    }

    /// <summary>
    /// §6.5: the single up-front colour resolution point — reads already-resolved item values
    /// (§5's fetch phase) and the parsed <c>colors</c> token table, never touches layout, and is
    /// safe to call once per render before sizing begins. Returns a literal colour spec (§6.1),
    /// ready to drop straight into Spectre markup, or null for no colour. An unknown <c>@name</c>
    /// resolves to no colour, silently (§6.3/§7).
    /// </summary>
    public static string? Resolve(ColorExpr? expr, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorRule> tokens) =>
        expr switch
        {
            null => null,
            ColorExpr.Literal lit => lit.Spec,
            ColorExpr.TokenRef tok => tokens.TryGetValue(tok.Name, out var rule) ? ResolveRuleColor(rule, values, tokens) : null,
            ColorExpr.Inline inline => ResolveRuleColor(inline.Rule, values, tokens),
            _ => null,
        };

    /// <summary>
    /// A thin adapter over <see cref="Resolve"/> for the single-pane <c>Panel</c> path: it needs a
    /// full Spectre <see cref="Style"/>, not just a foreground <see cref="Color"/>, so a
    /// decoration-only spec (<c>dim</c>, <c>bold</c>, ...) survives into <c>Panel.BorderStyle</c>
    /// the same way it already does via markup on the pane-tree path (§6.6). A border always
    /// renders in *some* style, so an expression that resolves to no colour (or fails to parse)
    /// falls back to plain <see cref="Color.Grey"/>, the same default an absent <c>color</c>
    /// config has always used.
    /// </summary>
    public static Style ResolveBorderColor(ColorExpr expr, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorRule> tokens) =>
        Resolve(expr, values, tokens)?.Trim() is { Length: > 0 } spec && Style.TryParse(spec, out var style)
            ? style
            : new Style(Color.Grey);

    /// <summary>
    /// §6.4: a missing, failed, or empty source value takes the <c>default</c> branch, same as a
    /// value that matches nothing — both degrade to <see cref="ColorRule.Default"/>, which is
    /// itself optional and, when absent, yields no colour rather than the previous rule's colour
    /// or a suppressed item.
    /// </summary>
    private static string? ResolveRuleColor(ColorRule rule, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorRule> tokens)
    {
        var raw = rule.From is { Length: > 0 } from ? values.GetValueOrDefault(from) : null;

        var value = string.IsNullOrEmpty(raw)
            ? rule.Default
            : rule.Thresholds is { Count: > 0 } && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric)
                ? ResolveNumeric(numeric, rule)
                : rule.Match is { Count: > 0 }
                    ? ResolveString(raw, rule)
                    : rule.Default;

        return ResolveColorValue(value, tokens);
    }

    /// <summary>
    /// SPEC-44-color-token-in-rule-branches.md §4.2: maps a resolved <see cref="ColorValue"/> leaf
    /// to a spec string. A <see cref="ColorValue.TokenRef"/> here is, by §3.2's reference-validation
    /// restriction, always a constant rule — one hop to a <see cref="ColorValue.Literal"/> default,
    /// no recursion. An unknown or non-constant token resolves to no colour, silently (§7's
    /// established convention, extended unchanged to this leaf position).
    /// </summary>
    private static string? ResolveColorValue(ColorValue? value, IReadOnlyDictionary<string, ColorRule> tokens) =>
        value switch
        {
            null => null,
            ColorValue.Literal lit => lit.Spec,
            ColorValue.TokenRef tok =>
                tokens.TryGetValue(tok.Name, out var rule) && rule.Default is ColorValue.Literal lit2
                    ? lit2.Spec
                    : null,
            _ => null,
        };

    /// <summary>
    /// SPEC.md's captured 50/80 rule, expressed as data instead of code, so the legacy default
    /// segments (context, rate-limits) and any item's own numeric "thresholds" config are driven by
    /// one evaluator rather than two independent copies of the same ladder.
    /// </summary>
    public static readonly ColorRule StandardThreshold = new(
        Thresholds: new[] { new ThresholdRule(80, new ColorValue.Literal("maroon")), new ThresholdRule(50, new ColorValue.Literal("olive")) },
        Match: null,
        Default: new ColorValue.Literal("green"));

    /// <summary>
    /// Replaces the old SegmentBuilder.ThresholdTag — same ladder, now the shared evaluator.
    /// <see cref="StandardThreshold"/> is constructed in source with only <see cref="ColorValue.Literal"/>
    /// branches, so the <c>Literal</c> arm always hits when a threshold matched; the fallback below
    /// covers "no threshold matched" (this evaluator's only reachable non-literal case today). If
    /// <see cref="StandardThreshold"/> ever becomes configurable, a <see cref="ColorValue.TokenRef"/>
    /// could reach here and would silently coerce to "green" — revisit this line first.
    /// </summary>
    public static string ResolveStandardThreshold(double value) =>
        ResolveNumeric(value, StandardThreshold) is ColorValue.Literal lit ? lit.Spec : "green";

    /// <summary>
    /// §6: a value matches the highest threshold whose <c>min</c> it clears, regardless of the
    /// order thresholds were declared in — the existing 80/50 ladder depends on this (85 must match
    /// 80, not whichever of the two rules happens to be declared first). Falls to
    /// <see cref="ColorRule.Default"/>, then null (ambient/no color, silent per §7).
    /// </summary>
    public static ColorValue? ResolveNumeric(double value, ColorRule rule)
    {
        if (rule.Thresholds is { Count: > 0 } thresholds)
        {
            foreach (var t in thresholds.OrderByDescending(t => t.Min))
            {
                if (value >= t.Min)
                {
                    return t.Color;
                }
            }
        }

        return rule.Default;
    }

    /// <summary>
    /// §6 string-match form: first match wins, in declaration order — unlike numeric thresholds,
    /// match rules have no natural ranking, so order is authored, not inferred. <c>contains</c> is
    /// a case-insensitive substring match (the documented form: a builtin like <c>model</c> yields
    /// "Claude Opus 4.5", and exact matching would break on every version bump); <c>equals</c> is
    /// case-insensitive exact match for authors who want it.
    /// </summary>
    public static ColorValue? ResolveString(string value, ColorRule rule)
    {
        if (rule.Match is { Count: > 0 } match)
        {
            foreach (var m in match)
            {
                if (m.Contains is { Length: > 0 } contains && value.Contains(contains, StringComparison.OrdinalIgnoreCase))
                {
                    return m.Color;
                }

                if (m.EqualsValue is { Length: > 0 } equalsValue && string.Equals(value, equalsValue, StringComparison.OrdinalIgnoreCase))
                {
                    return m.Color;
                }
            }
        }

        return rule.Default;
    }

    /// <summary>
    /// §6 addendum: resolves a literal color spec — a standard-16 name, a Spectre 256-palette name,
    /// or "#rrggbb" hex — to a Spectre <see cref="Color"/>. Unparseable ⇒ null (no color specified),
    /// silent per §7. Parsing itself was never limited to 16 colors (<see cref="Style.TryParse"/>
    /// already accepts all three forms); §6's "widen the palette" is a rendering-profile change
    /// (Program.cs's <c>ColorSystem</c>), not a parsing one.
    /// </summary>
    public static Color? ResolveLiteral(string spec)
    {
        var trimmed = spec.Trim();
        return trimmed.Length > 0 && Style.TryParse(trimmed, out var style) ? style.Foreground : (Color?)null;
    }

    /// <summary>
    /// STATUS.md's empirically-verified core sixteen: the ANSI standard palette, closed by the
    /// standard itself rather than by this library's version. §6.2.1's minimum-colour-system check
    /// and §9.6.3's <c>--colors</c> output both need exactly this set — one constant, two consumers,
    /// hand-maintained here only because it cannot drift out from under a library upgrade the way
    /// Spectre's much larger 256-name palette could.
    /// </summary>
    public static readonly IReadOnlyCollection<string> StandardColorNames = new HashSet<string>(
        new[]
        {
            "black", "maroon", "green", "olive", "navy", "purple", "teal", "silver",
            "grey", "red", "lime", "yellow", "blue", "fuchsia", "aqua", "white",
        },
        StringComparer.OrdinalIgnoreCase);
}
