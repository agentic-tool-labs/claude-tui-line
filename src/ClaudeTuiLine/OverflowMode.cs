namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: governs only the case of a single segment/value wider than its
/// pane. General multi-segment row packing (<see cref="RowLayout.Wrap"/>) is identical and
/// unaffected under every mode.
/// </summary>
public enum OverflowMode
{
    /// <summary>Hard-broken across continuation rows; nothing is lost. Never exceeds pane width.</summary>
    Wrap,

    /// <summary>Cut to fit, ending with the ellipsis marker; the tail is lost. Never exceeds pane width.</summary>
    Truncate,

    /// <summary>v1 behavior: emitted whole, spilling past the pane. Legal only when the surface has exactly one pane.</summary>
    Overflow,
}

public static class OverflowModeParsing
{
    private static readonly (string Token, OverflowMode Value)[] Accepted =
    {
        ("wrap", OverflowMode.Wrap),
        ("truncate", OverflowMode.Truncate),
        ("overflow", OverflowMode.Overflow),
    };

    public static IReadOnlyList<string> AcceptedTokens { get; } = Accepted.Select(a => a.Token).ToArray();

    /// <summary>
    /// Parses a config string ("wrap" | "truncate" | "overflow", case-insensitive). Returns null
    /// for an absent or unrecognized value — SPEC-V2-FRAMEWORK.md §2.6's default is deliberately
    /// context-sensitive (root pane vs. inside a split), so resolving "no value" to a concrete
    /// mode is the caller's job, not the parser's.
    /// </summary>
    public static OverflowMode? Parse(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in Accepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="value"/> was present but matched none of the recognized tokens —
    /// distinct from an absent field, both of which <see cref="Parse"/> maps to null. §9.4's
    /// config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    public static bool IsUnrecognized(string? value) => !string.IsNullOrWhiteSpace(value) && Parse(value) is null;
}
