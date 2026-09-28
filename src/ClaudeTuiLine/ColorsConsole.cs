using Spectre.Console;

namespace ClaudeTuiLine;

// §9.6.3.1 + SPEC-colors-terminal-fidelity §3.2: Ansi stays auto-detected so --colors still
// degrades to bare names under a pipe. ColorSystem is pinned to Standard — the sixteen are
// recommended *because* they are theme-mapped, and that property only exists under indexed SGR.
internal static class ColorsConsole
{
    internal static IAnsiConsole Create(TextWriter output, AnsiSupport ansi = AnsiSupport.Detect) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ansi,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output),
        });
}
