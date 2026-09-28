using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTuiLineMcp;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>
/// SPEC-12.6-mcp-tools.md §10 V6-V13. Every test here constructs <see cref="BackupLedger"/>
/// directly with a temp-directory backup root and a temp-directory settings.json path — never the
/// real ~/.claude paths (§9.7). See <see cref="V6_BackupLedgerHasNoDefaultConstructorOrRealRoot"/>
/// for the structural guarantee that makes this impossible to get wrong by omission.
/// </summary>
public sealed class BackupLedgerTests : IDisposable
{
    private readonly string _root;
    private readonly string _backupRoot;
    private readonly string _settingsPath;
    private readonly string _configPath;

    public BackupLedgerTests()
    {
        _root = Directory.CreateTempSubdirectory("claude-tui-line-ledger-test-").FullName;
        _backupRoot = Path.Combine(_root, "backups");
        _settingsPath = Path.Combine(_root, "settings.json");
        _configPath = Path.Combine(_root, "claude-tui-line.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void V6_BackupLedgerHasNoDefaultConstructorOrRealRoot()
    {
        var ctors = typeof(BackupLedger).GetConstructors();
        Assert.Single(ctors);
        var parameters = ctors[0].GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, p => Assert.False(p.HasDefaultValue, $"{p.Name} must not have a default value — every call site must state its target explicitly (§9.7)."));
    }

    [Fact]
    public void V7_OneEntryCarriesBothSettingsAndConfigBeforeAnyWrite()
    {
        File.WriteAllText(_settingsPath, """{"statusLine":{"type":"command","command":"/bin/echo"}}""");
        File.WriteAllText(_configPath, """{"border":{"style":"rounded"}}""");
        var originalConfigBytes = File.ReadAllBytes(_configPath);

        var ledger = new BackupLedger(_backupRoot, _settingsPath);
        var outcome = ledger.WriteCheckpoint(_configPath);

        Assert.True(outcome.Ok);
        var entry = JsonNode.Parse(outcome.EntryJson)!.AsObject();
        Assert.True(entry.ContainsKey("settingsCopy"));
        Assert.True(entry.ContainsKey("settingsSha256"));
        Assert.True(entry.ContainsKey("configCopy"));
        Assert.True(entry.ContainsKey("configSha256"));

        // The checkpoint never modifies the config it is backing up.
        Assert.Equal(originalConfigBytes, File.ReadAllBytes(_configPath));
    }

    [Fact]
    public void V8_NoConfigFilePresentRecordsPathAndNullCopyOmitsHash()
    {
        File.WriteAllText(_settingsPath, "{}");
        // _configPath deliberately not created.

        var ledger = new BackupLedger(_backupRoot, _settingsPath);
        var outcome = ledger.WriteCheckpoint(_configPath);

        Assert.True(outcome.Ok);
        var entry = JsonNode.Parse(outcome.EntryJson)!.AsObject();
        Assert.Equal(_configPath, entry["configOriginalPath"]!.GetValue<string>());
        Assert.True(entry.TryGetPropertyValue("configCopy", out var copy));
        Assert.Null(copy);
        Assert.False(entry.ContainsKey("configSha256"));
    }

    [Fact]
    public void V9_NeverWritesKindOrigin()
    {
        // Empty ledger and a statusLine pointing at a script that is not a claude-tui-line
        // binary — exactly the state docs/backup-ledger.md says the model-facing procedure would
        // treat as "write origin". The MCP server must never take that branch (§9.3).
        var scriptPath = Path.Combine(_root, "not-claude-tui-line.sh");
        File.WriteAllText(scriptPath, "#!/bin/sh\necho hi\n");
        var settingsJson = "{\"statusLine\":{\"type\":\"command\",\"command\":" + JsonSerializer.Serialize(scriptPath) + "}}";
        File.WriteAllText(_settingsPath, settingsJson);
        File.WriteAllText(_configPath, "{}");

        var ledger = new BackupLedger(_backupRoot, _settingsPath);
        var outcome = ledger.WriteCheckpoint(_configPath);

        Assert.True(outcome.Ok);
        var entry = JsonNode.Parse(outcome.EntryJson)!.AsObject();
        Assert.Equal("checkpoint", entry["kind"]!.GetValue<string>());

        var ledgerPath = Path.Combine(_backupRoot, "ledger.jsonl");
        var allLines = File.ReadAllLines(ledgerPath);
        Assert.All(allLines, line => Assert.DoesNotContain("\"kind\":\"origin\"", line));
    }

    [Fact]
    public void V10_UnwritableBackupDirectoryFailsCheckpointFailedAndLeavesConfigUntouched()
    {
        File.WriteAllText(_settingsPath, "{}");
        File.WriteAllText(_configPath, """{"border":{"style":"rounded"}}""");
        var originalConfigBytes = File.ReadAllBytes(_configPath);

        // Make the parent of the backup root read-only so Directory.CreateDirectory(_backupRoot) fails.
        var parent = Path.Combine(_root, "readonly-parent");
        Directory.CreateDirectory(parent);
        var unwritableBackupRoot = Path.Combine(parent, "backups");
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var ledger = new BackupLedger(unwritableBackupRoot, _settingsPath);
            var outcome = ledger.WriteCheckpoint(_configPath);

            Assert.False(outcome.Ok);
            Assert.NotNull(outcome.FailedMessage);
            Assert.Equal(originalConfigBytes, File.ReadAllBytes(_configPath));
        }
        finally
        {
            // restore write permission so cleanup (Dispose) can remove the directory tree.
            File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void V11_TornFinalLedgerLineIsToleratedAndEarlierLinesSurvive()
    {
        Directory.CreateDirectory(_backupRoot);
        var ledgerPath = Path.Combine(_backupRoot, "ledger.jsonl");
        var completeLine = """{"kind":"checkpoint","timestamp":"2026-01-01T00:00:00Z","statusLine":null,"settingsCopy":null,"configOriginalPath":"/x","configCopy":null}""";
        // A torn final line: valid JSON prefix, no closing brace, no trailing newline.
        var tornLine = """{"kind":"checkpoint","timestamp":"2026-01-01T00:00:01Z","statusLine":null""";
        File.WriteAllText(ledgerPath, completeLine + "\n" + tornLine);

        File.WriteAllText(_settingsPath, "{}");
        File.WriteAllText(_configPath, "{}");

        var ledger = new BackupLedger(_backupRoot, _settingsPath);
        var outcome = ledger.WriteCheckpoint(_configPath);

        Assert.True(outcome.Ok);
        var allLines = File.ReadAllLines(ledgerPath);
        Assert.Equal(completeLine, allLines[0]);
        Assert.Equal(tornLine, allLines[1]);
        Assert.Equal(outcome.EntryJson, allLines[2]);
    }

    [Fact]
    public void V12_ArtifactFilenameCollisionGetsCounterSuffixAndNeverOverwrites()
    {
        File.WriteAllText(_settingsPath, """{"statusLine":null}""");
        File.WriteAllText(_configPath, "{\"first\":true}");

        var ledger = new BackupLedger(_backupRoot, _settingsPath);
        var first = ledger.WriteCheckpoint(_configPath);
        Assert.True(first.Ok);
        var firstEntry = JsonNode.Parse(first.EntryJson)!.AsObject();
        var firstConfigCopyName = firstEntry["configCopy"]!.GetValue<string>();
        var firstConfigCopyPath = Path.Combine(_backupRoot, firstConfigCopyName);
        var firstConfigCopyBytes = File.ReadAllBytes(firstConfigCopyPath);

        // Change the config and checkpoint again immediately — same wall-clock second in practice
        // for two synchronous in-process calls, which is what actually exercises the collision path.
        File.WriteAllText(_configPath, "{\"second\":true}");
        var second = ledger.WriteCheckpoint(_configPath);
        Assert.True(second.Ok);
        var secondEntry = JsonNode.Parse(second.EntryJson)!.AsObject();
        var secondConfigCopyName = secondEntry["configCopy"]!.GetValue<string>();

        if (secondConfigCopyName == firstConfigCopyName)
        {
            // Ran across a second boundary between the two calls — collision path not exercised.
            // Re-run once more to make a second collision overwhelmingly likely.
            File.WriteAllText(_configPath, "{\"third\":true}");
            second = ledger.WriteCheckpoint(_configPath);
            secondEntry = JsonNode.Parse(second.EntryJson)!.AsObject();
            secondConfigCopyName = secondEntry["configCopy"]!.GetValue<string>();
        }

        Assert.NotEqual(firstConfigCopyName, secondConfigCopyName);
        // Rule 1: the original copy is never overwritten.
        Assert.Equal(firstConfigCopyBytes, File.ReadAllBytes(firstConfigCopyPath));
    }

    [Fact]
    public void V13_HashMatchesShasumMinus256()
    {
        var knownFile = Path.Combine(_root, "known.txt");
        File.WriteAllText(knownFile, "claude-tui-line backup ledger test fixture\n");

        var psi = new ProcessStartInfo
        {
            FileName = "shasum",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add("256");
        psi.ArgumentList.Add(knownFile);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var expectedHex = stdout.Split(' ')[0].Trim();

        var actualHex = ConfigFile.ComputeRevision(File.ReadAllBytes(knownFile));
        Assert.Equal(expectedHex, actualHex);
    }
}
