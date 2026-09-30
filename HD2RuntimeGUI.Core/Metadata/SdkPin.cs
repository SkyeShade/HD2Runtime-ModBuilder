using System.Security.Cryptography;
using System.Text;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>
/// The exact HD2Runtime SDK this ModBuilder release is built and validated against: HD2Runtime 0.28.1. ModBuilder bundles these files; every
/// file it reads is byte-identical in the public release asset (ContentFingerprint), and any SDK with the pinned version whose content differs
/// (a later development build, a local sdk/ folder) is reported as a different build, never silently used as the pin.
/// </summary>
public static class SdkPin
{
    // 0.28.1 publishes the 0.28.0 capabilities unchanged (only their version label differs) plus hd2.diagnostics.operations() in the stubs;
    // Runtime 0.28.1 applies SDK 0.27-era operations by the SDK version each export's wrapper declares.
    public const string Version = "0.28.1";
    // The Runtime commit the SDK was generated at, which is the public v0.28.1 release commit.
    public const string SdkCommit = "96ab2d258d867a5df4f22bb7b3321d84d28de21d";
    public const string RuntimeCommit = "96ab2d258d867a5df4f22bb7b3321d84d28de21d";
    public const int ApiVersion = 1, SchemaVersion = 1;
    // HD2Runtime-0.28.1-sdk.zip, the public release asset (the test fixture sdk-0.28.1.zip).
    public const string ArchiveName = "HD2Runtime-0.28.1-sdk.zip";
    public const string ArchiveSha256 = "02060ea78afe4fd59426cb86b85d087cc247b1a2f06d88a822a4190bb323f7c8";
    // Fingerprint (below) of every SDK file ModBuilder reads, as bundled.
    public const string ContentFingerprint = "eae0c3ecbe687dcde3cbbc1ca1ba4ddc9fba4b9a4e1ad8f61cc5d3b3f1433015";

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
