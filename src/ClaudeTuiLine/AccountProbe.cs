using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeTuiLine;

/// <summary>The Claude account a session is logged in to, as the profile endpoint reports it.</summary>
public sealed record AccountInfo(string? Email, string? Organization);

/// <summary>
/// Backs the <c>email</c> and <c>organization</c> items. The statusline payload carries no account
/// fields, and <c>~/.claude.json</c>'s <c>oauthAccount</c> is shared by every session using that
/// config, so it names whichever account logged in last. This instead finds the login this
/// process would use — the same credential store Claude Code picks from
/// <c>CLAUDE_SECURESTORAGE_CONFIG_DIR</c> / <c>CLAUDE_CONFIG_DIR</c>, both inherited from the
/// session — and asks <c>/api/oauth/profile</c> who it belongs to.
///
/// Only the access token is read; it is sent to api.anthropic.com and nowhere else, never cached,
/// never put in argv. The cache holds email and organization only, keyed by the credential store,
/// not the cwd, so two sessions on different logins in the same folder never share an entry.
/// </summary>
public static class AccountProbe
{
    // Short enough that a /login to another account in the same store shows up within minutes.
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(10);

    // A failure (offline, expired token, no login yet) re-tries after a minute rather than every
    // render; the last good value keeps showing meanwhile.
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private const string ProfileUrl = "https://api.anthropic.com/api/oauth/profile";
    private const char Separator = '\u001f';
    private static readonly IReadOnlyDictionary<string, string> NoEnv = new Dictionary<string, string>();

    public static AccountInfo? Probe(string cacheDir)
    {
        // With a token or API key in the environment the stored login isn't the one in use.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            return null;
        }

        var secure = Environment.GetEnvironmentVariable("CLAUDE_SECURESTORAGE_CONFIG_DIR");
        var config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var service = KeychainService(secure, config, home);
        var file = CredentialsFile(secure, config, home);
        var store = OperatingSystem.IsMacOS() ? $"keychain:{service}|file:{file}" : $"file:{file}";

        return Probe(cacheDir, store, DateTimeOffset.UtcNow, () =>
        {
            var token = (OperatingSystem.IsMacOS() ? ReadKeychain(service) : null) ?? ReadFile(file);
            return token is null ? null : FetchProfile(token);
        });
    }

    /// <summary>Cache policy, separated from the I/O so it is testable.</summary>
    internal static AccountInfo? Probe(string cacheDir, string store, DateTimeOffset now, Func<AccountInfo?> fetch)
    {
        var key = ItemCache.KeyFor("account", new[] { store }, cwd: null, paneWidth: null, NoEnv);
        var cached = ItemCache.TryRead(cacheDir, key);
        if (cached is { } entry && now - entry.CapturedAt < (entry.ExitCode == 0 ? SuccessTtl : FailureTtl))
        {
            return Decode(entry.Value);
        }

        // Claude Code may cancel a statusline run that outlasts the next refresh, so the backoff
        // entry is written before the slow part and only replaced once the lookup succeeds.
        var lastGood = cached?.Value;
        ItemCache.Write(cacheDir, key, new CacheEntry(lastGood, now, ExitCode: 1));

        AccountInfo? fetched;
        try
        {
            fetched = fetch();
        }
        catch
        {
            fetched = null;
        }

        if (fetched is not null)
        {
            ItemCache.Write(cacheDir, key, new CacheEntry(Encode(fetched), now, ExitCode: 0));
            return fetched;
        }

        return Decode(lastGood);
    }

    /// <summary>
    /// Claude Code's Keychain service name (2.1.330): the plain name unless one of the two
    /// variables picks another store, then a suffix of the first 8 hex digits of the SHA-256 of
    /// that directory. An empty <c>CLAUDE_SECURESTORAGE_CONFIG_DIR</c> means the default store.
    /// ponytail: the CLAUDE_CONFIG_DIR branch hashes the value as given; Claude Code may resolve
    /// it first — read from its code, not verified against a real login.
    /// </summary>
    internal static string KeychainService(string? secureDir, string? configDir, string home)
    {
        var useDefault = secureDir is not null ? secureDir.Length == 0 : string.IsNullOrEmpty(configDir);
        if (useDefault)
        {
            return "Claude Code-credentials";
        }

        var dir = Nfc(secureDir ?? configDir!);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir))).ToLowerInvariant();
        return $"Claude Code-credentials-{hash[..8]}";
    }

    /// <summary>Where Claude Code keeps the login on Linux/Windows, and on macOS when the Keychain write fails.</summary>
    internal static string CredentialsFile(string? secureDir, string? configDir, string home)
    {
        var defaultDir = Path.Combine(home, ".claude");
        var dir = secureDir is not null
            ? (secureDir.Length > 0 ? Nfc(secureDir) : defaultDir)
            : (string.IsNullOrEmpty(configDir) ? defaultDir : configDir);
        return Path.Combine(dir, ".credentials.json");
    }

    internal static string? ParseAccessToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                && oauth.TryGetProperty("accessToken", out var token)
                && token.GetString() is { Length: > 0 } value
                    ? value
                    : null;
        }
        catch
        {
            return null;
        }
    }

    internal static AccountInfo? ParseProfile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var email = root.TryGetProperty("account", out var a) && a.TryGetProperty("email", out var e) ? e.GetString() : null;
            var org = root.TryGetProperty("organization", out var o) && o.TryGetProperty("name", out var n) ? n.GetString() : null;
            return email is null && org is null ? null : new AccountInfo(email, org);
        }
        catch
        {
            return null;
        }
    }

    private static string Encode(AccountInfo info) => $"{info.Email}{Separator}{info.Organization}";

    private static AccountInfo? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var parts = value.Split(Separator);
        static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
        return new AccountInfo(NullIfEmpty(parts[0]), parts.Length > 1 ? NullIfEmpty(parts[1]) : null);
    }

    private static string Nfc(string s)
    {
        try
        {
            return s.Normalize(NormalizationForm.FormC);
        }
        catch
        {
            return s;
        }
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? ParseAccessToken(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadKeychain(string service)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("find-generic-password");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(service);
        psi.ArgumentList.Add("-w");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            _ = stderr.Result;
            return process.ExitCode == 0 ? ParseAccessToken(stdout.Result) : null;
        }
        catch
        {
            return null;
        }
    }

    private static AccountInfo? FetchProfile(string accessToken)
    {
        using var client = new HttpClient { Timeout = Timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        using var response = client.Send(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var reader = new StreamReader(response.Content.ReadAsStream());
        return ParseProfile(reader.ReadToEnd());
    }
}
