namespace ClaudeTuiLine;

/// <summary>Builds a `file://` URI for a local absolute path.</summary>
internal static class FileUri
{
    /// <summary>
    /// A `file://` URI for <paramref name="absolutePath"/>, or null when it is null, empty, or not
    /// rooted. Each segment is percent-escaped, so spaces, `#`, `?`, `%`, braces and non-ASCII are
    /// all safe; `%3A` is restored to a literal `:` because a colon is a legal path character
    /// (RFC 3986 pchar) and Windows drive letters must keep it.
    /// </summary>
    internal static string? ForDirectory(string? absolutePath)
    {
        if (Segments(absolutePath) is not { } segments)
        {
            return null;
        }

        var escaped = segments.Select(s =>
            Uri.EscapeDataString(s).Replace("%3A", ":", StringComparison.Ordinal));

        // A trailing slash marks the target as a directory rather than a file.
        return "file:///" + string.Join('/', escaped) + "/";
    }

    /// <summary>
    /// The path segments of <paramref name="absolutePath"/> with separators normalized, or
    /// null when it is null or empty. Shared with <see cref="DirectoryLink"/>, which escapes
    /// them under different rules.
    /// </summary>
    internal static string[]? Segments(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return null;
        }

        var path = absolutePath.Replace('\\', '/');

        // A rooted POSIX path already starts with '/'; a Windows path starts with its drive letter,
        // and `file://` requires the third slash before it.
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
