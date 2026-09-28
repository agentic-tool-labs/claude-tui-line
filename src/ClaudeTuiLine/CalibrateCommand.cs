using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClaudeTuiLineShared;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-101-calibrate-chrome-reserve.md: `--calibrate` measures the real chrome budget on this
/// machine/Claude Code version and writes it to `layout.chromeReserve`. Two files back this: a
/// transient calibration-in-progress state (§6.1, 30-minute expiry) and a durable record of what
/// was calibrated/prompted/dismissed (§12.2, no expiry) — deliberately not merged, since a corrupt
/// transient file should cost only a re-run, while losing the record resurrects the first-run
/// prompt and forgets a dismissal.
/// </summary>
public static class CalibrateCommand
{
    private const int PromptWindowDays = 7;

    // §3.1: the ruler. Character at 1-based emitted column i is the ASCII digit (i mod 10).
    public static string BuildRulerRow(int columns)
    {
        if (columns <= 0)
        {
            return string.Empty;
        }

        var chars = new char[columns];
        for (var i = 1; i <= columns; i++)
        {
            chars[i - 1] = (char)('0' + (i % 10));
        }

        return new string(chars);
    }

    // §3.3: P = COLUMNS - 1 - ((COLUMNS - 1 - d) mod 10); chromeReserve = COLUMNS - P - 1 (§3.2).
    // No indent term anywhere in this arithmetic — see §3.2's warning. If you find yourself adding
    // one, you have reintroduced SPEC-98's bug.
    public static int ResolveReserveFromDigit(int columns, int digit)
    {
        var mod = ((columns - 1 - digit) % 10 + 10) % 10;
        var p = (columns - 1) - mod;
        return columns - p - 1;
    }

    // §3.4/§3.5: never ends in whitespace, plain ASCII except the one non-ASCII glyph (─, U+2500;
    // see SPEC-101 §9 E1 — fall back to ASCII '-' if a real pane shows it mangled or wide).
    public static string BuildVerifyRow(int width, char marker)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (width == 1)
        {
            return marker.ToString();
        }

        return marker + new string('─', width - 2) + marker;
    }

    /// <summary>
    /// §5.1: called immediately after the stdin read and before ParseInput. Returns an exit code
    /// when a probe row was emitted (the caller must return it immediately, without touching
    /// stdin's parsed contents further), or null when no calibration is active and the caller
    /// should proceed with the normal render. §13.8: still needs the payload's version despite
    /// running before ParseInput — <see cref="TryExtractVersion"/> reads it out of rawInput
    /// independently, so the two original constraints (stdin already drained; still ahead of
    /// LoadRenderConfig, so a broken config can't block a ruler) stay intact.
    /// </summary>
    public static int? TryRunHookProbe(string? rawInput)
    {
        var state = LoadState();
        if (state is null || IsExpired(state))
        {
            return null;
        }

        var columnsEnv = Environment.GetEnvironmentVariable("COLUMNS");
        if (!int.TryParse(columnsEnv, out var columns) || columns <= 0)
        {
            Console.Out.WriteLine("claude-tui-line: calibration needs COLUMNS");
            return 0;
        }

        // §5.4: the hook records the COLUMNS it saw so a later --saw (a different terminal
        // process, possibly a different width) computes the reserve from what was actually
        // rendered rather than from its own environment.
        if (state.Phase == "ruler" && state.ObservedColumns is null)
        {
            state.ObservedColumns = columns;
        }

        // §5.4/§13.4 fix: the Claude Code version being calibrated against is a fact known only to
        // the hook (it read the payload), so --confirm must read it back from here rather than
        // inferring it from the record or the CLI's own environment. Written on every probe render
        // (ruler and verify), always overwriting, so --confirm reflects whichever pane rendered most
        // recently. Left null on a malformed/versionless payload rather than guessed.
        state.ObservedVersion = TryExtractVersion(rawInput);
        WriteState(state);

        var showLabel = columns >= 70;
        var rows = new List<string>();

        if (state.Phase == "verify" && state.Candidate is int r)
        {
            if (showLabel)
            {
                rows.Add("ctl calibrate: does row A end in \"A\", row B in \"…\"?");
            }

            rows.Add(BuildVerifyRow(Math.Max(0, columns - r), 'A'));
            rows.Add(BuildVerifyRow(Math.Max(0, columns - r + 1), 'B'));
        }
        else
        {
            if (showLabel)
            {
                rows.Add("ctl calibrate: report last readable digit");
            }

            rows.Add(BuildRulerRow(columns));
        }

        foreach (var row in rows)
        {
            Console.Out.WriteLine(row);
        }

        return 0;
    }

    private static string? TryExtractVersion(string? rawInput)
    {
        if (string.IsNullOrEmpty(rawInput))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(rawInput, StatusInputJsonContext.Default.StatusInput)?.Version;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// §12: called late in RunAsync, immediately before rows are written to stdout — the opposite
    /// end from <see cref="TryRunHookProbe"/>, and mutually exclusive with it via §12.3 rule 2.
    /// </summary>
    public static void MaybeAppendNudge(string? version, bool calibrationPromptEnabled, int? surfaceWidth)
    {
        // §12.7 rule 1: config toggle checked first (already in memory) so an opted-out user pays
        // nothing beyond the boolean check.
        if (!calibrationPromptEnabled)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE")))
        {
            return;
        }

        var state = LoadState();
        if (state is not null && !IsExpired(state))
        {
            return; // rule 2: a calibration is already in progress.
        }

        var now = DateTimeOffset.UtcNow;
        var record = LoadRecord();
        bool shouldPrompt;

        if (record is null)
        {
            shouldPrompt = true; // rule 3: first run on this machine.
            if (version is not null)
            {
                WriteRecord(new CalibrationRecord
                {
                    Versions = new Dictionary<string, VersionEntry>
                    {
                        [version] = new VersionEntry { PromptFirstSeen = now, Dismissed = false },
                    },
                });
            }
        }
        else if (version is null)
        {
            shouldPrompt = false; // rule 4: unknown version can't establish a change.
        }
        else if (version == record.CalibratedVersion)
        {
            shouldPrompt = false; // rule 5: already calibrated for this exact version. Never
            // coarsened — this is the only rule that may declare a version "still calibrated".
        }
        else if (record.Versions is not null && record.Versions.TryGetValue(version, out var dismissEntry) && dismissEntry.Dismissed)
        {
            shouldPrompt = false; // rule 6: declined for this exact version.
        }
        else if (record.Versions is not null
                 && record.Versions.TryGetValue(version, out var seenEntry)
                 && seenEntry.PromptFirstSeen is DateTimeOffset firstSeen
                 && now - firstSeen > TimeSpan.FromDays(PromptWindowDays))
        {
            shouldPrompt = false; // rule 7: ignored for the window on this exact version; treat as a decline.
        }
        else
        {
            // Rule 8 (SPEC-101 §13.3/§13.4/§13.8): coarsened ONLY here, at the point of decision —
            // rules 5-7 above and the record's own keys always use the exact version. The baseline
            // is calibratedVersion, and ONLY calibratedVersion — a version we have actually
            // reconciled with. Without one, "has it changed since we calibrated?" is unanswerable,
            // and unanswerable must mean keep prompting, never suppress: the alternative (falling
            // back to some other tracked version) makes that version's own entry its baseline on
            // the very next render of itself, silently killing the first-run nudge after one render
            // for every user who is prompted but hasn't confirmed. Rules 6/7 are what bound this
            // case instead.
            if (record.CalibratedVersion is string calibratedVersion && MajorMinor(version) == MajorMinor(calibratedVersion))
            {
                shouldPrompt = false; // same major.minor series as the baseline — don't bother the
                // user again for a patch bump. Accepted loss: a patch that DOES move the chrome
                // budget produces no automatic nudge; manual --calibrate is still always available.
                // Rule 5 is unaffected and still compares exact versions, so nothing is ever
                // declared "still calibrated" that isn't — only the OFFER to recalibrate is lost.
            }
            else
            {
                shouldPrompt = true;

                // §13.3: write only if this exact version has never been seen, so N concurrent
                // panes on the same never-before-seen version write once between them, not once
                // per render.
                record.Versions ??= new Dictionary<string, VersionEntry>();
                if (!record.Versions.ContainsKey(version))
                {
                    record.Versions[version] = new VersionEntry { PromptFirstSeen = now, Dismissed = false };
                    record.Prune();
                    WriteRecord(record);
                }
            }
        }

        if (!shouldPrompt)
        {
            return;
        }

        // §12.4: suppressed (not truncated) below width 50 — the nudge reports a possibly-wrong
        // reserve, so it can be truncated by the exact condition it is reporting.
        if (surfaceWidth is not int width || width < 50)
        {
            return;
        }

        Console.Out.WriteLine("calibrate widths: claude-tui-line --calibrate");
    }

    // SPEC-101 §13.4 addendum: ordinal string equality only — never parse to ints, never order
    // versions. The only question this answers is "did the major.minor series change". Does not
    // strip pre-release/build suffixes on purpose: erring toward an extra nudge is the correct
    // direction, never toward silently suppressing one.
    public static string MajorMinor(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    public static int RunCli(bool json, string? saw, bool noEllipsis, string? set, bool confirm, bool reject, bool cancel, bool status, bool dismiss)
    {
        var subFlagCount = new[] { saw is not null, noEllipsis, set is not null, confirm, reject, cancel, status, dismiss }.Count(b => b);
        if (subFlagCount > 1)
        {
            return WriteError(json, "only one of --saw/--no-ellipsis/--set/--confirm/--reject/--cancel/--status/--dismiss may be given");
        }

        if (status)
        {
            return RunStatus(json);
        }

        if (dismiss)
        {
            return RunDismiss(json);
        }

        if (cancel)
        {
            ClearState();
            Print(json, "cancelled", "calibration cancelled; nothing was written.");
            return 0;
        }

        if (saw is not null)
        {
            return RunSaw(json, saw);
        }

        if (noEllipsis)
        {
            return RunNoEllipsis(json);
        }

        if (set is not null)
        {
            return RunSet(json, set);
        }

        if (confirm)
        {
            return RunConfirm(json);
        }

        if (reject)
        {
            return RunReject(json);
        }

        // Bare --calibrate: (re)start. §6: restarts/overwrites any calibration in progress.
        var state = new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) };
        WriteState(state);
        Print(
            json,
            "ruler",
            "calibration started. Cause a statusline redraw in a live Claude Code session (the " +
            "statusline updates on activity, not on file change), then look at the ruler row and " +
            "report the last digit you can read before it is cut off: " +
            "claude-tui-line --calibrate --saw <digit>. If nothing was cut off: " +
            "claude-tui-line --calibrate --no-ellipsis");
        return 0;
    }

    private static int RunSaw(bool json, string sawArg)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "ruler")
        {
            return WriteError(json, "--saw requires an active ruler-phase calibration; run --calibrate first.");
        }

        if (state.ObservedColumns is not int observedColumns)
        {
            return WriteError(json, "no ruler has rendered yet — cause a statusline redraw in Claude Code first, then retry.");
        }

        if (!int.TryParse(sawArg, out var digit) || digit < 0 || digit > 9)
        {
            return WriteError(json, "--saw requires a single digit 0-9");
        }

        var candidate = ResolveReserveFromDigit(observedColumns, digit);
        state.Phase = "verify";
        state.Candidate = candidate;
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        WriteState(state);

        Print(json, "verify", VerifyInstructionText(candidate));
        return 0;
    }

    // §13.2 (Amendment 2): must contain the literal phrase "different lengths on purpose" (§8.11
    // asserts it) and must NOT print the numeric widths of row A/row B — a number the user can't
    // interpret invites interpretation, and that is exactly what caused a correct reading to be
    // rejected. Widths belong in --status/--json, not here.
    private static string VerifyInstructionText(int candidate) =>
        $"candidate reserve: {candidate}. Two probe rows will appear on your statusline. They are " +
        "different lengths on purpose — row B is exactly one column wider than row A, and that " +
        "one-column difference IS the measurement. Ignore the lengths. Look only at how each row " +
        "ENDS: row A should end in \"A\" (not truncated); row B should end in \"…\" (truncated). " +
        "Cause a statusline redraw, then: both as expected? claude-tui-line --calibrate --confirm. " +
        "Anything else? claude-tui-line --calibrate --reject";

    private static int RunNoEllipsis(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "ruler")
        {
            return WriteError(json, "--no-ellipsis requires an active ruler-phase calibration; run --calibrate first.");
        }

        // §6.5: a reserve of 0 is legitimate but also what a stale state file, a non-redrawn
        // statusline, or a terminal wider than COLUMNS looks like — report it, write nothing.
        ClearState();
        Print(
            json,
            "no-ellipsis",
            "reported reserve: 0. Nothing was written. A reserve of 0 is possible, but this also " +
            "matches a stale calibration, a statusline that never redrew, or a terminal wider than " +
            "COLUMNS — re-run --calibrate if you're not sure this is real.");
        return 0;
    }

    private static int RunSet(bool json, string setArg)
    {
        if (!int.TryParse(setArg, out var value) || value < 0 || value > 9)
        {
            return WriteError(json, "--set requires a digit 0-9");
        }

        var state = LoadState() ?? new CalibrationState();
        state.Phase = "verify";
        state.Candidate = value;
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        WriteState(state);

        Print(json, "verify", $"(set manually) {VerifyInstructionText(value)}");
        return 0;
    }

    private static int RunConfirm(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "verify" || state.Candidate is not int candidate)
        {
            return WriteError(json, "--confirm requires an active verify-phase calibration; run --calibrate first.");
        }

        var (ok, error, oldValue) = WriteChromeReserveToConfig(candidate);
        if (!ok)
        {
            return WriteError(json, error ?? "could not write config");
        }

        ClearState();

        // §5.4/§13.4/§13.8: --confirm implicitly dismisses by setting calibratedVersion — read from
        // the state file's observedVersion (the hook's own record of what it saw), never inferred
        // from the calibration record or the CLI's environment. Left unchanged if the hook never
        // saw a version (rather than guessed) — rule 5 simply can't fire for this pane, costing at
        // most one extra nudge later, never a wrong reserve. §12.6/§13.8: --confirm reads the STATE
        // file (not the record) because this is about a measurement just taken, which only the hook
        // that drew the probes saw — contrast RunDismiss below, which reads the RECORD because it's
        // about prompts already shown, a fact the record already holds.
        var record = LoadRecord() ?? new CalibrationRecord();
        record.CalibratedVersion = state.ObservedVersion ?? record.CalibratedVersion;
        record.CalibratedReserve = candidate;
        WriteRecord(record);

        Print(json, "confirmed", $"chromeReserve: {(oldValue?.ToString() ?? "unset, default 4")} -> {candidate}");
        return 0;
    }

    private static int RunReject(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "verify" || state.Candidate is not int candidate)
        {
            return WriteError(json, "--reject requires an active verify-phase calibration; run --calibrate first.");
        }

        // §6.6: print a diagnosis, never auto-advance the candidate — a failed verify is a broken
        // assumption, and stepping the candidate ±1 would hide exactly the signal this is for.
        ClearState();
        Print(
            json,
            "rejected",
            $"verify failed. Row A truncated: the true reserve is larger than {candidate} — retry with " +
            $"claude-tui-line --calibrate --set {candidate + 1}. Row B NOT truncated: the true reserve is " +
            $"smaller — retry with claude-tui-line --calibrate --set {candidate - 1}. Both wrong, or " +
            "neither row visible: the ellipsis may no longer replace the boundary cell, or the statusline " +
            "did not redraw — see SPEC-98 §2. If you rejected because the two rows looked like different " +
            $"lengths: that is expected — rerun with claude-tui-line --calibrate --set {candidate}");
        return 0;
    }

    private static int RunStatus(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state))
        {
            Print(json, "none", "no calibration in progress.");
            return 0;
        }

        Print(json, state.Phase ?? "unknown", $"phase={state.Phase}, candidate={(state.Candidate?.ToString() ?? "none")}");
        return 0;
    }

    private static int RunDismiss(bool json)
    {
        // §13.3: the CLI can't know which pane's version the user was looking at — with several
        // concurrent Claude Code versions there may genuinely be more than one — so --dismiss marks
        // every entry rather than guessing. A user who typed --dismiss wants the nudge to stop, not
        // to play whack-a-mole across panes. §12.6/§13.8: --dismiss reads the RECORD (not the state
        // file) because this is about prompts already shown, a fact the record already holds —
        // contrast RunConfirm above, which reads the STATE file because it's about a measurement
        // just taken, which only the hook that drew the probes saw.
        var record = LoadRecord();
        if (record?.Versions is not { Count: > 0 } versions)
        {
            Print(json, "nothing-to-dismiss", "no active prompt to dismiss.");
            return 0;
        }

        foreach (var entry in versions.Values)
        {
            entry.Dismissed = true;
        }

        WriteRecord(record);
        Print(json, "dismissed", $"dismissed the calibration prompt for {versions.Count} version(s).");
        return 0;
    }

    private static (bool Ok, string? Error, int? OldValue) WriteChromeReserveToConfig(int newValue)
    {
        var configPath = ConfigPath.ResolveConfigPath();
        if (configPath is null)
        {
            return (false, "no config path could be resolved ($HOME is not set)", null);
        }

        JsonObject obj;
        int? oldValue = null;

        if (File.Exists(configPath))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(configPath);
            }
            catch (Exception ex)
            {
                return (false, $"could not read {configPath}: {ex.Message}", null);
            }

            // §6.4: JsonNode, never a typed model — a typed round-trip silently drops unknown keys.
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(bytes);
            }
            catch (Exception ex)
            {
                return (false, $"{configPath} does not parse ({ex.Message}); fix it before calibrating — refusing to overwrite a config we could not read.", null);
            }

            obj = parsed as JsonObject ?? new JsonObject();
            if (obj["layout"] is JsonObject existingLayout
                && existingLayout["chromeReserve"] is JsonValue existingValue
                && existingValue.TryGetValue<int>(out var existingInt))
            {
                oldValue = existingInt;
            }
        }
        else
        {
            obj = new JsonObject();
        }

        if (obj["layout"] is not JsonObject layout)
        {
            layout = new JsonObject();
            obj["layout"] = layout;
        }

        layout["chromeReserve"] = newValue;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var outBytes = System.Text.Encoding.UTF8.GetBytes(obj.ToJsonString(options));
        ConfigWriter.WriteAtomic(configPath, outBytes);
        return (true, null, oldValue);
    }

    private static bool IsExpired(CalibrationState state) =>
        state.ExpiresAt is DateTimeOffset expires && DateTimeOffset.UtcNow > expires;

    private static CalibrationState? LoadState()
    {
        var path = ConfigPath.ResolveCalibrationStatePath();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize(bytes, CalibrateJsonContext.Default.CalibrationState);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(CalibrationState state)
    {
        var path = ConfigPath.ResolveCalibrationStatePath();
        if (path is null)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, CalibrateJsonContext.Default.CalibrationState);
        ConfigWriter.WriteAtomic(path, bytes);
    }

    private static void ClearState()
    {
        var path = ConfigPath.ResolveCalibrationStatePath();
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // best-effort: a calibration state file is transient by design (§6.1/§6.7's own expiry).
        }
    }

    private static CalibrationRecord? LoadRecord()
    {
        var path = ConfigPath.ResolveCalibrationRecordPath();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var record = JsonSerializer.Deserialize(bytes, CalibrateJsonContext.Default.CalibrationRecord);
            record?.MigrateIfNeeded();
            return record;
        }
        catch
        {
            // §13.3: an unparseable record is treated as absent — deliberately the opposite of
            // §6.4's refuse-to-write rule for the config. The config is user-authored and
            // overwriting it destroys work; the record is tool-owned and its worst-case loss is
            // one extra prompt.
            return null;
        }
    }

    private static void WriteRecord(CalibrationRecord record)
    {
        var path = ConfigPath.ResolveCalibrationRecordPath();
        if (path is null)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, CalibrateJsonContext.Default.CalibrationRecord);
        ConfigWriter.WriteAtomic(path, bytes);
    }

    private static void Print(bool json, string phase, string message)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new CalibrateResultJson(true, phase, message), CalibrateJsonContext.Default.CalibrateResultJson));
        }
        else
        {
            Console.Out.WriteLine($"claude-tui-line: {message}");
        }
    }

    private static int WriteError(bool json, string message)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new CalibrateResultJson(false, "error", message), CalibrateJsonContext.Default.CalibrateResultJson));
        }
        else
        {
            Console.Error.WriteLine($"claude-tui-line: {message}");
        }

        return 2;
    }
}

public sealed class CalibrationState
{
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("observedColumns")]
    public int? ObservedColumns { get; set; }

    // §13.8: a payload with no version must leave this key ABSENT from the file, not present-as-null
    // or present-as-"" — "absent" is the only representation --confirm can trust not to be a guess.
    [JsonPropertyName("observedVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObservedVersion { get; set; }

    [JsonPropertyName("candidate")]
    public int? Candidate { get; set; }
}

public sealed record CalibrateResultJson(bool Ok, string Phase, string Message);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CalibrationState))]
[JsonSerializable(typeof(CalibrationRecord))]
[JsonSerializable(typeof(VersionEntry))]
[JsonSerializable(typeof(Dictionary<string, VersionEntry>))]
[JsonSerializable(typeof(CalibrateResultJson))]
public partial class CalibrateJsonContext : JsonSerializerContext
{
}
