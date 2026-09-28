using System.Diagnostics;

namespace ClaudeTuiLine;

/// <summary>
/// Live probe for the current repo's <c>origin</c> remote, matching
/// <c>git remote get-url origin</c> — chosen over reading <c>.git/config</c> directly because it
/// honors the user's own <c>url.&lt;base&gt;.insteadOf</c> rewrites. Synchronous: nothing awaits
/// this (see <see cref="ItemContext.RemoteUrl"/>'s lazy property), so there is no async wrapper to
/// block on — <see cref="Process.WaitForExit(int)"/> is already the blocking-with-timeout primitive
/// this needs. Cached through <see cref="ItemCache"/> with the same
/// <see cref="CommandProvider"/>-style TTL, since a shell re-rendering the statusline on every
/// prompt would otherwise re-shell out to git for a value that rarely changes within a session.
/// </summary>
public static class RemoteUrl
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // Matches CommandProvider.DefaultTtlSeconds: the same per-render subprocess-cost tradeoff
    // applies here, and origin changes far less often than a typical command item's output.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly string[] Argv = { "git", "remote", "get-url", "origin" };
    private static readonly IReadOnlyDictionary<string, string> NoExportedEnv = new Dictionary<string, string>();

    public static string? Probe(string? cwd, string cacheDir)
    {
        if (string.IsNullOrEmpty(cwd))
        {
            return null;
        }

        // cwd is folded into the key so two repos (or the same session's cwd changing) never
        // read or write each other's entry.
        var key = ItemCache.KeyFor("remote-url", Argv, cwd, paneWidth: null, NoExportedEnv);
        var cached = ItemCache.TryRead(cacheDir, key);
        if (cached is { } fresh && DateTimeOffset.UtcNow - fresh.CapturedAt < CacheTtl)
        {
            return fresh.Value;
        }

        var value = Run(cwd);
        ItemCache.Write(cacheDir, key, new CacheEntry(value, DateTimeOffset.UtcNow, ExitCode: 0));
        return value;
    }

    private static string? Run(string cwd)
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
        psi.ArgumentList.Add("remote");
        psi.ArgumentList.Add("get-url");
        psi.ArgumentList.Add("origin");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        }
        catch
        {
            return null;
        }

        try
        {
            // git remote get-url's output is one short line, well under a pipe buffer, so it is
            // safe to wait for exit before reading: the child never blocks on a full stdout pipe.
            var exited = process.WaitForExit((int)Timeout.TotalMilliseconds);
            if (!exited)
            {
                TryKill(process);
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                return null;
            }

            var raw = stdout.Trim();
            return raw.Length == 0 ? null : Normalize(raw);
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

    internal static string? Normalize(string raw)
    {
        var url = raw.EndsWith(".git", StringComparison.Ordinal) ? raw[..^4] : raw;

        if (url.StartsWith("git@", StringComparison.Ordinal))
        {
            var colonIndex = url.IndexOf(':', "git@".Length);
            if (colonIndex > 0)
            {
                var host = url["git@".Length..colonIndex];
                var path = url[(colonIndex + 1)..];
                return $"https://{host}/{path}";
            }

            return null;
        }

        if (url.StartsWith("ssh://git@", StringComparison.Ordinal))
        {
            // The SSH port has no bearing on the host's web UI (almost always 443); carrying it
            // into the rewritten https:// URL produces a link that looks plausible and reliably
            // fails, so it is dropped rather than preserved.
            var rest = url["ssh://git@".Length..];
            var slashIndex = rest.IndexOf('/');
            if (slashIndex > 0)
            {
                var hostPart = rest[..slashIndex];
                var path = rest[(slashIndex + 1)..];
                var colonIndex = hostPart.IndexOf(':');
                var host = colonIndex >= 0 ? hostPart[..colonIndex] : hostPart;
                if (host.Length > 0 && path.Length > 0)
                {
                    return $"https://{host}/{path}";
                }
            }

            return null;
        }

        // Anything else — a local filesystem path, a file:// URL, or a scheme this function
        // doesn't map — is not a recognized web remote. §3.2.1's link-suppression path (a missing
        // resolved value) handles a null return by dropping the link and keeping the item.
        return url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal)
            ? url
            : null;
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
