using System.Diagnostics;
using System.Text.Json;

namespace ClaudeTuiLine.Tests;

[CollectionDefinition("PreviewCli", DisableParallelization = true)]
public sealed class PreviewCliCollection
{
}

// Both PreviewCliTests and PreviewJsonRowsTests shell out to the built CLI as a subprocess (see
// their own header comments for why). This runner is the single place that resolves the CLI
// binary and execs it, so no test invocation ever drives `dotnet run`/MSBuild on the hot path —
// MSBuild diagnostics on stdout (e.g. an absolute-path-prefixed warning) would otherwise corrupt
// the JSON these tests parse (task #65).
internal static class PreviewCliRunner
{
    // The tests/ClaudeTuiLine.Tests.csproj ProjectReference to src/ClaudeTuiLine already copies a
    // runnable apphost (or, failing that, the DLL + runtimeconfig.json) into this test assembly's
    // own output directory — no separate build step is needed to obtain a binary to exec.
    static readonly Lazy<(string Command, string? DllArg)> ResolvedBinary = new(ResolveBinary, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static (int ExitCode, string StdOut, string StdErr) Run(params string[] cliArgs) => Run(null, cliArgs);

    internal static (int ExitCode, string StdOut, string StdErr) Run(IReadOnlyDictionary<string, string?>? envOverrides, params string[] cliArgs)
        => Execute(stdin: null, envOverrides, cliArgs);

    internal static (int ExitCode, string StdOut, string StdErr) RunWithStdin(string stdin, params string[] cliArgs)
        => Execute(stdin, null, cliArgs);

    internal static (int ExitCode, string StdOut, string StdErr) RunWithStdin(string stdin, IReadOnlyDictionary<string, string?>? envOverrides, params string[] cliArgs)
        => Execute(stdin, envOverrides, cliArgs);

    internal static JsonDocument ParseJsonOrFail((int ExitCode, string StdOut, string StdErr) result)
    {
        try
        {
            return JsonDocument.Parse(result.StdOut);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"failed to parse CLI stdout as JSON: {ex.Message}\n" +
                $"exit code: {result.ExitCode}\n" +
                $"stdout (first 2000 chars): {Truncate(result.StdOut, 2000)}\n" +
                $"stderr (first 2000 chars): {Truncate(result.StdErr, 2000)}",
                ex);
        }
    }

    static string Truncate(string text, int maxChars) => text.Length <= maxChars ? text : text[..maxChars];

    static (int ExitCode, string StdOut, string StdErr) Execute(
        string? stdin, IReadOnlyDictionary<string, string?>? envOverrides, string[] cliArgs)
    {
        var (command, dllArg) = ResolvedBinary.Value;
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        if (dllArg is not null)
        {
            psi.ArgumentList.Add(dllArg);
        }

        foreach (var arg in cliArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        // Tests must not inherit whatever COLUMNS happens to be set in the host shell — the
        // fallback-chain tests need deterministic control over whether it's present at all.
        psi.Environment.Remove("COLUMNS");
        if (envOverrides is not null)
        {
            foreach (var (key, value) in envOverrides)
            {
                if (value is null)
                {
                    psi.Environment.Remove(key);
                }
                else
                {
                    psi.Environment[key] = value;
                }
            }
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start claude-tui-line process");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
        }
        process.StandardInput.Close();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return (process.ExitCode, stdout, stderr);
    }

    static (string Command, string? DllArg) ResolveBinary()
    {
        var bin = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_BIN");
        if (!string.IsNullOrWhiteSpace(bin) && File.Exists(bin))
        {
            return (bin, null);
        }

        var apphostPath = Path.Combine(AppContext.BaseDirectory, "claude-tui-line");
        if (File.Exists(apphostPath))
        {
            return (apphostPath, null);
        }

        var dllPath = Path.Combine(AppContext.BaseDirectory, "claude-tui-line.dll");
        if (File.Exists(dllPath))
        {
            return ("dotnet", dllPath);
        }

        throw new InvalidOperationException(
            $"could not find the claude-tui-line CLI apphost or DLL in {AppContext.BaseDirectory}. " +
            "Expected the ProjectReference to src/ClaudeTuiLine/ClaudeTuiLine.csproj to have copied one there.");
    }
}
