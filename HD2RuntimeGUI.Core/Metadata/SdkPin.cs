using System.Security.Cryptography;
using System.Text;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>
/// The exact HD2Runtime SDK this ModBuilder release is built and validated against: the frozen HD2Runtime 0.28.0 release candidate.
/// ModBuilder bundles these files; the published release asset must be byte-identical (ArchiveSha256), and any SDK with the pinned version
/// whose content differs (a later development build, a local sdk/ folder) is reported as a different build, never silently used as the pin.
/// </summary>
public static class SdkPin
{
    public const string Version = "0.28.0";
    // The Runtime commit the SDK was generated at, and the release-candidate commit (no sdk/ change between them).
    public const string SdkCommit = "e304f26fe0c09e9c059fa6b2b5a2a2d91e81c65c";
    public const string RuntimeCommit = "39aabe3c68dc67aec71e1db796a6f85cd076a40b";
    public const int ApiVersion = 1, SchemaVersion = 1;
    // HD2Runtime-0.28.0-sdk.zip from the release candidate.
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
