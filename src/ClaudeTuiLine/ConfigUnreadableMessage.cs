namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.2.2: formats the one diagnostic row the render path draws when an
/// asserted config could not be read — a degradation ladder of five rungs, each tried only when
/// the one above it does not fit the available width. The prefix is always the literal string
/// "claude-tui-line", never argv[0], and the ellipsis is always the built-in default, never a
/// configured one, since the settings that would supply either live in the file that failed.
/// </summary>
public static class ConfigUnreadableMessage
{
    private const string Prefix = "claude-tui-line: ";
    private const string BareName = "claude-tui-line";

    // reasonProtectedLength marks a prefix of reason (the composed "line <n>") that rung 4 must
    // never truncate into — only reason[reasonProtectedLength..] is eaten. §9.2.2: truncation
    // degrades toward what the user cannot otherwise obtain, and a line number the parser
    // reported is not otherwise obtainable, while the Pointer and message after it are (open the
    // file).
    // SPEC-47 §3.3: path is nullable because a resolution failure can occur with no configPath at
    // all (§9.2.1 row 1). A null path enters rung 3 directly — the same pathless rendering rungs
    // 1-2 fall through to under width pressure — rather than gaining a sixth, path-specific rung.
    public static string Format(string? path, string reason, int? width, int reasonProtectedLength = 0)
    {
        if (path is not null)
        {
            var full = Prefix + path + ": " + reason;
            if (Fits(full.Length, width))
            {
                return full;
            }

            var pathBudget = width!.Value - Prefix.Length - ": ".Length - reason.Length;
            var elidedPath = ElidePath(path, pathBudget);
            if (elidedPath is not null)
            {
                return Prefix + elidedPath + ": " + reason;
            }
        }

        var pathless = Prefix + reason;
        if (Fits(pathless.Length, width))
        {
            return pathless;
        }

        var reasonBudget = width!.Value - Prefix.Length;
        if (reasonBudget >= Math.Max(1, reasonProtectedLength))
        {
            return Prefix + TruncateProtected(reason, reasonProtectedLength, reasonBudget);
        }

        var bareBudget = Math.Clamp(width.Value, 0, BareName.Length);
        return BareName[..bareBudget];
    }

    private static bool Fits(int length, int? width) => width is null || length <= width.Value;

    // §9.2.2 rung 2: elides the middle of the path to exactly the given budget, keeping the
    // leading '/' or '~' and the file name intact — the two ends a reader needs to recognize
    // which file this is. Returns null when the budget is too narrow to keep both.
    private static string? ElidePath(string path, int budget)
    {
        var fileName = Path.GetFileName(path);
        var minimum = 1 + ConfigLoader.DefaultEllipsis.Length + fileName.Length;
        if (budget < minimum)
        {
            return null;
        }

        var headLength = budget - ConfigLoader.DefaultEllipsis.Length - fileName.Length;
        return path[..headLength] + ConfigLoader.DefaultEllipsis + fileName;
    }

    // Splits reason at reasonProtectedLength and only ever truncates the part after it, leaving
    // the protected prefix (when there is one) intact regardless of how tight budget is. If
    // nothing but joining punctuation survives that truncation, the remainder is dropped
    // entirely rather than left dangling on a bare "," or ": ".
    private static string TruncateProtected(string reason, int reasonProtectedLength, int budget)
    {
        if (reasonProtectedLength == 0)
        {
            return TruncateWithEllipsis(reason, budget);
        }

        var protectedPart = reason[..reasonProtectedLength];
        var truncatablePart = reason[reasonProtectedLength..];
        var truncatedTail = TruncateWithEllipsis(truncatablePart, budget - reasonProtectedLength);
        return HasRealContent(truncatedTail) ? protectedPart + truncatedTail : protectedPart;
    }

    // A truncated tail is worth showing only if some character survives besides the punctuation
    // that joins it to the protected prefix ("," ":" " ") and the ellipsis appended by
    // TruncateWithEllipsis — a tail that is only that punctuation says nothing the protected
    // prefix didn't already.
    private static bool HasRealContent(string truncatedTail)
    {
        var withoutEllipsis = truncatedTail.EndsWith(ConfigLoader.DefaultEllipsis, StringComparison.Ordinal)
            ? truncatedTail[..^ConfigLoader.DefaultEllipsis.Length]
            : truncatedTail;
        return withoutEllipsis.Any(c => c is not (',' or ':' or ' '));
    }

    // Mirrors PaneRenderer.TruncateSegment's policy: too narrow for the marker at all clips the
    // real content instead of appending an ellipsis that would consume the entire budget itself.
    private static string TruncateWithEllipsis(string text, int budget)
    {
        if (budget <= ConfigLoader.DefaultEllipsis.Length)
        {
            return text[..Math.Min(budget, text.Length)];
        }

        var contentBudget = budget - ConfigLoader.DefaultEllipsis.Length;
        return text[..Math.Min(contentBudget, text.Length)] + ConfigLoader.DefaultEllipsis;
    }
}
