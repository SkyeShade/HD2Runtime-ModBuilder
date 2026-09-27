using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record MagazineGraphSummary(int Weapons, int NativeOptionIdentities, int WeaponsWithNativeDefaultOption, int DefaultRelationshipsProven, int PerOptionAmmoOwnersProven, int WritableOptionFields, int SimpleMagazineWeapons, int RoundsFeedWeapons, int? PrimaryWeaponsWithAttachments = null, int? AttachmentOptionsMapped = null, int? AttachmentOptionsTotal = null,
    int? MagazineOptionsMapped = null, int? MagazineOptionsTotal = null, int? MagazineOptionsWithNativeIdentity = null, int? WeaponsWithWritablePerOptionFields = null,
    int? OpticsMapped = null, int? UnderbarrelMapped = null, int? MuzzleMapped = null, int? CompleteCustomizationRecordsCompared = null);
public sealed record ProjectileGraphSummary(int Weapons, int WeaponsWithProjectileAttack, int ProjectileAttacks, int WritableTargetAttacks, int CompatibleSourceAttacks, int? WritableExplosiveSelectors = null, int? SharedProjectileGroups = null, Dictionary<string, int>? CompatibilityClasses = null);
public sealed record FireModeGraphSummary(int Weapons, int NativePrimaryValueReadable, int AllowedModeListsProven, int WritableWeapons);
public sealed record TerminalGraphSummary(int Weapons, int ProjectileAttacks, int ReadableActions, int WritableActions, int ImpactExplosionLinks, int ExpiryExplosionLinks, int? WritableImpactRefs = null, int? WritableExpiryRefs = null);
public sealed record CompositionSummary(MagazineGraphSummary Magazine, ProjectileGraphSummary Projectile,
    [property: JsonPropertyName("fire_mode")] FireModeGraphSummary FireMode, TerminalGraphSummary Terminal, ExplosionSummary? Explosion = null);
public sealed record ProjectileSettingsIdentity(int Group, int RecordType, int Row, string SettingsType);
public sealed record ProjectileBaseline(string Weapon, string Attack, int ProjectileType);
public sealed record ProjectileSource(string Weapon, string Role, int ProjectileType);
public sealed record ProjectileAttack(string Role, int ProjectileType, ProjectileSettingsIdentity? ProjectileSettings,
    string CompatibilityClass, FieldBacking? TargetBacking, bool SourceIdentityResolvable, bool TargetOwnershipProven, bool WritableReferenceSwap, string? Reason, ProjectileObject? ProjectileObject = null, ProjectileResidency? Residency = null);
public sealed record ProjectileWeaponGraph(string Weapon, IReadOnlyList<string> Resources, string Resolution, IReadOnlyList<string> ImplementationFamilies, IReadOnlyList<ProjectileAttack> Attacks);
public sealed record MagazineOptionGraph(string OptionId, string Name, string AddPath, AmmoValues BackingBaseValues, AmmoValues Values,
    bool Writable, string Reason, IReadOnlyList<string> SharedWithWeapons, bool Shared, bool Default, bool Allowed,
    bool OptionIdentityProven, bool AllowedRelationshipProven, bool AmmoValueOwnerProven, string BackingOwner, int DefaultEntryOffset);
public sealed record ObservedMagazineOption(string OptionId, string Name, string AddPath, IReadOnlyList<int> OptionIdOffsets, IReadOnlyList<int> AddPathOffsets);
public sealed record MagazineWeaponGraph(string Weapon, bool OrdinaryWritesBlocked, string? BackingDomain, bool SimpleMagazineApi,
    MagazineOptionGraph? DefaultOption, AlternateMagazineOptions AlternateOptions, EffectiveAmmoCapacity EffectiveCapacity, AmmoFields Fields,
    IReadOnlyList<ObservedMagazineOption> ObservedCustomizationOptions, int? AttachmentCount = null, IReadOnlyList<AttachmentCategory>? Categories = null);
public sealed record FireModeWeaponGraph(string Weapon, int PrimaryFireModeNativeValue, FieldBacking Backing,
    IReadOnlyList<int>? AllowedModes, string DefaultModeSemantics, int? SelectedRuntimeMode, bool Writable, string? Reason, IReadOnlyList<int>? NativeModeVector = null, string? WriteKind = null);
public sealed record TerminalAction(string Phase, int ReferenceType, string ActionKind, bool LinkedExplosionRecord, bool Readable, bool Writable, string? Reason, string? ReferenceClass = null, int? NullSentinel = null, IReadOnlyList<ProjectileConsumer>? ProjectileSettingsConsumers = null, bool? AffectsMultipleResources = null);
public sealed record TerminalAttack(string Role, int ProjectileType, ProjectileSettingsIdentity? ProjectileSettings, IReadOnlyList<TerminalAction> Actions);
public sealed record TerminalWeaponGraph(string Weapon, string Resolution, IReadOnlyList<TerminalAttack> Attacks);
public sealed record CompositionSafety(int Writes, int ProtectionChanges, string FixtureFallback, bool SnapshotOnly);
public sealed record CompositionGraph<T>(int SchemaVersion, string Hd2RuntimeVersion, string SourceSnapshot, BuildFingerprints GameFingerprints,
    int CatalogWeapons, string Feature, CompositionSafety Safety, IReadOnlyList<T> Weapons, IReadOnlyList<ProjectileSource>? CompatibleSources,
    IReadOnlyList<NativeMagazineOption>? NativeMagazineOptions, Dictionary<string, IReadOnlyList<ProjectileSource>>? CompatibleSourcesByClass = null,
    ProjectileGuardPolicy? GuardPolicy = null);
public sealed record PlayerWeaponComposition(CompositionGraph<ProjectileWeaponGraph> Projectiles, CompositionGraph<MagazineWeaponGraph> Magazines,
    CompositionGraph<FireModeWeaponGraph> FireModes, CompositionGraph<TerminalWeaponGraph> TerminalActions)
{
    public ProjectileAttack Attack(string weapon, string role) => Projectiles.Weapons.SingleOrDefault(w => w.Weapon == weapon)?.Attacks.SingleOrDefault(a => a.Role == role)
        ?? throw new InvalidDataException("Projectile attack is unavailable: " + weapon + " / " + role);
}
public interface IPlayerWeaponCompositionReader { PlayerWeaponComposition Read(IReadOnlyDictionary<string, byte[]> files, PlayerWeaponCatalog catalog); }
public sealed class PlayerWeaponCompositionReader : IPlayerWeaponCompositionReader
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly string[] FileNames = ["PlayerWeaponProjectileReferenceGraph.json", "PlayerWeaponMagazineOptionGraph.json", "PlayerWeaponFireModeGraph.json", "PlayerWeaponTerminalActionGraph.json"];
    // Graph reports also carry research findings/layout annotations. Read the stable typed
    // authoring surface, validating its envelope and relationships independently.
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    public PlayerWeaponComposition Read(IReadOnlyDictionary<string, byte[]> files, PlayerWeaponCatalog catalog)
    {
        try
        {
            CompositionGraph<T> ReadGraph<T>(int index, string feature, Func<T, string> name)
            {
                if (!files.TryGetValue(FileNames[index], out var bytes) || bytes.Length > MaxBytes) throw new InvalidDataException("Missing or oversized composition graph: " + FileNames[index]);
                using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
                var g = JsonSerializer.Deserialize<CompositionGraph<T>>(bytes, Options)!;
                if (g.SchemaVersion != 1) throw new UnsupportedSdkException("Unsupported composition graph schema.");
                if (g.Hd2RuntimeVersion != catalog.Hd2RuntimeVersion || g.Feature != feature || g.GameFingerprints != catalog.BuildFingerprints || g.SourceSnapshot != catalog.SourceSnapshot
                    || g.CatalogWeapons != catalog.Weapons.Count || g.Safety.Writes != 0 || g.Safety.ProtectionChanges != 0 || g.Safety.FixtureFallback != "disabled" || !g.Safety.SnapshotOnly
                    || !g.Weapons.Select(name).Order().SequenceEqual(catalog.Weapons.Select(w => w.Name).Order())) throw new InvalidDataException("Composition graph identity or safety mismatch.");
                return g;
            }
            var modern = catalog.Summary.Composition?.Explosion != null;
            var result = new PlayerWeaponComposition(ReadGraph<ProjectileWeaponGraph>(0, "projectile_reference_graph", w => w.Weapon),
                ReadGraph<MagazineWeaponGraph>(1, modern ? "attachment_option_capabilities" : "magazine_option_graph", w => w.Weapon), ReadGraph<FireModeWeaponGraph>(2, "fire_mode_graph", w => w.Weapon),
                ReadGraph<TerminalWeaponGraph>(3, "projectile_terminal_action_graph", w => w.Weapon));
            foreach (var w in result.Projectiles.Weapons)
            {
                var weapon = catalog.Weapon(w.Weapon);
                if (w.Resolution != weapon.Resolution || !w.Resources.SequenceEqual(weapon.Resources) || w.Attacks.Select(a => a.Role).Distinct().Count() != w.Attacks.Count) throw new InvalidDataException("Invalid composition weapon identity.");
                foreach (var a in w.Attacks)
                {
                    if (!Regex.IsMatch(a.Role, "\\A[a-z][a-z_0-9]{0,63}\\z")) throw new InvalidDataException("Invalid attack role.");
                    var f = weapon.Fields.SingleOrDefault(f => f.Domain == "attack" && f.ReferenceRole == a.Role);
                    if (f == null || f.Editable != a.WritableReferenceSwap || f.CompatibilityClass != a.CompatibilityClass) throw new InvalidDataException("Projectile graph/capability mismatch.");
                    var baseline = f.CurrentDefault.Deserialize<ProjectileBaseline>(Options)!;
                    if (baseline.Weapon != w.Weapon || baseline.Attack != a.Role || baseline.ProjectileType != a.ProjectileType || f.ReferenceSettings != a.ProjectileSettings) throw new InvalidDataException("Projectile baseline mismatch.");
                    if (a.WritableReferenceSwap && (weapon.OrdinaryWritesBlocked || !f.WriteAccepted || f.AffectsMultipleWeapons || !a.SourceIdentityResolvable || !a.TargetOwnershipProven
                        || a.TargetBacking?.UniqueOwner != true || a.ProjectileSettings == null || !(modern ? result.Projectiles.GuardPolicy?.ApprovedClasses?.Contains(a.CompatibilityClass) == true : a.CompatibilityClass == "conventional_plain"))) throw new InvalidDataException("Unsafe projectile selector.");
                }
            }
            var sources = result.Projectiles.CompatibleSources ?? result.Projectiles.CompatibleSourcesByClass?.Values.SelectMany(s => s).ToArray() ?? throw new InvalidDataException("Missing compatible source contract.");
            if (sources.Select(s => (s.Weapon, s.Role)).Distinct().Count() != sources.Count) throw new InvalidDataException("Duplicate projectile sources.");
            foreach (var s in sources)
            {
                var a = result.Attack(s.Weapon, s.Role);
                if (!a.SourceIdentityResolvable || !(modern ? result.Projectiles.GuardPolicy?.ApprovedClasses?.Contains(a.CompatibilityClass) == true : a.CompatibilityClass == "conventional_plain") || a.ProjectileType != s.ProjectileType || catalog.Weapon(s.Weapon).OrdinaryWritesBlocked) throw new InvalidDataException("Unsafe compatible source.");
            }
            if (!modern && (result.Magazines.Weapons.Any(w => w.DefaultOption?.Writable == true) || result.FireModes.Weapons.Any(w => w.Writable)
                || result.TerminalActions.Weapons.SelectMany(w => w.Attacks).SelectMany(a => a.Actions).Any(a => a.Writable || a.Phase is not ("impact" or "expiry")))) throw new UnsupportedSdkException("Unsupported writable inspection graph.");
            var summary = catalog.Summary.Composition ?? throw new InvalidDataException("Missing composition summary.");
            var attacks = result.Projectiles.Weapons.SelectMany(w => w.Attacks).ToArray();
            var actions = result.TerminalActions.Weapons.SelectMany(w => w.Attacks).SelectMany(a => a.Actions).ToArray();
            foreach (var w in result.TerminalActions.Weapons)
            {
                if (w.Resolution != catalog.Weapon(w.Weapon).Resolution || !w.Attacks.Select(a => a.Role).Order().SequenceEqual(result.Projectiles.Weapons.Single(p => p.Weapon == w.Weapon).Attacks.Select(a => a.Role).Order())) throw new InvalidDataException("Terminal graph attack mismatch.");
                foreach (var a in w.Attacks)
                {
                    if (a.ProjectileType != result.Attack(w.Weapon, a.Role).ProjectileType || a.Actions.Count != 2 || a.Actions.Select(x => x.Phase).Distinct().Count() != 2
                        || a.Actions.Any(x => x.LinkedExplosionRecord != (x.ActionKind == "explosion") || x.ActionKind == "none" && x.ReferenceType != 0)) throw new InvalidDataException("Invalid terminal action evidence.");
                }
            }
            if (summary.Projectile.ProjectileAttacks != attacks.Length || summary.Projectile.WritableTargetAttacks != attacks.Count(a => a.WritableReferenceSwap)
                || summary.Projectile.CompatibleSourceAttacks != sources.Count || summary.Terminal.ReadableActions != actions.Count(a => a.Readable)
                || summary.Magazine.NativeOptionIdentities != result.Magazines.NativeMagazineOptions?.Count
                || summary.Terminal.ImpactExplosionLinks != actions.Count(a => a.Phase == "impact" && a.LinkedExplosionRecord)
                || summary.Terminal.ExpiryExplosionLinks != actions.Count(a => a.Phase == "expiry" && a.LinkedExplosionRecord)
                || summary.Magazine.WeaponsWithNativeDefaultOption != result.Magazines.Weapons.Count(w => w.DefaultOption != null)) throw new InvalidDataException("Composition summary mismatch.");
            return result;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new InvalidDataException("Malformed composition metadata: " + e.Message, e); }
    }
}
