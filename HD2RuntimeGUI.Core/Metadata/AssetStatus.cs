using System.Globalization;

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

public sealed record AssetStatusView(AssetState State, string Family, AssetReferenceFamily? FamilyProof, string? Package, bool? PackageNamed,
    string? Derivation, bool LiveTested, bool PackageLiveLoaded, string? Blocker, AssetLoadPolicy? Policy)
{
    public string Label => State switch
    {
        AssetState.LiveVerified => "Assets live-verified",
        AssetState.AutoLoaded => "Assets loaded automatically",
        AssetState.AlwaysResident => "Assets always loaded",
        AssetState.Unknown => "Assets unknown",
        _ => "",
    };
    public string Short => State switch
    {
        AssetState.LiveVerified => "assets live-verified", AssetState.AutoLoaded => "assets auto-load", AssetState.AlwaysResident => "assets always loaded",
        AssetState.Unknown => "assets unknown", _ => "",
    };
    public bool Warning => State == AssetState.Unknown;
    public string Description => State switch
    {
        AssetState.LiveVerified => "HD2Runtime loads the package holding these assets automatically before the write; that package was loaded in a passing live test with nobody carrying the donor item.",
        AssetState.AutoLoaded => "HD2Runtime loads the package holding these assets automatically before the write. Nobody needs to carry the donor item.",
        AssetState.AlwaysResident => "These assets ship in a package the game always has loaded.",
        AssetState.Unknown => "HD2Runtime cannot load these assets automatically: " + (Blocker ?? "the package holding them is not known.")
            + " Unless the item is already loaded in the mission, it may appear as a missing asset (for example a purple placeholder).",
        _ => "",
    };
    // What happens in game: the operation waits (waiting_for_assets), then applies; or Runtime rejects it with ASSET_UNAVAILABLE
    // and the original reference stays. Uses Runtime's published load policy.
    public string? InGame => State is AssetState.LiveVerified or AssetState.AutoLoaded && Policy is { } p
        ? string.Create(CultureInfo.InvariantCulture, $"In game the write waits for the assets (status waiting_for_assets, at most {p.LoadTimeoutSeconds} s), then applies. If they cannot be loaded, Runtime rejects it with ASSET_UNAVAILABLE and the original reference stays in place.")
        : null;
    public string? FamilyProofText => FamilyProof == null ? null
        : (FamilyProof.LiveProven ? "Loader live-proven for this kind of reference" : "Loader proven offline for this kind of reference; not yet live-tested") + ". " + FamilyProof.Basis;
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
