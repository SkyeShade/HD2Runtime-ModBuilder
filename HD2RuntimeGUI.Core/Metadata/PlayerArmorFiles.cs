namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>The SDK files of the Helldiver, armor and weapon attachment modifier domains (HD2Runtime 0.30.0 development, b3f42ec and the armor
/// catalogs): their names and size limits. SdkCache reads them whenever present; each domain's reader validates its own file.</summary>
public static class PlayerArmorFiles
{
    public const string Helldiver = "HelldiverFieldCapabilities.json";
    public const string AttachmentModifiers = "WeaponAttachmentModifierCapabilities.json";
    public const string ArmorStats = "ArmorStatsCapabilities.json";
    public const string ArmorPassives = "ArmorPassiveCatalog.json";
    public static readonly IReadOnlyList<string> All = [Helldiver, AttachmentModifiers, ArmorStats, ArmorPassives];
    public static int MaxBytes(string name) => name switch
    {
        Helldiver => 1 * 1024 * 1024,
        AttachmentModifiers => 4 * 1024 * 1024,
        ArmorStats => 8 * 1024 * 1024,
        _ => 2 * 1024 * 1024,
    };
}
