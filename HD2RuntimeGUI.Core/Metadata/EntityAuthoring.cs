using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.23.0+ guarded vehicle (hd2.vehicle) and backpack (hd2.backpack) authoring catalogs.
// Every control comes from a published canonical field instance; mount replacements come only from published allowed values.
public sealed record EntityTarget(string Resource, string Path, string? Vehicle = null, string? Backpack = null, string? Zone = null, string? Mount = null)
{
    [JsonIgnore] public string Entity => Vehicle ?? Backpack ?? "";
}
public sealed record EntityEvidence(string Tier, string? ReferenceMod = null, string? Proof = null, string[]? ProvenOn = null, bool? SharedTypedSchema = null);
public sealed record EntityConsumer(string? Vehicle = null, string? Backpack = null);
public sealed record EntityField(string InstanceKey, string SemanticFieldId, string DisplayName, string Type, string? Unit, JsonElement CurrentDefault,
    bool Editable, string? Reason, EntityTarget Target, string BackingObjectId, string OperationGroup, string PlanGroup, string Requires,
    bool AllowSharedRequired, bool Shared, EntityConsumer[] SharedConsumers, string SharedScopeKey, bool ReviewedScopeComplete,
    bool DynamicConsumersPossible, string BackingObjectKind, string Domain, string ApiFieldConstant, int PlanPhase, string[] DependsOn,
    EntityEvidence Evidence, string Provenance, string[]? AllowedValues = null, string? Acknowledgement = null, string? ValueKind = null,
    string? ResidencyWarning = null)
{
    public const string ReferenceType = "mounted_weapon_reference";
    [JsonIgnore] public bool IsReference => Type == ReferenceType;
}
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
public sealed record BackpackSettingGroup(string Group, string[] FieldInstanceKeys);
public sealed record Backpack(string Name, string SemanticId, EntityCallIn CallInStratagem, string[] DeliveryChain, string[] Components,
    BackpackSettingGroup[] SettingGroups, EntityBlocked[] BlockedFields, string[] FieldInstanceKeys);
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
    public IEnumerable<EntityField> AllFields => Vehicles.FieldInstances.Concat(Backpacks.FieldInstances);
    public EntityField? Field(string instanceKey) => AllFields.FirstOrDefault(f => f.InstanceKey == instanceKey);
    public string? CallInFor(string resource, string entity) => CallIns.FirstOrDefault(p => p.Value == (resource, entity)).Key;
}

public static class EntityAuthoringReader
{
    public const string VehicleFile = "VehicleAuthoringCapabilities.json", BackpackFile = "BackpackAuthoringCapabilities.json";
    public const int MaxBytes = 8 * 1024 * 1024;
    public static readonly string[] Tiers = ["gameplay_proven", "gameplay_proven_combined", "schema_proven", "live_write_verified", "structural_reference"];
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Slot = new(@"\A(zone|slot)_[0-9]{1,3}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid) { if (!valid) throw new InvalidDataException("Inconsistent vehicle/backpack capability metadata."); }

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
            return new() { Vehicles = v, Backpacks = b, CallIns = LinkCallIns(v, b, stratagems) };
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
                "damage_zone" => resource == "vehicle" && f.Target.Zone != null && Slot.IsMatch(f.Target.Zone) && f.Target.Mount == null,
                "mount" => resource == "vehicle" && f.Target.Mount != null && Slot.IsMatch(f.Target.Mount) && f.Target.Zone == null,
                "backpack" => resource == "backpack" && f.Target.Zone == null && f.Target.Mount == null,
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
        foreach (var x in b.Backpacks) Link("backpack", x.Name, x.SemanticId, x.CallInStratagem);
        // No one-sided reverse links.
        Check(stratagems.Stratagems.Where(s => s.Delivers is { Known: true, Kind: "vehicle" or "backpack" }).All(s => links.ContainsKey(s.Name))
            && stratagems.Stratagems.Where(s => s.Family is "vehicle" or "backpack").All(s => links.ContainsKey(s.Name) || s.Delivers is { Known: false }));
        return links;
    }
    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
}
