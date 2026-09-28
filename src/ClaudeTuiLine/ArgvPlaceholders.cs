using System.Text;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4.2: expands a <c>command</c> item's argv placeholders against the
/// up-front resolution set (§5), reusing <see cref="PlaceholderTemplate"/> — the same vocabulary
/// §3.2's link templates use, not a fourth way to name a value.
///
/// Non-shell: each placeholder substitutes directly into its argv element (§4.2's security
/// ruling — no shell ever sees the resolved value). <c>shell: true</c>: nothing is substituted
/// into <see cref="Expansion.Argv"/> at all; instead every *referenced* id is exported into
/// <see cref="Expansion.ExportedEnv"/> under <see cref="EnvVarNameFor"/>, for
/// <see cref="CommandProvider"/> to set as an environment variable. Bare <c>{}</c>
/// (self-reference) has no value to substitute — the command has not run yet — and is dropped
/// silently here; §4.2.1 makes it a declaration-time <c>--check</c> error
/// (<c>placeholder-self-reference</c>), not a runtime concern.
/// </summary>
internal static class ArgvPlaceholders
{
    public readonly record struct Expansion(
        IReadOnlyList<string> Argv,
        IReadOnlyDictionary<string, string> ExportedEnv,
        IReadOnlyCollection<string> ReferencedIds);

    public static Expansion Expand(IReadOnlyList<string> command, bool shell, IReadOnlyDictionary<string, string?> values)
    {
        if (shell)
        {
            // §4.2: shell:true substitutes nothing into the command string — a resolved value
            // reaching `sh -c` verbatim is command injection. The argv is unchanged; only the
            // referenced ids matter, to know what to export.
            var referencedForShell = ReferencedIds(command).ToList();
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in referencedForShell)
            {
                // §4.2/§4.2's empty-value ruling: exported empty rather than unset, so a known but
                // empty source still yields one (empty) argument-equivalent on the script side.
                env[EnvVarNameFor(id)] = values.GetValueOrDefault(id) ?? "";
            }

            return new Expansion(command, env, referencedForShell);
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var argv = new List<string>(command.Count);
        foreach (var element in command)
        {
            var sb = new StringBuilder();
            foreach (var token in PlaceholderTemplate.Tokenize(element))
            {
                if (!token.IsPlaceholder)
                {
                    sb.Append(token.Text);
                    continue;
                }

                if (token.Text.Length == 0)
                {
                    continue;
                }

                referenced.Add(token.Text);
                sb.Append(values.GetValueOrDefault(token.Text) ?? "");
            }

            argv.Add(sb.ToString());
        }

        return new Expansion(argv, new Dictionary<string, string>(), referenced);
    }

    /// <summary>Every non-self placeholder id a <c>command</c> item's argv names, deduplicated.</summary>
    public static IEnumerable<string> ReferencedIds(IReadOnlyList<string> command) =>
        command
            .SelectMany(PlaceholderTemplate.Tokenize)
            .Where(t => t.IsPlaceholder && t.Text.Length > 0)
            .Select(t => t.Text)
            .Distinct(StringComparer.Ordinal);

    public static bool HasSelfReference(IReadOnlyList<string> command) =>
        command.SelectMany(PlaceholderTemplate.Tokenize).Any(t => t.IsPlaceholder && t.Text.Length == 0);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4.2: "the id upper-cased and non-alphanumerics replaced by _" — the
    /// one implementation, shared between this runtime export and <c>ConfigCheck</c>'s
    /// <c>placeholder-env-collision</c> detector, so the two can never disagree about which ids
    /// collide.
    /// </summary>
    public static string EnvVarNameFor(string id)
    {
        var chars = id.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray();
        return "CLAUDE_TUI_LINE_VAL_" + new string(chars);
    }
}
