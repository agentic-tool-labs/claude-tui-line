using System.Text.Json.Nodes;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>
/// Stands up a fake "claude-tui-line" CLI binary under a temp CLAUDE_PLUGIN_DATA directory, so
/// tests can exercise CliRunner's spawn path without touching the real compiled CLI. The fake
/// answers `--items --json` with a trivial success payload (the presence probe), `--check
/// --config &lt;path&gt; --json` by inspecting the candidate file: a config file containing the literal
/// marker "FORCE_INVALID" is reported invalid, everything else reported valid, and `--schema --json`
/// by `cat`-ing a schema envelope file this fixture writes (see <see cref="DefaultSchemaEnvelope"/>
/// / <see cref="SetSchemaJson"/>) — a full five-section envelope shaped like the real binary's
/// output (SPEC-84), rich enough for GetConfigSchema's projection/addressing/section-filtering
/// tests (schema-mcp-query.md §7).
///
/// Also saves/restores CLAUDE_PLUGIN_DATA, HOME and CLAUDE_TUI_LINE_CONFIG so tests never leak
/// environment state into one another (the whole assembly runs serialized — see AssemblyInfo.cs).
/// </summary>
public sealed class TestCliFixture : IDisposable
{
    private readonly string? _savedPluginData;
    private readonly string? _savedHome;
    private readonly string? _savedConfigOverride;
    private readonly string _schemaJsonPath;
    private readonly JsonObject _defaultSchemaEnvelope;

    public string TempRoot { get; }
    public string HomeDir { get; }
    public string BinPath { get; }

    /// <summary>A fresh deep clone of the fixture's default `--schema --json` envelope, safe to mutate.</summary>
    public JsonObject DefaultSchemaEnvelope => (JsonObject)_defaultSchemaEnvelope.DeepClone();

    public TestCliFixture()
    {
        _savedPluginData = Environment.GetEnvironmentVariable("CLAUDE_PLUGIN_DATA");
        _savedHome = Environment.GetEnvironmentVariable("HOME");
        _savedConfigOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");

        TempRoot = Directory.CreateTempSubdirectory("claude-tui-line-mcp-test-").FullName;
        HomeDir = Path.Combine(TempRoot, "home");
        Directory.CreateDirectory(HomeDir);

        var binDir = Path.Combine(TempRoot, "plugin-data", "bin");
        Directory.CreateDirectory(binDir);
        BinPath = Path.Combine(binDir, "claude-tui-line");
        _schemaJsonPath = Path.Combine(TempRoot, "schema.json");

        _defaultSchemaEnvelope = BuildDefaultSchemaEnvelope();
        File.WriteAllText(_schemaJsonPath, _defaultSchemaEnvelope.ToJsonString());

        File.WriteAllText(BinPath, BuildFakeCliScript(_schemaJsonPath));
        File.SetUnixFileMode(
            BinPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Environment.SetEnvironmentVariable("CLAUDE_PLUGIN_DATA", Path.Combine(TempRoot, "plugin-data"));
        Environment.SetEnvironmentVariable("HOME", HomeDir);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", null);
    }

    /// <summary>Points CLAUDE_PLUGIN_DATA and HOME at a bin-less directory, so CliLocator finds nothing.</summary>
    public void RemoveCli()
    {
        File.Delete(BinPath);
    }

    /// <summary>Overrides the `--schema --json` output the fake CLI serves for the rest of this fixture's life.</summary>
    public void SetSchemaJson(JsonNode envelope) => File.WriteAllText(_schemaJsonPath, envelope.ToJsonString());

    // 16 ANSI-style names + the three non-color keywords, mirroring ColorsCommand.cs's shape
    // (16 themeMapped + default/dim/bold not themeMapped) without needing the real Spectre.Console
    // name table — the tests care about cardinality and the themeMapped split, not the literal names.
    private static readonly string[] AnsiColorNames =
    {
        "color0", "color1", "color2", "color3", "color4", "color5", "color6", "color7",
        "color8", "color9", "color10", "color11", "color12", "color13", "color14", "color15",
    };

    private static JsonObject BuildDefaultSchemaEnvelope()
    {
        return new JsonObject
        {
            ["version"] = "test",
            ["items"] = BuildItemsSection(),
            ["colors"] = BuildColorsSection(),
            ["accepted"] = BuildAcceptedSection(),
            ["kindSupport"] = BuildKindSupportSection(),
            ["structures"] = BuildStructuresSection(),
        };
    }

    private static JsonObject BuildItemsSection()
    {
        var items = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "cwd",
                ["reports"] = "the current working directory",
                ["color"] = "decorative",
                ["default"] = true,
                ["example"] = "~/projects",
            },
            new JsonObject
            {
                ["id"] = "time",
                ["reports"] = "the current time",
                ["color"] = "semantic",
                ["default"] = false,
                ["example"] = "12:00",
            },
        };

        var kinds = new JsonObject
        {
            ["builtin"] = new JsonObject { ["required"] = new JsonArray { "item" }, ["optional"] = new JsonArray { "format", "color" } },
            ["derived"] = new JsonObject { ["required"] = new JsonArray { "id", "from" }, ["optional"] = new JsonArray { "extract", "case" } },
            ["command"] = new JsonObject { ["required"] = new JsonArray { "id", "command" }, ["optional"] = new JsonArray { "shell", "ttlSeconds" } },
            ["compound"] = new JsonObject { ["required"] = new JsonArray { "id", "parts" }, ["optional"] = new JsonArray { "color" } },
        };

        return new JsonObject { ["version"] = "test", ["items"] = items, ["kinds"] = kinds };
    }

    private static JsonObject BuildColorsSection()
    {
        var recommended = new JsonArray();
        foreach (var name in AnsiColorNames)
        {
            recommended.Add(new JsonObject { ["name"] = name, ["themeMapped"] = true });
        }

        foreach (var name in new[] { "default", "dim", "bold" })
        {
            recommended.Add(new JsonObject { ["name"] = name, ["themeMapped"] = false });
        }

        var palette = new JsonArray();
        for (var i = 0; i < 256; i++)
        {
            var name = i < AnsiColorNames.Length ? AnsiColorNames[i] : $"palette{i}";
            palette.Add(new JsonObject { ["number"] = i, ["name"] = name, ["themeMapped"] = i < AnsiColorNames.Length });
        }

        return new JsonObject
        {
            ["version"] = "test",
            ["recommended"] = recommended,
            ["alsoAccepted"] = "Any Spectre.Console color name or #rrggbb hex.",
            ["palette"] = palette,
        };
    }

    private static JsonObject BuildAcceptedSection()
    {
        var keys = new JsonArray
        {
            new JsonObject { ["key"] = "border.style", ["accepted"] = new JsonArray { "all", "outline", "none" }, ["alsoAccepted"] = null },
            new JsonObject { ["key"] = "overflow", ["accepted"] = new JsonArray { "wrap", "truncate" }, ["alsoAccepted"] = null },
            new JsonObject { ["key"] = "size", ["accepted"] = null, ["alsoAccepted"] = "an integer, or a percentage" },

            // Deliberate collision with structures.border, so a bare "border" select resolves in
            // two sections at once — exercises the ambiguous-entry path (schema-mcp-query.md §3.5).
            new JsonObject { ["key"] = "border", ["accepted"] = new JsonArray { "true", "false" }, ["alsoAccepted"] = null },
        };

        return new JsonObject { ["version"] = "test", ["keys"] = keys };
    }

    private static JsonObject BuildKindSupportSection()
    {
        JsonObject Support(bool supported, params string[] unsupportedKeys) =>
            new() { ["supported"] = supported, ["unsupportedKeys"] = new JsonArray(unsupportedKeys.Select(k => (JsonNode)k).ToArray()) };

        return new JsonObject
        {
            ["builtin"] = Support(true),
            ["derived"] = Support(true),
            ["command"] = Support(true),
            ["compound"] = Support(false, "parts"),
        };
    }

    private static JsonArray BuildStructuresSection()
    {
        JsonObject Field(string name, string type, string? acceptedKey = null) => new()
        {
            ["name"] = name,
            ["type"] = type,
            ["description"] = $"{name} field description.",
            ["acceptedKey"] = acceptedKey,
        };

        JsonObject Entry(string name, string record, string[] required, string[] optional, JsonObject[] fields, string[]? notes = null) => new()
        {
            ["name"] = name,
            ["record"] = record,
            ["description"] = $"{name} structure description.",
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
            ["optional"] = new JsonArray(optional.Select(o => (JsonNode)o).ToArray()),
            ["fields"] = new JsonArray(fields.Cast<JsonNode>().ToArray()),
            ["notes"] = new JsonArray((notes ?? Array.Empty<string>()).Select(n => (JsonNode)n).ToArray()),
            ["example"] = new JsonObject(),
        };

        return new JsonArray
        {
            Entry("config", "UserConfig", Array.Empty<string>(), new[] { "border", "layout", "items" },
                new[] { Field("border", "border | string (shorthand)"), Field("layout", "layout"), Field("items", "array<item>") }),
            Entry("border", "BorderConfig", Array.Empty<string>(), new[] { "enabled", "color", "style", "edges" },
                new[] { Field("enabled", "boolean"), Field("color", "colorExpr"), Field("style", "string", "border.style"), Field("edges", "borderEdges") }),
            Entry("borderEdges", "BorderEdgesConfig", Array.Empty<string>(), new[] { "top", "right", "bottom", "left" },
                new[] { Field("top", "boolean"), Field("right", "boolean"), Field("bottom", "boolean"), Field("left", "boolean") }),
            Entry("layout", "LayoutConfig", Array.Empty<string>(), new[] { "chromeReserve" },
                new[] { Field("chromeReserve", "integer") }),
            Entry("surface", "SurfaceConfig", Array.Empty<string>(), new[] { "maxRows", "pane", "border" },
                new[] { Field("maxRows", "integer"), Field("pane", "pane"), Field("border", "border") }),
            Entry("pane", "PaneConfig", Array.Empty<string>(), new[] { "split", "children", "items" },
                new[] { Field("split", "string", "split"), Field("children", "array<pane>"), Field("items", "array<item>") },
                new[] { "A branch pane sets split and children; a leaf pane sets items." }),
            Entry("item", "PaneItemJsonConfig", Array.Empty<string>(), new[] { "item", "format", "color" },
                new[] { Field("item", "string"), Field("format", "string"), Field("color", "colorExpr") }),
            Entry("colorRule", "ColorRuleJsonConfig", Array.Empty<string>(), new[] { "from", "thresholds", "match", "default" },
                new[] { Field("from", "string"), Field("thresholds", "array<threshold>"), Field("match", "array<match>"), Field("default", "string") }),
            Entry("threshold", "ThresholdJsonConfig", new[] { "min" }, new[] { "color" },
                new[] { Field("min", "number"), Field("color", "string") }),
            Entry("match", "MatchJsonConfig", Array.Empty<string>(), new[] { "contains", "equals", "color" },
                new[] { Field("contains", "string"), Field("equals", "string"), Field("color", "string") }),
            Entry("colorExpr", "ColorExprJsonConfig", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<JsonObject>(),
                new[] { "Accepts either a colour string or a colorRule object." }),
            Entry("compoundPart", "PaneItemPartJsonConfig", Array.Empty<string>(), new[] { "text", "item", "from" },
                new[] { Field("text", "string"), Field("item", "string"), Field("from", "string") }),
        };
    }

    private const string FakeCliScriptTemplate = """
        #!/bin/sh
        if [ "$1" = "--items" ]; then
          echo '{"items":[],"kinds":{}}'
          exit 0
        fi
        if [ "$1" = "--check" ]; then
          candidate="$3"
          if grep -q "FORCE_INVALID" "$candidate" 2>/dev/null; then
            echo '{"ok":false,"diagnostics":[{"path":"/surface","severity":"error","code":"forced-invalid","message":"forced invalid for testing"}]}'
            exit 1
          fi
          echo '{"ok":true,"diagnostics":[]}'
          exit 0
        fi
        if [ "$1" = "--schema" ]; then
          cat "__SCHEMA_JSON_PATH__"
          exit 0
        fi
        exit 0
        """;

    private static string BuildFakeCliScript(string schemaJsonPath) =>
        FakeCliScriptTemplate.Replace("__SCHEMA_JSON_PATH__", schemaJsonPath);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_PLUGIN_DATA", _savedPluginData);
        Environment.SetEnvironmentVariable("HOME", _savedHome);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _savedConfigOverride);

        try
        {
            Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup of a temp dir under the OS temp root; not user data.
        }
    }
}
