using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

public class RemoteUrlTests
{
    [Theory]
    [InlineData("git@github.com:user/repo.git", "https://github.com/user/repo")]
    [InlineData("git@github.com:user/repo", "https://github.com/user/repo")]
    [InlineData("https://github.com/user/repo.git", "https://github.com/user/repo")]
    [InlineData("https://github.com/user/repo", "https://github.com/user/repo")]
    [InlineData("http://github.example.com/user/repo", "http://github.example.com/user/repo")]
    [InlineData("git@gitlab.com:group/subgroup/repo.git", "https://gitlab.com/group/subgroup/repo")]
    public void RemoteUrlNormalize_HandlesSshHttpsAndGitSuffix(string raw, string expected)
    {
        Assert.Equal(expected, RemoteUrl.Normalize(raw));
    }

    [Fact]
    public void RemoteUrlNormalize_LocalPathRemote_ReturnsNullAsUnrecognized()
    {
        // A local filesystem path has no git@ prefix, no ssh://git@ prefix, and no https://
        // scheme, so it is not a recognized web remote — Normalize returns null, and §3.2.1's
        // link-suppression path (a missing resolved value) drops the link while keeping the item.
        var actual = RemoteUrl.Normalize("/Users/x/repos/foo");

        Assert.Null(actual);
    }

    [Fact]
    public void RemoteUrlNormalize_SshUrlWithExplicitPort_RewritesToHttpsWithoutThePort()
    {
        // 2222 is the SSH port; a web UI on that host is almost certainly on 443, so carrying
        // the SSH port into the rewritten https:// URL would produce a link that looks plausible
        // and reliably fails. Dropping it is wrong only in the rare case the web UI itself runs
        // on the SSH port; keeping it is wrong nearly always.
        var actual = RemoteUrl.Normalize("ssh://git@host:2222/org/repo.git");

        Assert.Equal("https://host/org/repo", actual);
    }

    // ---- TTL cache (task #13) ----

    private static readonly string[] ProbeArgv = { "git", "remote", "get-url", "origin" };
    private static readonly Dictionary<string, string> NoExportedEnv = new();

    private static string KeyFor(string cwd) =>
        ItemCache.KeyFor("remote-url", ProbeArgv, cwd, paneWidth: null, NoExportedEnv);

    [Fact]
    public void Probe_FreshCacheEntryWithinTtl_ReturnsCachedValueWithoutReprobing()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"remote-url-cache-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"remote-url-cwd-{Guid.NewGuid():N}");
        var key = KeyFor(cwd);
        // A directory that is not a git repo makes a live `git remote get-url` fail (null), so a
        // returned non-null value that matches this sentinel can only have come from the cache —
        // proving the cache hit path never re-shells out.
        ItemCache.Write(cacheDir, key, new CacheEntry("https://cached.example/repo", DateTimeOffset.UtcNow, ExitCode: 0));

        var value = RemoteUrl.Probe(cwd, cacheDir);

        Assert.Equal("https://cached.example/repo", value);
    }

    [Fact]
    public void Probe_ExpiredCacheEntry_ReprobesInsteadOfReturningStaleValue()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"remote-url-cache-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"remote-url-cwd-{Guid.NewGuid():N}");
        var key = KeyFor(cwd);
        ItemCache.Write(cacheDir, key, new CacheEntry("https://stale.example/repo", DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5), ExitCode: 0));

        // cwd is not a git repo, so the live re-probe this expiry should trigger fails and
        // returns null — distinct from the stale cached value, so a null result here proves the
        // expired entry was NOT served.
        var value = RemoteUrl.Probe(cwd, cacheDir);

        Assert.Null(value);

        var rewritten = ItemCache.TryRead(cacheDir, key);
        Assert.NotNull(rewritten);
        Assert.True(DateTimeOffset.UtcNow - rewritten!.CapturedAt < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Probe_DifferentCwd_DoesNotReadAnotherCwdsCacheEntry()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"remote-url-cache-{Guid.NewGuid():N}");
        var cwdA = Path.Combine(Path.GetTempPath(), $"remote-url-cwd-a-{Guid.NewGuid():N}");
        var cwdB = Path.Combine(Path.GetTempPath(), $"remote-url-cwd-b-{Guid.NewGuid():N}");
        ItemCache.Write(cacheDir, KeyFor(cwdA), new CacheEntry("https://repo-a.example/repo", DateTimeOffset.UtcNow, ExitCode: 0));

        var value = RemoteUrl.Probe(cwdB, cacheDir);

        Assert.NotEqual("https://repo-a.example/repo", value);
    }

    [Fact]
    public void Probe_NullOrEmptyCwd_ReturnsNullWithoutTouchingCache()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"remote-url-cache-{Guid.NewGuid():N}");

        Assert.Null(RemoteUrl.Probe(null, cacheDir));
        Assert.Null(RemoteUrl.Probe("", cacheDir));
        Assert.False(Directory.Exists(cacheDir));
    }
}
