namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.4: composes one or more sibling pane buffers into the root buffer
/// that gets printed, one row per line. Phase 2 only ever drives this with a single pane
/// (root leaf pane only), but the rules themselves — pad every row to its pane's width, pad
/// sibling buffers to a common height, trim the composed root rows once at the very end — are
/// not specific to having exactly one pane, so this is written and tested that way rather than
/// special-cased for one.
/// </summary>
public static class Compositor
{
    /// <summary>
    /// One sibling's contribution: its rendered buffer, its resolved pane width (rule 1's pad
    /// target — may exceed the buffer's own natural max row width), and whether it carries a
    /// background color (rule 4's trim exception). No Phase 2 pane or item has a background
    /// concept yet, so every current caller passes <c>false</c>. <see cref="Valign"/> (§3.1)
    /// decides where this pane's blank padding rows land when it is shorter than its tallest
    /// sibling; it defaults to <see cref="PaneValign.Top"/>, which reproduces the pre-Phase-3
    /// behavior of appending every blank row at the bottom.
    /// </summary>
    public sealed record PaneContribution(PaneBuffer Buffer, int Width, bool HasBackground, PaneValign Valign = PaneValign.Top);

    /// <summary>
    /// §2.4 rules 1, 2 and 4, in order: pad each pane's rows to its own width, pad siblings to a
    /// common height with full-width blank rows, join siblings left to right per row, then trim
    /// trailing whitespace once on the composed row — unless the rightmost sibling has a
    /// background color, in which case those trailing cells are visible and must survive.
    /// </summary>
    public static IReadOnlyList<PaneRow> ComposeRoot(IReadOnlyList<PaneContribution> siblings)
    {
        if (siblings.Count == 0)
        {
            return Array.Empty<PaneRow>();
        }

        var height = siblings.Max(s => s.Buffer.Rows.Count);
        var paddedPerPane = siblings.Select(s => PadRows(s, height)).ToList();
        var rightmostHasBackground = siblings[^1].HasBackground;

        var composed = new List<PaneRow>(height);
        for (var row = 0; row < height; row++)
        {
            var joinedMarkup = string.Concat(paddedPerPane.Select(p => p[row].Markup));
            var joinedWidth = paddedPerPane.Sum(p => p[row].Width);

            if (rightmostHasBackground)
            {
                composed.Add(new PaneRow(joinedMarkup, joinedWidth));
                continue;
            }

            // Padding is always appended as literal ASCII space characters (never markup
            // syntax), so the character-count delta TrimEnd removes exactly equals the width
            // it removes — no re-measurement needed.
            var trimmed = joinedMarkup.TrimEnd(' ');
            var trimmedWidth = joinedWidth - (joinedMarkup.Length - trimmed.Length);
            composed.Add(new PaneRow(trimmed, trimmedWidth));
        }

        return composed;
    }

    // Rule 1 (pad every row to the pane's own width, in Markup form) + rule 2 (pad the buffer
    // itself to targetHeight with full-width blank rows). Padding is measured against
    // PaneRow.Width — the ANSI-stripped metric (rule 3) — and appended as literal, unstyled
    // trailing spaces after the row's own (already well-formed) markup, which is what makes it
    // safe for ComposeRoot to later trim with a plain string TrimEnd.
    private static List<PaneRow> PadRows(PaneContribution contribution, int targetHeight)
    {
        var width = Math.Max(0, contribution.Width);

        var content = contribution.Buffer.Rows
            .Select(row => new PaneRow(row.Markup + new string(' ', Math.Max(0, width - row.Width)), Math.Max(width, row.Width)))
            .ToList();

        var blankRow = new PaneRow(new string(' ', width), width);
        var deficit = Math.Max(0, targetHeight - content.Count);
        var (before, after) = contribution.Valign switch
        {
            PaneValign.Middle => (deficit / 2, deficit - deficit / 2),
            PaneValign.Bottom => (deficit, 0),
            _ => (0, deficit),
        };

        var padded = new List<PaneRow>(targetHeight);
        padded.AddRange(Enumerable.Repeat(blankRow, before));
        padded.AddRange(content);
        padded.AddRange(Enumerable.Repeat(blankRow, after));
        return padded;
    }
}
