namespace ClaudeTuiLine.Tests;

public class AccountProbeTests
{
    private const string Home = "/home/you";

    [Fact]
    public void KeychainService_DefaultStoreHasNoSuffix()
    {
        Assert.Equal("Claude Code-credentials", AccountProbe.KeychainService(null, null, Home));
        Assert.Equal("Claude Code-credentials", AccountProbe.KeychainService(null, "", Home));
        Assert.Equal("Claude Code-credentials", AccountProbe.KeychainService("", "/home/you/alt-config", Home));
    }

    [Fact]
    public void KeychainService_SuffixIsFirstEightHexOfTheDirectorysSha256()
    {
        // printf %s /home/you/.claude-work | shasum -a 256 → 994a9417…
        Assert.Equal("Claude Code-credentials-994a9417", AccountProbe.KeychainService("/home/you/.claude-work", null, Home));
        Assert.Equal("Claude Code-credentials-994a9417", AccountProbe.KeychainService("/home/you/.claude-work", "/home/you/alt-config", Home));
        Assert.Equal("Claude Code-credentials-c53ab267", AccountProbe.KeychainService(null, "/home/you/alt-config", Home));
    }

    [Fact]
    public void CredentialsFile_FollowsTheSameDirectoryChoice()
    {
        Assert.Equal("/home/you/.claude/.credentials.json", AccountProbe.CredentialsFile(null, null, Home));
        Assert.Equal("/home/you/.claude/.credentials.json", AccountProbe.CredentialsFile("", "/home/you/alt-config", Home));
        Assert.Equal("/home/you/.claude-work/.credentials.json", AccountProbe.CredentialsFile("/home/you/.claude-work", "/home/you/alt-config", Home));
        Assert.Equal("/home/you/alt-config/.credentials.json", AccountProbe.CredentialsFile(null, "/home/you/alt-config", Home));
    }

    [Fact]
    public void ParseAccessToken_ReadsOnlyTheAccessToken()
    {
        Assert.Equal("at-1", AccountProbe.ParseAccessToken("""{"claudeAiOauth":{"accessToken":"at-1","refreshToken":"rt-1"}}"""));
        Assert.Null(AccountProbe.ParseAccessToken("""{"claudeAiOauth":{}}"""));
        Assert.Null(AccountProbe.ParseAccessToken("not json"));
    }

    [Fact]
    public void ParseProfile_ReadsEmailAndOrganizationName()
    {
        var info = AccountProbe.ParseProfile("""{"account":{"email":"you@example.com"},"organization":{"name":"Acme Inc"}}""");
        Assert.Equal(new AccountInfo("you@example.com", "Acme Inc"), info);
        Assert.Null(AccountProbe.ParseProfile("""{"error":"unauthorized"}"""));
    }

    [Fact]
    public void Probe_CachesPerStoreAndKeepsTheLastGoodValueOnFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctl-account-" + Guid.NewGuid().ToString("N"));
        try
        {
            var t0 = DateTimeOffset.UtcNow;
            var calls = 0;
            AccountInfo? Fetch(AccountInfo? result) { calls++; return result; }

            var a = new AccountInfo("a@example.com", "A");
            var b = new AccountInfo("b@example.com", "B");
            Assert.Equal(a, AccountProbe.Probe(dir, "store-a", t0, () => Fetch(a)));
            Assert.Equal(b, AccountProbe.Probe(dir, "store-b", t0, () => Fetch(b)));

            // Fresh: served from cache, per store.
            Assert.Equal(a, AccountProbe.Probe(dir, "store-a", t0.AddMinutes(5), () => Fetch(null)));
            Assert.Equal(2, calls);

            // Stale and the fetch fails: the last good value stays, and the retry waits a minute.
            Assert.Equal(a, AccountProbe.Probe(dir, "store-a", t0.AddHours(2), () => Fetch(null)));
            Assert.Equal(3, calls);
            Assert.Equal(a, AccountProbe.Probe(dir, "store-a", t0.AddHours(2).AddSeconds(30), () => Fetch(null)));
            Assert.Equal(3, calls);

            // A throwing fetch is a failure like any other.
            Assert.Equal(a, AccountProbe.Probe(dir, "store-a", t0.AddHours(2).AddSeconds(90), () => throw new IOException()));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Probe_AFetchCutOffMidwayStillBacksOff()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctl-account-" + Guid.NewGuid().ToString("N"));
        try
        {
            var t0 = DateTimeOffset.UtcNow;
            var a = new AccountInfo("a@example.com", "A");
            AccountProbe.Probe(dir, "s", t0, () => a);

            // The process is killed during the lookup: simulated by checking the cache from inside it.
            AccountInfo? seenFromInside = null;
            AccountProbe.Probe(dir, "s", t0.AddHours(1), () =>
            {
                seenFromInside = AccountProbe.Probe(dir, "s", t0.AddHours(1).AddSeconds(1), () => throw new InvalidOperationException("must not refetch"));
                return null;
            });
            Assert.Equal(a, seenFromInside);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void SyntheticContext_NeverProbesTheRealLogin()
    {
        var ctx = SyntheticFixture.CreateItemContext();
        Assert.Equal("you@example.com", SegmentBuilder.ResolveEmail(ctx.Account));
        Assert.Equal("Acme Inc", SegmentBuilder.ResolveOrganization(ctx.Account));
    }
}
