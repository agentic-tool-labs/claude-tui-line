namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.4.2: picks the nearest known key to suggest for an unknown one.
/// A confidently wrong suggestion is worse than none, so a candidate must clear one of two
/// bars: a small bounded edit distance, or a prefix relation (the abbreviation class —
/// "ttl" for "ttlSeconds" — which no distance bound small enough to be safe would reach).
/// </summary>
internal static class KeySuggestion
{
    /// <summary>
    /// The best qualifying candidates, or empty when none qualify. Comparison is case-sensitive
    /// (Ordinal), matching the binder's PropertyNameCaseInsensitive = false.
    ///
    /// Qualification is two classes: a bounded edit distance, or a prefix relation where the
    /// shorter of the two strings is at least three characters (the abbreviation class, e.g.
    /// "ttl" for "ttlSeconds"). A prefix match always outranks a distance match — the two are
    /// incomparable quantities, not points on one scale. Within a class the best-ranked
    /// candidates (shortest for prefix, nearest for distance) are returned together, ordinally
    /// sorted: a genuine tie is two equally good answers, not a coin flip to resolve.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string unknown, IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrEmpty(unknown))
        {
            return Array.Empty<string>();
        }

        var prefixMatches = new List<(string Candidate, int Length)>();
        var distanceMatches = new List<(string Candidate, int Distance)>();

        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0)
            {
                continue;
            }

            var isPrefixMatch = (unknown.StartsWith(candidate, StringComparison.Ordinal)
                    || candidate.StartsWith(unknown, StringComparison.Ordinal))
                && Math.Min(unknown.Length, candidate.Length) >= 3;

            if (isPrefixMatch)
            {
                prefixMatches.Add((candidate, candidate.Length));
                continue;
            }

            var distance = EditDistance(unknown, candidate);
            if (distance <= 2 && distance * 2 < unknown.Length)
            {
                distanceMatches.Add((candidate, distance));
            }
        }

        if (prefixMatches.Count > 0)
        {
            var shortest = prefixMatches.Min(m => m.Length);
            return prefixMatches.Where(m => m.Length == shortest)
                .Select(m => m.Candidate)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToArray();
        }

        if (distanceMatches.Count > 0)
        {
            var nearest = distanceMatches.Min(m => m.Distance);
            return distanceMatches.Where(m => m.Distance == nearest)
                .Select(m => m.Candidate)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>Levenshtein distance, case-sensitive, two-row DP.</summary>
    public static int EditDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
