namespace ClaudeTuiLine;

/// <summary>
/// One rendered row of a pane: its markup and its measured ANSI/markup-stripped width.
/// SPEC-V2-FRAMEWORK.md §2.4 rule 3: width is measured on stripped text, never on the markup
/// string, using the same <c>Plain.Length</c> metric v1 used per segment.
/// </summary>
public sealed record PaneRow(string Markup, int Width);

/// <summary>
/// A leaf pane's render output: an ordered list of rows. §2.4: "A leaf pane renders to a
/// PaneBuffer... A split composes its children's buffers into one buffer."
/// </summary>
public sealed record PaneBuffer(IReadOnlyList<PaneRow> Rows);
