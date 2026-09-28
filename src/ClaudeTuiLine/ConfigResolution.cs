using ClaudeTuiLineShared;
using Spectre.Console;

namespace ClaudeTuiLine;

// SPEC-47 §5.0.2: relocated out of Program.cs's top-level statements so the config-loading path
// is reachable from tests via InternalsVisibleTo — those local functions were implicitly private
// regardless of what InternalsVisibleTo grants. Pure move: no signature or behavior changes here.
internal static class ConfigResolution
{
    // SPEC-V2-FRAMEWORK.md §9.2.1: the render path's config-loading step. An explicit --config, or a
    // config found by the §5 search order, that turns out unreadable (missing when asserted, or
    // present but unparseable) is reported via UnreadableReason rather than silently replaced by
    // defaults; only "no --config, nothing at any searched path" (row 1) is a legitimate default.
    internal static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath, string? UnreadableReason, int UnreadableReasonProtectedLength) LoadRenderConfig(string? explicitConfigPath)
    {
        var configPath = explicitConfigPath ?? ConfigPath.ResolveConfigPath();
        UserConfig? config = null;

        if (configPath is not null)
        {
            var result = ConfigLoader.ReadConfigForCheck(configPath);

            if (result.Status == ConfigReadStatus.ParseError)
            {
                var (reason, protectedLength) = ComposeUnreadableReason(result);
                return FallbackResult(configPath, reason, protectedLength);
            }

            if (result.Status == ConfigReadStatus.NoFile)
            {
                if (explicitConfigPath is not null)
                {
                    return FallbackResult(configPath, "no such file", 0);
                }

                configPath = null; // §9.2.1 row 1: nothing at the searched path, and none asserted.
            }
            else
            {
                config = result.Config;
            }
        }

        try
        {
            var topLevel = ConfigLoader.ResolveTopLevel(config);
            var rootPane = ConfigLoader.ResolveRootPane(config, topLevel);
            return (topLevel, rootPane, configPath, null, 0);
        }
        // SPEC-47 §5.2: unreachable today (SPEC-47 §1.3); if anything below ResolveTopLevel/
        // ResolveRootPane gains a throw, SPEC-47 §5.2 requires the end-to-end test that becomes
        // writable at that point.
        catch (Exception ex)
        {
            return FallbackResult(configPath, ComposeResolutionFailureReason(ex), 0);
        }
    }

    // SPEC-47 §4.3: reason is non-nullable and has no default — the whole mechanism. No caller can
    // obtain a fallback pane without also supplying a reason, so the coupling that let :896's third
    // call site fall through with a null reason is unrepresentable rather than merely conventional.
    internal static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath, string? UnreadableReason, int UnreadableReasonProtectedLength) FallbackResult(string? configPath, string reason, int protectedLength)
    {
        var (fallbackTopLevel, fallbackPane) = BuildFallbackConfig();
        return (fallbackTopLevel, fallbackPane, configPath, reason, protectedLength);
    }

    // SPEC-47 §3.1/§3.2: names the failure class so the row is not mistaken for a parse error (the
    // config parsed fine and resolution threw), and carries ex.Message verbatim per
    // ComposeUnreadableReason's precedent — no stack trace, no exception type name, since the
    // output is one row degraded under width pressure through five rungs.
    // SPEC-47 §5.1 test 3: the output channel is one row (Program.cs's diagnostic WriteLine), and
    // ex.Message is not guaranteed newline-free — a .NET exception message can legitimately contain
    // one. Newlines are replaced rather than the row split, since a second line would be silently
    // dropped by the one-row output channel this reason feeds.
    internal static string ComposeResolutionFailureReason(Exception ex) =>
        $"config could not be resolved: {StripNewlines(ex.Message)}";

    private static string StripNewlines(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    // SPEC-V2-FRAMEWORK.md §9.2.2: "line <n>, <path within the document>: <message>", read from
    // JsonException's typed LineNumber/Path rather than scraped from Message text .NET is free to
    // reword. LineNumber is 0-indexed in the CLR API; +1 matches the line a text editor shows, since
    // that's the file position this exists to let a reader jump to. Only "line <n>" is irreplaceable
    // — the Pointer is what a reader sees the moment they open the file at that line, and the message
    // is .NET's own wording — so the protected length covers the line number alone; the caller's
    // rung-4 truncation eats the Pointer and message first, and the line number only once nothing
    // else is left to give up.
    internal static (string Reason, int ProtectedLength) ComposeUnreadableReason(ConfigReadResult result)
    {
        var message = result.ErrorMessage ?? "could not be parsed";
        if (result.ErrorLineNumber is not { } lineNumber || result.ErrorJsonPath is not { } jsonPath)
        {
            return (message, 0);
        }

        var lineNumberText = $"line {lineNumber + 1}";
        return ($"{lineNumberText}, {jsonPath}: {message}", lineNumberText.Length);
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) BuildFallbackConfig()
    {
        var fallbackTopLevel = new ResolvedConfig(
            new ColorResolution.ColorExpr.Literal("grey"),
            BoxBorder.Rounded,
            PaneBorderEdges.All,
            ConfigLoader.DefaultChromeReserve,
            ColorSystemSupport.Standard,
            new Dictionary<string, ColorResolution.ColorRule>());
        var fallbackPane = new Pane(
            PaneSplit.None,
            Array.Empty<Pane>(),
            "auto",
            new PaneBorder(fallbackTopLevel.BorderColor, fallbackTopLevel.Style, fallbackTopLevel.Edges),
            null,
            ConfigLoader.DefaultEllipsis,
            null,
            Array.Empty<PaneItem>());
        return (fallbackTopLevel, fallbackPane);
    }
}
