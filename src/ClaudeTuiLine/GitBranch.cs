using System.Diagnostics;

namespace ClaudeTuiLine;

/// <summary>
/// Live probe for the current git branch, matching
/// <c>git --no-optional-locks -C &lt;cwd&gt; branch --show-current</c>.
/// </summary>
public static class GitBranch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static Task<string?> ProbeAsync(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd))
        {
            return Task.FromResult<string?>(null);
        }

        return RunAsync(cwd);
    }

    private static async Task<string?> RunAsync(string cwd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(cwd);
        psi.ArgumentList.Add("branch");
        psi.ArgumentList.Add("--show-current");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        }
        catch
        {
            return null;
        }

        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            var branch = stdout.Trim();
            return branch.Length == 0 ? null : branch;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return null;
        }
        catch
        {
            TryKill(process);
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best-effort cleanup only
        }
    }
}
