using System.Text.Json;
using System.Text.Json.Serialization;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// 0.27.0 automatic asset loading: PACKAGE_AUTO_LOADED sources carry the donor package (short name, when recovered), its residency proof
// (LIVE_PROVEN / OFFLINE_PROVEN / UNRESOLVED), whether this source was itself live-tested, and the pre-loader observation.
public sealed record ProjectileResidency(string Classification, string Evidence, bool PreloadSupported, string? Reason,
    string? Package = null, string? PackageResidency = null, bool? LiveTested = null, string? ObservedWithoutLoader = null, string? ObservedEvidence = null)
{
    public const string AutoLoaded = "PACKAGE_AUTO_LOADED";
    [System.Text.Json.Serialization.JsonIgnore] public bool AssetsAutoLoaded => Classification == AutoLoaded;
}
public sealed record ProjectileConsumer(string Component, int Offset, string ResourceHash);
public sealed record ProjectileObject(ProjectileSettingsIdentity Identity, IReadOnlyList<ProjectileConsumer> Consumers, string ScalarWriteScope, bool WeaponLocalOverrideProven);
public sealed record ProjectileGuardPolicy(IReadOnlyList<string>? ApprovedClasses);
public sealed record ExplosionSummary(int WeaponsWithExplosions, int ExplosiveProjectileAttacksResolved, int ExplosionSettingsResolved,
    int ExplosionScalarFieldsWritable, int SharedExplosionGroups, int ShrapnelGraphsResolved, int ShrapnelWrites);
public sealed record ExplosionField(string Id, int Offset, string Storage, string Type, string? Unit, JsonElement Value);
public sealed record ExplosionConsumer(string Phase, int ProjectileType);
public sealed record ExplosionDescriptor(int ExplosionType, ProjectileSettingsIdentity Settings, int DamageType, ProjectileSettingsIdentity DamageSettings,
    IReadOnlyList<ExplosionField> Fields, IReadOnlyList<ExplosionConsumer> Consumers, IReadOnlyList<ProjectileSource> PlayerConsumers,
    IReadOnlyList<int> DamageConsumers, bool Shared, string WriteScope, bool Writable, int WritableScalarFields, string? Reason);
public sealed record ExplosionCatalog(int SchemaVersion, string Hd2RuntimeVersion, BuildFingerprints GameFingerprints, CompositionSafety Safety,
    string Feature, ExplosionSummary Summary, IReadOnlyList<ExplosionDescriptor> Explosions);
public sealed record NativeAttachmentIdentity(string OptionId, string AddPath, string Name);
public sealed record AttachmentEffects(double? CapacityRounds, double? StartingMagazines, double? MaxMagazines, double? FullReloadSeconds,
    double? PartialReloadSeconds, double? ErgonomicsDelta, IReadOnlyList<double> ZoomValues, IReadOnlyList<double> MagnificationValues,
    double? VerticalRecoilDeltaPercent, double? HorizontalRecoilDeltaPercent, double? SwayDeltaPercent, double? CooldownRateDegreesCPerSecond,
    double? OverheatsAtDegreesC, double? VerticalSpreadDeltaPercent, double? HorizontalSpreadDeltaPercent);
public sealed record AttachmentOption(string Category, string Name, bool Default, bool CatalogAllowed, bool NativeAllowedRelationshipProven,
    bool OptionIdentityProven, NativeAttachmentIdentity? NativeOption, AttachmentEffects Effects, bool Writable, string Reason);
public sealed record AttachmentCategory(string Category, IReadOnlyList<AttachmentOption> Options);
public sealed record SupportSummary(int CatalogWeapons, int UniqueIdentities, int DuplicateIdentityGroups, int SharedSettingsGroups, bool GuardedAuthoringReady);
public sealed record SupportBranch(int CatalogIndex, string Name, string Kind, string? ParentAttack, IReadOnlyList<string> ChildAttacks,
    JsonElement Charge, string State, JsonElement RuntimeMatch, string? UnresolvedReason);
public sealed record SupportAttack(string Role, string Kind, string? ParentRole, Dictionary<string, JsonElement> ResolvedFields,
    ProjectileSettingsIdentity? ProjectileSettings, ProjectileSettingsIdentity? ExplosionSettings, ProjectileSettingsIdentity? ArcSettings,
    ProjectileSettingsIdentity? DamageInfo, ProjectileSettingsIdentity? BeamSettings, ProjectileSettingsIdentity? SpraySettings,
    ProjectileSettingsIdentity? StatusSettings, JsonElement StatusEffects);
public sealed record SupportOwnershipNode(string Kind, string? Component, string? ResourceHash, string? Package);
public sealed record SupportWeapon(string CatalogIdentity, string Slot, string SourceSection, string WeaponType, IReadOnlyList<string> Traits,
    string Confidence, string IdentityResolution, IReadOnlyList<string> ResourceHashes, IReadOnlyList<SupportBranch> AttackGraph,
    IReadOnlyList<SupportAttack> RuntimeAttacks, IReadOnlyList<SupportOwnershipNode> OwnershipChain, bool BackpackDependent, bool Expendable,
    IReadOnlyList<string> FiringModes, IReadOnlyList<string> SelectableAmmoModes, IReadOnlyList<string> UnresolvedLinks, bool ReadOnlyResolutionReady,
    bool GuardedAuthoringReady, string AuthoringReason, JsonElement ChargeCadence, JsonElement AmmoFeedMagazine, JsonElement Relationships,
    JsonElement Sharedness, JsonElement WeaponComponents);
public sealed record SupportCatalog(int SchemaVersion, string Contract, string Hd2RuntimeVersion, BuildFingerprints GameFingerprints,
    CompositionSafety Safety, SupportSummary Summary, Dictionary<string, SupportWeapon> Weapons, JsonElement SharedSettingsGroups);
public sealed record AdvancedCapabilities(ExplosionCatalog Explosions, SupportCatalog Support);
public interface IAdvancedCapabilitiesReader { AdvancedCapabilities Read(IReadOnlyDictionary<string, byte[]> files, PlayerWeaponCatalog catalog, PlayerWeaponComposition composition); }
public sealed class AdvancedCapabilitiesReader : IAdvancedCapabilitiesReader
{
    public static readonly string[] FileNames = ["ExplosionAuthoringCapabilities.json", "SupportWeaponCapabilities.json", "ProjectileCompositionCapabilities.json", "AttachmentOptionCapabilities.json"];
    public const int MaxBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    public AdvancedCapabilities Read(IReadOnlyDictionary<string, byte[]> files, PlayerWeaponCatalog catalog, PlayerWeaponComposition composition)
    {
        try
        {
            T Read<T>(string name)
            {
                if (!files.TryGetValue(name, out var bytes) || bytes.Length > MaxBytes) throw new InvalidDataException("Missing or oversized published capability: " + name);
                using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 }); MetadataReader.RejectDuplicates(doc.RootElement);
                if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new UnsupportedSdkException("Unsupported capability schema: " + name);
                if (!PublishedArtifactVersion.Matches(name, bytes, doc.RootElement.GetProperty("hd2RuntimeVersion").GetString(), catalog.Hd2RuntimeVersion)) throw new InvalidDataException("Capability version mismatch.");
                return JsonSerializer.Deserialize<T>(bytes, Options)!;
            }
            // In 0.17 these named contracts publish the same payloads as the legacy graph names.
            var projectiles = Read<CompositionGraph<ProjectileWeaponGraph>>(FileNames[2]);
            var attachments = Read<CompositionGraph<MagazineWeaponGraph>>(FileNames[3]);
            bool Same(string a, string b) { using var left = JsonDocument.Parse(files[a]); using var right = JsonDocument.Parse(files[b]); return JsonElement.DeepEquals(left.RootElement, right.RootElement); }
            if (!Same(FileNames[2], PlayerWeaponCompositionReader.FileNames[0]) || !Same(FileNames[3], PlayerWeaponCompositionReader.FileNames[1])) throw new InvalidDataException("Named capability/graph contract mismatch.");
            var explosions = Read<ExplosionCatalog>(FileNames[0]); var support = Read<SupportCatalog>(FileNames[1]);
            void Safety(CompositionSafety s, BuildFingerprints b) { if (s.Writes != 0 || s.ProtectionChanges != 0 || s.FixtureFallback != "disabled" || b != catalog.BuildFingerprints) throw new InvalidDataException("Invalid capability evidence/safety."); }
            Safety(explosions.Safety, explosions.GameFingerprints); Safety(support.Safety, support.GameFingerprints);
            if (support.Contract != "hd2runtime.support_weapon.read_only.v1" || support.Summary.GuardedAuthoringReady || support.Weapons.Count != support.Summary.CatalogWeapons
                || support.Weapons.Any(p => p.Key != p.Value.CatalogIdentity || p.Value.Slot != "support" || p.Value.GuardedAuthoringReady || p.Value.AttackGraph == null || p.Value.RuntimeAttacks == null)) throw new InvalidDataException("Unsupported support weapon contract.");
            if (explosions.Explosions.Count != explosions.Summary.ExplosionSettingsResolved || explosions.Explosions.Select(e => e.ExplosionType).Distinct().Count() != explosions.Explosions.Count
                || explosions.Explosions.Sum(e => e.WritableScalarFields) != explosions.Summary.ExplosionScalarFieldsWritable) throw new InvalidDataException("Explosion summary mismatch.");
            var options = attachments.Weapons.SelectMany(w => w.Categories ?? []).SelectMany(c => c.Options).ToArray();
            if (options.Length != catalog.Summary.Composition!.Magazine.AttachmentOptionsMapped || options.Any(o => o.Writable || o.Effects == null || string.IsNullOrWhiteSpace(o.Name))) throw new InvalidDataException("Unsupported attachment authoring contract.");
            foreach (var e in explosions.Explosions)
            {
                if (e.Fields.Count > 100 || e.PlayerConsumers.Any(p => !catalog.Weapons.Any(w => w.Name == p.Weapon))) throw new InvalidDataException("Invalid explosion identity.");
                foreach (var f in e.Fields) if (!catalog.FieldDefinitions.Any(d => d.Id == f.Id && d.Type == f.Type) || f.Value.ValueKind != JsonValueKind.Number) throw new InvalidDataException("Invalid explosion field.");
            }
            foreach (var weapon in catalog.Weapons)
            {
                var mode = weapon.Fields.Single(f => f.SemanticFieldId == "weapon.default_fire_mode");
                var graph = composition.FireModes.Weapons.Single(w => w.Weapon == weapon.Name);
                if (mode.EnumValues?.Count != 2 || mode.EnumValues.GetValueOrDefault("full_auto").GetInt32() != 1 || mode.EnumValues.GetValueOrDefault("semi_auto").GetInt32() != 2
                    || mode.Editable != graph.Writable || !mode.AllowedValues!.SequenceEqual(graph.AllowedModes!) || !mode.NativeModeVector!.SequenceEqual(graph.NativeModeVector!)
                    || mode.CurrentDefault.GetInt32() != graph.PrimaryFireModeNativeValue || mode.Editable && (mode.WriteKind != "reorder_native_mode_vector" || !mode.AllowedValues!.Contains(1) || !mode.AllowedValues!.Contains(2))) throw new InvalidDataException("Fire-mode capability/vector mismatch.");
                foreach (var f in weapon.Fields.Where(f => f.Domain == "terminal"))
                {
                    var action = composition.TerminalActions.Weapons.Single(w => w.Weapon == weapon.Name).Attacks.Single(a => a.Role == f.ReferenceRole).Actions.Single(a => a.Phase == f.ReferencePhase);
                    if (f.Type != "explosion_reference" || f.Editable != action.Writable || f.CurrentDefault.GetProperty("explosionType").GetInt32() != action.ReferenceType
                        || f.Editable && (action.ReferenceClass != "ExplosionSettings?" || f.NullSentinel != 0)) throw new InvalidDataException("Terminal authoring contract mismatch.");
                }
                foreach (var f in weapon.Fields.Where(f => f.Domain == "explosion" && f.Editable))
                {
                    var explosion = explosions.Explosions.Single(e => e.ExplosionType == f.ExplosionType);
                    var value = explosion.Fields.Single(x => x.Id == f.SemanticTarget);
                    if (!explosion.Writable || value.Type != f.Type || !Generation.WeaponScalar.Equal(f, value.Value, f.CurrentDefault)) throw new InvalidDataException("Explosion authoring baseline mismatch.");
                }
            }
            return new(explosions, support);
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new InvalidDataException("Malformed advanced capability metadata: " + e.Message, e); }
    }
}
