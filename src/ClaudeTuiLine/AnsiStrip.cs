using System.Text.RegularExpressions;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2 rule 2: OSC sequences are scanned separately from CSI, never folded
/// into one pattern — a CSI-only scanner leaves an OSC 8 hyperlink's invisible URL/state bytes
/// counted as visible width. The one shared strip implementation in the repo; every consumer that
/// needs an ANSI-free string for measurement, or that would otherwise keep its own copy of "what
/// does an escape sequence look like", drives this one.
/// </summary>
public static class AnsiStrip
{
    private static readonly Regex Csi = new("\\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    // Non-greedy body, terminator is ST (ESC \) or BEL: a greedy match spans two OSC sequences on
    // the same row and eats the visible text between them, and an ST-only terminator leaves a
    // BEL-terminated sequence (common from shell scripts) unstripped.
    private static readonly Regex Osc = new("\\].*?(\\\\|)", RegexOptions.Compiled);

    public static string Strip(string text) => Csi.Replace(Osc.Replace(text, string.Empty), string.Empty);
}
