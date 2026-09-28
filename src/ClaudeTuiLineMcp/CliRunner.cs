using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-12.6-mcp-tools.md §1: the server SPAWNS the CLI rather than linking the core in-process —
/// <c>--check</c> is behaviour, and the server drives it without owning or reimplementing it. A
/// successful spawn is evidence the statusline binary actually works on this machine; that
/// evidence is unavailable to a linked server (§1.3(b)).
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// SPEC-12.6-mcp-tools.md §5/N1/N2: <c>--check --config &lt;path&gt; --json</c> validates a
    /// candidate config at an arbitrary path without writing anything.
    /// </summary>
    public static async Task<CliCheckResult> RunCheckAsync(string configPath)
    {
        var location = CliLocator.Locate();
        if (location.Path is null)
        {
            return new CliCheckResult(false, location.SearchedPaths, -1, null);
        }

        var psi = new ProcessStartInfo
        {
            FileName = location.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--check");
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(configPath);
        psi.ArgumentList.Add("--json");

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("the CLI process did not start");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            JsonNode? payload;
            try
            {
                payload = JsonNode.Parse(stdout);
            }
            catch
            {
                payload = null;
            }

            return new CliCheckResult(true, location.SearchedPaths, process.ExitCode, payload);
        }
        catch
        {
            return new CliCheckResult(false, location.SearchedPaths, -1, null);
        }
    }

    /// <summary>
    /// SPEC-84-mcp-schema-explorer.md §6.2 step 1: <c>--schema --json</c> is spawned with the same
    /// shape <see cref="RunCheckAsync"/> uses — no config path, no other arguments.
    /// </summary>
    public static async Task<CliCheckResult> RunSchemaAsync()
    {
        var location = CliLocator.Locate();
        if (location.Path is null)
        {
            return new CliCheckResult(false, location.SearchedPaths, -1, null);
        }

        var psi = new ProcessStartInfo
        {
            FileName = location.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--schema");
        psi.ArgumentList.Add("--json");

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("the CLI process did not start");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            JsonNode? payload;
            try
            {
                payload = JsonNode.Parse(stdout);
            }
            catch
            {
                payload = null;
            }

            return new CliCheckResult(true, location.SearchedPaths, process.ExitCode, payload);
        }
        catch
        {
            return new CliCheckResult(false, location.SearchedPaths, -1, null);
        }
    }

    /// <summary>
    /// SPEC-12.6-mcp-tools.md §2.2/§7.3: <c>get_config</c> must still perform the CLI presence
    /// check even though it is otherwise read-only — skipping it lets a machine with no CLI get a
    /// happily-served config and a blank statusline. <c>--items --json</c> reads no config and
    /// probes nothing, so it is the cheapest genuine spawn available for the purpose.
    /// </summary>
    public static async Task<CliPresence> ProbePresenceAsync()
    {
        var location = CliLocator.Locate();
        if (location.Path is null)
        {
            return new CliPresence(false, location.SearchedPaths);
        }

        var psi = new ProcessStartInfo
        {
            FileName = location.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--items");
        psi.ArgumentList.Add("--json");

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("the CLI process did not start");
            await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return new CliPresence(process.ExitCode == 0, location.SearchedPaths);
        }
        catch
        {
            return new CliPresence(false, location.SearchedPaths);
        }
    }
}

internal sealed record CliCheckResult(bool CliFound, IReadOnlyList<string> SearchedPaths, int ExitCode, JsonNode? Payload);

internal sealed record CliPresence(bool Found, IReadOnlyList<string> SearchedPaths);
