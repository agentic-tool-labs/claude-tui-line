using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-101 §13.3: keyed by Claude Code version, because N concurrently-running client versions
/// (normal on a machine with several panes open mid-auto-update) are a multi-valued fact and a
/// single `promptedForVersion` slot could only ever hold one of them, racing every other pane that
/// tried to write its own. `calibratedVersion`/`calibratedReserve` stay singular — calibration
/// produces one config value, so one slot is the correct model there.
/// </summary>
public sealed class CalibrationRecord
{
    private const int MaxVersions = 10;

    [JsonPropertyName("calibratedVersion")]
    public string? CalibratedVersion { get; set; }

    [JsonPropertyName("calibratedReserve")]
    public int? CalibratedReserve { get; set; }

    [JsonPropertyName("versions")]
    public Dictionary<string, VersionEntry>? Versions { get; set; }

    // Pre-Amendment-2 shape. Read-only in practice: MigrateIfNeeded folds these into Versions and
    // clears them, so a record written under the new shape never has them set again.
    [JsonPropertyName("promptedForVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromptedForVersion { get; set; }

    [JsonPropertyName("promptFirstSeen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? PromptFirstSeen { get; set; }

    [JsonPropertyName("dismissedVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DismissedVersion { get; set; }

    /// <summary>
    /// §13.3 migration: an old-shape record modeled the multi-valued "which versions have we
    /// prompted for" fact with one slot. Called on every load; never writes by itself — the caller
    /// only persists the migrated shape when the trigger rules call for a write anyway.
    /// </summary>
    public void MigrateIfNeeded()
    {
        if (Versions is not null)
        {
            return;
        }

        Versions = new Dictionary<string, VersionEntry>();

        if (PromptedForVersion is string prompted)
        {
            Versions[prompted] = new VersionEntry
            {
                PromptFirstSeen = PromptFirstSeen ?? DateTimeOffset.UtcNow,
                Dismissed = DismissedVersion == prompted,
            };
        }

        if (DismissedVersion is string dismissed && dismissed != PromptedForVersion)
        {
            Versions[dismissed] = new VersionEntry { PromptFirstSeen = DateTimeOffset.UtcNow, Dismissed = true };
        }

        PromptedForVersion = null;
        PromptFirstSeen = null;
        DismissedVersion = null;
    }

    /// <summary>
    /// §13.3 pruning: without a cap, `versions` grows by one key per Claude Code release forever, in
    /// a file read on every render. Evicts the oldest `promptFirstSeen` entry until back at the cap.
    /// </summary>
    public void Prune()
    {
        if (Versions is null)
        {
            return;
        }

        while (Versions.Count > MaxVersions)
        {
            var oldestKey = Versions
                .OrderBy(kv => kv.Value.PromptFirstSeen ?? DateTimeOffset.MinValue)
                .First().Key;
            Versions.Remove(oldestKey);
        }
    }
}

public sealed class VersionEntry
{
    [JsonPropertyName("promptFirstSeen")]
    public DateTimeOffset? PromptFirstSeen { get; set; }

    [JsonPropertyName("dismissed")]
    public bool Dismissed { get; set; }
}
