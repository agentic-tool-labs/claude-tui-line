using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

public sealed record SchemaKindSupportJson(
    [property: JsonPropertyName("supported")] bool Supported,
    [property: JsonPropertyName("unsupportedKeys")] IReadOnlyList<string> UnsupportedKeys);

public sealed record SchemaKindSupportTableJson(
    [property: JsonPropertyName("builtin")] SchemaKindSupportJson Builtin,
    [property: JsonPropertyName("derived")] SchemaKindSupportJson Derived,
    [property: JsonPropertyName("command")] SchemaKindSupportJson Command,
    [property: JsonPropertyName("compound")] SchemaKindSupportJson Compound);

public sealed record StructureFieldJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("acceptedKey")] string? AcceptedKey);

public sealed record StructureEntryJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("record")] string? Record,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("required")] IReadOnlyList<string> Required,
    [property: JsonPropertyName("optional")] IReadOnlyList<string> Optional,
    [property: JsonPropertyName("fields")] IReadOnlyList<StructureFieldJson> Fields,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes,
    [property: JsonPropertyName("example")] JsonElement Example);

public sealed record SchemaResultJson(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("items")] ItemsResultJson Items,
    [property: JsonPropertyName("colors")] ColorsResultJson Colors,
    [property: JsonPropertyName("accepted")] AcceptedResultJson Accepted,
    [property: JsonPropertyName("kindSupport")] SchemaKindSupportTableJson KindSupport,
    [property: JsonPropertyName("structures")] IReadOnlyList<StructureEntryJson> Structures);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(SchemaResultJson))]
public partial class SchemaJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-84-mcp-schema-explorer.md §5: <c>--schema --json</c> aggregates <see cref="ItemsCommand"/>,
/// <see cref="ColorsCommand"/>, and <see cref="AcceptedCommand"/>'s own results verbatim (§5.1/D3 —
/// embedding, never re-deriving, is what keeps this byte-identical to each solo command), plus two
/// sections no existing command covers: <c>kindSupport</c> (§5.2, computed against the real config
/// model so #85 flips it with no edit here) and <c>structures</c> (§5.3, the config document's
/// structural shape, hand-authored and pinned by SchemaCommandTests' V4 reflection check). Reads no
/// config and probes nothing — always exits 0, no failure mode short of a crash.
/// </summary>
public static class SchemaCommand
{
    // §5.2: declared, not reflected — the core publishes AOT, and reflecting over
    // PaneItemJsonConfig's properties is unsound under trimming. SchemaCommandTests.V3 asserts this
    // list against the real type, so drift fails the build instead of producing a wrong answer at
    // runtime.
    private static readonly IReadOnlyList<string> ModelItemKeys = new[]
    {
        "item", "format", "color", "overflow", "id", "command", "shell",
        "ttlSeconds", "timeoutMs", "link", "from", "extract", "case", "maxLines", "parts",
    };

    public static SchemaResultJson Build()
    {
        var items = ItemsCommand.Build();
        var colors = ColorsCommand.Build();
        var accepted = AcceptedCommand.Build();

        var kindSupport = new SchemaKindSupportTableJson(
            ComputeKindSupport(items.Kinds.Builtin),
            ComputeKindSupport(items.Kinds.Derived),
            ComputeKindSupport(items.Kinds.Command),
            ComputeKindSupport(items.Kinds.Compound));

        return new SchemaResultJson(
            AssemblyVersionInfo.InformationalVersion,
            items,
            colors,
            accepted,
            kindSupport,
            BuildStructures());
    }

    private static SchemaKindSupportJson ComputeKindSupport(ItemKindJson kind)
    {
        var unsupported = kind.Required
            .Concat(kind.Optional)
            .Where(k => !ModelItemKeys.Contains(k, StringComparer.Ordinal))
            .ToList();

        return new SchemaKindSupportJson(unsupported.Count == 0, unsupported);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static StructureFieldJson Field(string name, string type, string description, string? acceptedKey = null) =>
        new(name, type, description, acceptedKey);

    // §5.3: hand-authored, eighteen entries — there is no runtime derivation available under AOT.
    // SchemaCommandTests.V4 asserts, by reflection in the test, that each entry with a non-null
    // `record` matches its record type's [JsonPropertyName] values exactly; V4 also pins the entry
    // set itself to these eighteen names.
    private static IReadOnlyList<StructureEntryJson> BuildStructures() => new[]
    {
        new StructureEntryJson(
            "config",
            "UserConfig",
            "The config document root.",
            Array.Empty<string>(),
            new[] { "border", "layout", "items", "surface", "colorSystem", "colors", "itemSettings" },
            new[]
            {
                Field("border", "border | string (shorthand)", "Top-level border, used for the single root leaf pane when no surface/pane tree is configured."),
                Field("layout", "layout", "Layout settings."),
                Field("items", "array<item>", "Top-level items for the single root leaf pane, used when no surface/pane tree is configured."),
                Field("surface", "surface", "The pane tree root. When present, items above is ignored and the pane tree is authoritative."),
                Field("colorSystem", "string", "Opt-in rendering colour-system profile.", "colorSystem"),
                Field("colors", "object<string,colorRule>", "Named, reusable colour tokens table, referenced elsewhere as \"@name\"."),
                Field("itemSettings", "itemSettings", "Per-item settings, keyed by builtin item id."),
            },
            Array.Empty<string>(),
            Parse("{}")),

        new StructureEntryJson(
            "border",
            "BorderConfig",
            "Border settings for a pane, a surface, or the top-level config.",
            Array.Empty<string>(),
            new[] { "enabled", "color", "style", "edges", "collapse" },
            new[]
            {
                Field("enabled", "boolean", "Whether this pane/surface draws a border at all."),
                Field("color", "colorExpr", "The border's colour — a literal/@token string, or an inline colour rule."),
                Field("style", "string", "The border box-drawing style.", "border.style"),
                Field("edges", "borderEdges", "Explicit per-edge on/off overrides, used instead of a shorthand."),
                Field("collapse", "boolean", "Whether adjacent panes' shared border edges draw as one shared line. Legal only on surface.border; declared elsewhere it is flagged by the collapse-not-surface-level diagnostic."),
            },
            new[]
            {
                "A bare JSON string in place of this object (\"all\"/\"outline\"/\"inside\"/\"none\") is also accepted — the border shorthand form. BorderConfig.Shorthand carries no wire key ([JsonPropertyName]) and is bound only by BorderConfigConverter's string branch.",
            },
            Parse("""{"enabled":true}""")),

        new StructureEntryJson(
            "borderEdges",
            "BorderEdgesConfig",
            "An explicit per-edge border.edges declaration.",
            Array.Empty<string>(),
            new[] { "top", "right", "bottom", "left" },
            new[]
            {
                Field("top", "boolean", "Whether the top edge draws; defaults to true when omitted."),
                Field("right", "boolean", "Whether the right edge draws; defaults to true when omitted."),
                Field("bottom", "boolean", "Whether the bottom edge draws; defaults to true when omitted."),
                Field("left", "boolean", "Whether the left edge draws; defaults to true when omitted."),
            },
            Array.Empty<string>(),
            Parse("{}")),

        new StructureEntryJson(
            "layout",
            "LayoutConfig",
            "Layout settings.",
            Array.Empty<string>(),
            new[] { "chromeReserve", "calibrationPrompt" },
            new[]
            {
                Field("chromeReserve", "integer", "Columns reserved for Claude Code's own statusline chrome, subtracted from COLUMNS when sizing the surface. Raise it if rows are being truncated with an ellipsis on your terminal or Claude Code version. Run `claude-tui-line --calibrate` to measure it instead of guessing. The automatic nudge only fires on a major.minor Claude Code version change, so a patch release that shifts the chrome width will not trigger it — if truncation appears without a version nudge, re-run --calibrate manually."),
                Field("calibrationPrompt", "boolean", "Whether claude-tui-line may append a one-line nudge to run --calibrate on first use or after a Claude Code version change. Defaults to true; set to false to opt out."),
            },
            Array.Empty<string>(),
            Parse("{}")),

        new StructureEntryJson(
            "surface",
            "SurfaceConfig",
            "The pane tree, present only once splits are configured.",
            Array.Empty<string>(),
            new[] { "maxRows", "pane", "border" },
            new[]
            {
                Field("maxRows", "integer", "The whole surface's row budget."),
                Field("pane", "pane", "The pane tree root."),
                Field("border", "border", "The only legal home for border.collapse."),
            },
            Array.Empty<string>(),
            Parse("""{"pane":{"items":[]}}""")),

        new StructureEntryJson(
            "pane",
            "PaneConfig",
            "A rectangular region of the statusline. A branch pane carries split+children; a leaf pane carries items.",
            Array.Empty<string>(),
            new[] { "split", "children", "size", "minSize", "maxSize", "border", "overflow", "ellipsis", "maxRows", "gutter", "valign", "align", "distribute", "height", "id", "title", "titleAlign", "selfAlign", "items" },
            new[]
            {
                Field("split", "string", "none/horizontal/vertical/flex — set together with children for a branch pane.", "split"),
                Field("children", "array<pane>", "Child panes, present on a branch pane."),
                Field("size", "string", "This pane's share of its parent's axis.", "size"),
                Field("minSize", "integer", "Lower bound on this pane's resolved size."),
                Field("maxSize", "integer", "Upper bound on this pane's resolved size."),
                Field("border", "border | string (shorthand)", "This pane's own border."),
                Field("overflow", "string", "How this pane's content behaves when it doesn't fit.", "overflow"),
                Field("ellipsis", "string", "Truncation marker used when content overflows."),
                Field("maxRows", "integer", "This pane's own row budget, applied on top of the surface's."),
                Field("gutter", "integer", "Cell gap inserted between this pane's children."),
                Field("valign", "string", "Vertical alignment of this pane's content within its own box; also places this pane's box within its sibling band when it is shorter than its siblings."),
                Field("align", "string", "Horizontal alignment of this pane's content within its own box (see selfAlign for positioning the box itself, and titleAlign for the caption)."),
                Field("distribute", "string", "How this pane distributes leftover space among its children.", "distribute"),
                Field("height", "string", "This pane's height policy.", "height"),
                Field("id", "string", "An optional identifier for this pane. Separate namespace from an item's id; not referencable by 'from', link substitution, or 'color.from'."),
                Field("title", "item", "This pane's title, authored with the ordinary item shape and drawn as a caption in the pane's top border line. Requires a border with a top edge; always single-line and truncating."),
                Field("titleAlign", "string", "Where the title caption sits along the top border run. Only has a visible effect when the caption is short enough to leave slack.", "titleAlign"),
                Field("selfAlign", "string", "Where this pane's own box sits in the leftover space of its parent's row — as opposed to align, which positions content inside this pane's box. No effect when a sibling is fill-sized.", "selfAlign"),
                Field("items", "array<item>", "This pane's items, present on a leaf pane."),
            },
            new[]
            {
                "A branch pane sets split and children; a leaf pane sets items. Setting both is a config error.",
                "PaneConfig serves both roles — there is no separate split/branch record.",
                "selfAlign positions this pane within its parent's row and only has an effect when the row has leftover width; align positions content inside this pane.",
                "title is drawn into the top border line, not as a content row: it consumes no rows and no row budget, and it is dropped when the pane has no top border edge or is too narrow to hold it.",
                "titleAlign moves the caption along the border run; it never changes whether the caption fits, and a caption truncated to the full available width renders identically under all three values.",
            },
            Parse("""{"items":[]}""")),

        new StructureEntryJson(
            "item",
            "PaneItemJsonConfig",
            "An item placed in a leaf pane. Every wire key the model type accepts is listed here regardless of item kind — per-kind required/optional sets are reported in items.kinds and kindSupport, not here.",
            Array.Empty<string>(),
            new[] { "item", "format", "color", "overflow", "id", "command", "shell", "ttlSeconds", "timeoutMs", "link", "from", "extract", "case", "maxLines", "parts" },
            new[]
            {
                Field("item", "string", "Selects a builtin item id."),
                Field("format", "string", "A format template applied to this item's raw value."),
                Field("color", "colorExpr", "This item's colour — a literal/@token string, or an inline colour rule."),
                Field("overflow", "string", "How this item's content behaves when it doesn't fit.", "overflow"),
                Field("id", "string", "A command/derived/compound item's own id — distinct from item, which selects a builtin."),
                Field("command", "array<string> | string (shell form)", "The command to run — a JSON array (argv), or a JSON string, only meaningful with shell:true."),
                Field("shell", "boolean", "Whether command runs through a shell (sh -c) rather than as argv."),
                Field("ttlSeconds", "integer", "How long a command item's cached value stays fresh."),
                Field("timeoutMs", "integer", "How long a command item is allowed to run before being treated as unavailable."),
                Field("link", "string", "A URL template wrapping this item's rendered text in an OSC 8 hyperlink."),
                Field("from", "string", "Names another item's id as this item's raw-value source, making this a derived item."),
                Field("extract", "string", "A regex applied to the from value; the first capture group, or the whole match when the pattern has none."),
                Field("case", "string", "upper/lower; any other value passes the text through unchanged.", "case"),
                Field("maxLines", "integer", "Caps how many lines this item's provider output produces."),
                Field("parts", "array<compoundPart>", "Declares this item as a compound item: its ordered fragments, concatenated with no separator."),
            },
            new[]
            {
                "command accepts a bare JSON string as well as an array — CommandJsonConverter normalizes either to a list; the string form is only meaningful with shell:true.",
            },
            Parse("""{"item":"cwd"}""")),

        new StructureEntryJson(
            "colorRule",
            "ColorRuleJsonConfig",
            "The shared shape of a colors-table token and an inline colour rule.",
            Array.Empty<string>(),
            new[] { "from", "thresholds", "match", "default" },
            new[]
            {
                Field("from", "string", "The item id (or explicit override) this rule's thresholds/match compare against."),
                Field("thresholds", "array<threshold>", "Numeric threshold arms, evaluated in order."),
                Field("match", "array<match>", "String-match arms, evaluated in order."),
                Field("default", "string", "Fallback colour value when no threshold/match arm applies."),
            },
            Array.Empty<string>(),
            Parse("""{"from":"some-item","default":"green"}""")),

        new StructureEntryJson(
            "threshold",
            "ThresholdJsonConfig",
            "One threshold arm of a colour rule.",
            new[] { "min" },
            new[] { "color" },
            new[]
            {
                Field("min", "number", "The threshold's lower bound (inclusive)."),
                Field("color", "string", "The colour value applied when min is met."),
            },
            Array.Empty<string>(),
            Parse("""{"min":0,"color":"green"}""")),

        new StructureEntryJson(
            "match",
            "MatchJsonConfig",
            "One match arm of a colour rule.",
            Array.Empty<string>(),
            new[] { "contains", "equals", "color" },
            new[]
            {
                Field("contains", "string", "Case-insensitive substring predicate."),
                Field("equals", "string", "Case-insensitive full-match predicate."),
                Field("color", "string", "The colour value applied when the predicate matches."),
            },
            new[]
            {
                "Exactly one predicate — contains or equals — is meaningful per entry; carrying both is not rejected by the type.",
            },
            Parse("""{"contains":"error","color":"red"}""")),

        new StructureEntryJson(
            "colorExpr",
            "ColorExprJsonConfig",
            "A \"color\" config value: either a literal/@token colour string, or an inline colour rule object.",
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<StructureFieldJson>(),
            new[]
            {
                "Accepts either a colour string (a standard name from the colors section, or a @name reference), or a colorRule object.",
            },
            Parse("\"green\"")),

        new StructureEntryJson(
            "compoundPart",
            "PaneItemPartJsonConfig",
            "A compound item's part — one fragment of a compound item (SPEC-V2-FRAMEWORK.md §3.3). Exactly one of text/item/from is its source; the rest of its vocabulary mirrors a pane item's, because a part is an item fragment.",
            Array.Empty<string>(),
            new[] { "text", "item", "from", "extract", "case", "format", "color", "parts", "link" },
            new[]
            {
                Field("text", "string", "A literal text fragment."),
                Field("item", "string", "Selects a builtin item id as this part's source."),
                Field("from", "string", "Names another item's id as this part's raw-value source."),
                Field("extract", "string", "A regex applied to the from value; the first capture group, or the whole match when the pattern has none."),
                Field("case", "string", "upper/lower; any other value passes the text through unchanged.", "case"),
                Field("format", "string", "A format template applied to this part's raw value."),
                Field("color", "colorExpr", "This part's colour — a literal/@token string, or an inline colour rule."),
                Field("parts", "array<compoundPart>", "Declared so a part carrying parts is reported as a part-forbidden-key diagnostic rather than an unknown-key warning — a part may not itself be compound."),
                Field("link", "string", "Declared so a part carrying link is reported as a part-forbidden-key diagnostic rather than an unknown-key warning — link stays at item level and wraps the whole compound."),
            },
            new[]
            {
                "Exactly one of text/item/from is the part's source; a part with zero or more than one source is a part-source-count diagnostic.",
                "Parts render concatenated with no separator; an all-empty compound renders nothing and collapses.",
                "A part may not carry parts or link — both are declared as wire keys solely so violating them is reported as a part-forbidden-key diagnostic rather than an unknown-key warning.",
            },
            Parse("""{"text":"agent:"}""")),

        new StructureEntryJson(
            "itemSettings",
            "ItemSettingsJsonConfig",
            "Per-item settings, keyed by builtin item id.",
            Array.Empty<string>(),
            new[] { "directory", "context", "rateLimits", "pr", "linear", "worktree", "outputStyle", "autocompact", "engram" },
            new[]
            {
                Field("directory", "directoryItemSettings", "Settings for the directory item."),
                Field("context", "contextItemSettings", "Settings for the context item."),
                Field("rateLimits", "rateLimitsItemSettings", "Settings for the rate-limits item."),
                Field("pr", "prItemSettings", "Settings for the pr item."),
                Field("linear", "linearItemSettings", "Settings for the linear item."),
                Field("worktree", "worktreeItemSettings", "Settings for the worktree item."),
                Field("outputStyle", "outputStyleItemSettings", "Settings for the output-style item."),
                Field("autocompact", "autocompactItemSettings", "Settings for the autocompact item."),
                Field("engram", "engramItemSettings", "Settings for the engram item."),
            },
            Array.Empty<string>(),
            Parse("{}")),

        new StructureEntryJson(
            "directoryItemSettings",
            "DirectoryItemSettings",
            "Settings for the directory item.",
            Array.Empty<string>(),
            new[] { "depth", "openWith" },
            new[]
            {
                Field("depth", "integer", "How many trailing path segments to show. 1 = basename (the default)."),
                Field("openWith", "string", "Where the default hyperlink points: \"files\" (the OS file browser, the default) or \"vscode\" (open the directory in VS Code)."),
            },
            Array.Empty<string>(),
            Parse("""{"depth":2}""")),

        new StructureEntryJson(
            "contextItemSettings",
            "ContextItemSettings",
            "Settings for the context item.",
            Array.Empty<string>(),
            new[] { "showDetail" },
            new[]
            {
                Field("showDetail", "boolean", "Whether the token-count parenthetical renders beside the percentage. Default true."),
            },
            Array.Empty<string>(),
            Parse("""{"showDetail":false}""")),

        new StructureEntryJson(
            "worktreeItemSettings",
            "WorktreeItemSettings",
            "Settings for the worktree item.",
            Array.Empty<string>(),
            new[] { "showBranch" },
            new[]
            {
                Field("showBranch", "boolean", "Whether the branch renders in parentheses after the worktree name. Default false."),
            },
            Array.Empty<string>(),
            Parse("""{"showBranch":true}""")),

        new StructureEntryJson(
            "outputStyleItemSettings",
            "OutputStyleItemSettings",
            "Settings for the output-style item.",
            Array.Empty<string>(),
            new[] { "showLabel", "labelColor" },
            new[]
            {
                Field("showLabel", "boolean", "Whether the \"style:\" label renders before the style name. Default true."),
                Field("labelColor", "string", "Color for the \"style:\" label only, leaving the value's color unchanged. Unset renders the whole item in one color."),
            },
            Array.Empty<string>(),
            Parse("""{"showLabel":false}""")),

        new StructureEntryJson(
            "autocompactItemSettings",
            "AutocompactItemSettings",
            "Settings for the autocompact item.",
            Array.Empty<string>(),
            new[] { "showLabel", "labelColor" },
            new[]
            {
                Field("showLabel", "boolean", "Whether the \"autocompact:\" label renders before the value. Default true."),
                Field("labelColor", "string", "Color for the \"autocompact:\" label only, leaving the value's color unchanged. Unset renders the whole item in one color."),
            },
            Array.Empty<string>(),
            Parse("""{"showLabel":false}""")),

        new StructureEntryJson(
            "engramItemSettings",
            "EngramItemSettings",
            "Settings for the engram item. Unconfigured (the default), it renders exactly the free telemetry.jsonl-derived facts/verb pair, with zero subprocesses. Selecting a field from the \"server\"/\"version\"/\"uptime\"/\"port\"/\"pid\" set or setting stateColors triggers one `engram status --json` spawn per render; selecting a field from the \"lastKind\"/\"lastAge\"/\"windowCount\"/\"kind:<name>\" set, or setting stateColors, triggers one `engram activity --json` spawn per render.",
            Array.Empty<string>(),
            new[] { "factsColor", "verbColor", "fields", "activityWindow", "ttlSeconds", "timeoutMs", "idleAfterSeconds", "binaryPath", "stateColors" },
            new[]
            {
                Field("factsColor", "string", "Color for the \"engram:N\" fragment when fields is unset. A literal color name or hex; token references and inline rules are not supported here. Default dim."),
                Field("verbColor", "string", "Color for the activity fragment (e.g. \"◉ recalled\") when fields is unset. A literal color name or hex; token references and inline rules are not supported here. Default purple."),
                Field("fields", "array<engramField>", "Which fields render and in what order. Unset renders exactly facts then verb (today's default)."),
                Field("activityWindow", "string", "The --since window passed to `engram activity --json` (e.g. \"1h\"). Any non-empty string is accepted and deferred to the CLI. Default \"1h\"."),
                Field("ttlSeconds", "number", "Cache TTL in seconds for the engram CLI probes. Default matches the command item's own TTL (30)."),
                Field("timeoutMs", "number", "Subprocess timeout in milliseconds for the engram CLI probes. Default matches the command item's own timeout (150)."),
                Field("idleAfterSeconds", "number", "How many seconds since the newest activity before the state is Idle. Default 300."),
                Field("binaryPath", "string", "Explicit path to the engram binary. A value with a directory separator (/ or \\) must be an absolute path; a bare name (no separator) is looked up on PATH only, never resolved relative to the current directory. Unset falls back to a PATH lookup for \"engram\", then $HOME/.local/bin/engram, /usr/local/bin/engram, /opt/homebrew/bin/engram in order."),
                Field("stateColors", "engramStateColors", "Per-reachability-state colour overrides. When set, a non-null override recolours every rendered fragment for that render, taking priority over fields[].color and factsColor/verbColor."),
            },
            new[]
            {
                "The closed field vocabulary: facts, verb, lastEventAge (no subprocess); server, version, uptime, port, pid (from `engram status --json`); lastKind, lastAge, windowCount, kind:<name> (from `engram activity --json`, case-sensitive; kind:<name> renders \"0\" when the named kind is absent from the window but the activity probe otherwise succeeded). Any other field name is a --check error.",
            },
            Parse("""{"factsColor":"cyan","verbColor":"red"}""")),

        new StructureEntryJson(
            "engramField",
            "EngramFieldJsonConfig",
            "One field selected into the engram item's fields list.",
            new[] { "field" },
            new[] { "format", "color" },
            new[]
            {
                Field("field", "string", "The field name — see engramItemSettings' closed field vocabulary."),
                Field("format", "string", "A format template with a single \"{}\" placeholder for this field's rendered value."),
                Field("color", "string", "This field's colour. A literal color name or hex; token references and inline rules are not supported here. Unset uses factsColor/verbColor (for those two fields) or the field's own built-in default."),
            },
            Array.Empty<string>(),
            Parse("""{"field":"server"}""")),

        new StructureEntryJson(
            "engramStateColors",
            "EngramStateColorsJsonConfig",
            "Per-reachability-state colour overrides for the engram item. All default null — an unconfigured engram item never uses state-based colouring.",
            Array.Empty<string>(),
            new[] { "active", "idle", "unreachable", "unavailable" },
            new[]
            {
                Field("active", "string", "Colour when the store is reachable and its newest activity is within idleAfterSeconds."),
                Field("idle", "string", "Colour when the store is reachable but its newest activity is older than idleAfterSeconds."),
                Field("unreachable", "string", "Colour when a status probe ran but the server isn't reporting Running/initialised/a pid."),
                Field("unavailable", "string", "Colour when there is no telemetry and (if probed) the store reports not initialised."),
            },
            Array.Empty<string>(),
            Parse("""{"unreachable":"red"}""")),

        new StructureEntryJson(
            "rateLimitsItemSettings",
            "RateLimitsItemSettings",
            "Settings for the rate-limits item.",
            Array.Empty<string>(),
            new[] { "windows" },
            new[]
            {
                Field("windows", "string", "Which window(s) to display: \"5h\", \"7d\", or \"both\" (the default)."),
            },
            Array.Empty<string>(),
            Parse("""{"windows":"5h"}""")),

        new StructureEntryJson(
            "prItemSettings",
            "PrItemSettings",
            "Settings for the pr item.",
            Array.Empty<string>(),
            new[] { "reviewStateLabels" },
            new[]
            {
                Field("reviewStateLabels", "object<string,string>", "Overrides for the review-state suffix, keyed by review state token (e.g. \"approved\")."),
            },
            Array.Empty<string>(),
            Parse("""{"reviewStateLabels":{"approved":" [OK]"}}""")),

        new StructureEntryJson(
            "linearItemSettings",
            "LinearItemSettings",
            "Settings for the linear item.",
            Array.Empty<string>(),
            new[] { "workspace" },
            new[]
            {
                Field("workspace", "string", "The Linear workspace slug, used only to build this item's default issue link. Absent leaves the ticket id rendering as plain text."),
            },
            Array.Empty<string>(),
            Parse("""{"workspace":"acme-corp"}""")),
    };
}
