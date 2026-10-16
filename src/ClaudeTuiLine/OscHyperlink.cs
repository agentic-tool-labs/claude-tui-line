using System.Text;
using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2: the OSC 8 hyperlink escape sequence this project emits for its own
/// links — <c>ESC ] 8 ; ; URL ST text ESC ] 8 ; ; ST</c>. Kept as one shared Wrap/TryUnwrap pair so
/// <see cref="LeafContent"/> (attaches a link) and <see cref="PaneRenderer"/> (must preserve one
/// across a wrap or drop it before a truncation ellipsis) agree on the exact byte sequence.
/// </summary>
public static class OscHyperlink
{
    private const string Esc = "";
    private const string St = Esc + "\\";
    private const string OpenPrefix = Esc + "]8;;";
    public const string Close = Esc + "]8;;" + St;

    // Linked text is underlined so it reads as clickable; the underline lives inside the wrap so
    // TryUnwrap can hand back the caller's own markup and a re-wrap never nests a second one.
    private const string UnderlineOpen = "[underline]";
    private const string UnderlineClose = "[/]";

    public static string Wrap(string url, string text) => $"{OpenPrefix}{url}{St}{UnderlineOpen}{text}{UnderlineClose}{Close}";

    /// <summary>
    /// True when <paramref name="text"/> is exactly one OSC 8 open/text/close triple with nothing
    /// outside it — the shape <see cref="Wrap"/> always produces. Splits it into the URL and the
    /// wrapped inner text so a caller can re-wrap a substring of the text without hand-parsing the
    /// escape sequence itself.
    /// </summary>
    public static bool TryUnwrap(string text, out string url, out string inner)
    {
        url = string.Empty;
        inner = string.Empty;

        if (!text.StartsWith(OpenPrefix, StringComparison.Ordinal) || !text.EndsWith(Close, StringComparison.Ordinal))
        {
            return false;
        }

        var afterPrefix = text[OpenPrefix.Length..];
        var stIndex = afterPrefix.IndexOf(St, StringComparison.Ordinal);
        if (stIndex < 0)
        {
            return false;
        }

        var innerStart = OpenPrefix.Length + stIndex + St.Length;
        var innerEnd = text.Length - Close.Length;
        if (innerEnd < innerStart)
        {
            return false;
        }

        url = afterPrefix[..stIndex];
        inner = text[innerStart..innerEnd];
        if (inner.StartsWith(UnderlineOpen, StringComparison.Ordinal) && inner.EndsWith(UnderlineClose, StringComparison.Ordinal)
            && inner.Length >= UnderlineOpen.Length + UnderlineClose.Length)
        {
            inner = inner[UnderlineOpen.Length..^UnderlineClose.Length];
        }

        return true;
    }

    /// <summary>
    /// Prepares a fully-composed row for Spectre's markup parser. <see cref="Wrap"/>'s raw OSC 8
    /// bytes are not valid markup source on their own — the close sequence's bare ']' reads as an
    /// unescaped token to Spectre's tokenizer, the same failure <c>Markup.Remove</c> has on this
    /// input — so every wrap found within <paramref name="markup"/> has its own open/close bytes
    /// escaped like ordinary text, while the styled text between them is left as live Spectre
    /// [tag] syntax; Spectre reconstructs the raw OSC 8 bytes byte-exact on the way out. Text
    /// outside a wrap, and a trailing occurrence this method cannot fully delimit, is returned
    /// unchanged.
    /// </summary>
    public static string EscapeForRender(string markup)
    {
        var openIndex = markup.IndexOf(OpenPrefix, StringComparison.Ordinal);
        if (openIndex < 0)
        {
            return markup;
        }

        var result = new StringBuilder(markup.Length + 16);
        var cursor = 0;

        while (openIndex >= 0)
        {
            var stIndex = markup.IndexOf(St, openIndex + OpenPrefix.Length, StringComparison.Ordinal);
            var closeIndex = stIndex < 0 ? -1 : markup.IndexOf(Close, stIndex + St.Length, StringComparison.Ordinal);
            if (stIndex < 0 || closeIndex < 0)
            {
                break;
            }

            result.Append(markup, cursor, openIndex - cursor);

            var openThroughSt = markup[openIndex..(stIndex + St.Length)];
            var innerText = markup[(stIndex + St.Length)..closeIndex];

            result.Append(Markup.Escape(openThroughSt));
            result.Append(SwapVisibleSlashes(innerText));
            result.Append(Markup.Escape(Close));

            cursor = closeIndex + Close.Length;
            openIndex = markup.IndexOf(OpenPrefix, cursor, StringComparison.Ordinal);
        }

        result.Append(markup, cursor, markup.Length - cursor);
        return result.ToString();
    }

    // herdr (with mouse capture on) runs its own URL detection over visible text and lets it win
    // over the OSC 8 target whenever the text contains '/', so "owner/name" opens http://owner/name.
    // Linked text shows U+2215 DIVISION SLASH instead, which looks the same and is not detected;
    // the cost is that copying the text yields the lookalike. Only text outside Spectre [tags]
    // changes, so closing tags like [/] and escaped [[ ]] survive.
    internal const char VisibleSlash = '∕';

    internal static string SwapVisibleSlashes(string markup)
    {
        if (!markup.Contains('/'))
        {
            return markup;
        }

        var result = new StringBuilder(markup.Length);
        var inTag = false;
        for (var i = 0; i < markup.Length; i++)
        {
            var c = markup[i];
            if (!inTag && c == '[' && i + 1 < markup.Length && markup[i + 1] == '[')
            {
                result.Append("[[");
                i++;
                continue;
            }

            if (c == '[' && !inTag)
            {
                inTag = true;
            }
            else if (c == ']' && inTag)
            {
                inTag = false;
            }
            else if (c == '/' && !inTag)
            {
                result.Append(VisibleSlash);
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }
}
