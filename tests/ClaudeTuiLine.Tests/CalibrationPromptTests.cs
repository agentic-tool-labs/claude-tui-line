using System.Text.Json;
using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-101-calibrate-chrome-reserve.md §12.9 / §13.5. No existing golden/render-invariant test in
/// this suite calls RunAsync (the only caller of <see cref="CalibrateCommand.MaybeAppendNudge"/>) —
/// they all exercise ComputeRows/DrawRows or lower directly — so §12.5's "quiet path stays
/// byte-identical" invariant is verified here, at the level MaybeAppendNudge is actually reachable.
/// </summary>
[Collection(nameof(CalibrationEnvVarCollection))]
public class CalibrationPromptTests : IDisposable
{
    private readonly string? _originalOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");
    private readonly string? _originalNoNudge = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE");
    private readonly string _configPath;

    public CalibrationPromptTests()
    {
        var dir = Directory.CreateTempSubdirectory("ctl-calibration-prompt-tests-");
        _configPath = Path.Combine(dir.FullName, "claude-tui-line.json");
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _configPath);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _originalOverride);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE", _originalNoNudge);

        var dir = Path.GetDirectoryName(_configPath)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // 1. Version round-trips from the real captured payload — the field the synthetic fixture lacks.
    [Fact]
    public void StatusInput_RealCapturedPayload_VersionRoundTrips()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "real_captured_workspace.json"));
        var input = JsonSerializer.Deserialize(json, StatusInputJsonContext.Default.StatusInput);

        Assert.Equal("2.1.233", input!.Version);
    }

    // 2. Trigger table — one case per §13.3/§13.4 rule (5-8 revised from §12.3).
    [Fact]
    public void Rule1_PromptDisabled_NoPrompt()
    {
        var appended = InvokeNudge("2.1.300", calibrationPromptEnabled: false);
        Assert.False(appended);
    }

    [Fact]
    public void Rule2_CalibrationInProgress_NoPrompt()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule3_NoRecord_FirstRunPrompts()
    {
        Assert.False(File.Exists(RecordPath));

        var appended = InvokeNudge("2.1.300");

        Assert.True(appended);
        Assert.True(File.Exists(RecordPath));
    }

    [Fact]
    public void Rule4_NullVersion_NeverMatchesARecordedVersionAndNeverPrompts()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = null, DismissedVersion = null });

        var appended = InvokeNudge(null);

        Assert.False(appended);
    }

    [Fact]
    public void Rule5_VersionMatchesCalibratedVersion_ExactMatchOnly_NoPrompt()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.300" });

        Assert.False(InvokeNudge("2.1.300"));
    }

    [Fact]
    public void Rule6_VersionMatchesDismissedVersion_NoPrompt()
    {
        // Legacy shape on disk — migration must fold this into a Versions entry before rule 6 sees it.
        WriteRecord(new CalibrationRecord { DismissedVersion = "2.1.300" });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule7_PromptWindowExpiredForSameVersion_NoPrompt()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.300", PromptFirstSeen = DateTimeOffset.UtcNow.AddDays(-8) });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule8_DifferentMajorMinorFromBaseline_Prompts()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.299" });

        Assert.True(InvokeNudge("2.2.0"));
    }

    [Fact]
    public void Rule8_SameMajorMinorAsBaseline_NoPromptAndNoWrite()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.299" });
        var before = File.ReadAllText(RecordPath);

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
        Assert.Equal(before, File.ReadAllText(RecordPath));
    }

    // §13.8 regression guard: absent calibratedVersion means there is no baseline to compare
    // against, so "has it changed since we calibrated?" is unanswerable — that must mean keep
    // prompting, never suppress. The original §13.4 formula fell back to the newest-seen entry in
    // `Versions`, which after the first render IS this version, making it its own baseline and
    // silently killing the first-run nudge after exactly one render for every unconfirmed user.
    [Fact]
    public void Rule8_NoCalibratedVersion_AlreadySeenVersionWithinWindow_StillPromptsOnSubsequentRenders()
    {
        Assert.True(InvokeNudge("2.1.300")); // first render: rule 3, record created.

        Assert.True(InvokeNudge("2.1.300")); // second render: must still prompt.
        Assert.True(InvokeNudge("2.1.300")); // third render: must still prompt.
    }

    // 3. Invariant 1: quiet path (prompt disabled) never appends a row.
    [Fact]
    public void Invariant1_PromptDisabled_NoRowEverAppended_RegardlessOfRecordState()
    {
        Assert.False(InvokeNudge("2.1.300", calibrationPromptEnabled: false));

        WriteRecord(new CalibrationRecord());
        Assert.False(InvokeNudge("2.1.300", calibrationPromptEnabled: false));
    }

    // 4. Invariant 2: exactly one row appended, and it is the only output written.
    [Fact]
    public void Invariant2_PromptActive_AppendsExactlyOneRow()
    {
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            CalibrateCommand.MaybeAppendNudge("2.1.300", calibrationPromptEnabled: true, surfaceWidth: 100);
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    // 5. Nudge width.
    [Fact]
    public void Nudge_IsAtMost48CharactersAndDoesNotEndInWhitespace()
    {
        var line = CaptureNudgeLine("2.1.300", surfaceWidth: 100);

        Assert.NotNull(line);
        Assert.True(line!.Length <= 48);
        Assert.False(char.IsWhiteSpace(line[^1]));
    }

    [Fact]
    public void Nudge_SuppressedBelowSurfaceWidth50()
    {
        var line = CaptureNudgeLine("2.1.300", surfaceWidth: 49);

        Assert.Null(line);
    }

    // 6. Record write frequency — §13.5 item 1 (single version, retained) and item 2 (replaces
    // §12.9 items 6/7: alternating within one series writes nothing further; a series change writes
    // exactly once). Asserted on write COUNT, never on file mtime.
    [Fact]
    public void WriteFrequency_ConsecutiveRendersAtSameVersion_WritesOnceTotal()
    {
        InvokeNudge("2.1.300");
        var afterFirst = File.ReadAllBytes(RecordPath);

        InvokeNudge("2.1.300");
        InvokeNudge("2.1.300");

        Assert.Equal(afterFirst, File.ReadAllBytes(RecordPath));
    }

    [Fact]
    public void WriteFrequency_AlternatingSameSeriesVersions_ZeroPromptsZeroAdditionalWrites()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.237" });
        var before = File.ReadAllBytes(RecordPath);

        foreach (var version in new[] { "2.1.233", "2.1.238", "2.1.233", "2.1.238" })
        {
            Assert.False(InvokeNudge(version));
        }

        Assert.Equal(before, File.ReadAllBytes(RecordPath));
    }

    [Fact]
    public void WriteFrequency_AlternatingDifferentSeriesVersions_ExactlyOneWriteForTheNewSeries()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.237" });

        Assert.False(InvokeNudge("2.1.237"));
        Assert.True(InvokeNudge("2.2.0"));
        var afterTheOneWrite = File.ReadAllBytes(RecordPath);

        Assert.False(InvokeNudge("2.1.237"));
        Assert.True(InvokeNudge("2.2.0")); // still prompts every render until dismissed/calibrated...

        Assert.Equal(afterTheOneWrite, File.ReadAllBytes(RecordPath)); // ...but never writes again.
    }

    // 7. §13.3 keyed shape round-trips without loss, and one version's entry cannot resurrect
    // another's dismissal.
    [Fact]
    public void KeyedShape_RoundTripsMultipleVersionsWithoutLoss()
    {
        var seenA = DateTimeOffset.UtcNow.AddDays(-3);
        var seenB = DateTimeOffset.UtcNow.AddDays(-1);
        WriteRecord(new CalibrationRecord
        {
            CalibratedVersion = "2.1.237",
            Versions = new Dictionary<string, VersionEntry>
            {
                ["2.1.233"] = new VersionEntry { PromptFirstSeen = seenA, Dismissed = true },
                ["2.2.0"] = new VersionEntry { PromptFirstSeen = seenB, Dismissed = false },
            },
        });

        var bytes = File.ReadAllBytes(RecordPath);
        var record = JsonSerializer.Deserialize(bytes, CalibrateJsonContext.Default.CalibrationRecord)!;

        Assert.Equal(2, record.Versions!.Count);
        Assert.True(record.Versions["2.1.233"].Dismissed);
        Assert.Equal(seenA, record.Versions["2.1.233"].PromptFirstSeen);
        Assert.False(record.Versions["2.2.0"].Dismissed);
        Assert.Equal(seenB, record.Versions["2.2.0"].PromptFirstSeen);
    }

    [Fact]
    public void KeyedShape_DismissingOneVersionDoesNotAffectAnother()
    {
        WriteRecord(new CalibrationRecord
        {
            Versions = new Dictionary<string, VersionEntry>
            {
                ["3.0.0"] = new VersionEntry { PromptFirstSeen = DateTimeOffset.UtcNow, Dismissed = true },
                ["4.0.0"] = new VersionEntry { PromptFirstSeen = DateTimeOffset.UtcNow, Dismissed = false },
            },
        });

        Assert.False(InvokeNudge("3.0.0"));
        Assert.True(InvokeNudge("4.0.0"));
    }

    // 8. §13.3 migration — an old-shape record on disk reads as the equivalent keyed shape.
    [Fact]
    public void Migration_DismissedVersionDiffersFromPromptedForVersion_CreatesTwoEntries()
    {
        WriteRecord(new CalibrationRecord
        {
            PromptedForVersion = "2.1.300",
            PromptFirstSeen = DateTimeOffset.UtcNow.AddDays(-2),
            DismissedVersion = "1.9.0",
        });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);
        Assert.Equal(0, exitCode);

        var doc = JsonDocument.Parse(File.ReadAllBytes(RecordPath));
        var versions = doc.RootElement.GetProperty("versions");
        Assert.Equal(2, versions.EnumerateObject().Count());
        Assert.True(versions.GetProperty("2.1.300").GetProperty("dismissed").GetBoolean());
        Assert.True(versions.GetProperty("1.9.0").GetProperty("dismissed").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("promptedForVersion", out _));
        Assert.False(doc.RootElement.TryGetProperty("dismissedVersion", out _));
    }

    [Fact]
    public void Migration_PromptFirstSeenAbsent_DefaultsToNow()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.300" });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);
        Assert.Equal(0, exitCode);

        var doc = JsonDocument.Parse(File.ReadAllBytes(RecordPath));
        var firstSeen = doc.RootElement.GetProperty("versions").GetProperty("2.1.300").GetProperty("promptFirstSeen").GetDateTimeOffset();
        Assert.True(DateTimeOffset.UtcNow - firstSeen < TimeSpan.FromMinutes(1));
    }

    // 9. §13.3: an unparseable record is treated as absent, never thrown.
    [Fact]
    public void UnparseableRecord_TreatedAsAbsent_RendersNormally()
    {
        File.WriteAllText(RecordPath, "{ this is not json");

        var appended = InvokeNudge("2.1.300");

        Assert.True(appended); // treated as record-null -> rule 3, first run.
    }

    // 10. §13.3 pruning: an 11th version evicts the oldest promptFirstSeen entry.
    [Fact]
    public void Pruning_EleventhVersion_EvictsOldest_KeepsTen()
    {
        var now = DateTimeOffset.UtcNow;
        var versions = new Dictionary<string, VersionEntry>();
        for (var i = 1; i <= 10; i++)
        {
            versions[$"{i}.0.0"] = new VersionEntry { PromptFirstSeen = now.AddMinutes(-(10 - i)), Dismissed = false };
        }
        WriteRecord(new CalibrationRecord { Versions = versions });

        Assert.True(InvokeNudge("11.0.0"));

        var doc = JsonDocument.Parse(File.ReadAllBytes(RecordPath));
        var written = doc.RootElement.GetProperty("versions");
        Assert.Equal(10, written.EnumerateObject().Count());
        Assert.False(written.TryGetProperty("1.0.0", out _));
        Assert.True(written.TryGetProperty("11.0.0", out _));
    }

    // 11. --dismiss marks every tracked version, not just one.
    [Fact]
    public void Dismiss_MarksEveryEntry_NewVersionStillPrompts()
    {
        WriteRecord(new CalibrationRecord
        {
            Versions = new Dictionary<string, VersionEntry>
            {
                ["1.0.0"] = new VersionEntry { PromptFirstSeen = DateTimeOffset.UtcNow, Dismissed = false },
                ["2.0.0"] = new VersionEntry { PromptFirstSeen = DateTimeOffset.UtcNow, Dismissed = false },
                ["3.0.0"] = new VersionEntry { PromptFirstSeen = DateTimeOffset.UtcNow, Dismissed = false },
            },
        });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);
        Assert.Equal(0, exitCode);

        var doc = JsonDocument.Parse(File.ReadAllBytes(RecordPath));
        var written = doc.RootElement.GetProperty("versions");
        foreach (var key in new[] { "1.0.0", "2.0.0", "3.0.0" })
        {
            Assert.True(written.GetProperty(key).GetProperty("dismissed").GetBoolean());
        }

        Assert.False(InvokeNudge("1.0.0"));
        Assert.False(InvokeNudge("2.0.0"));
        Assert.False(InvokeNudge("3.0.0"));
        Assert.True(InvokeNudge("9.0.0"));
    }

    [Fact]
    public void Dismiss_NoRecord_WritesNothing()
    {
        Assert.False(File.Exists(RecordPath));

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(RecordPath));
    }

    [Fact]
    public void Dismiss_RecordWithEmptyVersions_WritesNothing()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.237" });
        var before = File.ReadAllText(RecordPath);

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(before, File.ReadAllText(RecordPath));
    }

    // 12. Prompt window — covered structurally by Rule7 above; asserted again against the boundary.
    [Fact]
    public void PromptWindow_OlderThanSevenDays_Suppresses()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "9.9.9", PromptFirstSeen = DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(-1) });

        Assert.False(InvokeNudge("9.9.9"));
    }

    // 13. Cache dir independence.
    [Fact]
    public void DeletingCacheDir_DoesNotResurrectPromptOrLoseDismissal()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.300", PromptFirstSeen = DateTimeOffset.UtcNow });
        CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);

        var cacheDir = ClaudeTuiLine.ItemCache.ResolveCacheDir();
        if (Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
        }

        Assert.False(InvokeNudge("2.1.300"));
    }

    // 14. Calibration in progress suppresses the prompt, regardless of record state.
    [Fact]
    public void CalibrationInProgress_SuppressesPrompt_EvenOnFirstRunRecord()
    {
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        Assert.False(InvokeNudge("2.1.300"));
        Assert.False(File.Exists(RecordPath));
    }

    // 15. ctl-arch1 addendum: MajorMinor is ordinal-string-only — never parsed, never ordered.
    [Theory]
    [InlineData("2.1.238", "2.1")]
    [InlineData("2.1", "2.1")]
    [InlineData("2", "2")]
    [InlineData("dev", "dev")]
    [InlineData("2.1.238-beta.1", "2.1")]
    [InlineData("2.1-rc1", "2.1-rc1")]
    [InlineData("", "")]
    public void MajorMinor_MatchesTheSpecifiedTable(string version, string expected)
    {
        Assert.Equal(expected, CalibrateCommand.MajorMinor(version));
    }

    private string RecordPath => ClaudeTuiLineShared.ConfigPath.ResolveCalibrationRecordPath()!;

    private static bool InvokeNudge(string? version, bool calibrationPromptEnabled = true) =>
        CaptureNudgeLine(version, surfaceWidth: 100, calibrationPromptEnabled) is not null;

    private static string? CaptureNudgeLine(string? version, int surfaceWidth, bool calibrationPromptEnabled = true)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            CalibrateCommand.MaybeAppendNudge(version, calibrationPromptEnabled, surfaceWidth);
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? null : lines[0];
    }

    private void WriteState(CalibrationState state)
    {
        var path = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationStatePath()!;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, CalibrateJsonContext.Default.CalibrationState);
        File.WriteAllBytes(path, bytes);
    }

    private void WriteRecord(CalibrationRecord record)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, CalibrateJsonContext.Default.CalibrationRecord);
        File.WriteAllBytes(RecordPath, bytes);
    }
}
