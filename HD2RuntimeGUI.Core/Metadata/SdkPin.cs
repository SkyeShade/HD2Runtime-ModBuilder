using System.Security.Cryptography;
using System.Text;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>
/// The exact HD2Runtime SDK this ModBuilder release is built and validated against: the HD2Runtime 0.30.3 release. ModBuilder bundles these files; every
/// file it reads is byte-identical in the public release asset (ContentFingerprint), and any SDK with the pinned version whose content differs
/// (a later development build, a local sdk/ folder) is reported as a different build, never silently used as the pin.
/// </summary>
public static class SdkPin
{
    // 0.30.3 (ModBuilder 1.7.0 bundled 0.30.0; 0.30.1 changed no SDK content; 0.30.2 was bundled by the 1.7.x SDK move, and 0.30.3 changed only
    // version strings and the custom stratagem uses range), built at its release candidate commit; the v0.30.3 tag adds only migration
    // records. Its SDK ZIP is the published release asset, so GitHubReleaseClient finds the same version that is already installed.
    // Projects bound to 0.30.0, 0.30.1, 0.30.2 or the 0.30.0-dev test SDKs (SemVer prereleases, never downloaded) keep their SDK until rebound.
    // 0.30.2 regenerated the eight player-weapon graphs and labelled them 0.30.2; 0.30.3 labels the same graphs 0.30.3 (PublishedArtifactVersion).
    //
    // Swapping in another SDK (docs/runtime030-integration-plan.md, "Swapping the bundled SDK"; tools/swap-bundled-sdk.py):
    //  1. copy the archive to HD2RuntimeGUI.Tests/Fixtures/sdk-<version>.zip and replace every file in Metadata/Bundled with the archive's
    //     file of the same name (hd2runtime-stubs.lua is stubs/mods/skyeshade/hd2runtime.lua), byte for byte;
    //  2. set Version (and SdkCompatibility.NewestSupportedVersion), the commits, ArchiveName, ArchiveSha256 and ContentFingerprint (SdkPinTests
    //     prints the fingerprint it computes when it differs);
    //  3. for a new version also: PublishedArtifactVersion, Runtime030Tests.Version / Fixture, and the custom content target's files.
    public const string Version = "0.30.3";
    // The Runtime commit the SDK was generated at (the 0.30.3 release candidate; built with scripts/build_release.py).
    public const string SdkCommit = "7da218b53d20ab74c70cd77deb521397b7854e2d";
    public const string RuntimeCommit = "7da218b53d20ab74c70cd77deb521397b7854e2d";
    public const int ApiVersion = 1, SchemaVersion = 1;
    // HD2Runtime-0.30.3-sdk.zip of that build, the published release asset (the test fixture sdk-0.30.3.zip).
    public const string ArchiveName = "HD2Runtime-0.30.3-sdk.zip";
    public const string ArchiveSha256 = "f1f53735470328968fab658427188b93ba8f49d925f0a6e469b30e2ae997af9d";
    // Fingerprint (below) of every SDK file ModBuilder reads, as bundled.
    public const string ContentFingerprint = "491a89a90717841ecbba19cfcea213f6e3b90d11eb01afffab0d018027d72ede";

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
