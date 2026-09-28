using System.Text.Json;
using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// Not calibration-specific despite the name's origin: this collection exists because xUnit runs
/// different test classes in parallel by default, and ANY class that mutates the process-wide
/// CLAUDE_TUI_LINE_CONFIG or HOME env vars (currently CalibrateTests, CalibrationPromptTests, and
/// ConfigTests) races every other member's file paths unless serialized against them. Add a class
/// here whenever it mutates those same env vars, regardless of what it's actually testing — do not
/// delete this as unused calibration scaffolding, and do not assume membership implies the test is
/// about calibration.
/// </summary>
[CollectionDefinition(nameof(CalibrationEnvVarCollection), DisableParallelization = true)]
public class CalibrationEnvVarCollection
{
}

/// <summary>
/// SPEC-101-calibrate-chrome-reserve.md §8. These tests verify the arithmetic and the plumbing —
/// never that the resulting reserve is correct for a real terminal. That boundary is outside this
/// process; measuring it is the whole reason `--calibrate` exists, and confirming it is E1 in §9,
/// which is blocking and requires a live Claude Code pane.
/// </summary>
[Collection(nameof(CalibrationEnvVarCollection))]
public class CalibrateTests : IDisposable
{
    private static readonly int[] ColumnsSamples = { 40, 64, 70, 89, 90, 100, 137 };

    private readonly string? _originalHome = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _originalOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");
    private readonly string? _originalColumns = Environment.GetEnvironmentVariable("COLUMNS");
    private readonly string _configPath;

    public CalibrateTests()
    {
        var dir = Directory.CreateTempSubdirectory("ctl-calibrate-tests-");
        _configPath = Path.Combine(dir.FullName, "claude-tui-line.json");
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _configPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _originalHome);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _originalOverride);
        Environment.SetEnvironmentVariable("COLUMNS", _originalColumns);

        var dir = Path.GetDirectoryName(_configPath)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // 1. Ruler shape.
    [Theory]
    [InlineData(40)]
    [InlineData(64)]
    [InlineData(70)]
    [InlineData(89)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(137)]
    public void BuildRulerRow_ShapeMatchesColumns(int columns)
    {
        var row = CalibrateCommand.BuildRulerRow(columns);

        Assert.Equal(columns, row.Length);
        for (var i = 1; i <= columns; i++)
        {
            Assert.Equal((char)('0' + (i % 10)), row[i - 1]);
        }

        Assert.All(row, c => Assert.True(c is >= '0' and <= '9'));
        Assert.False(char.IsWhiteSpace(row[^1]));
    }

    // 2. Digit -> reserve round-trip — the test that would have caught SPEC-98's formula error.
    [Fact]
    public void ResolveReserveFromDigit_RoundTripsThroughTheRuler()
    {
        foreach (var columns in ColumnsSamples)
        {
            var ruler = CalibrateCommand.BuildRulerRow(columns);
            for (var r = 0; r <= 9; r++)
            {
                // §3.3: P (the 1-based last-visible column) = COLUMNS - r - 1; the 0-based ruler
                // index for that 1-based position is one less again.
                var column = columns - r - 2;
                var digit = ruler[column] - '0';
                var recovered = CalibrateCommand.ResolveReserveFromDigit(columns, digit);
                Assert.Equal(r, recovered);
            }
        }
    }

    // 3. SPEC-98 anchor.
    [Fact]
    public void ResolveReserveFromDigit_Spec98Measurement_Columns90DigitFive_ReservesFour()
    {
        Assert.Equal(4, CalibrateCommand.ResolveReserveFromDigit(90, 5));
    }

    // 4. Verify row widths.
    [Theory]
    [InlineData(90, 4)]
    [InlineData(40, 0)]
    [InlineData(137, 9)]
    public void BuildVerifyRow_WidthsMatchColumnsMinusReserve(int columns, int reserve)
    {
        var rowA = CalibrateCommand.BuildVerifyRow(columns - reserve, 'A');
        var rowB = CalibrateCommand.BuildVerifyRow(columns - reserve + 1, 'B');

        Assert.Equal(columns - reserve, rowA.Length);
        Assert.Equal(columns - reserve + 1, rowB.Length);
        Assert.False(char.IsWhiteSpace(rowA[^1]));
        Assert.False(char.IsWhiteSpace(rowB[^1]));
    }

    // 5. Label suppression.
    [Fact]
    public void TryRunHookProbe_Below70Columns_EmitsNoLabelRow()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });
        Environment.SetEnvironmentVariable("COLUMNS", "69");

        var lines = CaptureProbeOutput();

        Assert.Single(lines);
        Assert.Equal(69, lines[0].Length);
    }

    [Fact]
    public void TryRunHookProbe_AtOrAbove70Columns_EmitsALabelRowNoLongerThan60Chars()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });
        Environment.SetEnvironmentVariable("COLUMNS", "70");

        var lines = CaptureProbeOutput();

        Assert.Equal(2, lines.Length);
        Assert.True(lines[0].Length <= 60);
    }

    // 5b. §5.4/§13.4 fix: the hook writes observedVersion on every probe render (ruler and verify),
    // and leaves it null rather than guessing when the payload has none.
    [Fact]
    public void TryRunHookProbe_RulerPhase_RecordsObservedVersionFromPayload()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });
        Environment.SetEnvironmentVariable("COLUMNS", "90");

        CalibrateCommand.TryRunHookProbe("""{"version":"2.1.238"}""");

        var statePath = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationStatePath()!;
        var state = JsonSerializer.Deserialize(File.ReadAllBytes(statePath), CalibrateJsonContext.Default.CalibrationState);
        Assert.Equal("2.1.238", state!.ObservedVersion);
    }

    [Fact]
    public void TryRunHookProbe_VerifyPhase_RecordsObservedVersionFromPayload()
    {
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });
        Environment.SetEnvironmentVariable("COLUMNS", "90");

        CalibrateCommand.TryRunHookProbe("""{"version":"2.1.239"}""");

        var statePath = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationStatePath()!;
        var state = JsonSerializer.Deserialize(File.ReadAllBytes(statePath), CalibrateJsonContext.Default.CalibrationState);
        Assert.Equal("2.1.239", state!.ObservedVersion);
    }

    [Fact]
    public void TryRunHookProbe_MalformedPayload_LeavesObservedVersionNullRatherThanGuessing()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });
        Environment.SetEnvironmentVariable("COLUMNS", "90");

        CalibrateCommand.TryRunHookProbe("{ this is not json");

        var statePath = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationStatePath()!;
        var state = JsonSerializer.Deserialize(File.ReadAllBytes(statePath), CalibrateJsonContext.Default.CalibrationState);
        Assert.Null(state!.ObservedVersion);
    }

    // 6. Config write preserves unknown keys.
    [Fact]
    public void Confirm_PreservesUnknownConfigKeys()
    {
        File.WriteAllText(_configPath, """{"unknownTopLevel":"keepme","layout":{"unknownNested":42}}""");
        WriteState(new CalibrationState { Phase = "verify", Candidate = 6, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, confirm: true, false, false, false, false);

        Assert.Equal(0, exitCode);
        var written = JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement;
        Assert.Equal("keepme", written.GetProperty("unknownTopLevel").GetString());
        Assert.Equal(42, written.GetProperty("layout").GetProperty("unknownNested").GetInt32());
        Assert.Equal(6, written.GetProperty("layout").GetProperty("chromeReserve").GetInt32());
    }

    // 6b. §5.4/§13.4 fix: --confirm reads the version from the state file's observedVersion (the
    // hook's own record of what it saw), never inferred from the calibration record.
    [Fact]
    public void Confirm_SetsCalibratedVersionFromStateObservedVersion()
    {
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), ObservedVersion = "2.3.4" });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, confirm: true, false, false, false, false);

        Assert.Equal(0, exitCode);
        var recordPath = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationRecordPath()!;
        var record = JsonDocument.Parse(File.ReadAllText(recordPath)).RootElement;
        Assert.Equal("2.3.4", record.GetProperty("calibratedVersion").GetString());
    }

    // §13.5 item 11, the discriminating case: the record's newest-seen version and the state
    // file's observedVersion must differ, so a test can't pass under both the correct rule and the
    // rejected "infer from the record" rule at once.
    [Fact]
    public void Confirm_UsesStateObservedVersion_NotTheRecordsNewestVersionsEntry()
    {
        var recordPath = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationRecordPath()!;
        File.WriteAllText(
            recordPath,
            """{"calibratedVersion":null,"calibratedReserve":null,"versions":{"5.5.5":{"promptFirstSeen":"2026-08-19T00:00:00Z","dismissed":false}}}""");
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), ObservedVersion = "6.6.6" });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, confirm: true, false, false, false, false);

        Assert.Equal(0, exitCode);
        var record = JsonDocument.Parse(File.ReadAllText(recordPath)).RootElement;
        Assert.Equal("6.6.6", record.GetProperty("calibratedVersion").GetString());
    }

    [Fact]
    public void Confirm_ObservedVersionAbsent_WritesConfigButLeavesCalibratedVersionUnchanged()
    {
        var recordPath = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationRecordPath()!;
        File.WriteAllText(recordPath, """{"calibratedVersion":"1.0.0","calibratedReserve":3,"versions":null}""");
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), ObservedVersion = null });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, confirm: true, false, false, false, false);

        Assert.Equal(0, exitCode);
        var written = JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement;
        Assert.Equal(4, written.GetProperty("layout").GetProperty("chromeReserve").GetInt32());
        var record = JsonDocument.Parse(File.ReadAllText(recordPath)).RootElement;
        Assert.Equal("1.0.0", record.GetProperty("calibratedVersion").GetString());
    }

    // 7a. Refuses to write an unparseable config.
    [Fact]
    public void Confirm_UnparseableExistingConfig_RefusesToWrite()
    {
        const string original = "{ this is not valid json";
        File.WriteAllText(_configPath, original);
        WriteState(new CalibrationState { Phase = "verify", Candidate = 3, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, confirm: true, false, false, false, false);

        Assert.Equal(2, exitCode);
        Assert.Equal(original, File.ReadAllText(_configPath));
    }

    // 7b. Refuses to write on --no-ellipsis.
    [Fact]
    public void NoEllipsis_DoesNotWriteConfig()
    {
        Assert.False(File.Exists(_configPath));
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var exitCode = CalibrateCommand.RunCli(false, null, noEllipsis: true, null, false, false, false, false, false);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(_configPath));
    }

    // 8. Expiry.
    [Fact]
    public void TryRunHookProbe_ExpiredState_ReturnsNull()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Environment.SetEnvironmentVariable("COLUMNS", "90");

        Assert.Null(CalibrateCommand.TryRunHookProbe(null));
    }

    // 9. Absent state file.
    [Fact]
    public void TryRunHookProbe_NoStateFile_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("COLUMNS", "90");

        Assert.Null(CalibrateCommand.TryRunHookProbe(null));
    }

    // 10. Phase preconditions.
    [Fact]
    public void Saw_WithoutRulerPhase_IsUsageErrorAndWritesNothing()
    {
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var exitCode = CalibrateCommand.RunCli(false, "5", false, null, false, false, false, false, false);

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(_configPath));
    }

    [Fact]
    public void Confirm_WithoutVerifyPhase_IsUsageErrorAndWritesNothing()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, confirm: true, false, false, false, false);

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(_configPath));
    }

    [Fact]
    public void Saw_BeforeObservedColumnsRecorded_IsUsageError()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var exitCode = CalibrateCommand.RunCli(false, "5", false, null, false, false, false, false, false);

        Assert.Equal(2, exitCode);
    }

    // 11. SPEC-101 §13.2/§8.11: the verify-phase instruction must explain that row A and row B are
    // deliberately different widths, and must never print either row's numeric width — a user who
    // doesn't know the bracket design read the two widths themselves as suspicious and rejected a
    // correct reading (§13's E1 finding).
    [Fact]
    public void Saw_VerifyInstructionText_ExplainsLengthDifference_NeverPrintsRowWidths()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), ObservedColumns = 90 });

        var text = CaptureStdout(() => CalibrateCommand.RunCli(false, "5", false, null, false, false, false, false, false));

        Assert.Contains("different lengths on purpose", text, StringComparison.OrdinalIgnoreCase);
        // digit=5 at columns=90 -> candidate reserve 4 (§3.3) -> row A width 86, row B width 87.
        Assert.DoesNotContain("86", text);
        Assert.DoesNotContain("87", text);
    }

    [Fact]
    public void Set_VerifyInstructionText_ExplainsLengthDifference_NeverPrintsRowWidths()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), ObservedColumns = 90 });

        var text = CaptureStdout(() => CalibrateCommand.RunCli(false, null, false, "4", false, false, false, false, false));

        Assert.Contains("different lengths on purpose", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("86", text);
        Assert.DoesNotContain("87", text);
    }

    private static string CaptureStdout(Func<int> action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exitCode = action();
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static string[] CaptureProbeOutput()
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var result = CalibrateCommand.TryRunHookProbe(null);
            Assert.NotNull(result);
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private void WriteState(CalibrationState state)
    {
        var path = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationStatePath()!;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, CalibrateJsonContext.Default.CalibrationState);
        File.WriteAllBytes(path, bytes);
    }
}
