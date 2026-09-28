using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-12.6-mcp-tools.md §2.2/§10 (first slice): <c>get_config</c> and <c>set_config</c> only.
/// <see cref="BackupLedger"/> is taken as a method parameter rather than looked up internally so
/// tests can call these methods directly with a temp-directory ledger and never go through
/// <see cref="BackupLedgerFactory"/> (§9.7/V6).
/// </summary>
[McpServerToolType]
public sealed class ConfigTools
{
    [McpServerTool(Name = "get_config")]
    [Description(
        "Return the current claude-tui-line config, the path it was read from, and a revision " +
        "hash for compare-and-swap with set_config. revision is \"absent\" when no config file " +
        "exists yet. Fails with code \"cli-not-found\" if the claude-tui-line CLI cannot be " +
        "located — point the user at /claude-tui-line:setup rather than improvising.")]
    public static async Task<object> GetConfig(
        [Description("Explicit config file path, overriding the normal search order.")] string? configPath = null)
    {
        var presence = await CliRunner.ProbePresenceAsync().ConfigureAwait(false);
        if (!presence.Found)
        {
            return McpResults.CliNotFound(presence.SearchedPaths);
        }

        var (resolvedPath, source) = ResolveConfigPath(configPath);
        if (resolvedPath is null)
        {
            return new { ok = true, config = (object?)null, configPath = (string?)null, source, revision = "absent" };
        }

        var bytes = File.Exists(resolvedPath) ? File.ReadAllBytes(resolvedPath) : null;
        var config = bytes is null ? null : JsonNode.Parse(bytes);
        var revision = ConfigFile.ComputeRevision(bytes);

        return new { ok = true, config, configPath = resolvedPath, source, revision };
    }

    [McpServerTool(Name = "set_config")]
    [Description(
        "Write a full claude-tui-line config. Validates via --check before committing; a " +
        "candidate that fails validation is rejected and the config on disk is left untouched. " +
        "Checkpoints every artifact (settings.json's statusLine, its referenced script if any, " +
        "and the config) before writing, so a failed checkpoint (code \"checkpoint-failed\") means " +
        "nothing was written. If baseRevision is supplied and no longer matches the file on disk, " +
        "the write is refused with code \"stale-revision\" and the refusal's payload carries the " +
        "CURRENT config and revision — re-derive the intended change against that payload; do not " +
        "resend the original config with a freshly-fetched revision, since that defeats the " +
        "refusal and silently clobbers the intervening write. Fails with \"cli-not-found\" if the " +
        "CLI cannot be located.")]
    public static async Task<object> SetConfig(
        BackupLedger ledger,
        [Description("The full config object to write.")] JsonNode config,
        [Description("Explicit config file path, overriding the normal search order.")] string? configPath = null,
        [Description("The revision last read via get_config (or \"absent\" for a first write). Optional; when supplied and stale, the write is refused.")] string? baseRevision = null)
    {
        var (resolvedPath, source) = ResolveConfigPath(configPath);
        if (resolvedPath is null)
        {
            return McpResults.Failure("checkpoint-failed", "no config path could be resolved: $HOME is not set and no explicit configPath was given.", null);
        }

        var existingBytes = File.Exists(resolvedPath) ? File.ReadAllBytes(resolvedPath) : null;
        var currentRevision = ConfigFile.ComputeRevision(existingBytes);

        if (baseRevision is not null && baseRevision != currentRevision)
        {
            var currentConfig = existingBytes is null ? null : JsonNode.Parse(existingBytes);
            return new
            {
                ok = false,
                code = "stale-revision",
                message = "the config on disk has changed since baseRevision was read; re-derive the intended change against the config in this payload rather than resending the original.",
                config = currentConfig,
                revision = currentRevision,
                configPath = resolvedPath,
                source,
            };
        }

        var candidateBytes = JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions { WriteIndented = true });
        var candidatePath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(resolvedPath)) ?? ".",
            $".{Path.GetFileName(resolvedPath)}.mcp-candidate.{Guid.NewGuid():N}.tmp");

        File.WriteAllBytes(candidatePath, candidateBytes);
        try
        {
            var checkResult = await CliRunner.RunCheckAsync(candidatePath).ConfigureAwait(false);
            if (!checkResult.CliFound)
            {
                return McpResults.CliNotFound(checkResult.SearchedPaths);
            }

            var diagnostics = checkResult.Payload?["diagnostics"]?.DeepClone() ?? new JsonArray();
            var checkOk = checkResult.Payload?["ok"]?.GetValue<bool>() ?? false;

            if (!checkOk)
            {
                return new
                {
                    ok = false,
                    code = "invalid-config",
                    message = "the candidate config failed validation; it was not written.",
                    diagnostics,
                    configPath = resolvedPath,
                    source,
                };
            }

            // SPEC-12.6-mcp-tools.md §5 (N1/N2 consequences), A2: validate -> checkpoint -> write.
            // The checkpoint runs only after validation succeeds (a rejected candidate needs no
            // checkpoint) and strictly before the real file is touched.
            var checkpoint = ledger.WriteCheckpoint(resolvedPath);
            if (!checkpoint.Ok)
            {
                return McpResults.Failure("checkpoint-failed", checkpoint.FailedMessage ?? "the checkpoint could not be written; nothing was changed.", checkpoint.FailedPath);
            }

            ConfigFile.WriteAtomic(resolvedPath, candidateBytes);
            var newRevision = ConfigFile.ComputeRevision(candidateBytes);

            return new
            {
                ok = true,
                diagnostics,
                revision = newRevision,
                checkpoint = checkpoint.EntryJson,
                configPath = resolvedPath,
                source,
            };
        }
        finally
        {
            try
            {
                File.Delete(candidatePath);
            }
            catch
            {
                // best-effort cleanup of a transient validation file; nothing downstream depends on it.
            }
        }
    }

    /// <summary>SPEC-84-mcp-schema-explorer.md §5.1: the section names an agent may request via <see cref="GetConfigSchema"/>'s <c>sections</c> filter.</summary>
    private static readonly string[] ValidSchemaSections = { "items", "colors", "accepted", "structures", "kindSupport" };

    private static readonly string[] ProseKeys = { "description", "notes", "example" };

    private const string DefaultIndexHint =
        "Descriptions, notes, examples and the 256-entry colour palette are omitted here. Fetch "
        + "them with get_config_schema(select: [\"structures.pane\", \"colors.palette\"]) using the "
        + "ids below, or whole sections with get_config_schema(sections: [\"items\"]).";

    private const string PaletteOmittedNote =
        "The extended 256-colour xterm palette. Fetch with select:[\"colors.palette\"], or a single "
        + "entry with select:[\"colors.palette.196\"].";

    [McpServerTool(Name = "get_config_schema")]
    [Description(
        "The claude-tui-line config schema, read live from the installed binary. Called with no "
        + "arguments it returns a compact default: every item kind, named colour, accepted-value key "
        + "and config structure, with each one's field names and types — omitting descriptions, notes, "
        + "examples, and the 256-entry extended colour palette. That is usually enough to write a "
        + "config. For prose, examples, or the extended palette, pass select with ids from the "
        + "response (e.g. select: [\"structures.pane\"], select: [\"colors.palette\"]). To get whole "
        + "sections in full, pass sections. Use this instead of documentation — documentation can be "
        + "stale, this is what the binary actually accepts.")]
    public static async Task<object> GetConfigSchema(
        [Description("Entry ids to return in full, as listed in the response (e.g. \"structures.pane\", \"accepted.border.style\", \"colors.palette\"). Mutually exclusive with sections.")]
        string[]? select = null,
        [Description("Return these whole sections in full: items, colors, accepted, structures, kindSupport. Mutually exclusive with select.")]
        string[]? sections = null)
    {
        var result = await CliRunner.RunSchemaAsync().ConfigureAwait(false);
        if (!result.CliFound)
        {
            return McpResults.CliNotFound(result.SearchedPaths);
        }

        if (result.ExitCode != 0 || result.Payload is not JsonObject envelope)
        {
            return McpResults.Failure(
                "schema-unavailable",
                $"claude-tui-line --schema --json exited {result.ExitCode} or produced output that did not parse as JSON; the schema is not available.",
                null);
        }

        var hasSelect = select is { Length: > 0 };
        var hasSections = sections is { Length: > 0 };

        if (hasSelect && hasSections)
        {
            return McpResults.Failure("conflicting-args", "select and sections are mutually exclusive; pass one or the other.", null);
        }

        if (hasSections)
        {
            return BuildSectionsResponse(envelope, sections!);
        }

        if (hasSelect)
        {
            return BuildDetailResponse(envelope, select!);
        }

        return BuildDefaultIndex(envelope);
    }

    private static object BuildSectionsResponse(JsonObject envelope, string[] sections)
    {
        var unknown = sections.Where(s => !ValidSchemaSections.Contains(s, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            return McpResults.Failure(
                "unknown-section",
                $"unrecognized section(s): {string.Join(", ", unknown)}. Valid sections: {string.Join(", ", ValidSchemaSections)}.",
                null);
        }

        var filtered = new JsonObject
        {
            ["version"] = envelope["version"]?.DeepClone(),
            ["mode"] = "sections",
        };
        foreach (var section in sections)
        {
            filtered[section] = envelope[section]?.DeepClone();
        }

        return filtered;
    }

    // §4.1: the only response that projects the live envelope — elides prose (§2.2), replaces
    // colors.palette with an omission marker (§2.6.2), and stamps addressable ids onto entries
    // whose natural shape doesn't already carry one. §2.4: any top-level key the projector does
    // not recognize is emitted whole, never dropped.
    private static object BuildDefaultIndex(JsonObject envelope)
    {
        var index = new JsonObject
        {
            ["version"] = envelope["version"]?.DeepClone(),
            ["mode"] = "index",
            ["hint"] = DefaultIndexHint,
        };

        foreach (var kvp in envelope)
        {
            if (kvp.Key == "version")
            {
                continue;
            }

            var cloned = kvp.Value?.DeepClone();
            index[kvp.Key] = kvp.Key switch
            {
                "structures" => ProjectStructures(cloned),
                "colors" => ProjectColors(cloned),
                "items" => ElideProse(cloned),
                "accepted" => ProjectAccepted(cloned),
                "kindSupport" => ElideProse(cloned),
                _ => cloned,
            };
        }

        return index;
    }

    // §3.2/§4.1: structures entries are keyed by `name`, which never collides with the `id`
    // the index adds. §4.1's field-object rule: an absent (null) acceptedKey is omitted, not
    // emitted as null.
    private static JsonNode? ProjectStructures(JsonNode? node)
    {
        if (node is JsonArray entries)
        {
            foreach (var entry in entries.OfType<JsonObject>())
            {
                if (entry["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name))
                {
                    entry["id"] = $"structures.{name}";
                }

                if (entry["fields"] is JsonArray fields)
                {
                    foreach (var field in fields.OfType<JsonObject>())
                    {
                        if (field.TryGetPropertyValue("acceptedKey", out var acceptedKey) && acceptedKey is null)
                        {
                            field.Remove("acceptedKey");
                        }
                    }
                }
            }
        }

        return ElideProse(node);
    }

    // §2.6.2/§4.4: the palette is replaced by an in-band omission marker, never truncated or
    // dropped; its count/themeMappedCount are derived from the live array, never hardcoded.
    // §3.2: recommended entries are keyed by `name`, so an added `id` cannot collide.
    private static JsonNode? ProjectColors(JsonNode? node)
    {
        if (node is JsonObject colors)
        {
            if (colors["recommended"] is JsonArray recommended)
            {
                foreach (var entry in recommended.OfType<JsonObject>())
                {
                    if (entry["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name))
                    {
                        entry["id"] = $"colors.recommended.{name}";
                    }
                }
            }

            if (colors["palette"] is JsonArray palette)
            {
                var count = palette.Count;
                var themeMappedCount = palette.OfType<JsonObject>()
                    .Count(p => p["themeMapped"] is JsonValue tm && tm.TryGetValue<bool>(out var mapped) && mapped);

                colors["palette"] = new JsonObject
                {
                    ["omitted"] = true,
                    ["count"] = count,
                    ["note"] = PaletteOmittedNote,
                    ["themeMappedCount"] = themeMappedCount,
                };
            }
        }

        return ElideProse(node);
    }

    // §3.2: accepted.keys entries are keyed by `key`, which never collides with the `id` the
    // index adds. §2.6.5: alsoAccepted is normative, not prose — the generic elide-set never
    // touches it.
    private static JsonNode? ProjectAccepted(JsonNode? node)
    {
        if (node is JsonObject accepted && accepted["keys"] is JsonArray keys)
        {
            foreach (var entry in keys.OfType<JsonObject>())
            {
                if (entry["key"] is JsonValue keyValue && keyValue.TryGetValue<string>(out var key))
                {
                    entry["id"] = $"accepted.keys.{key}";
                }
            }
        }

        return ElideProse(node);
    }

    // §2.2/§2.4: one recursive projection over the parsed JsonNode, keyed on the elide-set,
    // applied at every depth — mutates in place and returns the same reference so callers can
    // use it inline without re-parenting nodes that are still attached to their container.
    private static JsonNode? ElideProse(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var prose in ProseKeys)
                {
                    obj.Remove(prose);
                }

                foreach (var kvp in obj.ToList())
                {
                    ElideProse(kvp.Value);
                }

                return obj;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    ElideProse(item);
                }

                return arr;
            default:
                return node;
        }
    }

    // §4.2: entries are returned verbatim and un-projected — the resolved node is the same
    // bytes the full envelope would have carried, never elided or id-stamped.
    private static object BuildDetailResponse(JsonObject envelope, string[] select)
    {
        var entries = new JsonArray();
        foreach (var rawId in select)
        {
            var resolution = ResolveEntry(envelope, rawId);
            if (resolution.Ambiguous)
            {
                return McpResults.EntryFailure(
                    "ambiguous-entry",
                    $"\"{rawId}\" matches an entry in more than one section; qualify it with the section prefix.",
                    resolution.Candidates!);
            }

            if (!resolution.Found)
            {
                return McpResults.EntryFailure(
                    "unknown-entry",
                    $"no schema entry matches \"{rawId}\".",
                    FindCandidates(envelope, rawId));
            }

            entries.Add(new JsonObject
            {
                ["id"] = resolution.QualifiedId,
                ["value"] = resolution.Node?.DeepClone(),
            });
        }

        return new JsonObject
        {
            ["version"] = envelope["version"]?.DeepClone(),
            ["mode"] = "detail",
            ["entries"] = entries,
        };
    }

    private sealed record ResolveResult(bool Found, bool Ambiguous, JsonNode? Node, string QualifiedId, IReadOnlyList<string>? Candidates)
    {
        public static ResolveResult Hit(JsonNode? node, string qualifiedId) => new(true, false, node, qualifiedId, null);
        public static ResolveResult Miss() => new(false, false, null, "", null);
        public static ResolveResult AmbiguousHit(IReadOnlyList<string> candidates) => new(false, true, null, "", candidates);
    }

    // §3.3: split, walk, resolve. §3.3's bare-name convenience applies only to a dot-free id
    // that is not itself a section name; a dotted id whose first segment is not a valid section
    // fails outright (no retry).
    private static ResolveResult ResolveEntry(JsonObject envelope, string id)
    {
        if (!id.Contains('.'))
        {
            if (ValidSchemaSections.Contains(id, StringComparer.Ordinal))
            {
                TryWalk(envelope, id, Array.Empty<string>(), out var whole);
                return ResolveResult.Hit(whole, id);
            }

            // Bare-name convenience must consider every addressable id, not just one level below
            // the section — accepted.keys.<key> and items.items.<id> are two segments deep, so a
            // bare "border" matching structures.border must also see accepted.keys.border.
            var candidateIds = ValidSchemaSections
                .SelectMany(section => EnumerateIds(envelope, section))
                .Where(fullId => fullId[(fullId.LastIndexOf('.') + 1)..] == id)
                .Distinct()
                .ToList();

            var hits = new List<(string QualifiedId, JsonNode? Node)>();
            foreach (var candidateId in candidateIds)
            {
                var candidateSegments = candidateId.Split('.');
                if (TryWalk(envelope, candidateSegments[0], candidateSegments.Skip(1).ToArray(), out var node))
                {
                    hits.Add((candidateId, node));
                }
            }

            return hits.Count switch
            {
                0 => ResolveResult.Miss(),
                1 => ResolveResult.Hit(hits[0].Node, hits[0].QualifiedId),
                _ => ResolveResult.AmbiguousHit(hits.Select(h => h.QualifiedId).ToList()),
            };
        }

        var segments = id.Split('.');
        var sectionSegment = segments[0];
        if (!ValidSchemaSections.Contains(sectionSegment, StringComparer.Ordinal))
        {
            return ResolveResult.Miss();
        }

        return TryWalk(envelope, sectionSegment, segments.Skip(1).ToArray(), out var result)
            ? ResolveResult.Hit(result, id)
            : ResolveResult.Miss();
    }

    // §3.3 step 2: at an object, match a property by name; at an array, match an element whose
    // name/key/id/number equals the segment (ordinal; number compared as an invariant decimal
    // string). Every array is checked against all four keys, per §3.3's shape-agnostic rule.
    private static bool TryWalk(JsonObject envelope, string sectionName, string[] remainingSegments, out JsonNode? result)
    {
        JsonNode? current = envelope[sectionName];
        foreach (var segment in remainingSegments)
        {
            switch (current)
            {
                case JsonObject obj when obj.TryGetPropertyValue(segment, out var next):
                    current = next;
                    break;
                case JsonArray arr:
                    var match = arr.OfType<JsonObject>().FirstOrDefault(el => MatchesSegment(el, segment));
                    if (match is null)
                    {
                        result = null;
                        return false;
                    }

                    current = match;
                    break;
                default:
                    result = null;
                    return false;
            }
        }

        result = current;
        return true;
    }

    private static bool MatchesSegment(JsonObject obj, string segment)
    {
        if (obj["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name) && name == segment)
        {
            return true;
        }

        if (obj["key"] is JsonValue keyValue && keyValue.TryGetValue<string>(out var key) && key == segment)
        {
            return true;
        }

        if (obj["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var id) && id == segment)
        {
            return true;
        }

        if (obj["number"] is JsonValue numberValue && numberValue.TryGetValue<int>(out var number)
            && number.ToString(CultureInfo.InvariantCulture) == segment)
        {
            return true;
        }

        return false;
    }

    // §3.5: up to 5 nearest ids, scoped to the requested section when the id names one. Nearest
    // is by edit distance rather than a literal substring check, since a one-character typo
    // (the motivating case) need not be a substring of its intended target.
    private static List<string> FindCandidates(JsonObject envelope, string id)
    {
        var dotIndex = id.IndexOf('.');
        var section = dotIndex > 0 ? id[..dotIndex] : null;
        var scoped = section is not null && ValidSchemaSections.Contains(section, StringComparer.Ordinal);

        var pool = scoped
            ? EnumerateIds(envelope, section!)
            : ValidSchemaSections.SelectMany(s => EnumerateIds(envelope, s));

        var lowerId = id.ToLowerInvariant();
        return pool
            .OrderBy(candidate => LevenshteinDistance(candidate.ToLowerInvariant(), lowerId))
            .Take(5)
            .ToList();
    }

    private static IEnumerable<string> EnumerateIds(JsonObject envelope, string section)
    {
        var root = envelope[section];
        switch (section)
        {
            case "structures":
                if (root is JsonArray structures)
                {
                    foreach (var entry in structures.OfType<JsonObject>())
                    {
                        if (entry["name"] is JsonValue nv && nv.TryGetValue<string>(out var n))
                        {
                            yield return $"structures.{n}";
                        }
                    }
                }

                break;
            case "kindSupport":
                if (root is JsonObject kindSupport)
                {
                    foreach (var kvp in kindSupport)
                    {
                        yield return $"kindSupport.{kvp.Key}";
                    }
                }

                break;
            case "items":
                if (root is JsonObject items)
                {
                    if (items["items"] is JsonArray itemsArr)
                    {
                        foreach (var entry in itemsArr.OfType<JsonObject>())
                        {
                            if (entry["id"] is JsonValue iv && iv.TryGetValue<string>(out var i))
                            {
                                yield return $"items.items.{i}";
                            }
                        }
                    }

                    if (items["kinds"] is JsonObject kinds)
                    {
                        foreach (var kvp in kinds)
                        {
                            yield return $"items.kinds.{kvp.Key}";
                        }
                    }
                }

                break;
            case "colors":
                if (root is JsonObject colors)
                {
                    if (colors["recommended"] is JsonArray recommended)
                    {
                        foreach (var entry in recommended.OfType<JsonObject>())
                        {
                            if (entry["name"] is JsonValue nv2 && nv2.TryGetValue<string>(out var n2))
                            {
                                yield return $"colors.recommended.{n2}";
                            }
                        }
                    }

                    yield return "colors.palette";
                    if (colors["palette"] is JsonArray palette)
                    {
                        foreach (var entry in palette.OfType<JsonObject>())
                        {
                            if (entry["name"] is JsonValue pnv && pnv.TryGetValue<string>(out var pn))
                            {
                                yield return $"colors.palette.{pn}";
                            }

                            if (entry["number"] is JsonValue pnumValue && pnumValue.TryGetValue<int>(out var pnum))
                            {
                                yield return $"colors.palette.{pnum.ToString(CultureInfo.InvariantCulture)}";
                            }
                        }
                    }
                }

                break;
            case "accepted":
                if (root is JsonObject accepted && accepted["keys"] is JsonArray keys)
                {
                    foreach (var entry in keys.OfType<JsonObject>())
                    {
                        if (entry["key"] is JsonValue kv && kv.TryGetValue<string>(out var k))
                        {
                            yield return $"accepted.keys.{k}";
                        }
                    }
                }

                break;
        }
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    // SPEC-V2-FRAMEWORK.md §12.6.2: an explicit configPath argument overrides resolution outright.
    // Otherwise mirrors ConfigPath.ResolveConfigPath()'s search order ($CLAUDE_TUI_LINE_CONFIG,
    // then $HOME/.claude/claude-tui-line.json) so source can report which branch fired — "env",
    // "default", or "none" per §12.6.2; "explicit" is this implementation's extension for the
    // caller-supplied-path case, which §12.6.2's enumeration does not itself cover.
    private static (string? Path, string Source) ResolveConfigPath(string? explicitConfigPath)
    {
        if (!string.IsNullOrEmpty(explicitConfigPath))
        {
            return (explicitConfigPath, "explicit");
        }

        var envOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");
        var home = Environment.GetEnvironmentVariable("HOME");
        var resolved = ClaudeTuiLineShared.ConfigPath.ResolveConfigPath(envOverride, home);

        if (resolved is null)
        {
            return (null, "none");
        }

        var source = !string.IsNullOrEmpty(envOverride) ? "env" : "default";
        return (resolved, source);
    }
}

internal static class McpResults
{
    public static object CliNotFound(IReadOnlyList<string> searchedPaths) => new
    {
        ok = false,
        code = "cli-not-found",
        message = "the claude-tui-line CLI could not be found; point the user at /claude-tui-line:setup.",
        searchedPaths,
    };

    public static object Failure(string code, string message, string? path) => new
    {
        ok = false,
        code,
        message,
        path,
    };

    public static object EntryFailure(string code, string message, IReadOnlyList<string> candidates) => new
    {
        ok = false,
        code,
        message,
        candidates,
    };
}
