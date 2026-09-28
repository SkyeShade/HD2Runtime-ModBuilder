using System.Security.Cryptography;
namespace HD2RuntimeGUI.Core.Metadata;

// Published 0.20.1 through 0.22.1 archives reuse these 0.19 artifacts byte for byte.
// Permit only these reviewed exact bytes; never accept arbitrary version drift.
internal static class PublishedArtifactVersion
{
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
    public static bool Matches(string file, byte[] bytes, string? actual, string expected) => actual == expected ||
        expected is "0.20.1" or "0.21.0" or "0.22.0" or "0.22.1" && actual == "0.19.0" && Reused019.TryGetValue(file, out var digest)
        && Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest, StringComparison.OrdinalIgnoreCase);
}
