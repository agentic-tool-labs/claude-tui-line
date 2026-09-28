using System.Text.RegularExpressions;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2/§4.2.1: the one <c>{}</c> / <c>{other-id}</c> tokenizer, shared by a
/// <c>link</c> template (§3.2, via <see cref="LeafContent"/>) and a <c>command</c> item's argv
/// (§4.2, via <see cref="ArgvPlaceholders"/>) — one implementation of the grammar, not a second
/// parser wearing the same syntax. <c>{{</c>/<c>}}</c> are literal braces; otherwise
/// <c>{</c>…<c>}</c> is a placeholder only when its contents are empty (self-reference) or match
/// <see cref="IdCharset"/>, so an argv entry like <c>jq '{name: .name}'</c> — whose braces contain
/// a space, a colon, and hold no resemblance to an id — passes through unchanged instead of being
/// misread as a dangling reference.
/// </summary>
internal static class PlaceholderTemplate
{
    // Every item id in the spec's own examples ("agent-short", "agent.short", "git-branch",
    // "remote-url") is letters/digits/hyphen/underscore/dot; nothing wider is needed to keep
    // `{name: .name}` (space, colon) out and `{other-id}` in.
    private static readonly Regex IdCharset = new(@"\A[A-Za-z0-9_.\-]*\z", RegexOptions.Compiled);

    /// <param name="IsPlaceholder">
    /// False: <see cref="Text"/> is literal, already unescaped. True: <see cref="Text"/> is the
    /// placeholder's body — empty for <c>{}</c> (the item's own value/self-reference).
    /// </param>
    public readonly record struct Token(bool IsPlaceholder, string Text);

    public static IEnumerable<Token> Tokenize(string template)
    {
        var literal = new System.Text.StringBuilder();
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }

            if (c == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close >= 0)
                {
                    var body = template[(i + 1)..close];
                    if (!body.Contains('{') && IdCharset.IsMatch(body))
                    {
                        if (literal.Length > 0)
                        {
                            yield return new Token(false, literal.ToString());
                            literal.Clear();
                        }

                        yield return new Token(true, body);
                        i = close + 1;
                        continue;
                    }
                }
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
        {
            yield return new Token(false, literal.ToString());
        }
    }
}
