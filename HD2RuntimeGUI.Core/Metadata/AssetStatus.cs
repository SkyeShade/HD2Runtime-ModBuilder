using System.Globalization;
using HD2RuntimeGUI.Core.Localization;

namespace HD2RuntimeGUI.Core.Metadata;

// One compact asset-loading state for a reference swap (SDK 0.27.0+). This answers only "will the replacement's assets be loaded?";
// slot/gameplay compatibility (allow_unverified_reference) is a separate question the editors show next to it.
public enum AssetState
{
    // Vanilla value, or an SDK before 0.27.0: nothing to show.
    None,
    // Runtime loads the package automatically, and that package was loaded in a passing live test.
    LiveVerified,
    // Runtime knows the package and loads it automatically before the write.
    AutoLoaded,
    // The object ships in a package that is always loaded.
    AlwaysResident,
    // Runtime does not know the package, so it cannot load it; the object may appear as a missing asset.
    Unknown,
}

// Label, Short, Description, InGame and FamilyProofText are ModBuilder's UI text in the current UI language (State is the logic value);
// Blocker, Basis, package names and Runtime status tokens are shown as published.
public sealed record AssetStatusView(AssetState State, string Family, AssetReferenceFamily? FamilyProof, string? Package, bool? PackageNamed,
    string? Derivation, bool LiveTested, bool PackageLiveLoaded, string? Blocker, AssetLoadPolicy? Policy)
{
    public string Label => State switch
    {
        AssetState.LiveVerified => CoreText.Get("Asset.Label.LiveVerified"),
        AssetState.AutoLoaded => CoreText.Get("Asset.Label.AutoLoaded"),
        AssetState.AlwaysResident => CoreText.Get("Asset.Label.AlwaysResident"),
        AssetState.Unknown => CoreText.Get("Asset.Label.Unknown"),
        _ => "",
    };
    public string Short => State switch
    {
        AssetState.LiveVerified => CoreText.Get("Asset.Short.LiveVerified"), AssetState.AutoLoaded => CoreText.Get("Asset.Short.AutoLoaded"),
        AssetState.AlwaysResident => CoreText.Get("Asset.Short.AlwaysResident"), AssetState.Unknown => CoreText.Get("Asset.Short.Unknown"), _ => "",
    };
    public bool Warning => State == AssetState.Unknown;
    public string Description => State switch
    {
        AssetState.LiveVerified => CoreText.Get("Asset.Description.LiveVerified"),
        AssetState.AutoLoaded => CoreText.Get("Asset.Description.AutoLoaded"),
        AssetState.AlwaysResident => CoreText.Get("Asset.Description.AlwaysResident"),
        AssetState.Unknown => CoreText.Format("Asset.Description.Unknown", Blocker ?? CoreText.Get("Asset.Description.UnknownPackage")),
        _ => "",
    };
    // What happens in game: the operation waits (waiting_for_assets), then applies; or Runtime rejects it with ASSET_UNAVAILABLE
    // and the original reference stays. Uses Runtime's published load policy.
    public string? InGame => State is AssetState.LiveVerified or AssetState.AutoLoaded && Policy is { } p
        ? CoreText.Format("Asset.InGame", p.LoadTimeoutSeconds.ToString(CultureInfo.InvariantCulture))
        : null;
    public string? FamilyProofText => FamilyProof == null ? null
        : CoreText.Format(FamilyProof.LiveProven ? "Asset.Loader.LiveProven" : "Asset.Loader.Offline", FamilyProof.Basis);
}

public static class AssetStatus
{
    // Drop-pod slot: nothing for the vanilla occupant; otherwise the pickup's own published dependency.
    public static AssetStatusView? Pickup(SdkMetadata sdk, PodRackSlot slot, Pickup p)
    {
        if (sdk.Assets is not { } assets || p.PackageDependency is not { } d) return null;
        var family = assets.Family(AssetDependencyReader.PodPayloadFamily);
        if (slot.Current?.Pickup == p.SemanticId) return View(AssetState.None, AssetDependencyReader.PodPayloadFamily, family, null, assets);
        if (d.AlwaysResident) return View(AssetState.AlwaysResident, AssetDependencyReader.PodPayloadFamily, family, assets.Pickup(p.SemanticId)?.PackageDependency, assets);
        return View(StateOf(assets.Pickup(p.SemanticId)?.PackageDependency), AssetDependencyReader.PodPayloadFamily, family, assets.Pickup(p.SemanticId)?.PackageDependency, assets);
    }
    // Vehicle mount: nothing for the vanilla weapon; otherwise the replacement mounted weapon's dependency.
    public static AssetStatusView? MountedWeapon(SdkMetadata sdk, string currentSemanticId, string replacementSemanticId)
    {
        if (sdk.Assets is not { } assets) return null;
        var family = assets.Family(AssetDependencyReader.MountFamily);
        if (currentSemanticId == replacementSemanticId) return View(AssetState.None, AssetDependencyReader.MountFamily, family, null, assets);
        var d = assets.MountedWeapon(replacementSemanticId)?.PackageDependency;
        return View(StateOf(d), AssetDependencyReader.MountFamily, family, d, assets);
    }
    // Projectile (or, for explosions, the source weapon's projectile) reference: the source attack's published residency.
    public static AssetStatusView? ProjectileSource(SdkMetadata sdk, string weapon, string role, bool baseline, bool explosion = false)
    {
        if (sdk.Assets is not { } assets || sdk.Composition is not { } composition) return null;
        var familyName = explosion ? AssetDependencyReader.ExplosionFamily : AssetDependencyReader.ProjectileFamily;
        var family = assets.Family(familyName);
        if (baseline) return View(AssetState.None, familyName, family, null, assets);
        var r = composition.Attack(weapon, role).Residency;
        var state = r is { AssetsAutoLoaded: true } ? r.LiveTested == true ? AssetState.LiveVerified : AssetState.AutoLoaded : AssetState.Unknown;
        return new(state, familyName, family, r?.Package, r?.Package != null ? true : null, null, r?.LiveTested == true, r?.LiveTested == true,
            state == AssetState.Unknown ? r?.Reason ?? r?.Evidence : null, assets.Policy);
    }
    private static AssetState StateOf(AssetDependency? d) => d is not { Known: true, AutoLoadSupported: true } ? AssetState.Unknown
        : d.PackageLiveLoaded ? AssetState.LiveVerified : AssetState.AutoLoaded;
    private static AssetStatusView View(AssetState state, string family, AssetReferenceFamily? proof, AssetDependency? d, AssetDependencyCatalog assets) =>
        new(state, family, proof, d?.Package, d?.PackageNamed, d?.Derivation, d?.LiveTested == true, d?.PackageLiveLoaded == true, d?.Blocker, assets.Policy);
}
