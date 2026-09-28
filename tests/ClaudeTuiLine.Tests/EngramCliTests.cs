using System.Runtime.CompilerServices;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-104 §G: the engram CLI probe boundary. No test here invokes the real `engram` binary —
/// each spawn-exercising test uses a small stub shell script as `binaryPath` instead.
/// </summary>
public class EngramCliTests
{
    private static string IsolatedTempDir([CallerMemberName] string? testName = null) =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ctl-engram-tests", $"{testName}-{Guid.NewGuid():N}")).FullName;

    private static string WriteStubScript(string body, [CallerMemberName] string? testName = null)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ctl-engram-stubs", $"{testName}-{Guid.NewGuid():N}")).FullName;
        var path = Path.Combine(dir, "engram-stub.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    // ---- §G.1: default config — no fields, no stateColors — never spawns anything ----

    [Fact]
    public async Task BuildAsync_NoFieldsNoStateColors_NeverSpawnsProbesStatusOrActivityNull()
    {
        var settings = new EngramItemSettings();

        var result = await EngramProbe.BuildAsync(sessionId: null, DateTimeOffset.UtcNow, settings, IsolatedTempDir());

        Assert.NotNull(result);
        Assert.Null(result!.Status);
        Assert.Null(result.Activity);
    }

    [Fact]
    public async Task BuildAsync_NullSettings_NeverSpawnsProbesStatusOrActivityNull()
    {
        var result = await EngramProbe.BuildAsync(sessionId: null, DateTimeOffset.UtcNow, settings: null, IsolatedTempDir());

        Assert.NotNull(result);
        Assert.Null(result!.Status);
        Assert.Null(result.Activity);
    }

    // ---- §D.4: parse-stdout-before-exit-code ----

    [Fact]
    public async Task ProbeStatusAsync_ValidJsonNonZeroExit_TrustsStdoutRegardlessOfExitCode()
    {
        var binary = WriteStubScript("""echo '{"Initialised":true,"Server":"Stopped","Pid":null,"Port":null,"Version":"1.0.0","UptimeSeconds":null}'; exit 1""");

        var status = await EngramCli.ProbeStatusAsync(binary, IsolatedTempDir(), ttlSeconds: 30, timeoutMs: 2000);

        Assert.NotNull(status);
        Assert.Equal("Stopped", status!.Server);
        Assert.True(status.Initialised);
    }

    [Fact]
    public async Task ProbeStatusAsync_AllNullFields_TreatedAsParseFailureNotStoppedServer()
    {
        var binary = WriteStubScript("""echo '{"Initialised":false,"Server":null,"Pid":null,"Port":null,"Version":null,"UptimeSeconds":null}'; exit 0""");

        var status = await EngramCli.ProbeStatusAsync(binary, IsolatedTempDir(), ttlSeconds: 30, timeoutMs: 2000);

        Assert.Null(status);
    }

    [Fact]
    public async Task ProbeActivityAsync_ValidJson_ParsesKindsDictionary()
    {
        var binary = WriteStubScript("""echo '{"LastKind":"embedding","LastAgeSeconds":42,"WindowSeconds":3600,"WindowCount":9,"Kinds":{"embedding":9}}'""");

        var activity = await EngramCli.ProbeActivityAsync(binary, "1h", IsolatedTempDir(), ttlSeconds: 30, timeoutMs: 2000);

        Assert.NotNull(activity);
        Assert.Equal("embedding", activity!.LastKind);
        Assert.Equal(9L, activity.Kinds["embedding"]);
    }

    [Fact]
    public async Task ProbeStatusAsync_MalformedJson_ReturnsNullNotThrows()
    {
        var binary = WriteStubScript("""echo 'not json'""");

        var status = await EngramCli.ProbeStatusAsync(binary, IsolatedTempDir(), ttlSeconds: 30, timeoutMs: 2000);

        Assert.Null(status);
    }

    // ---- §C.8.3/§G.9/§G.10/§G.11: binary resolution security ----

    [Fact]
    public void ResolveBinary_RootedExplicitPath_ReturnedVerbatim()
    {
        var resolved = EngramProbe.ResolveBinary("/opt/nonexistent/engram");

        Assert.Equal("/opt/nonexistent/engram", resolved);
    }

    [Fact]
    public void ResolveBinary_UnrootedPathWithSeparator_ReturnsNullNoFallthrough()
    {
        // §G.10: "foo/engram" has a separator but is not rooted — invalid, and must not fall
        // through to a PATH lookup or the fixed fallback list.
        var resolved = EngramProbe.ResolveBinary("foo/engram");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveBinary_RelativeDotSlashPath_ReturnsNullNoFallthrough()
    {
        var resolved = EngramProbe.ResolveBinary("./engram");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveBinary_BareNameNotOnPath_ReturnsNullNoFallthroughToFixedList()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/nonexistent-dir-for-test");

            // A bare, explicitly-configured binaryPath that isn't found on PATH must not fall
            // through to the fixed fallback list (§G.9) — that list is for the unconfigured case only.
            var resolved = EngramProbe.ResolveBinary("definitely-not-a-real-engram-binary");

            Assert.Null(resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void ResolveBinary_Unconfigured_NeverSelectsAFileNamedEngramInCwd()
    {
        // §G.11: the concrete cwd-execution regression test. A file literally named "engram"
        // sitting in the working directory must never be selected by any resolution path.
        var cwd = IsolatedTempDir();
        var trap = Path.Combine(cwd, "engram");
        File.WriteAllText(trap, "#!/bin/sh\necho pwned\n");
        File.SetUnixFileMode(trap, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var originalCwd = Directory.GetCurrentDirectory();
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Directory.SetCurrentDirectory(cwd);
            Environment.SetEnvironmentVariable("PATH", "/nonexistent-dir-for-test");
            Environment.SetEnvironmentVariable("HOME", "/nonexistent-home-for-test");

            var resolved = EngramProbe.ResolveBinary(binaryPath: null);

            Assert.NotEqual(trap, resolved);
            Assert.False(resolved is { } r && Path.GetFullPath(r) == trap);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }
    }
}
