using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.23.0+ guarded vehicle (hd2.vehicle) and backpack (hd2.backpack) authoring catalogs.
// Every control comes from a published canonical field instance; mount replacements come only from published allowed values.
public sealed record EntityTarget(string Resource, string Path, string? Vehicle = null, string? Backpack = null, string? Zone = null, string? Mount = null,
    string? Attachment = null, string? Booster = null,
    // 0.26.0 vehicle weapons (weapon key + attack role) and drop-pod racks (rack name + slot). Omitted when absent, so evidence hashes of
    // older targets stay byte-identical.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Weapon = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Attack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Slot = null,
    // 0.27.0 throwables (published name) and a throwable status effect's key (for example "fire").
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Throwable = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Effect = null,
    // Enemies and enemy structures (0.28.0 development SDKs): the class's semantic ID and the native class name Lua addresses it by.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Enemy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EnemyClass = null,
    // 0.28.0: an entity a backpack deploys or projects ("drone" for Guard Dogs, "energy_shield" for the SH-51), reached through the
    // backpack (hd2.backpack(name):drone()). Fields on it target path "linked" or its own damage zones.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Linked = null)
{
    // Vehicle or backpack name, the magazine attachment semantic ID (weapon_attachment, 0.23.1+), the booster name (booster, 0.24.0+),
    // the vehicle weapon key (vehicle_weapon, 0.26.0), the rack name (pod_rack, 0.26.0), the throwable name (0.27.0) or the enemy
    // class semantic ID (enemy / structure).
    [JsonIgnore] public string Entity => Vehicle ?? Backpack ?? Attachment ?? Booster ?? Weapon ?? Rack ?? Throwable ?? Enemy ?? "";
}
// Whether a write takes effect (unreleased Runtime 0.28.0 development SDKs): the active source status, when it applies, and for a dormant
// member the field that is active instead.
public sealed record FieldEffect(string? ActiveSource = null, bool? ActiveSourceProven = null, string? AppliesWhen = null, bool? InstantiationOnly = null,
    string? ActiveField = null, string? DamageRule = null, string? Reason = null)
{
    public const string Dormant = "DORMANT_OR_METADATA";
}
public sealed record EntityEvidence(string Tier, string? ReferenceMod = null, string? Proof = null, string[]? ProvenOn = null, bool? SharedTypedSchema = null,
    string? NativeOwner = null, string? GameplayWriteEffect = null);
public sealed record EntityConsumer(string? Vehicle = null, string? Backpack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Enemy = null)
{
    [JsonIgnore] public string Name => Vehicle ?? Backpack ?? Enemy ?? "";
}
// 0.25.0+: a published safe value range (inclusive); Runtime rejects writes outside it.
public sealed record EntityRange(double Min, double Max, bool Integer, string? Reason = null);
public sealed record EntityField(string InstanceKey, string SemanticFieldId, string DisplayName, string Type, string? Unit, JsonElement CurrentDefault,
    bool Editable, string? Reason, EntityTarget Target, string BackingObjectId, string OperationGroup, string PlanGroup, string Requires,
    bool AllowSharedRequired, bool Shared, EntityConsumer[] SharedConsumers, string SharedScopeKey, bool ReviewedScopeComplete,
    bool DynamicConsumersPossible, string BackingObjectKind, string Domain, string ApiFieldConstant, int PlanPhase, string[] DependsOn,
    EntityEvidence Evidence, string Provenance, string[]? AllowedValues = null, string? Acknowledgement = null, string? ValueKind = null,
    string? ResidencyWarning = null, EntityRange? Range = null, double? Min = null, double? Max = null, string? AcknowledgementReason = null, string? UiGroup = null,
    // Unreleased Runtime (0.28.0 development): why a published bound exists (for example a 10-bit network field), the hit actors a damage
    // zone lists, and whether/when a write takes effect (effect.activeSource, appliesWhen, the field that is active instead).
    string? RangeReason = null, string[]? ZoneActors = null, FieldEffect? Effect = null,
    // 0.28.0: the native value each published value was matched against (entity, native, published).
    EntityCorrelation[]? Correlations = null)
{
    public const string ReferenceType = "mounted_weapon_reference";
    // 0.26.0 drop-pod slot payload: a reviewed pickup semantic ID or 'empty'.
    public const string PickupType = "pickup_reference";
    // A status reference (0.28.0 development SDKs) is never authored by this build: it is published read-only with a reason.
    [JsonIgnore] public bool IsStatusReference => Type == WeaponCapability.StatusReference;
    [JsonIgnore] public bool IsReference => Type == ReferenceType;
    [JsonIgnore] public bool IsPickup => Type == PickupType;
    // A value chosen from a published list (mount weapon or pickup) rather than typed.
    [JsonIgnore] public bool IsChoice => IsReference || IsPickup;
    // Safe range: 0.25.0 boosters publish "range"; 0.26.0 attachment and backpack-ammo fields publish "min"/"max".
    [JsonIgnore] public EntityRange? EffectiveRange => Range ?? (Min == null && Max == null ? null
        : new EntityRange(Min ?? double.NegativeInfinity, Max ?? double.PositiveInfinity, Type == "integer", RangeReason));
}
public sealed record EntityCorrelation(string? Entity, JsonElement Native, JsonElement Published);
public sealed record EntityBackingObject(string BackingObjectId, string Kind, bool Shared, EntityConsumer[] SharedConsumers, string SharedScopeKey, string[] FieldInstances);
public sealed record EntityOperationGroup(string OperationGroup, string BackingObjectId, EntityTarget Target, string[] FieldInstances, string RecommendedApi, bool AllowSharedRequired);
public sealed record EntityCallIn(bool Known, string? SemanticId = null, string? Name = null, string? Relationship = null, string? Provenance = null, string? Reason = null);
public sealed record EntityBlocked(string Field, string Reason);
public sealed record EntityAudit(int InternalInstances, int PublishedInstances, int MissingInstances, int UnexpectedInstances, bool ExactMatch);

public sealed record VehicleZoneValues(JsonElement Armor, JsonElement Health, JsonElement Constitution, JsonElement AffectsMainHealth);
public sealed record VehicleZone(string ZoneId, int Index, string SemanticId, string? Name, bool NameResolved, VehicleZoneValues Values,
    string[] ChildZones, int UnresolvedChildLinks, int ActorCount, string[] FieldInstanceKeys)
{
    [JsonIgnore] public string Label => NameResolved && Name != null ? Name.Replace('_', ' ') : "Zone " + Index;
}
public sealed record VehicleDurability(JsonElement MainHealth, JsonElement MainArmor, string[] FieldInstanceKeys, int PopulatedZones, int UnpopulatedZoneSlots, VehicleZone[] Zones);
public sealed record MountOccupant(string SemanticId, string DisplayName, string AttackFamily);
public sealed record VehicleMount(string MountId, int SlotIndex, string? Role, string? RoleSource, string? AttachNode, int? MountSide, string SemanticId,
    string CurrentKind, MountOccupant? Current, bool Swappable, string[] AllowedReplacements, string? BlockedReason = null)
{
    [JsonIgnore] public string Label => (Role ?? "slot " + SlotIndex).Replace('_', ' ');
}
public sealed record Vehicle(string Name, string SemanticId, string CatalogSource, string? VehicleClass, EntityCallIn CallInStratagem,
    VehicleDurability Durability, VehicleMount[] Mounts, EntityBlocked[] BlockedFields, string[] FieldInstanceKeys);
public sealed record MountedWeaponReference(string? Vehicle, string? VehicleSemanticId, string? MountId, bool CatalogVehicle);
public sealed record MountedWeapon(string SemanticId, string DisplayName, string DisplayNameSource, bool NativePathKnown, string AttackFamily, bool Turret,
    string[] Components, string PackageGroup, MountedWeaponReference[] ReferencedBy, int ReferenceCount, bool ReferencedByCatalogVehicle, string Provenance);
public sealed record VehicleSummary(int Vehicles, int StratagemVehicles, int NativeOnlyVehicles, int FieldInstances, int WritableFieldInstances,
    int PopulatedZones, int ZoneFieldInstances, int MountSlots, int SwappableMountSlots, int NonWeaponMountSlots, int DiscoveredMountedWeapons,
    Dictionary<string, int> MountedWeaponsByFamily, Dictionary<string, int> WritableByTier);
public sealed record VehicleReferenceContract(string Field, string ValueKind, string Acknowledgement, string Compatibility, bool ArbitraryIdentifiersAccepted, bool RawNativeIdentifiersPublished);
public sealed record VehicleCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string CanonicalCollection, Vehicle[] Vehicles,
    MountedWeapon[] MountedWeapons, EntityField[] FieldInstances, EntityBackingObject[] BackingObjects, EntityOperationGroup[] OperationGroups,
    VehicleReferenceContract ReferenceContract, Dictionary<string, string> EvidenceTiers, VehicleSummary Summary, EntityAudit InstanceAudit)
{
    public Vehicle? Find(string name) => Vehicles.FirstOrDefault(v => v.Name == name);
    public MountedWeapon? Weapon(string semanticId) => MountedWeapons.FirstOrDefault(w => w.SemanticId == semanticId);
}
public sealed record BackpackSettingGroup(string Group, string[] FieldInstanceKeys, string? Linked = null);
// 0.28.0: an entity the backpack deploys or projects (a Guard Dog drone, the SH-51 energy shield), with its own fields and damage zones.
// A Guard Dog's weapon is a vehicle weapon whose carrier is this backpack (VehicleWeaponCapabilities).
public sealed record BackpackLinkedEntity(string Linked, string Relationship, string[] Chain, string? WeaponFamily, BackpackZone[]? DamageZones, string[] FieldInstanceKeys)
{
    public const string Drone = "drone", EnergyShield = "energy_shield";
}
// 0.26.0 backpack-fed support weapons: the weapon this backpack's deposit supplies (support weapon -> ammoBackpack, reverse link).
public sealed record BackpackFeeds(string SupportWeapon, string SupportWeaponSemanticId, string Relationship, int AmmoMode, string InventorySlot,
    string RefillStyle, bool WeaponOwnsMagazine, string[] Chain);
public sealed record BackpackAmmoBox(JsonElement Value, bool Writable, string? Reason);
public sealed record BackpackAmmo(int Capacity, int StartAmount, int RefillAmount, BackpackAmmoBox? FromAmmoBox);
// A backpack's published damage zones (0.28.0 development SDKs), for example the SH-20 Ballistic Shield's "shield" plate.
public sealed record BackpackZone(string ZoneId, int Index, string? Name)
{
    [JsonIgnore] public string Label => Name ?? ZoneId;
}
public sealed record Backpack(string Name, string SemanticId, EntityCallIn CallInStratagem, string[] DeliveryChain, string[] Components,
    BackpackSettingGroup[] SettingGroups, EntityBlocked[] BlockedFields, string[] FieldInstanceKeys, BackpackFeeds? Feeds = null, BackpackAmmo? Ammo = null,
    BackpackZone[]? DamageZones = null, BackpackLinkedEntity[]? LinkedEntities = null)
{
    public const string AmmoGroup = "backpack_ammo";
    public BackpackLinkedEntity? Linked(string linked) => LinkedEntities?.FirstOrDefault(l => l.Linked == linked);
}
public sealed record BackpackSummary(int Backpacks, int FieldInstances, int WritableFieldInstances, int ReadOnlyFieldInstances, int BackpacksWithWritableFields,
    Dictionary<string, int> WritableByTier, int RackChainsResolved);
public sealed record BackpackCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string CanonicalCollection, Backpack[] Backpacks,
    EntityField[] FieldInstances, EntityBackingObject[] BackingObjects, EntityOperationGroup[] OperationGroups, Dictionary<string, string> EvidenceTiers,
    BackpackSummary Summary, EntityAudit InstanceAudit)
{
    public Backpack? Find(string name) => Backpacks.FirstOrDefault(b => b.Name == name);
}

// Validated catalogs plus the stratagem call-in joins (by semantic ID only, both directions).
public sealed class EntityAuthoring
{
    public required VehicleCatalog Vehicles { get; init; }
    public required BackpackCatalog Backpacks { get; init; }
    // Stratagem root name -> ("vehicle"|"backpack", entity name) for validated call-in links.
    public required IReadOnlyDictionary<string, (string Resource, string Entity)> CallIns { get; init; }
    // Magazine attachment definitions (hd2.weapon_attachment, SDK 0.23.1+); null on 0.23.0.
    public MagazineAttachmentCatalog? Attachments { get; init; }
    // Booster authoring (hd2.booster, SDK 0.24.0+); null on older SDKs, which have no Booster category.
    public BoosterCatalog? Boosters { get; init; }
    // Mounted vehicle weapons and drop-pod payloads (SDK 0.26.0+); null on older SDKs.
    public VehicleWeaponCatalog? VehicleWeapons { get; init; }
    public PodPayloadCatalog? Pods { get; init; }
    // SDK 0.27.0+: throwables (hd2.throwable).
    public ThrowableCatalog? Throwables { get; init; }
    // Unreleased Runtime (0.28.0 development) SDKs: enemies and enemy structures (hd2.enemy / hd2.structure).
    public EnemyCatalog? Enemies { get; init; }
    public IEnumerable<EntityField> AllFields => Vehicles.FieldInstances.Concat(Backpacks.FieldInstances).Concat(Attachments?.FieldInstances ?? []).Concat(Boosters?.FieldInstances ?? [])
        .Concat(VehicleWeapons?.FieldInstances ?? []).Concat(Pods?.FieldInstances ?? []).Concat(Throwables?.FieldInstances ?? []).Concat(Enemies?.FieldInstances ?? []);
    // The same catalogs with the enemy catalog attached (the catalogs are immutable once read).
    public EntityAuthoring WithEnemies(EnemyCatalog? enemies) => new() { Vehicles = Vehicles, Backpacks = Backpacks, CallIns = CallIns, Attachments = Attachments,
        Boosters = Boosters, VehicleWeapons = VehicleWeapons, Pods = Pods, Throwables = Throwables, Enemies = enemies };
    // The backpack whose deposit feeds a support weapon (0.26.0 backpack ammunition).
    public Backpack? AmmoBackpackOf(string supportWeapon) => Backpacks.Backpacks.FirstOrDefault(b => b.Feeds?.SupportWeapon == supportWeapon);
    // Indexed once per SDK load: with enemy catalogs there are more than fifteen thousand published fields, and every edit, validation and
    // Changes render resolves fields by key.
    private Dictionary<string, EntityField>? index;
    public EntityField? Field(string instanceKey) => (index ??= AllFields.GroupBy(f => f.InstanceKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal))
        .GetValueOrDefault(instanceKey);
    public string? CallInFor(string resource, string entity) => CallIns.FirstOrDefault(p => p.Value == (resource, entity)).Key;
}

public static class EntityAuthoringReader
{
    public const string VehicleFile = "VehicleAuthoringCapabilities.json", BackpackFile = "BackpackAuthoringCapabilities.json";
    public const int MaxBytes = 8 * 1024 * 1024;
    // 0.28.0 adds native_correlated: the native member matches the published value (backpack settings, linked drones and shields).
    public static readonly string[] Tiers = ["gameplay_proven", "gameplay_proven_combined", "schema_proven", "live_write_verified", "structural_reference", "native_correlated"];
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Slot = new(@"\A(zone|slot)_[0-9]{1,3}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent vehicle/backpack capability metadata (check {line})."); }

    public static EntityAuthoring Read(byte[] vehicles, byte[] backpacks, string version, StratagemCatalog stratagems)
    {
        try
        {
            var v = Parse<VehicleCatalog>(vehicles, "hd2runtime.vehicle.guarded_authoring.v1");
            var b = Parse<BackpackCatalog>(backpacks, "hd2runtime.backpack.guarded_authoring.v1");
            Check(v.Hd2RuntimeVersion == version && b.Hd2RuntimeVersion == version && v.CanonicalCollection == "fieldInstances" && b.CanonicalCollection == "fieldInstances");
            ValidateFields("vehicle", v.FieldInstances, v.BackingObjects, v.OperationGroups, v.EvidenceTiers, v.InstanceAudit);
            ValidateFields("backpack", b.FieldInstances, b.BackingObjects, b.OperationGroups, b.EvidenceTiers, b.InstanceAudit);
            ValidateVehicles(v);
            ValidateBackpacks(b);
            var links = LinkCallIns(v, b, stratagems);
            // Fields of a backpack's linked entities stay read-only until this build authors them (AuthoredTypes).
            if (!AuthoredTypes.LinkedBackpackEntities && b.FieldInstances.Any(f => f.Target.Linked != null && f.Editable))
                b = b with { FieldInstances = b.FieldInstances.Select(f => f.Target.Linked != null && f.Editable ? f with { Editable = false, Reason = AuthoredTypes.NotAuthoredReason } : f).ToArray() };
            return new() { Vehicles = v, Backpacks = b, CallIns = links };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed vehicle/backpack capability metadata.", e); }
    }
    private static T Parse<T>(byte[] bytes, string contract)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Vehicle/backpack capability file exceeds size limit.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 }); MetadataReader.RejectDuplicates(doc.RootElement);
        var root = doc.RootElement;
        if (root.GetProperty("contract").GetString() != contract || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new UnsupportedSdkException("Unsupported vehicle/backpack authoring contract.");
        var safety = root.GetProperty("safety");
        Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && !safety.GetProperty("rawResourceIdentifiers").GetBoolean()
            && safety.GetProperty("writesDuringGeneration").GetInt32() == 0 && safety.GetProperty("protectionChangesDuringGeneration").GetInt32() == 0
            && safety.GetProperty("fixtureFallback").GetString() == "disabled");
        return JsonSerializer.Deserialize<T>(bytes, Options)!;
    }
    private static void ValidateFields(string resource, EntityField[] fields, EntityBackingObject[] backings, EntityOperationGroup[] groups,
        Dictionary<string, string> tiers, EntityAudit audit)
    {
        Check(audit.ExactMatch && audit.MissingInstances == 0 && audit.UnexpectedInstances == 0 && audit.InternalInstances == fields.Length && audit.PublishedInstances == fields.Length);
        Check(tiers.Keys.All(Tiers.Contains) && fields.Length <= 4000 && fields.Select(f => f.InstanceKey).Distinct().Count() == fields.Length);
        var byBacking = backings.ToDictionary(o => o.BackingObjectId, StringComparer.Ordinal);
        var byGroup = groups.ToDictionary(g => g.OperationGroup, StringComparer.Ordinal);
        foreach (var f in fields)
        {
            Check(f.InstanceKey.StartsWith(resource + ":", StringComparison.Ordinal) && f.InstanceKey.Length <= 512 && f.Target.Resource == resource
                && !string.IsNullOrWhiteSpace(f.Target.Entity) && (resource == "vehicle" ? f.Target.Backpack == null : f.Target.Vehicle == null)
                && Api.IsMatch(f.ApiFieldConstant) && !string.IsNullOrWhiteSpace(f.DisplayName) && tiers.ContainsKey(f.Evidence.Tier)
                && f.PlanPhase == 1 && f.DependsOn.Length == 0 && f.Requires == "patch_or_transaction" && f.AllowSharedRequired == f.Shared
                && (f.Editable ? f.Reason == null : !string.IsNullOrWhiteSpace(f.Reason)));
            Check(f.Target.Path switch
            {
                "entity" => resource == "vehicle" && f.Target.Zone == null && f.Target.Mount == null,
                "mount" => resource == "vehicle" && f.Target.Mount != null && Slot.IsMatch(f.Target.Mount) && f.Target.Zone == null,
                "backpack" => resource == "backpack" && f.Target.Zone == null && f.Target.Mount == null && f.Target.Linked == null,
                // 0.28.0: a backpack's own damage zone (the SH-20 shield plate), or a zone of the entity it deploys (a drone, the SH-51 shield).
                "damage_zone" => resource is "vehicle" or "backpack" && f.Target.Zone != null && Slot.IsMatch(f.Target.Zone) && f.Target.Mount == null
                    && (f.Target.Linked == null || resource == "backpack"),
                "linked" => resource == "backpack" && f.Target.Linked is BackpackLinkedEntity.Drone or BackpackLinkedEntity.EnergyShield && f.Target.Zone == null && f.Target.Mount == null,
                _ => false,
            });
            if (f.IsReference)
                Check(f.Target.Path == "mount" && f.CurrentDefault.ValueKind == JsonValueKind.String && f.AllowedValues is { Length: > 0 }
                    && f.Acknowledgement == "allow_unverified_reference" && f.ValueKind == "mounted_weapon_semantic_id" && !string.IsNullOrWhiteSpace(f.ResidencyWarning));
            else { Check(f.Type is "integer" or "number" && f.Target.Path != "mount" && f.AllowedValues == null); _ = Generation.EntityScalar.Normalize(f, f.CurrentDefault); }
            var backing = byBacking[f.BackingObjectId]; var group = byGroup[f.OperationGroup];
            Check(backing.FieldInstances.Contains(f.InstanceKey) && backing.Kind == f.BackingObjectKind && backing.SharedScopeKey == f.SharedScopeKey && backing.Shared == f.Shared
                && group.FieldInstances.Contains(f.InstanceKey) && group.BackingObjectId == f.BackingObjectId && group.Target == f.Target
                && group.AllowSharedRequired == f.AllowSharedRequired && group.RecommendedApi == (group.FieldInstances.Length > 1 ? "hd2.transaction" : "hd2.patch"));
        }
        var keys = fields.Select(f => f.InstanceKey).ToHashSet(StringComparer.Ordinal);
        Check(backings.All(o => o.FieldInstances.All(keys.Contains)) && groups.All(g => g.FieldInstances.All(keys.Contains))
            && backings.Sum(o => o.FieldInstances.Length) == fields.Length && groups.Sum(g => g.FieldInstances.Length) == fields.Length);
    }
    private static void ValidateVehicles(VehicleCatalog v)
    {
        Check(v.ReferenceContract is { Field: "mount.weapon", ValueKind: "mounted_weapon_semantic_id", Acknowledgement: "allow_unverified_reference",
            ArbitraryIdentifiersAccepted: false, RawNativeIdentifiersPublished: false });
        var weapons = v.MountedWeapons.ToDictionary(w => w.SemanticId, StringComparer.Ordinal);
        Check(weapons.Count == v.MountedWeapons.Length && v.Vehicles.Select(x => x.Name).Distinct().Count() == v.Vehicles.Length
            && v.Vehicles.Select(x => x.SemanticId).Distinct().Count() == v.Vehicles.Length);
        foreach (var x in v.Vehicles)
        {
            var own = v.FieldInstances.Where(f => f.Target.Vehicle == x.Name).ToArray();
            Check(x.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(own.Select(f => f.InstanceKey).Order(StringComparer.Ordinal))
                && x.Durability.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(own.Where(f => f.Target.Path == "entity").Select(f => f.InstanceKey).Order(StringComparer.Ordinal))
                && x.Durability.Zones.Length == x.Durability.PopulatedZones && x.Durability.Zones.Select(z => z.ZoneId).Distinct().Count() == x.Durability.Zones.Length
                && x.Mounts.Select(m => m.MountId).Distinct().Count() == x.Mounts.Length
                && (x.CallInStratagem.Known ? x.CallInStratagem.SemanticId != null : !string.IsNullOrWhiteSpace(x.CallInStratagem.Reason)));
            foreach (var z in x.Durability.Zones)
                Check(Slot.IsMatch(z.ZoneId) && z.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(own.Where(f => f.Target.Path == "damage_zone" && f.Target.Zone == z.ZoneId).Select(f => f.InstanceKey).Order(StringComparer.Ordinal)));
            Check(own.Where(f => f.Target.Path == "damage_zone").All(f => x.Durability.Zones.Any(z => z.ZoneId == f.Target.Zone)));
            foreach (var m in x.Mounts)
            {
                var field = own.SingleOrDefault(f => f.Target.Path == "mount" && f.Target.Mount == m.MountId);
                // Only weapon occupants are swappable; racks, shields, fuel tanks and other non-weapon slots never get a control.
                if (m.Swappable)
                    Check(m.CurrentKind == "weapon" && m.Current != null && weapons.TryGetValue(m.Current.SemanticId, out var current) && current.AttackFamily == m.Current.AttackFamily
                        && field != null && field.IsReference && field.CurrentDefault.GetString() == m.Current.SemanticId && field.AllowedValues!.SequenceEqual(m.AllowedReplacements)
                        && m.AllowedReplacements.Distinct().Count() == m.AllowedReplacements.Length
                        && m.AllowedReplacements.All(r => weapons.TryGetValue(r, out var w) && w.AttackFamily == current!.AttackFamily));
                else Check(field == null && m.AllowedReplacements.Length == 0 && !string.IsNullOrWhiteSpace(m.BlockedReason));
            }
        }
        var s = v.Summary; var fields = v.FieldInstances; var mounts = v.Vehicles.SelectMany(x => x.Mounts).ToArray();
        Check(s.Vehicles == v.Vehicles.Length && s.FieldInstances == fields.Length && s.WritableFieldInstances == fields.Count(f => f.Editable)
            && s.PopulatedZones == v.Vehicles.Sum(x => x.Durability.Zones.Length) && s.ZoneFieldInstances == fields.Count(f => f.Target.Path == "damage_zone")
            && s.MountSlots == mounts.Length && s.SwappableMountSlots == mounts.Count(m => m.Swappable) && s.NonWeaponMountSlots == mounts.Count(m => !m.Swappable)
            && s.DiscoveredMountedWeapons == weapons.Count && s.StratagemVehicles == v.Vehicles.Count(x => x.CallInStratagem.Known)
            && s.NativeOnlyVehicles == v.Vehicles.Count(x => !x.CallInStratagem.Known)
            && Same(s.WritableByTier, fields.Where(f => f.Editable).GroupBy(f => f.Evidence.Tier).ToDictionary(g => g.Key, g => g.Count()))
            && Same(s.MountedWeaponsByFamily, v.MountedWeapons.GroupBy(w => w.AttackFamily).ToDictionary(g => g.Key, g => g.Count())));
    }
    private static void ValidateBackpacks(BackpackCatalog b)
    {
        Check(b.Backpacks.Select(x => x.Name).Distinct().Count() == b.Backpacks.Length && b.Backpacks.Select(x => x.SemanticId).Distinct().Count() == b.Backpacks.Length);
        foreach (var x in b.Backpacks)
        {
            var own = b.FieldInstances.Where(f => f.Target.Backpack == x.Name).Select(f => f.InstanceKey).ToArray();
            Check(x.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(own.Order(StringComparer.Ordinal))
                && x.SettingGroups.All(g => !string.IsNullOrWhiteSpace(g.Group) && g.FieldInstanceKeys.All(own.Contains))
                && x.SettingGroups.SelectMany(g => g.FieldInstanceKeys).Distinct().Count() == x.SettingGroups.Sum(g => g.FieldInstanceKeys.Length)
                && (x.CallInStratagem.Known ? x.CallInStratagem.SemanticId != null : !string.IsNullOrWhiteSpace(x.CallInStratagem.Reason)));
            // Zone fields target one of the published damage zones of the backpack, or of the linked entity they name.
            static bool Zones(BackpackZone[] zones) => zones.Select(z => z.ZoneId).Distinct().Count() == zones.Length && zones.All(z => z.ZoneId == "zone_" + z.Index);
            var linked = x.LinkedEntities ?? [];
            Check(Zones(x.DamageZones ?? []) && linked.All(l => Zones(l.DamageZones ?? [])) && linked.Select(l => l.Linked).Distinct().Count() == linked.Length
                && b.FieldInstances.Where(f => f.Target.Backpack == x.Name && f.Target.Path == "damage_zone")
                    .All(f => (f.Target.Linked == null ? x.DamageZones : x.Linked(f.Target.Linked)?.DamageZones)?.Any(z => z.ZoneId == f.Target.Zone) == true));
            // A linked entity lists exactly the fields that target it, and its setting group names it.
            foreach (var l in linked)
                Check(l.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(b.FieldInstances.Where(f => f.Target.Backpack == x.Name && f.Target.Linked == l.Linked)
                        .Select(f => f.InstanceKey).Order(StringComparer.Ordinal)) && !string.IsNullOrWhiteSpace(l.Relationship));
            Check(b.FieldInstances.Where(f => f.Target.Backpack == x.Name && f.Target.Linked != null).All(f => x.Linked(f.Target.Linked!) != null)
                && x.SettingGroups.All(g => g.Linked == null || x.Linked(g.Linked) is { } l && g.FieldInstanceKeys.All(l.FieldInstanceKeys.Contains)));
        }
        var s = b.Summary; var f = b.FieldInstances;
        Check(s.Backpacks == b.Backpacks.Length && s.FieldInstances == f.Length && s.WritableFieldInstances == f.Count(x => x.Editable)
            && s.ReadOnlyFieldInstances == f.Count(x => !x.Editable) && s.BackpacksWithWritableFields == f.Where(x => x.Editable).Select(x => x.Target.Backpack).Distinct().Count()
            && Same(s.WritableByTier, f.Where(x => x.Editable).GroupBy(x => x.Evidence.Tier).ToDictionary(g => g.Key, g => g.Count())));
    }
    // Call-in links must agree in both directions: entity.callInStratagem.semanticId <-> stratagem.delivers.semanticId.
    private static Dictionary<string, (string, string)> LinkCallIns(VehicleCatalog v, BackpackCatalog b, StratagemCatalog stratagems)
    {
        var roots = stratagems.Stratagems.Where(s => s.SemanticId != null).ToDictionary(s => s.SemanticId!, StringComparer.Ordinal);
        var links = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        void Link(string resource, string name, string semanticId, EntityCallIn callIn)
        {
            if (!callIn.Known) return;
            Check(roots.TryGetValue(callIn.SemanticId!, out var root) && root!.Family == resource && root.Delivers is { Known: true } d
                && d.Kind == resource && d.SemanticId == semanticId);
            links.Add(root.Name, (resource, name));
        }
        foreach (var x in v.Vehicles) Link("vehicle", x.Name, x.SemanticId, x.CallInStratagem);
        foreach (var x in b.Backpacks)
        {
            // 0.26.0 weapon-fed backpacks arrive in their support weapon's pod: the call-in is that weapon's stratagem, which must deliver
            // exactly the weapon this backpack feeds. They are authored on the support weapon's page, not as backpack stratagems.
            if (x.CallInStratagem.Relationship == "delivered_with_support_weapon")
            {
                Check(x.Feeds != null && roots.TryGetValue(x.CallInStratagem.SemanticId!, out var weapon) && weapon!.Family == "support"
                    && weapon.Delivers is { Known: true, Kind: "support_weapon" } d && d.SemanticId == x.Feeds.SupportWeaponSemanticId);
                continue;
            }
            Check(x.Feeds == null);
            Link("backpack", x.Name, x.SemanticId, x.CallInStratagem);
        }
        // No one-sided reverse links.
        Check(stratagems.Stratagems.Where(s => s.Delivers is { Known: true, Kind: "vehicle" or "backpack" }).All(s => links.ContainsKey(s.Name))
            && stratagems.Stratagems.Where(s => s.Family is "vehicle" or "backpack").All(s => links.ContainsKey(s.Name) || s.Delivers is { Known: false }));
        return links;
    }
    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
}
