using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §1.1.3: one row per constrained config key. <see cref="Accepted"/> is the
/// closed token set for a key with a parser-owned registry (#38); <see cref="AlsoAccepted"/> is a
/// prose description for a key with no closed set (currently only <c>size</c>). At least one of
/// the two must be present on every row — a row with neither is a bug, not a value this command emits.
/// </summary>
public sealed record AcceptedKeyJson(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("accepted")] IReadOnlyList<string>? Accepted,
    [property: JsonPropertyName("alsoAccepted")] string? AlsoAccepted);

public sealed record AcceptedResultJson(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("keys")] IReadOnlyList<AcceptedKeyJson> Keys);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(AcceptedResultJson))]
public partial class AcceptedJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §1.1.3: reads no config and probes nothing — every value comes from the
/// nine parser-colocated registries #38 built plus <see cref="ConfigChecker.SizeValues"/>, so
/// there is no failure mode here beyond a crash. Always exits 0.
/// </summary>
public static class AcceptedCommand
{
    public static AcceptedResultJson Build()
    {
        var keys = new List<AcceptedKeyJson>
        {
            new("border.style", BorderStyleParsing.AcceptedTokens, null),
            new("colorSystem", ConfigLoader.ColorSystemAcceptedTokens, null),
            new("split", ConfigLoader.SplitAcceptedTokens, null),
            new("valign", PaneValignParsing.AcceptedTokens, null),
            new("align", PaneAlignParsing.AcceptedTokens, null),
            new("selfAlign", PaneSelfAlignParsing.AcceptedTokens, null),
            new("titleAlign", PaneTitleAlignParsing.AcceptedTokens, null),
            new("distribute", PaneDistributeParsing.AcceptedTokens, null),
            new("overflow", OverflowModeParsing.AcceptedTokens, null),
            new("case", ItemValueResolver.CaseAcceptedTokens, null),
            new("height", PaneHeightParsing.AcceptedTokens, null),

            // size stays undecomposed: SPEC-V2-FRAMEWORK.md §1.1.3 section 3 ("size stays
            // undecomposed — ruled, not left open") already weighed splitting this into
            // accepted: ["content","fill","auto"] + alsoAccepted: "an integer, or a percentage"
            // and rejected it. Reopening that ruling means amending §1.1.1, §1.1.2, and §1.1.3
            // together, not just this line.
            new("size", null, ConfigChecker.FormatAccepted(ConfigChecker.SizeValues)),
        };

        ValidateInvariant(keys);

        return new AcceptedResultJson(AssemblyVersionInfo.InformationalVersion, keys);
    }

    /// <summary>
    /// §1.1.3 §2's invariant: every row must carry a non-empty <c>accepted</c> or a non-empty
    /// <c>alsoAccepted</c>. A gap must be stated, not silently emitted — the same fail-closed shape
    /// §9.5.1 rules for <c>PendingForm</c>.
    /// </summary>
    internal static void ValidateInvariant(IReadOnlyList<AcceptedKeyJson> keys)
    {
        foreach (var key in keys)
        {
            var hasAccepted = key.Accepted is { Count: > 0 };
            var hasAlsoAccepted = !string.IsNullOrEmpty(key.AlsoAccepted);
            if (!hasAccepted && !hasAlsoAccepted)
            {
                throw new InvalidOperationException($"accepted-command: key '{key.Key}' has neither accepted nor alsoAccepted");
            }
        }
    }
}
