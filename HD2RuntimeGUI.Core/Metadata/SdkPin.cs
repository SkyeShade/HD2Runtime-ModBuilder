using System.Security.Cryptography;
using System.Text;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>
/// The exact HD2Runtime SDK this ModBuilder release is built and validated against: HD2Runtime 0.28.0. ModBuilder bundles these files; every
/// file it reads is byte-identical in the public release asset (ContentFingerprint), and any SDK with the pinned version whose content differs
/// (a later development build, a local sdk/ folder) is reported as a different build, never silently used as the pin.
/// </summary>
public static class SdkPin
{
    public const string Version = "0.28.0";
    // The Runtime commit the SDK was generated at, and the public v0.28.0 release commit: no sdk/ change between them, nor since the release
    // candidate 39aabe3 ModBuilder was integrated on.
    public const string SdkCommit = "e304f26fe0c09e9c059fa6b2b5a2a2d91e81c65c";
    public const string RuntimeCommit = "085acc7cfc6ecccd57c9b8c415d455a92731083d";
    public const int ApiVersion = 1, SchemaVersion = 1;
    // HD2Runtime-0.28.0-sdk.zip from the release candidate (the test fixture). The public asset differs only in build-report.json, Runtime's own
    // build log, which ModBuilder does not read, so the content fingerprint below is the same.
    public const string ArchiveName = "HD2Runtime-0.28.0-sdk.zip";
    public const string ArchiveSha256 = "42b9cac4e0d3328a638b766d70bf04e03e064a357f897bc1f89e0066f188851e";
    // Fingerprint (below) of every SDK file ModBuilder reads, as bundled.
    public const string ContentFingerprint = "5fb31e05970c2b3fa68fee582ce0a0886dd852e81f8cd6a977cd987c1cbabc55";

    /// <summary>SHA-256 over "name:sha256" lines of the consumed files, sorted by name (independent of archive layout and timestamps).</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, byte[]> files)
    {
        var lines = files.OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => f.Key + ":" + Convert.ToHexString(SHA256.HashData(f.Value)).ToLowerInvariant() + "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines)))).ToLowerInvariant();
    }
    /// <summary>Whether an SDK with the pinned version is the pinned build (null for other versions: nothing to compare).</summary>
    public static bool? Matches(string version, string fingerprint) => version == Version ? fingerprint == ContentFingerprint : null;
}
