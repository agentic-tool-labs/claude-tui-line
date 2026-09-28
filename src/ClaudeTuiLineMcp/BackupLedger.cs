using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-12.6-mcp-tools.md §9 / docs/backup-ledger.md (normative — that file's procedure wins over
/// this class's comments wherever they'd disagree): writes one full checkpoint entry — every
/// artifact regardless of which one the caller is about to change — before <c>set_config</c>
/// writes the config. Always appends <c>kind: "checkpoint"</c>, never <c>"origin"</c> (§9.3): this
/// class has no code path that can produce the value <c>"origin"</c> at all, which is what makes
/// that ruling hold by construction rather than by discipline.
///
/// §9.7: the backup root (and, for the same "never touch the user's real ~/.claude" reason, the
/// settings.json path) are constructor arguments with no built-in default. Production wiring
/// (<see cref="BackupLedgerFactory"/>) is the only place the real
/// <c>~/.claude/claude-tui-line/backups/</c> and <c>~/.claude/settings.json</c> paths are ever
/// constructed; every test constructs this class directly with temp-directory paths and never
/// goes through the factory. Rule 1 (docs/backup-ledger.md) forbids ever deleting or overwriting
/// anything already written to the backup directory, so a test that reached the real default
/// would leave permanent, undeletable pollution in the user's actual recovery tree.
/// </summary>
public sealed class BackupLedger
{
    private const int MaxAppendAttempts = 5;
    private static readonly TimeSpan AppendRetryBackoff = TimeSpan.FromMilliseconds(40);

    public string BackupRoot { get; }
    public string SettingsPath { get; }

    public BackupLedger(string backupRoot, string settingsPath)
    {
        BackupRoot = backupRoot;
        SettingsPath = settingsPath;
    }

    public static string DefaultBackupRoot(string home) =>
        Path.Combine(home, ".claude", "claude-tui-line", "backups");

    /// <summary>
    /// SPEC-12.6-mcp-tools.md §9.2: the 7-step full entry, ordered so every abortable step comes
    /// before every write step (docs/backup-ledger.md: "a permanent record of a change that never
    /// happened cannot be cleaned up"). <paramref name="configPath"/> must be the same path
    /// resolved via <c>ConfigPath.ResolveConfigPath()</c> that the caller is about to write
    /// (§7.1/§9.2 step 6) — a diverging path here would checkpoint the wrong file.
    /// </summary>
    public CheckpointOutcome WriteCheckpoint(string configPath)
    {
        try
        {
            Directory.CreateDirectory(BackupRoot);
        }
        catch (Exception ex)
        {
            return CheckpointOutcome.Failure(BackupRoot, $"the backup directory could not be created or is not writable: {ex.Message}");
        }

        var ledgerPath = Path.Combine(BackupRoot, "ledger.jsonl");

        // §9.6: only a genuinely unreadable ledger aborts. A torn final line is NOT this case
        // (§12.2.1 rule 3) and must be tolerated silently — this probe only proves the file can be
        // opened and streamed, it never parses or acts on the contents (§9.3 sidesteps the
        // once-ever "origin" decision entirely, so nothing here needs to).
        if (File.Exists(ledgerPath))
        {
            try
            {
                using var probe = new FileStream(ledgerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(probe);
                reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                return CheckpointOutcome.Failure(ledgerPath, $"the ledger could not be read: {ex.Message}");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var isoTimestamp = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var compactTimestamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        JsonNode? statusLineNode = null;
        string? statusLineCommand = null;
        if (File.Exists(SettingsPath))
        {
            try
            {
                var settingsRoot = JsonNode.Parse(File.ReadAllBytes(SettingsPath)) as JsonObject;
                if (settingsRoot is not null && settingsRoot.TryGetPropertyValue("statusLine", out var statusLine))
                {
                    statusLineNode = statusLine?.DeepClone();
                    statusLineCommand = statusLine?["command"]?.GetValue<string>();
                }
            }
            catch (Exception ex)
            {
                return CheckpointOutcome.Failure(SettingsPath, $"settings.json could not be read: {ex.Message}");
            }
        }

        string? settingsCopy = null;
        string? settingsSha256 = null;
        if (File.Exists(SettingsPath))
        {
            try
            {
                (settingsCopy, settingsSha256) = CopyAndHash(SettingsPath, compactTimestamp, "settings.json");
            }
            catch (Exception ex)
            {
                return CheckpointOutcome.Failure(SettingsPath, $"settings.json could not be copied into the backup store: {ex.Message}");
            }
        }

        string? scriptOriginalPath = null;
        string? scriptCopy = null;
        string? scriptSha256 = null;
        if (!string.IsNullOrEmpty(statusLineCommand) && File.Exists(statusLineCommand))
        {
            try
            {
                scriptOriginalPath = statusLineCommand;
                (scriptCopy, scriptSha256) = CopyAndHash(statusLineCommand, compactTimestamp, Path.GetFileName(statusLineCommand));
            }
            catch (Exception ex)
            {
                return CheckpointOutcome.Failure(statusLineCommand, $"the referenced script could not be copied into the backup store: {ex.Message}");
            }
        }

        string? configCopy = null;
        string? configSha256 = null;
        if (File.Exists(configPath))
        {
            try
            {
                (configCopy, configSha256) = CopyAndHash(configPath, compactTimestamp, Path.GetFileName(configPath));
            }
            catch (Exception ex)
            {
                return CheckpointOutcome.Failure(configPath, $"the config could not be copied into the backup store: {ex.Message}");
            }
        }

        var entry = new JsonObject
        {
            ["kind"] = "checkpoint",
            ["timestamp"] = isoTimestamp,
            ["statusLine"] = statusLineNode,
        };

        entry["settingsCopy"] = settingsCopy;
        if (settingsSha256 is not null)
        {
            entry["settingsSha256"] = settingsSha256;
        }

        if (scriptCopy is not null)
        {
            entry["scriptOriginalPath"] = scriptOriginalPath;
            entry["scriptCopy"] = scriptCopy;
            entry["scriptSha256"] = scriptSha256;
        }

        entry["configOriginalPath"] = configPath;
        entry["configCopy"] = configCopy;
        if (configSha256 is not null)
        {
            entry["configSha256"] = configSha256;
        }

        var line = entry.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        try
        {
            AppendLine(ledgerPath, line);
        }
        catch (Exception ex)
        {
            return CheckpointOutcome.Failure(ledgerPath, $"the ledger append could not be completed: {ex.Message}");
        }

        return CheckpointOutcome.Success(line);
    }

    // docs/backup-ledger.md: "Artifact filenames are second-resolution... If the name you are
    // about to write already exists, append a counter (-2, -3) rather than writing over it."
    private string ReserveArtifactPath(string compactTimestamp, string baseName)
    {
        var candidate = Path.Combine(BackupRoot, $"{compactTimestamp}-{baseName}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var counter = 2; ; counter++)
        {
            var alt = Path.Combine(BackupRoot, $"{compactTimestamp}-{counter}-{baseName}");
            if (!File.Exists(alt))
            {
                return alt;
            }
        }
    }

    private (string CopyName, string Sha256Hex) CopyAndHash(string sourcePath, string compactTimestamp, string baseName)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var destPath = ReserveArtifactPath(compactTimestamp, baseName);
        File.WriteAllBytes(destPath, bytes);
        var hashHex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (Path.GetFileName(destPath), hashHex);
    }

    // SPEC-12.6-mcp-tools.md §9.4: one line, minified JSON, exactly one \n terminator, ONE write
    // call (never header-then-body — the crash-atomicity guard), FileShare.None with a mandatory
    // bounded retry (never a named Mutex — unverified cross-process semantics on this platform),
    // and never a whole-file write API. §12.2.1 rule 4: the correct outcome for two concurrent
    // ledger writes is that both entries land, so a lock that fails or skips the second writer
    // instead of retrying it is wrong.
    private static void AppendLine(string path, string line)
    {
        var lineBytes = Encoding.UTF8.GetBytes(line + "\n");
        Exception? lastError = null;

        for (var attempt = 0; attempt < MaxAppendAttempts; attempt++)
        {
            try
            {
                // FileMode.OpenOrCreate + FileShare.None rather than FileMode.Append: a prior
                // writer that died mid-write can leave the file not ending in '\n' (the torn line
                // §12.2.1 rule 3 tells readers to discard). Gluing our entry directly onto that
                // torn tail would merge the two into one unparseable line and lose the entry we
                // are about to write, not just the torn one — so repair the missing newline first,
                // inside the same locked session, before the new entry's own bytes.
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var needsLeadingNewline = false;
                if (stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    needsLeadingNewline = stream.ReadByte() != '\n';
                }

                stream.Seek(0, SeekOrigin.End);
                var payload = needsLeadingNewline
                    ? [.. "\n"u8.ToArray(), .. lineBytes]
                    : lineBytes;
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(AppendRetryBackoff * (attempt + 1));
            }
        }

        throw new IOException(
            $"could not append to the ledger after {MaxAppendAttempts} attempts",
            lastError);
    }
}

/// <summary>
/// SPEC-12.6-mcp-tools.md §9.6: on failure nothing was written — the entry line is empty and the
/// caller must not treat this as a partial checkpoint.
/// </summary>
public sealed record CheckpointOutcome(bool Ok, string EntryJson, string? FailedPath, string? FailedMessage)
{
    public static CheckpointOutcome Success(string entryJson) => new(true, entryJson, null, null);

    public static CheckpointOutcome Failure(string path, string message) => new(false, "", path, message);
}
