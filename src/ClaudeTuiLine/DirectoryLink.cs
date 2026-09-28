namespace ClaudeTuiLine;

/// <summary>Builds the `directory` item's default hyperlink for the configured target.</summary>
internal static class DirectoryLink
{
    internal const string OpenWithFiles = "files";
    internal const string OpenWithVsCode = "vscode";

    /// <summary>
    /// The default link for <paramref name="absolutePath"/>, or null when it is null, empty,
    /// or not rooted. An unrecognized <paramref name="openWith"/> token falls back to the
    /// file-browser target so a typo costs the user a diagnostic, not their link.
    /// </summary>
    internal static string? Build(string? absolutePath, string? openWith) => openWith switch
    {
        OpenWithVsCode => ForVsCode(absolutePath),
        _ => FileUri.ForDirectory(absolutePath),
    };

    /// <summary>
    /// A `vscode://file/` URI for <paramref name="absolutePath"/>, or null when it is null or
    /// empty. Colons stay percent-encoded except in a leading Windows drive letter, because
    /// VS Code reads a trailing `:line[:col]` suffix on this path.
    /// </summary>
    internal static string? ForVsCode(string? absolutePath)
    {
        if (FileUri.Segments(absolutePath) is not { } segments)
        {
            return null;
        }

        var escaped = segments.Select((s, i) =>
        {
            var e = Uri.EscapeDataString(s);
            return i == 0 && IsDriveLetter(e) ? e.Replace("%3A", ":", StringComparison.Ordinal) : e;
        });

        return "vscode://file/" + string.Join('/', escaped);
    }

    private static bool IsDriveLetter(string escapedSegment) =>
        escapedSegment.Length == 4
        && char.IsAsciiLetter(escapedSegment[0])
        && escapedSegment.AsSpan(1).SequenceEqual("%3A");
}
