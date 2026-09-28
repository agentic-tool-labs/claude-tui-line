namespace ClaudeTuiLine.Tests;

/// <summary>
/// The one shared stripper for test-side measurement (SPEC-V2-FRAMEWORK.md §10 item 3):
/// AnsiStrip.Strip removes raw ANSI/OSC 8 escape bytes, then Markup.Remove removes Spectre
/// [tag] syntax. Order is load-bearing, not stylistic: Markup.Remove's tokenizer throws
/// ("Encountered unescaped ']' token") if raw OSC 8 bytes reach it first, since OSC 8's close
/// sequence contains a literal ']'.
/// </summary>
internal static class DisplayWidth
{
    public static string Strip(string markup) => Spectre.Console.Markup.Remove(AnsiStrip.Strip(markup));

    public static int Measure(string markup) => Strip(markup).Length;
}
