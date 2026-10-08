using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace ClaudeTuiLine;

public sealed class UserConfig
{
    [JsonPropertyName("border")]
    [JsonConverter(typeof(BorderConfigConverter))]
    public BorderConfig? Border { get; set; }

    [JsonPropertyName("layout")]
    public LayoutConfig? Layout { get; set; }

    [JsonPropertyName("items")]
    public List<PaneItemJsonConfig>? Items { get; set; }

    [JsonPropertyName("surface")]
    public SurfaceConfig? Surface { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §6.2: opt-in rendering profile; default "standard" keeps the golden parity baseline byte-identical by construction.</summary>
    [JsonPropertyName("colorSystem")]
    public string? ColorSystem { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §6.3: named, reusable colour tokens, referenced elsewhere as <c>"@name"</c>.</summary>
    [JsonPropertyName("colors")]
    public Dictionary<string, ColorRuleJsonConfig>? Colors { get; set; }

    [JsonPropertyName("itemSettings")]
    public ItemSettingsJsonConfig? ItemSettings { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Per-item settings, keyed by builtin item id. Declared as properties rather than a
/// dictionary so each item's settings get their own JsonTypeInfo, which is what lets
/// ConfigCheck's unknown-key diagnostic scope per object without a second shape mirror.
/// </summary>
public sealed class ItemSettingsJsonConfig
{
    [JsonPropertyName("directory")]
    public DirectoryItemSettings? Directory { get; set; }

    [JsonPropertyName("context")]
    public ContextItemSettings? Context { get; set; }

    [JsonPropertyName("rateLimits")]
    public RateLimitsItemSettings? RateLimits { get; set; }

    [JsonPropertyName("pr")]
    public PrItemSettings? Pr { get; set; }

    [JsonPropertyName("linear")]
    public LinearItemSettings? Linear { get; set; }

    [JsonPropertyName("worktree")]
    public WorktreeItemSettings? Worktree { get; set; }

    [JsonPropertyName("outputStyle")]
    public OutputStyleItemSettings? OutputStyle { get; set; }

    [JsonPropertyName("autocompact")]
    public AutocompactItemSettings? Autocompact { get; set; }

    [JsonPropertyName("engram")]
    public EngramItemSettings? Engram { get; set; }

    [JsonPropertyName("ah")]
    public AhItemSettings? Ah { get; set; }

    [JsonPropertyName("ahShort")]
    public AhItemSettings? AhShort { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// Base for item settings whose item renders a "label:value" prefix.
/// showLabel == null or true renders the label; false renders the bare value.
public abstract class LabeledItemSettings
{
    [JsonPropertyName("showLabel")]
    public bool? ShowLabel { get; set; }

    /// Color for the label fragment only (the literal text around "{}" in the
    /// item's format), leaving the value's own color untouched. Null renders the
    /// whole item in one color, as before. Inert when ShowLabel is false.
    [JsonPropertyName("labelColor")]
    public string? LabelColor { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class DirectoryItemSettings
{
    /// <summary>How many trailing path segments to show. 1 = basename (today's behaviour).</summary>
    [JsonPropertyName("depth")]
    public int? Depth { get; set; }

    /// <summary>Where the default hyperlink points: "files" (the OS file browser, the
    /// default) or "vscode" (open the directory in VS Code).</summary>
    [JsonPropertyName("openWith")]
    public string? OpenWith { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ContextItemSettings
{
    /// <summary>Whether the token-count parenthetical renders beside the percentage. Default true.</summary>
    [JsonPropertyName("showDetail")]
    public bool? ShowDetail { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class WorktreeItemSettings
{
    /// <summary>Whether the branch renders in parentheses after the worktree name. Default false.</summary>
    [JsonPropertyName("showBranch")]
    public bool? ShowBranch { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class OutputStyleItemSettings : LabeledItemSettings
{
}

public sealed class AutocompactItemSettings : LabeledItemSettings
{
}

/// Settings for one agent-hierarchy status item; `ah` and `ah-short` each read their own block.
public sealed class AhItemSettings : LabeledItemSettings
{
    [JsonPropertyName("stateColors")]
    public AhStateColorsJsonConfig? StateColors { get; set; }
}

/// Per-tone colour overrides for an ah item. All default null; the defaults live in the render code.
public sealed class AhStateColorsJsonConfig
{
    [JsonPropertyName("work")]
    public string? Work { get; set; }

    [JsonPropertyName("warn")]
    public string? Warn { get; set; }

    [JsonPropertyName("bad")]
    public string? Bad { get; set; }

    [JsonPropertyName("idle")]
    public string? Idle { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// Settings for the engram item. Each color overrides one fragment; unset keeps
/// that fragment's built-in color, so a partially-specified block is valid.
public sealed class EngramItemSettings
{
    [JsonPropertyName("factsColor")]
    public string? FactsColor { get; set; }

    [JsonPropertyName("verbColor")]
    public string? VerbColor { get; set; }

    /// <summary>SPEC-104: which fields render and in what order. Unset renders exactly the SPEC-103 facts+verb pair.</summary>
    [JsonPropertyName("fields")]
    public List<EngramFieldJsonConfig>? Fields { get; set; }

    /// <summary>SPEC-104: the <c>--since</c> window passed to <c>engram activity --json</c>. Default "1h".</summary>
    [JsonPropertyName("activityWindow")]
    public string? ActivityWindow { get; set; }

    /// <summary>SPEC-104: cache TTL for the engram CLI probes. Defaults to CommandProvider's own TTL.</summary>
    [JsonPropertyName("ttlSeconds")]
    public int? TtlSeconds { get; set; }

    /// <summary>SPEC-104: subprocess timeout for the engram CLI probes. Defaults to CommandProvider's own timeout.</summary>
    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; set; }

    /// <summary>SPEC-104: how long since the newest activity before the state is Idle. Default 300.</summary>
    [JsonPropertyName("idleAfterSeconds")]
    public int? IdleAfterSeconds { get; set; }

    /// <summary>
    /// SPEC-104 §C.8: explicit engram binary path. A value with a directory separator must be
    /// rooted (absolute); a bare name (no separator) is looked up on PATH only, never resolved
    /// relative to cwd. Unset falls back to a PATH lookup, then a fixed absolute fallback list.
    /// </summary>
    [JsonPropertyName("binaryPath")]
    public string? BinaryPath { get; set; }

    /// <summary>SPEC-104 §C.7: reachability-state colour overrides. All default null (byte-identical defaults).</summary>
    [JsonPropertyName("stateColors")]
    public EngramStateColorsJsonConfig? StateColors { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>SPEC-104 §C.2: one field selected into the engram item's <c>fields</c> list.</summary>
public sealed class EngramFieldJsonConfig
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>A format template with a single "{}" placeholder for this field's rendered value.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>This field's colour — a literal color name or hex only; no @token, no inline rule.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>SPEC-104 §C.7: per-state colour overrides for the engram item. All default null.</summary>
public sealed class EngramStateColorsJsonConfig
{
    [JsonPropertyName("active")]
    public string? Active { get; set; }

    [JsonPropertyName("idle")]
    public string? Idle { get; set; }

    [JsonPropertyName("unreachable")]
    public string? Unreachable { get; set; }

    [JsonPropertyName("unavailable")]
    public string? Unavailable { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class RateLimitsItemSettings
{
    /// <summary>Which window(s) to display: "5h", "7d", or "both" (default).</summary>
    [JsonPropertyName("windows")]
    public string? Windows { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PrItemSettings
{
    /// <summary>Overrides for the review-state suffix, keyed by review state token (e.g. "approved").</summary>
    [JsonPropertyName("reviewStateLabels")]
    public Dictionary<string, string>? ReviewStateLabels { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LinearItemSettings
{
    /// <summary>
    /// The Linear workspace slug, used only to build this item's default issue link. Absent
    /// leaves the ticket id rendering as plain text.
    /// </summary>
    [JsonPropertyName("workspace")]
    public string? Workspace { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class BorderConfig
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("color")]
    [JsonConverter(typeof(ColorExprJsonConverter))]
    public ColorExprJsonConfig? Color { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §2.10: which of the four edges draw, when set individually rather than via a shorthand.</summary>
    [JsonPropertyName("edges")]
    public BorderEdgesConfig? Edges { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 1: populated only when this pane's <c>border</c> value was
    /// a bare JSON string (<c>"all"</c>/<c>"outline"</c>/<c>"inside"</c>/<c>"none"</c>) rather than an
    /// object — set by <see cref="BorderConfigConverter"/>'s string branch, never bound by ordinary
    /// property deserialization (deliberately no <see cref="JsonPropertyNameAttribute"/>).
    /// </summary>
    [JsonIgnore]
    public string? Shorthand { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 2: whether adjacent panes' shared edges draw as one line
    /// (§2.10's compositor border grid) instead of two. Legal only on
    /// <see cref="SurfaceConfig.Border"/> — present here on the shared <see cref="BorderConfig"/>
    /// shape purely so <c>collapse</c> declared anywhere else (<see cref="UserConfig.Border"/>, any
    /// <see cref="PaneConfig.Border"/>) is visible to <c>ConfigChecker</c>'s <c>collapse-not-surface-level</c>
    /// diagnostic rather than being silently dropped by a converter that never binds it.
    /// </summary>
    [JsonPropertyName("collapse")]
    public bool? Collapse { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>SPEC-V2-FRAMEWORK.md §2.10: an explicit per-edge <c>border.edges</c> declaration; an omitted field defaults to <c>true</c> (§2.9's "default is bordered" philosophy applied per edge).</summary>
public sealed class BorderEdgesConfig
{
    [JsonPropertyName("top")]
    public bool? Top { get; set; }

    [JsonPropertyName("right")]
    public bool? Right { get; set; }

    [JsonPropertyName("bottom")]
    public bool? Bottom { get; set; }

    [JsonPropertyName("left")]
    public bool? Left { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LayoutConfig
{
    [JsonPropertyName("chromeReserve")]
    public int? ChromeReserve { get; set; }

    /// <summary>SPEC-101-calibrate-chrome-reserve.md §12.7: default true; false suppresses the calibration nudge row entirely.</summary>
    [JsonPropertyName("calibrationPrompt")]
    public bool? CalibrationPrompt { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>SPEC-V2-FRAMEWORK.md §2.2/§8: the pane tree, present only once splits are configured.</summary>
public sealed class SurfaceConfig
{
    [JsonPropertyName("maxRows")]
    public int? MaxRows { get; set; }

    [JsonPropertyName("pane")]
    public PaneConfig? Pane { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §2.10.1 rule 2: the only legal home for <c>collapse</c>.</summary>
    [JsonPropertyName("border")]
    [JsonConverter(typeof(BorderConfigConverter))]
    public BorderConfig? Border { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PaneConfig
{
    /// <summary>SPEC pane-id-title-align §2: an optional identifier for this pane, for diagnostics
    /// and for addressing a specific pane. Separate namespace from an item's <c>id</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("split")]
    public string? Split { get; set; }

    [JsonPropertyName("children")]
    public List<PaneConfig>? Children { get; set; }

    [JsonPropertyName("size")]
    [JsonConverter(typeof(PaneSizeConverter))]
    public string? Size { get; set; }

    [JsonPropertyName("minSize")]
    public int? MinSize { get; set; }

    [JsonPropertyName("maxSize")]
    public int? MaxSize { get; set; }

    [JsonPropertyName("border")]
    [JsonConverter(typeof(BorderConfigConverter))]
    public BorderConfig? Border { get; set; }

    [JsonPropertyName("overflow")]
    public string? Overflow { get; set; }

    [JsonPropertyName("ellipsis")]
    public string? Ellipsis { get; set; }

    [JsonPropertyName("maxRows")]
    public int? MaxRows { get; set; }

    [JsonPropertyName("gutter")]
    public int? Gutter { get; set; }

    [JsonPropertyName("valign")]
    public string? Valign { get; set; }

    [JsonPropertyName("align")]
    public string? Align { get; set; }

    [JsonPropertyName("distribute")]
    public string? Distribute { get; set; }

    [JsonPropertyName("height")]
    public string? Height { get; set; }

    [JsonPropertyName("selfAlign")]
    public string? SelfAlign { get; set; }

    /// <summary>SPEC pane-id-title-align §3: this pane's title, authored with the ordinary item
    /// shape and drawn as a caption spliced into the pane's top border line. Requires a border
    /// with a top edge.</summary>
    [JsonPropertyName("title")]
    public PaneItemJsonConfig? Title { get; set; }

    /// <summary>SPEC pane-id-title-align §3.4: where the caption sits along the top border run.
    /// Defaults to <c>left</c>, which is a one-glyph inset from the top-left corner.</summary>
    [JsonPropertyName("titleAlign")]
    public string? TitleAlign { get; set; }

    [JsonPropertyName("items")]
    public List<PaneItemJsonConfig>? Items { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PaneItemJsonConfig
{
    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("color")]
    [JsonConverter(typeof(ColorExprJsonConverter))]
    public ColorExprJsonConfig? Color { get; set; }

    [JsonPropertyName("overflow")]
    public string? Overflow { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §4: a <c>command</c> item's own id — distinct from <see cref="Item"/>, which selects a builtin.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("command")]
    [JsonConverter(typeof(CommandJsonConverter))]
    public IReadOnlyList<string>? Command { get; set; }

    [JsonPropertyName("shell")]
    public bool? Shell { get; set; }

    [JsonPropertyName("ttlSeconds")]
    public int? TtlSeconds { get; set; }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §3.2: a URL template wrapping this item's rendered text in an OSC 8 hyperlink.</summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §3.2: names another item's id as this item's raw-value source, making this a derived item.</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>A regex applied to the <see cref="From"/> value; the first capture group, or the whole match when the pattern has none.</summary>
    [JsonPropertyName("extract")]
    public string? Extract { get; set; }

    /// <summary><c>"upper"</c>/<c>"lower"</c>; any other value passes the (post-<see cref="Extract"/>) text through unchanged.</summary>
    [JsonPropertyName("case")]
    public string? Case { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §4.0.1: caps how many lines this item's provider output produces, applied at the provider stage before §3.1's block-packing. Opt-in — no cap when unset.</summary>
    [JsonPropertyName("maxLines")]
    public int? MaxLines { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §3.3: this item's fragments, concatenated with no separator between them.</summary>
    [JsonPropertyName("parts")]
    public List<PaneItemPartJsonConfig>? Parts { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.3: one fragment of a compound item. Exactly one of <see cref="Text"/>/
/// <see cref="Item"/>/<see cref="From"/> is the part's source; the rest of the vocabulary is the
/// same one a pane item carries, because a part is an item fragment. <see cref="Parts"/> and
/// <see cref="Link"/> are declared solely so §3.3's one-level and item-level-link rules are
/// reported as <c>part-forbidden-key</c> errors rather than as <c>unknown-key</c> warnings.
/// </summary>
public sealed class PaneItemPartJsonConfig
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("extract")]
    public string? Extract { get; set; }

    [JsonPropertyName("case")]
    public string? Case { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("color")]
    [JsonConverter(typeof(ColorExprJsonConverter))]
    public ColorExprJsonConfig? Color { get; set; }

    [JsonPropertyName("parts")]
    public List<PaneItemPartJsonConfig>? Parts { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3: <c>size</c> is either a JSON number (exact cell count) or a JSON
/// string (<c>"auto"</c>/<c>"content"</c>/<c>"fill"</c>/<c>"NN%"</c>). Normalizes either token to
/// its string form so <see cref="PaneConfig.Size"/> stays a single plain type end to end.
/// </summary>
internal sealed class PaneSizeConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetInt32().ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4: <c>command</c> is a JSON array (argv, the default/documented form) or
/// a JSON string (only meaningful with <c>shell: true</c>, run as <c>sh -c "&lt;string&gt;"</c>).
/// Both forms normalize to a list here so <see cref="PaneItemJsonConfig.Command"/> stays one plain
/// type end to end; the shell form becomes a single-element list holding the script string, and
/// the <c>shell</c> flag is what tells the execution layer (§5) which of the two it is holding.
/// </summary>
internal sealed class CommandJsonConverter : JsonConverter<IReadOnlyList<string>?>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return value is null ? null : new[] { value };
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } s)
            {
                items.Add(s);
            }
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string>? value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Config is read-only; command is never serialized back out.");
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6: a "color" config value is either a JSON string (a literal like
/// <c>"blue"</c>/<c>"#ff5fd7"</c>, or a token reference like <c>"@model-accent"</c>) or a JSON
/// object (an inline rule). <see cref="ColorExprJsonConverter"/> normalizes both shapes into this
/// one holder, exactly one field populated, so binding does not re-sniff the JSON token type.
/// </summary>
public sealed class ColorExprJsonConfig
{
    public string? Literal { get; set; }

    public ColorRuleJsonConfig? Rule { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6.3/§6.4: the shared shape of a <c>colors</c>-table token and an inline
/// rule — identical JSON, differing only in whether <c>from</c> is required (table) or defaults to
/// the owning item (inline), which <see cref="ConfigLoader"/> enforces at binding time.
/// </summary>
public sealed class ColorRuleJsonConfig
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("thresholds")]
    public List<ThresholdJsonConfig>? Thresholds { get; set; }

    [JsonPropertyName("match")]
    public List<MatchJsonConfig>? Match { get; set; }

    [JsonPropertyName("default")]
    public string? Default { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ThresholdJsonConfig
{
    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>SPEC-V2-FRAMEWORK.md §6.4: each entry carries exactly one predicate — <see cref="Contains"/> (case-insensitive substring) or <see cref="EqualsValue"/> (case-insensitive full match), no regex.</summary>
public sealed class MatchJsonConfig
{
    [JsonPropertyName("contains")]
    public string? Contains { get; set; }

    [JsonPropertyName("equals")]
    public string? EqualsValue { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4.2: keys present in the JSON that this type does not define.
    /// Populated by the deserializer so <c>ConfigChecker</c>'s <c>unknown-key</c> diagnostic gets
    /// per-object scoping from binding rather than from a second hand-maintained shape mirror.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6: reads a "color" value as either a JSON string (literal or
/// <c>"@token"</c>) or a JSON object (inline rule), producing one <see cref="ColorExprJsonConfig"/>
/// shape. The nested <see cref="ColorRuleJsonConfig"/> deserialization reuses the caller's
/// <see cref="JsonSerializerOptions"/> so it still resolves through the AOT source-gen context.
/// </summary>
internal sealed class ColorExprJsonConverter : JsonConverter<ColorExprJsonConfig?>
{
    public override ColorExprJsonConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ColorExprJsonConfig { Literal = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        var rule = JsonSerializer.Deserialize(ref reader, ConfigJsonContext.Default.ColorRuleJsonConfig);
        return rule is null ? null : new ColorExprJsonConfig { Rule = rule };
    }

    public override void Write(Utf8JsonWriter writer, ColorExprJsonConfig? value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Config is read-only; color is never serialized back out.");
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.10: a pane's <c>border</c> value is either a JSON object (the existing
/// enabled/color/style/edges shape) or a bare shorthand string (<c>"all"</c>/<c>"outline"</c>/
/// <c>"inside"</c>/<c>"none"</c>, §2.10.1 rule 1). Mirrors <see cref="ColorExprJsonConverter"/>'s
/// string-or-object pattern; the object branch deserializes through <see cref="BorderConfig"/>'s
/// own source-gen metadata directly rather than recursing through this converter, since this
/// attribute is attached at the owning property (<see cref="UserConfig.Border"/>/
/// <see cref="PaneConfig.Border"/>), never at the <see cref="BorderConfig"/> type itself.
/// </summary>
internal sealed class BorderConfigConverter : JsonConverter<BorderConfig?>
{
    public override BorderConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new BorderConfig { Shorthand = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        return JsonSerializer.Deserialize(ref reader, ConfigJsonContext.Default.BorderConfig);
    }

    public override void Write(Utf8JsonWriter writer, BorderConfig? value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Config is read-only; border is never serialized back out.");
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(UserConfig))]
[JsonSerializable(typeof(ColorRuleJsonConfig))]
[JsonSerializable(typeof(BorderConfig))]
[JsonSerializable(typeof(BorderEdgesConfig))]
[JsonSerializable(typeof(LayoutConfig))]
[JsonSerializable(typeof(SurfaceConfig))]
[JsonSerializable(typeof(PaneConfig))]
[JsonSerializable(typeof(PaneItemJsonConfig))]
[JsonSerializable(typeof(PaneItemPartJsonConfig))]
[JsonSerializable(typeof(ThresholdJsonConfig))]
[JsonSerializable(typeof(MatchJsonConfig))]
[JsonSerializable(typeof(ItemSettingsJsonConfig))]
[JsonSerializable(typeof(DirectoryItemSettings))]
[JsonSerializable(typeof(ContextItemSettings))]
[JsonSerializable(typeof(RateLimitsItemSettings))]
[JsonSerializable(typeof(PrItemSettings))]
[JsonSerializable(typeof(LinearItemSettings))]
[JsonSerializable(typeof(WorktreeItemSettings))]
[JsonSerializable(typeof(OutputStyleItemSettings))]
[JsonSerializable(typeof(AutocompactItemSettings))]
[JsonSerializable(typeof(EngramItemSettings))]
[JsonSerializable(typeof(AhItemSettings))]
[JsonSerializable(typeof(AhStateColorsJsonConfig))]
public partial class ConfigJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Fully-resolved border configuration: every default has already been applied,
/// and <see cref="Style"/> is null exactly when the border should not render at all
/// (covers both <c>enabled: false</c> and <c>style: "none"</c>). <see cref="ChromeReserve"/> is
/// a layout setting, not a border one, but is resolved from the same config file/read as the
/// border settings (SPEC.md §6b "Config file"), so it travels on the same record.
/// <see cref="ColorSystem"/> (§6.2) defaults to <see cref="ColorSystemSupport.Standard"/>, keeping
/// the golden parity baseline byte-identical by construction. <see cref="Colors"/> (§6.3) is the
/// parsed <c>colors</c> token table, keyed by name without the leading <c>@</c>.
/// <see cref="SurfaceMaxRows"/> (§2.8.1) is the whole surface's row budget, defaulting to 8.
/// </summary>
public sealed record ResolvedConfig(
    ColorResolution.ColorExpr BorderColor,
    BoxBorder? Style,
    PaneBorderEdges Edges,
    int ChromeReserve,
    ColorSystemSupport ColorSystem,
    IReadOnlyDictionary<string, ColorResolution.ColorRule> Colors,
    int SurfaceMaxRows = ConfigLoader.DefaultSurfaceMaxRows,
    bool Collapse = false,
    ItemSettingsJsonConfig? ItemSettings = null,
    bool CalibrationPrompt = true);

public static class ConfigLoader
{
    private static readonly ColorResolution.ColorExpr DefaultColorExpr = new ColorResolution.ColorExpr.Literal("grey");
    private static readonly BoxBorder DefaultBoxBorder = BoxBorder.Rounded;

    // SPEC.md §6 "MEASURED", SPEC-98 (2026-08-20): 4, measured on an indented multi-row
    // bordered surface at COLUMNS=90 — Claude Code adds a 2-column left indent and trims
    // its own display to 2 columns short of COLUMNS, so 2+2=4 is required regardless of
    // row count (this repo deliberately reserves the multi-row figure uniformly rather
    // than varying by row count). One user's terminal, one Claude Code version; override
    // via layout.chromeReserve if yours differs.
    public const int DefaultChromeReserve = 4;

    // SPEC-V2-FRAMEWORK.md §2.8.1: the whole surface's default row budget.
    public const int DefaultSurfaceMaxRows = 8;

    public const string DefaultEllipsis = "…";

    /// <summary>
    /// Reads the config file exactly once and resolves both the top-level border/layout settings
    /// and the root pane from that single parse — the entry point <c>Program.cs</c> uses per
    /// render. <see cref="LoadBorderConfig"/> and <see cref="LoadRootPane"/> remain as independent,
    /// separately-testable single-purpose loaders (each still reads its own file), but a caller
    /// that needs both must use this instead of calling them back to back, which would read the
    /// file from disk twice.
    /// </summary>
    public static (ResolvedConfig TopLevel, Pane RootPane) LoadAll(string? configPath)
    {
        var config = configPath is null ? null : TryReadConfig(configPath);
        var topLevel = ResolveTopLevel(config);
        var rootPane = ResolveRootPane(config, topLevel);
        return (topLevel, rootPane);
    }

    public static ResolvedConfig LoadBorderConfig(string? configPath = null)
    {
        var config = configPath is null ? null : TryReadConfig(configPath);
        return ResolveTopLevel(config);
    }

    internal static ResolvedConfig ResolveTopLevel(UserConfig? config)
    {
        var (edges, _) = ResolveBorderPropagation(config?.Border, inherited: null, PaneSplit.None, childCount: 0);
        var resolvedBorder = ResolveBorder(config?.Border, isSplitContainer: false, edges);
        var chromeReserve = config?.Layout?.ChromeReserve ?? DefaultChromeReserve;
        var colorSystem = ParseColorSystem(config?.ColorSystem);
        var colors = ParseColorTable(config?.Colors);
        var surfaceMaxRows = config?.Surface?.MaxRows ?? DefaultSurfaceMaxRows;
        var collapse = config?.Surface?.Border?.Collapse ?? false;
        var calibrationPrompt = config?.Layout?.CalibrationPrompt ?? true;

        return new ResolvedConfig(resolvedBorder.Color, resolvedBorder.Style, resolvedBorder.Edges, chromeReserve, colorSystem, colors, surfaceMaxRows, collapse, config?.ItemSettings, calibrationPrompt);
    }

    private static readonly (string Token, ColorSystemSupport Value)[] ColorSystemAccepted =
    {
        ("standard", ColorSystemSupport.Standard),
        ("256", ColorSystemSupport.EightBit),
        ("truecolor", ColorSystemSupport.TrueColor),
    };

    internal static IReadOnlyList<string> ColorSystemAcceptedTokens { get; } = ColorSystemAccepted.Select(a => a.Token).ToArray();

    private static ColorSystemSupport? ParseColorSystemCore(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in ColorSystemAccepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    /// <summary>SPEC-V2-FRAMEWORK.md §6.2: unrecognized/absent values fall back to "standard", the golden-parity-preserving default.</summary>
    private static ColorSystemSupport ParseColorSystem(string? value) => ParseColorSystemCore(value) ?? ColorSystemSupport.Standard;

    /// <summary>
    /// True when <paramref name="value"/> was present but matched none of the recognized tokens —
    /// distinct from an absent field, which also defaults to <see cref="ColorSystemSupport.Standard"/>.
    /// §9.4's config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    internal static bool IsUnrecognizedColorSystem(string? value) => !string.IsNullOrWhiteSpace(value) && ParseColorSystemCore(value) is null;

    /// <summary>SPEC-V2-FRAMEWORK.md §6.3: a table entry's <c>from</c> is required and never defaulted — left null (and so silently degrading, §7) when omitted.</summary>
    private static IReadOnlyDictionary<string, ColorResolution.ColorRule> ParseColorTable(Dictionary<string, ColorRuleJsonConfig>? colors) =>
        colors?.ToDictionary(kv => kv.Key, kv => ParseColorRule(kv.Value, kv.Value.From), StringComparer.Ordinal)
        ?? (IReadOnlyDictionary<string, ColorResolution.ColorRule>)new Dictionary<string, ColorResolution.ColorRule>();

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §8: <c>surface</c> absent ⇒ a single root leaf pane holding the
    /// top-level <c>items</c>, with the top-level border. <c>surface</c> present ⇒ top-level
    /// <c>items</c> is ignored; the pane tree is authoritative.
    /// </summary>
    public static Pane LoadRootPane(string? configPath, ResolvedConfig topLevel)
    {
        var config = configPath is null ? null : TryReadConfig(configPath);
        return ResolveRootPane(config, topLevel);
    }

    public static Pane ResolveRootPane(UserConfig? config, ResolvedConfig topLevel)
    {
        var surfacePane = config?.Surface?.Pane;
        if (surfacePane is null)
        {
            return new Pane(
                PaneSplit.None,
                Array.Empty<Pane>(),
                "auto",
                new PaneBorder(topLevel.BorderColor, topLevel.Style, topLevel.Edges),
                null,
                DefaultEllipsis,
                null,
                ToPaneItems(config?.Items),
                Id: null,
                Title: null,
                SelfAlign: PaneSelfAlign.Left,
                TitleAlign: PaneTitleAlign.Left);
        }

        return ResolvePane(surfacePane, inherited: null);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 1: what an ancestor's <c>outline</c>/<c>inside</c>
    /// shorthand hands down to one child — the edges that child's border should resolve to absent
    /// its own explicit declaration, and whether that same instruction keeps propagating to
    /// <em>that child's own</em> descendants (<c>outline</c>: yes, unbounded depth, per "every
    /// descendant"; <c>inside</c>: no — see the open question flagged in the implementation report
    /// about whether a deeper, un-overridden nested split should inherit further).
    /// </summary>
    private readonly record struct InheritedBorderDirective(PaneBorderEdges Edges, bool ContinuesToDescendants);

    private static Pane ResolvePane(PaneConfig cfg, InheritedBorderDirective? inherited)
    {
        var childCfgs = cfg.Children ?? (IReadOnlyList<PaneConfig>)Array.Empty<PaneConfig>();
        var split = NormalizeSplit(ParseSplit(cfg.Split), childCfgs.Count);
        var isSplitContainer = split != PaneSplit.None;

        var (edges, childDirectives) = ResolveBorderPropagation(cfg.Border, inherited, split, childCfgs.Count);
        var border = ResolveBorder(cfg.Border, isSplitContainer, edges);

        var children = childCfgs.Count == 0
            ? (IReadOnlyList<Pane>)Array.Empty<Pane>()
            : childCfgs.Select((c, i) => ResolvePane(c, childDirectives[i])).ToList();

        return new Pane(
            split,
            children,
            cfg.Size ?? "auto",
            border,
            OverflowModeParsing.Parse(cfg.Overflow),
            cfg.Ellipsis ?? DefaultEllipsis,
            cfg.MaxRows,
            ToPaneItems(cfg.Items),
            cfg.MinSize,
            cfg.MaxSize,
            cfg.Gutter ?? 0,
            PaneValignParsing.Parse(cfg.Valign),
            PaneAlignParsing.Parse(cfg.Align),
            PaneDistributeParsing.Parse(cfg.Distribute),
            PaneHeightParsing.Parse(cfg.Height),
            cfg.Id,
            ToTitleItem(cfg.Title),
            PaneSelfAlignParsing.Parse(cfg.SelfAlign),
            PaneTitleAlignParsing.Parse(cfg.TitleAlign));
    }

    private static PaneItem? ToTitleItem(PaneItemJsonConfig? title) =>
        title is null ? null : ToPaneItems(new List<PaneItemJsonConfig> { title })[0] with { IsTitle = true };

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 1: resolves one pane's own edges plus, when that pane is a
    /// split, what each of its <paramref name="childCount"/> children inherits. A pane's own
    /// explicit <c>border</c> declaration — shorthand, <c>edges</c> object, or a plain
    /// enabled/color/style declaration — always wins over <paramref name="inherited"/> ("nearest
    /// declaration wins") and, since it is itself a fresh declaration, fully supersedes whatever the
    /// ancestor was propagating: only <c>outline</c>/<c>inside</c> hand anything further down.
    /// Absent an explicit declaration, an <c>outline</c> ancestor's instruction is flat and keeps
    /// propagating unchanged; an <c>inside</c> ancestor's instruction reaches only its direct
    /// children (<see cref="InsideChildDirectives"/>) and stops there.
    /// </summary>
    private static (PaneBorderEdges Edges, IReadOnlyList<InheritedBorderDirective?> ChildDirectives) ResolveBorderPropagation(
        BorderConfig? cfgBorder, InheritedBorderDirective? inherited, PaneSplit split, int childCount)
    {
        if (cfgBorder is not null)
        {
            if (string.Equals(cfgBorder.Shorthand, "outline", StringComparison.OrdinalIgnoreCase))
            {
                return (PaneBorderEdges.All, Repeat(new InheritedBorderDirective(PaneBorderEdges.None, ContinuesToDescendants: true), childCount));
            }

            if (string.Equals(cfgBorder.Shorthand, "inside", StringComparison.OrdinalIgnoreCase))
            {
                return (PaneBorderEdges.None, InsideChildDirectives(split, childCount));
            }

            if (string.Equals(cfgBorder.Shorthand, "all", StringComparison.OrdinalIgnoreCase))
            {
                return (PaneBorderEdges.All, Repeat(null, childCount));
            }

            if (string.Equals(cfgBorder.Shorthand, "none", StringComparison.OrdinalIgnoreCase))
            {
                return (PaneBorderEdges.None, Repeat(null, childCount));
            }

            if (cfgBorder.Edges is { } edgesCfg)
            {
                var edges = new PaneBorderEdges(
                    edgesCfg.Top ?? true,
                    edgesCfg.Right ?? true,
                    edgesCfg.Bottom ?? true,
                    edgesCfg.Left ?? true);
                return (edges, Repeat(null, childCount));
            }

            // A plain enabled/color/style-only declaration (no shorthand or edges object), or an
            // unrecognized shorthand token — falls back to all four edges, the same conservative
            // default a plain declaration always resolved to before edges existed.
            return (PaneBorderEdges.All, Repeat(null, childCount));
        }

        if (inherited is { } directive)
        {
            return (directive.Edges, Repeat(directive.ContinuesToDescendants ? directive : null, childCount));
        }

        return (PaneBorderEdges.All, Repeat(null, childCount));
    }

    private static IReadOnlyList<InheritedBorderDirective?> Repeat(InheritedBorderDirective? value, int count) =>
        Enumerable.Repeat(value, count).ToList();

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 1: <c>inside</c>'s per-child edges — each child is charged
    /// for the edge(s) it shares with its immediate neighbour(s) along the split axis (the first
    /// child has nothing before it, the last nothing after), and nothing on the cross axis.
    /// </summary>
    private static IReadOnlyList<InheritedBorderDirective?> InsideChildDirectives(PaneSplit split, int childCount)
    {
        var result = new InheritedBorderDirective?[childCount];
        for (var i = 0; i < childCount; i++)
        {
            var isFirst = i == 0;
            var isLast = i == childCount - 1;
            var edges = split == PaneSplit.Horizontal
                ? new PaneBorderEdges(Top: !isFirst, Right: false, Bottom: !isLast, Left: false)
                : new PaneBorderEdges(Top: false, Right: !isLast, Bottom: false, Left: !isFirst);
            result[i] = new InheritedBorderDirective(edges, ContinuesToDescendants: false);
        }

        return result;
    }

    private static readonly (string Token, PaneSplit Value)[] SplitAccepted =
    {
        ("none", PaneSplit.None),
        ("horizontal", PaneSplit.Horizontal),
        ("vertical", PaneSplit.Vertical),
        ("flex", PaneSplit.Flex),
    };

    internal static IReadOnlyList<string> SplitAcceptedTokens { get; } = SplitAccepted.Select(a => a.Token).ToArray();

    internal static PaneSplit? ParseSplitCore(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in SplitAccepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    private static PaneSplit ParseSplit(string? value) => ParseSplitCore(value) ?? PaneSplit.None;

    /// <summary>
    /// True when <paramref name="value"/> was present but matched none of the recognized tokens —
    /// distinct from an absent field, which also defaults to <see cref="PaneSplit.None"/>. §9.4's
    /// config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    internal static bool IsUnrecognizedSplit(string? value) => !string.IsNullOrWhiteSpace(value) && ParseSplitCore(value) is null;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.2: <c>split</c> and <c>children</c> must agree before a pane is
    /// stored — every downstream reader (border defaulting, layout, rendering) treats "has
    /// children" and "is a split container" as the same question, so this is the one place that
    /// question gets decided. Non-empty children with no explicit <c>split</c> normalizes to
    /// <c>vertical</c> (side-by-side is the statusline-shaped default for a stated split intent);
    /// an explicit <c>split</c> with no children drops back to a leaf.
    /// </summary>
    private static PaneSplit NormalizeSplit(PaneSplit parsedSplit, int childCount) =>
        childCount == 0 ? PaneSplit.None : (parsedSplit == PaneSplit.None ? PaneSplit.Vertical : parsedSplit);

    private static IReadOnlyList<PaneItem> ToPaneItems(List<PaneItemJsonConfig>? items) =>
        items?.Select(i => new PaneItem(
            i.Item,
            i.Format,
            ParseColorExpr(i.Color, i.Id ?? i.Item),
            OverflowModeParsing.Parse(i.Overflow),
            i.Id,
            i.Command,
            i.Shell ?? false,
            i.TtlSeconds,
            i.TimeoutMs,
            i.Link,
            i.From,
            i.Extract,
            i.Case,
            i.MaxLines,
            ToPaneItemParts(i.Parts))).ToList()
        ?? (IReadOnlyList<PaneItem>)Array.Empty<PaneItem>();

    private static IReadOnlyList<PaneItemPart>? ToPaneItemParts(List<PaneItemPartJsonConfig>? parts) =>
        parts?.Select(p => new PaneItemPart(
            p.Text,
            p.Item,
            p.From,
            p.Extract,
            p.Case,
            p.Format,
            ParseColorExpr(p.Color, p.Item))).ToList();

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.9 ruling: absent an explicit <c>border.enabled</c>, a leaf pane
    /// defaults to bordered (v1 behavior) and a split container defaults to borderless, so adding
    /// a split to a config never silently adds chrome.
    /// </summary>
    private static PaneBorder ResolveBorder(BorderConfig? border, bool isSplitContainer, PaneBorderEdges edges)
    {
        var enabled = border?.Enabled ?? !isSplitContainer;
        var color = ParseColorExpr(border?.Color, owningItemId: null) ?? DefaultColorExpr;

        BoxBorder? style = DefaultBoxBorder;
        if (!string.IsNullOrEmpty(border?.Style))
        {
            style = BorderStyleParsing.TryParse(border!.Style!, out var parsed) ? parsed : DefaultBoxBorder;
        }

        if (!enabled)
        {
            style = null;
        }

        // §2.10: a pane whose resolved edges are all off (an explicit "none"/all-false edges
        // object, or an "outline" ancestor forcing this descendant off) has nothing to draw —
        // collapse Style to null so it also carries zero reserve, rather than staying "enabled"
        // with unused padding reserve and nothing to render.
        if (style is not null && !edges.Top && !edges.Right && !edges.Bottom && !edges.Left)
        {
            style = null;
        }

        return new PaneBorder(color, style, edges);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §6.3/§6.4: a literal string, a <c>"@name"</c> token reference, or an
    /// inline rule object. <paramref name="owningItemId"/> is the inline-rule <c>from</c> default
    /// (§6.4) — an item's own id for an item's <c>color</c>, or null for a border, which has no
    /// value of its own to default to.
    /// </summary>
    private static ColorResolution.ColorExpr? ParseColorExpr(ColorExprJsonConfig? cfg, string? owningItemId) => cfg switch
    {
        null => null,
        { Literal: { Length: > 0 } lit } => lit[0] == '@'
            ? new ColorResolution.ColorExpr.TokenRef(lit[1..])
            : new ColorResolution.ColorExpr.Literal(lit),
        { Rule: { } rule } => new ColorResolution.ColorExpr.Inline(ParseColorRule(rule, rule.From ?? owningItemId)),
        _ => null,
    };

    // The leaf-position counterpart to ParseColorExpr's sigil test above. These two are the only
    // @-inspecting sites in the codebase, per SPEC-44-color-token-in-rule-branches.md §4.1.
    private static ColorResolution.ColorValue ParseColorValue(string raw) =>
        raw[0] == '@'
            ? new ColorResolution.ColorValue.TokenRef(raw[1..])
            : new ColorResolution.ColorValue.Literal(raw);

    /// <summary>
    /// A threshold/match entry with no <c>color</c> of its own specifies nothing usable, so it is
    /// dropped rather than carried forward as an empty colour spec (§7's silent-degrade convention).
    /// <paramref name="from"/> is the caller's resolved choice — a table entry passes its own
    /// (possibly null) <c>from</c> unchanged, while an inline rule has already applied its default.
    /// </summary>
    private static ColorResolution.ColorRule ParseColorRule(ColorRuleJsonConfig cfg, string? from) =>
        new(
            cfg.Thresholds?.Where(t => !string.IsNullOrEmpty(t.Color)).Select(t => new ColorResolution.ThresholdRule(t.Min, ParseColorValue(t.Color!))).ToList(),
            cfg.Match?.Where(m => !string.IsNullOrEmpty(m.Color)).Select(m => new ColorResolution.MatchRule(m.Contains, m.EqualsValue, ParseColorValue(m.Color!))).ToList(),
            string.IsNullOrEmpty(cfg.Default) ? null : ParseColorValue(cfg.Default),
            from);

    private static UserConfig? TryReadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize(text, ConfigJsonContext.Default.UserConfig);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.2/§9.4: unlike <see cref="TryReadConfig"/> (whose blanket
    /// missing-or-malformed → null is deliberate for the render path, §7), <c>--check</c> and
    /// <c>--config</c> need to tell "no file here" (fine — resolves to defaults) apart from "a file
    /// is here and it does not parse" (exit 3 — nothing could be checked). This is that read,
    /// reused by both.
    /// </summary>
    public static ConfigReadResult ReadConfigForCheck(string path)
    {
        if (!File.Exists(path))
        {
            return new ConfigReadResult(ConfigReadStatus.NoFile, null, null);
        }

        try
        {
            var text = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize(text, ConfigJsonContext.Default.UserConfig);
            return new ConfigReadResult(ConfigReadStatus.Parsed, config, null);
        }
        catch (Exception ex)
        {
            // SPEC-V2-FRAMEWORK.md §9.2.2: the render path's diagnostic row composes its reason
            // from JsonException's typed LineNumber/Path rather than parsing Message, since .NET's
            // wording is not a stable contract. Both are only present when the parser itself
            // pinpointed a position; a non-JsonException failure (e.g. a permission error from
            // File.ReadAllText) carries neither.
            if (ex is JsonException { LineNumber: { } lineNumber, Path: { } jsonPath })
            {
                return new ConfigReadResult(ConfigReadStatus.ParseError, null, ex.Message, lineNumber, jsonPath);
            }

            return new ConfigReadResult(ConfigReadStatus.ParseError, null, ex.Message);
        }
    }
}

public enum ConfigReadStatus
{
    NoFile,
    Parsed,
    ParseError,
}

public sealed record ConfigReadResult(ConfigReadStatus Status, UserConfig? Config, string? ErrorMessage, long? ErrorLineNumber = null, string? ErrorJsonPath = null);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.9's five named border styles plus <c>"none"</c> (no border), the same
/// set <see cref="ConfigLoader"/>'s border resolution has always accepted — pulled out to a named
/// parser (mirroring <see cref="OverflowModeParsing"/>/<see cref="PaneValignParsing"/>) so <c>--check</c>
/// can ask "was this string one of the recognized ones" through the exact same accepted-token lookup
/// the loader uses, rather than a second copy of the token list.
/// </summary>
internal static class BorderStyleParsing
{
    private static readonly (string Token, BoxBorder? Style)[] Accepted =
    {
        ("rounded", BoxBorder.Rounded),
        ("square", BoxBorder.Square),
        ("heavy", BoxBorder.Heavy),
        ("double", BoxBorder.Double),
        ("ascii", BoxBorder.Ascii),
        ("none", null),
    };

    internal static IReadOnlyList<string> AcceptedTokens { get; } = Accepted.Select(a => a.Token).ToArray();

    public static bool TryParse(string value, out BoxBorder? style)
    {
        var normalized = value.Trim().ToLowerInvariant();
        foreach (var (token, s) in Accepted)
        {
            if (token == normalized)
            {
                style = s;
                return true;
            }
        }

        style = null;
        return false;
    }
}
