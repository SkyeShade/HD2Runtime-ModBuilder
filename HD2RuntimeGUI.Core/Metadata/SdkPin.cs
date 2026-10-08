using System.Security.Cryptography;
using System.Text;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>
/// The exact HD2Runtime SDK this ModBuilder release is built and validated against: the HD2Runtime 0.30.0 release. ModBuilder bundles these files; every
/// file it reads is byte-identical in the public release asset (ContentFingerprint), and any SDK with the pinned version whose content differs
/// (a later development build, a local sdk/ folder) is reported as a different build, never silently used as the pin.
/// </summary>
public static class SdkPin
{
    // 0.30.0: the HD2Runtime release after 0.28.1 (there is no 0.29), built at its release candidate commit. Its SDK ZIP is the release
    // asset, so once 0.30.0 is published GitHubReleaseClient finds the same version that is already installed. The 0.30.0-dev test SDKs that
    // earlier 1.7.0 builds bundled are SemVer prereleases, never downloaded; a project bound to one keeps it until it is rebound.
    // The eight 0.19.0-labelled graphs are byte-identical to 0.28.1's (PublishedArtifactVersion).
    //
    // Swapping in another SDK (docs/runtime030-integration-plan.md, "Swapping the bundled SDK"; tools/swap-bundled-sdk.py):
    //  1. copy the archive to HD2RuntimeGUI.Tests/Fixtures/sdk-<version>.zip and replace every file in Metadata/Bundled with the archive's
    //     file of the same name (hd2runtime-stubs.lua is stubs/mods/skyeshade/hd2runtime.lua), byte for byte;
    //  2. set Version (and SdkCompatibility.NewestSupportedVersion), the commits, ArchiveName, ArchiveSha256 and ContentFingerprint (SdkPinTests
    //     prints the fingerprint it computes when it differs);
    //  3. for a new version also: PublishedArtifactVersion, Runtime030Tests.Version / Fixture, and the custom content target's files.
    public const string Version = "0.30.0";
    // The Runtime commit the SDK was generated at (the 0.30.0 release candidate; built with scripts/build_release.py).
    public const string SdkCommit = "893d31fdb25076fb3d19f95b8eaba0b7ba3327f5";
    public const string RuntimeCommit = "893d31fdb25076fb3d19f95b8eaba0b7ba3327f5";
    public const int ApiVersion = 1, SchemaVersion = 1;
    // HD2Runtime-0.30.0-sdk.zip of that build (the test fixture sdk-0.30.0.zip).
    public const string ArchiveName = "HD2Runtime-0.30.0-sdk.zip";
    public const string ArchiveSha256 = "a6875afbd144f3874d72e5b28c4baad4adf5671e991b4af5e6a32f1d3075ca42";
    // Fingerprint (below) of every SDK file ModBuilder reads, as bundled.
    public const string ContentFingerprint = "1343fa2b5c500aae96d506a656036587947309243fb922d3ee6d2fe20286766e";

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
