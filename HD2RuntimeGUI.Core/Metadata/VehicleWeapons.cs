using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.26.0 mounted vehicle weapons (hd2runtime.vehicle_weapon.v1, VehicleWeaponCapabilities.json). Each mount's weapon is its
// own target: hd2.vehicle(v):weapon(label), with :projectile(), :explosion(phase) and :attack(role) for its shared attack objects.
// Scopes are exactly as published: weapon_local (one mount), shared_mounted_weapon (one weapon entity in several mounts) and the
// shared projectile/damage/explosion rows. Every non-local scope requires allow_shared.
public sealed record VehicleWeaponFieldInstance(string InstanceKey, string Weapon, string WeaponSemanticId, string SemanticFieldId, string? ApiFieldConstant,
    VehicleWeaponTargetJson Target, string DisplayName, string? Unit, string Type, JsonElement Baseline, bool Writable, string Scope, bool AllowSharedRequired,
    string[] OtherConsumers, string? Acknowledgement, string? GameplayEvidence, string BackingComponent,
    // 0.28.0: the projectile a mounted weapon fires (a unified projectile host, hd2.vehicle(v):weapon(mount):attack(role)), and the
    // exact live-proven values of a write (their writes drop the acknowledgement).
    VehicleProjectileReference? ProjectileReference = null, FieldLiveEvidence? LiveEvidence = null, string[]? LiveProvenValues = null);
public sealed record VehicleProjectileReference(string ReferenceKind, string CompatibilityClass, string ReferenceRole, WeaponProjectileSource ProjectileSource);
// 0.28.0: a mounted weapon carried by something other than a vehicle, for example a Guard Dog drone (hd2.backpack(name):drone():weapon()).
public sealed record VehicleWeaponCarrier(string Kind, string Backpack, string Api, string[] Chain)
{
    public const string BackpackDrone = "backpack_drone";
}
public sealed record VehicleWeaponTargetJson(string Resource, string Path, string Weapon, string? Attack = null);
public sealed record VehicleWeaponBlock(string Field, string Reason, string? Attack = null);
public sealed record VehicleWeaponOwnership(string[] WeaponLocal, string[] AlsoMountedAt, int? SharedProjectileConsumers, int? SharedDamageConsumers);
public sealed record VehicleMountedWeapon(string Key, string SemanticId, string? NativePath, string[] Attacks, Dictionary<string, string[]> FieldGroups,
    JsonElement Values, VehicleWeaponOwnership Ownership, VehicleWeaponBlock[] Blocked);
public sealed record VehicleWeaponMount(int Slot, string Label, VehicleMountedWeapon? Weapon, string? Reason = null)
{
    [JsonIgnore] public string Title => Label switch { "left_gun" => "Left arm", "right_gun" => "Right arm", "attach_tank_gun" => "Main cannon", "attach_tank_gun_mg" => "Coaxial machine gun", _ => Label.Replace('_', ' ') };
}
public sealed record VehicleWeaponVehicle(string Vehicle, VehicleWeaponMount[] Mounts, VehicleWeaponCarrier? Carrier = null);
public sealed record VehicleWeaponSummary(int Vehicles, int WeaponMounts, int FieldInstances, int WritableFieldInstances, int WeaponLocalFields,
    int SharedMountedWeaponFields, int SharedFields, int GameplayProvenFields, int UnverifiedEffectFields);
public sealed record VehicleWeaponCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, Dictionary<string, string> Scopes,
    VehicleWeaponVehicle[] Vehicles, VehicleWeaponFieldInstance[] FieldInstances, VehicleWeaponSummary Summary);

public sealed class VehicleWeaponCatalog
{
    public required VehicleWeaponVehicle[] Vehicles { get; init; }
    public required IReadOnlyDictionary<string, string> Scopes { get; init; }
    public required IReadOnlyDictionary<string, VehicleWeaponFieldInstance> Published { get; init; }
    // The same fields adapted to the entity authoring pipeline (target resource "vehicle_weapon").
    public required EntityField[] FieldInstances { get; init; }
    public VehicleWeaponVehicle? Find(string vehicle) => Vehicles.FirstOrDefault(v => v.Vehicle == vehicle);
    public static readonly IReadOnlyDictionary<string, string> Tiers = new Dictionary<string, string>
    {
        ["gameplay_proven"] = "Changed in game by a working reference mod (listed); no acknowledgement needed.",
        ["structural_reference"] = "Owner and bytes proven; the in-game effect of changing it has not been tested.",
    };
}

public static class VehicleWeaponReader
{
    public const string FileName = "VehicleWeaponCapabilities.json", Contract = "hd2runtime.vehicle_weapon.v1";
    public const int MaxBytes = 4 * 1024 * 1024;
    // 0.28.0 adds the shared beam and arc settings rows.
    public static readonly string[] Scopes = ["weapon_local", "shared_mounted_weapon", "shared_projectile", "shared_damage", "shared_explosion", "shared_beam", "shared_arc"];
    public static readonly string[] Paths = ["weapon", "projectile_reference", "explosion", "attack"];
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Role = new(@"\A[a-z][a-z_0-9]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent vehicle weapon capability metadata (check {line})."); }
    // "<vehicle name> / <mount label>" as published.
    public static bool ValidKey(string key) => key.Length <= 256 && Regex.IsMatch(key, @"\A[^\p{C}/]{1,200} / [a-z][a-z_0-9]{0,63}\z");
    public static (string Vehicle, string Mount) Split(string key) { var i = key.LastIndexOf(" / ", StringComparison.Ordinal); return (key[..i], key[(i + 3)..]); }

    public const string NoApiConstantReason = "Runtime publishes no typed API field constant for this status slot, so it cannot be written.";
    public static VehicleWeaponCatalog Read(byte[] bytes, string version, VehicleCatalog vehicles, BackpackCatalog? backpacks = null)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Vehicle weapon capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported vehicle weapon authoring contract.");
            var safety = root.GetProperty("safety");
            Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && safety.GetProperty("writesDuringGeneration").GetInt32() == 0);
            var c = JsonSerializer.Deserialize<VehicleWeaponCatalogJson>(bytes, Options)!;
            // Scopes are exactly those this build knows; older SDKs publish the first five.
            Check(c.Hd2RuntimeVersion == version && c.Scopes.Keys.All(Scopes.Contains) && Scopes.Take(5).All(c.Scopes.ContainsKey));
            var weapons = new Dictionary<string, VehicleMountedWeapon>(StringComparer.Ordinal);
            foreach (var v in c.Vehicles)
            {
                // Vehicles are the published vehicle catalog's own entries; nothing is matched by display text.
                // Carriers (0.28.0 Guard Dog drones) are named by their published backpack instead.
                Check((v.Carrier == null ? vehicles.Find(v.Vehicle) != null
                        : v.Carrier is { Kind: VehicleWeaponCarrier.BackpackDrone } k && k.Backpack == v.Vehicle && backpacks?.Find(k.Backpack)?.Linked(BackpackLinkedEntity.Drone) != null)
                    && v.Mounts.Select(m => m.Slot).Distinct().Count() == v.Mounts.Length);
                foreach (var m in v.Mounts)
                {
                    Check(Role.IsMatch(m.Label) && (m.Weapon == null ? !string.IsNullOrWhiteSpace(m.Reason) : m.Weapon.Key == v.Vehicle + " / " + m.Label));
                    if (m.Weapon is { } w) Check(weapons.TryAdd(w.Key, w) && w.Attacks.All(Role.IsMatch) && w.Blocked.All(b => !string.IsNullOrWhiteSpace(b.Reason)));
                }
            }
            var published = new Dictionary<string, VehicleWeaponFieldInstance>(StringComparer.Ordinal);
            foreach (var f in c.FieldInstances)
            {
                Check(published.TryAdd(f.InstanceKey, f) && f.InstanceKey.StartsWith("vehicle-field/v1/", StringComparison.Ordinal) && f.InstanceKey.Length <= 512
                    && weapons.TryGetValue(f.Weapon, out var w) && w!.SemanticId == f.WeaponSemanticId
                    && f.Target.Resource == "vehicle_weapon" && f.Target.Weapon == f.Weapon && Paths.Contains(f.Target.Path)
                    && (f.Target.Path == "weapon" ? f.Target.Attack == null : f.Target.Attack != null && w.Attacks.Contains(f.Target.Attack))
                    // 0.28.0 development SDKs publish some explosion status slots without an API constant; those can only be shown (see Adapt).
                    && (f.ApiFieldConstant is { } api ? Api.IsMatch(api) : f.SemanticFieldId.Contains(".status_", StringComparison.Ordinal)) && !string.IsNullOrWhiteSpace(f.DisplayName)
                    // Status references (0.28.0 development SDKs) are a status key or 'none'; they stay read-only in this build (see Adapt).
                    && (f.Type is "integer" or "number" ? f.Baseline.ValueKind == JsonValueKind.Number
                        : f.Type == WeaponCapability.StatusReference ? f.Baseline.ValueKind == JsonValueKind.String && f.Target.Path != "weapon"
                        // 0.28.0 mounted projectile hosts: the attack's own projectile, fired directly ({weapon, attack} baseline).
                        : f.Type == "projectile_reference" && f.Target.Path == "attack" && f.SemanticFieldId == "attack." + f.Target.Attack + ".projectile"
                            && f.Baseline.ValueKind == JsonValueKind.Object && f.ProjectileReference is { ReferenceKind: "projectile" } r && r.ReferenceRole == f.Target.Attack
                            && r.ProjectileSource.Status == WeaponProjectileSource.ActiveDirect && !string.IsNullOrWhiteSpace(r.CompatibilityClass))
                    && (f.LiveProvenValues == null || f.LiveEvidence?.Values is { } proven && f.LiveProvenValues.SequenceEqual(proven))
                    && Scopes.Contains(f.Scope) && f.AllowSharedRequired == (f.Scope != "weapon_local")
                    && (f.Scope == "weapon_local" ? f.OtherConsumers.Length == 0 : f.Scope != "shared_mounted_weapon" || f.OtherConsumers.All(weapons.ContainsKey))
                    && f.Acknowledgement is null or "allow_unverified_effect" && (f.Acknowledgement == null) == (f.GameplayEvidence != null)
                    && !string.IsNullOrWhiteSpace(f.BackingComponent) && f.Writable);
            }
            foreach (var w in weapons.Values)
                Check(w.FieldGroups.Values.SelectMany(k => k).All(k => published.TryGetValue(k, out var f) && f.Weapon == w.Key)
                    && published.Values.Where(f => f.Weapon == w.Key).All(f => w.FieldGroups.Values.Any(g => g.Contains(f.InstanceKey))));
            var s = c.Summary; var all = c.FieldInstances;
            Check(s.Vehicles == c.Vehicles.Length && s.WeaponMounts == weapons.Count && s.FieldInstances == all.Length && s.WritableFieldInstances == all.Count(f => f.Writable)
                && s.WeaponLocalFields == all.Count(f => f.Scope == "weapon_local") && s.SharedMountedWeaponFields == all.Count(f => f.Scope == "shared_mounted_weapon")
                && s.SharedFields == all.Count(f => f.Scope != "weapon_local")
                && s.GameplayProvenFields == all.Count(f => f.GameplayEvidence != null) && s.UnverifiedEffectFields == all.Count(f => f.Acknowledgement != null));
            var carried = c.Vehicles.Where(v => v.Carrier != null).Select(v => v.Vehicle).ToHashSet(StringComparer.Ordinal);
            return new() { Vehicles = c.Vehicles, Scopes = c.Scopes, Published = published, FieldInstances = all.Select(f => Adapt(f, carried.Contains(Split(f.Weapon).Vehicle))).ToArray() };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed vehicle weapon capability metadata.", e); }
    }

    // A shared native owner is identified by its scope, component and every consumer that reaches it (the weapon plus otherConsumers),
    // so the same owner reached through two mounts gets the same key and is never written twice with different values.
    public static string OwnerKey(VehicleWeaponFieldInstance f) => f.Scope == "weapon_local"
        ? "vehicle-weapon-owner:" + f.Weapon + "|" + f.BackingComponent
        : "vehicle-weapon-owner:" + f.Scope + "|" + f.BackingComponent + "|" + (f.Target.Attack ?? "") + "|"
            + string.Join("|", f.OtherConsumers.Append(f.Weapon).Distinct().Order(StringComparer.Ordinal));

    private static EntityField Adapt(VehicleWeaponFieldInstance f, bool carried)
    {
        var owner = OwnerKey(f); var (vehicle, _) = Split(f.Weapon);
        var target = new EntityTarget("vehicle_weapon", f.Target.Path, Weapon: f.Weapon, Attack: f.Target.Attack);
        // One transaction per target and native component; all of a vehicle's operations form one plan.
        var operation = "vehicle-weapon-op:" + f.Weapon + "|" + f.Target.Path + "|" + (f.Target.Attack ?? "") + "|" + f.BackingComponent;
        var reason = !AuthoredTypes.VehicleWeapon(f, carried) ? AuthoredTypes.NotAuthoredReason : f.ApiFieldConstant == null ? NoApiConstantReason : null;
        return new EntityField(f.InstanceKey, f.SemanticFieldId, f.DisplayName, f.Type, f.Unit, f.Baseline.Clone(), f.Writable && reason == null, reason, target,
            owner, operation, "vehicle-weapon-plan:" + vehicle, "patch_or_transaction", f.AllowSharedRequired, f.Scope != "weapon_local", [], owner,
            ReviewedScopeComplete: f.Scope == "weapon_local", DynamicConsumersPossible: f.Scope is "shared_projectile" or "shared_damage" or "shared_explosion" or "shared_beam" or "shared_arc",
            BackingObjectKind: f.BackingComponent, Domain: f.SemanticFieldId.Split('.')[0], ApiFieldConstant: f.ApiFieldConstant ?? "", PlanPhase: 1, DependsOn: [],
            Evidence: new EntityEvidence(f.GameplayEvidence != null ? "gameplay_proven" : "structural_reference", ReferenceMod: f.GameplayEvidence),
            Provenance: "VehicleWeaponCapabilities (" + f.Scope + ", " + f.BackingComponent + ")", Acknowledgement: f.Acknowledgement);
    }
}
