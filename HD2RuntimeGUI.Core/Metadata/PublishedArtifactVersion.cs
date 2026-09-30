using System.Security.Cryptography;

namespace HD2RuntimeGUI.Core.Metadata;

// Some SDK releases republish player-weapon graph artifacts that still carry hd2RuntimeVersion 0.19.0. Only the exact published
// bytes of each release are accepted by digest; every other identity and safety check still applies.
internal static class PublishedArtifactVersion
{
    // 0.20.1 through 0.25.1 reuse the 0.19.0 artifacts unchanged.
    private static readonly Dictionary<string, string> Reused019 = new()
    {
        ["AttachmentOptionCapabilities.json"] = "273f1be69474290fce57d95f449c84da6ec861fd22784a1328e9d67aed4e730b",
        ["ExplosionAuthoringCapabilities.json"] = "e36113c56af909b926a9d9e892527067b652cc3bdb970ab8d76bd84aa406908f",
        ["PlayerWeaponFireModeGraph.json"] = "48d821a6eef0690801fce0cb99f417f8ee60fd26f16b6eb71ad875de9a52be56",
        ["PlayerWeaponHeatCapabilities.json"] = "8f7cb482d8241e0dc5dd218261c6603814fec3477b2b194070bc204debb2c0c3",
        ["PlayerWeaponMagazineOptionGraph.json"] = "273f1be69474290fce57d95f449c84da6ec861fd22784a1328e9d67aed4e730b",
        ["PlayerWeaponProjectileReferenceGraph.json"] = "232a56694fb69eb735c10568f7c4809d29346af844f75ac15a409a33dfaebc83",
        ["PlayerWeaponTerminalActionGraph.json"] = "d6ed08760a526ebb91bc2eace11a94b4f0a455af1b36995d4e63bd341655f2b9",
        ["ProjectileCompositionCapabilities.json"] = "232a56694fb69eb735c10568f7c4809d29346af844f75ac15a409a33dfaebc83",
    };
    // 0.26.0 republishes them (still labelled 0.19.0) regenerated from the 0.26.0 snapshot.
    private static readonly Dictionary<string, string> Reused026 = new()
    {
        ["AttachmentOptionCapabilities.json"] = "de1c4ec89e5271e48ba31dac7422b91fe7bf9de3f75d88eb48287228fa94db55",
        ["ExplosionAuthoringCapabilities.json"] = "7e7492542a9013e68129b5f7b6635ccb06319debf63b7dd8b6a00853b98092aa",
        ["PlayerWeaponFireModeGraph.json"] = "5eccc43e13fc6756b23970a757ccaefb5b43dead51df3f919ddfff070afb291b",
        ["PlayerWeaponHeatCapabilities.json"] = "6579a356d2e69feaf135fb053e95db3be437741bd67ce9981e67e695384358cb",
        ["PlayerWeaponMagazineOptionGraph.json"] = "de1c4ec89e5271e48ba31dac7422b91fe7bf9de3f75d88eb48287228fa94db55",
        ["PlayerWeaponProjectileReferenceGraph.json"] = "e686715ccecc060df0f7e6a8eca6497edcf477520a50662a7c093bcfdf746b57",
        ["PlayerWeaponTerminalActionGraph.json"] = "9390e4282a424b0b28e416ec37706ca8763125b396e5f858b4f5edc88d2fcfc1",
        ["ProjectileCompositionCapabilities.json"] = "e686715ccecc060df0f7e6a8eca6497edcf477520a50662a7c093bcfdf746b57",
    };
    // 0.27.0 republishes the six unchanged graphs byte-for-byte from 0.26.0; the two projectile graphs gain residency/asset-loading data.
    private static readonly Dictionary<string, string> Reused027 = new(Reused026)
    {
        ["PlayerWeaponProjectileReferenceGraph.json"] = "0ae305917112fef0b5808a91989c8f3b72df022b7d117a6edbebfdc2474fb0d0",
        ["ProjectileCompositionCapabilities.json"] = "0ae305917112fef0b5808a91989c8f3b72df022b7d117a6edbebfdc2474fb0d0",
    };
    public static bool Matches(string file, byte[] bytes, string? actual, string expected)
    {
        if (actual == expected) return true;
        if (actual != "0.19.0") return false;
        var pinned = expected switch
        {
            "0.20.1" or "0.21.0" or "0.22.0" or "0.22.1" or "0.23.0" or "0.23.1" or "0.23.2" or "0.24.0" or "0.25.0" or "0.25.1" => Reused019,
            "0.26.0" => Reused026,
            // 0.28.0 and 0.28.1 republish all eight byte-for-byte from 0.27.0.
            "0.27.0" or "0.28.0" or "0.28.1" => Reused027,
            _ => null,
        };
        return pinned != null && pinned.TryGetValue(file, out var digest) && Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest, StringComparison.OrdinalIgnoreCase);
    }
}
