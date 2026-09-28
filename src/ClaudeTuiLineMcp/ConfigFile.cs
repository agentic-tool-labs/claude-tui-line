using System.Security.Cryptography;
using ClaudeTuiLineShared;

namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-12.6-mcp-tools.md §1.4: reading the config file and hashing its bytes for <c>revision</c>
/// is the server's own file I/O. The atomic writer lives in ClaudeTuiLineShared's
/// <see cref="ClaudeTuiLineShared.ConfigWriter"/> (SPEC-101 §6.3) and is delegated to below; the
/// SHA-256 revision hash below remains MCP-only, since the CLI's calibration writer has no
/// revision concept to preserve.
/// </summary>
internal static class ConfigFile
{
    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §12.6.5/§4 item 3: a hash of the file's BYTES as read, never of the
    /// parsed config — two byte-different files that parse identically must hash differently.
    /// SPEC-V2-FRAMEWORK.md §12.6.9: <c>"absent"</c>, not null and not a missing field, when no
    /// file exists.
    /// </summary>
    public static string ComputeRevision(byte[]? bytes) =>
        bytes is null ? "absent" : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// docs/backup-ledger.md "Writing settings.json" rule 2 / SPEC-V2-FRAMEWORK.md §12.2 rule 3:
    /// atomic temp-file-then-rename, so an interrupted write cannot leave a torn config on disk.
    /// This is the opposite discipline from the ledger append (§9.4) — the two must not be
    /// unified.
    /// </summary>
    public static void WriteAtomic(string path, byte[] bytes) => ConfigWriter.WriteAtomic(path, bytes);
}
