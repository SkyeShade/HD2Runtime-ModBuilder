using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.26.0 mounted vehicle weapons (hd2runtime.vehicle_weapon.v1, VehicleWeaponCapabilities.json). Each mount's weapon is its
// own target: hd2.vehicle(v):weapon(label), with :projectile(), :explosion(phase) and :attack(role) for its shared attack objects.
// Scopes are exactly as published: weapon_local (one mount), shared_mounted_weapon (one weapon entity in several mounts) and the
// shared projectile/damage/explosion rows. Every non-local scope requires allow_shared.
public sealed record VehicleWeaponFieldInstance(string InstanceKey, string Weapon, string WeaponSemanticId, string SemanticFieldId, string ApiFieldConstant,
    VehicleWeaponTargetJson Target, string DisplayName, string? Unit, string Type, JsonElement Baseline, bool Writable, string Scope, bool AllowSharedRequired,
    string[] OtherConsumers, string? Acknowledgement, string? GameplayEvidence, string BackingComponent);
public sealed record VehicleWeaponTargetJson(string Resource, string Path, string Weapon, string? Attack = null);
public sealed record VehicleWeaponBlock(string Field, string Reason, string? Attack = null);
public sealed record VehicleWeaponOwnership(string[] WeaponLocal, string[] AlsoMountedAt, int? SharedProjectileConsumers, int? SharedDamageConsumers);
public sealed record VehicleMountedWeapon(string Key, string SemanticId, string? NativePath, string[] Attacks, Dictionary<string, string[]> FieldGroups,
    JsonElement Values, VehicleWeaponOwnership Ownership, VehicleWeaponBlock[] Blocked);
public sealed record VehicleWeaponMount(int Slot, string Label, VehicleMountedWeapon? Weapon, string? Reason = null)
{
    [JsonIgnore] public string Title => Label switch { "left_gun" => "Left arm", "right_gun" => "Right arm", "attach_tank_gun" => "Main cannon", "attach_tank_gun_mg" => "Coaxial machine gun", _ => Label.Replace('_', ' ') };
}
public sealed record VehicleWeaponVehicle(string Vehicle, VehicleWeaponMount[] Mounts);
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
    public static readonly string[] Scopes = ["weapon_local", "shared_mounted_weapon", "shared_projectile", "shared_damage", "shared_explosion"];
    public static readonly string[] Paths = ["weapon", "projectile_reference", "explosion", "attack"];
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Role = new(@"\A[a-z][a-z_0-9]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent vehicle weapon capability metadata (check {line})."); }
    // "<vehicle name> / <mount label>" as published.
    public static bool ValidKey(string key) => key.Length <= 256 && Regex.IsMatch(key, @"\A[^\p{C}/]{1,200} / [a-z][a-z_0-9]{0,63}\z");
    public static (string Vehicle, string Mount) Split(string key) { var i = key.LastIndexOf(" / ", StringComparison.Ordinal); return (key[..i], key[(i + 3)..]); }

    public static VehicleWeaponCatalog Read(byte[] bytes, string version, VehicleCatalog vehicles)
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
            Check(c.Hd2RuntimeVersion == version && Scopes.All(c.Scopes.ContainsKey) && c.Scopes.Keys.All(Scopes.Contains));
            var weapons = new Dictionary<string, VehicleMountedWeapon>(StringComparer.Ordinal);
            foreach (var v in c.Vehicles)
            {
                // Vehicles are the published vehicle catalog's own entries; nothing is matched by display text.
                Check(vehicles.Find(v.Vehicle) != null && v.Mounts.Select(m => m.Slot).Distinct().Count() == v.Mounts.Length);
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
                    && Api.IsMatch(f.ApiFieldConstant) && !string.IsNullOrWhiteSpace(f.DisplayName) && f.Type is "integer" or "number"
                    && f.Baseline.ValueKind == JsonValueKind.Number && Scopes.Contains(f.Scope) && f.AllowSharedRequired == (f.Scope != "weapon_local")
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
            return new() { Vehicles = c.Vehicles, Scopes = c.Scopes, Published = published, FieldInstances = all.Select(Adapt).ToArray() };
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

    private static EntityField Adapt(VehicleWeaponFieldInstance f)
    {
        var owner = OwnerKey(f); var (vehicle, _) = Split(f.Weapon);
        var target = new EntityTarget("vehicle_weapon", f.Target.Path, Weapon: f.Weapon, Attack: f.Target.Attack);
        // One transaction per target and native component; all of a vehicle's operations form one plan.
        var operation = "vehicle-weapon-op:" + f.Weapon + "|" + f.Target.Path + "|" + (f.Target.Attack ?? "") + "|" + f.BackingComponent;
        return new EntityField(f.InstanceKey, f.SemanticFieldId, f.DisplayName, f.Type, f.Unit, f.Baseline.Clone(), f.Writable, null, target,
            owner, operation, "vehicle-weapon-plan:" + vehicle, "patch_or_transaction", f.AllowSharedRequired, f.Scope != "weapon_local", [], owner,
            ReviewedScopeComplete: f.Scope == "weapon_local", DynamicConsumersPossible: f.Scope is "shared_projectile" or "shared_damage" or "shared_explosion",
            BackingObjectKind: f.BackingComponent, Domain: f.SemanticFieldId.Split('.')[0], ApiFieldConstant: f.ApiFieldConstant, PlanPhase: 1, DependsOn: [],
            Evidence: new EntityEvidence(f.GameplayEvidence != null ? "gameplay_proven" : "structural_reference", ReferenceMod: f.GameplayEvidence),
            Provenance: "VehicleWeaponCapabilities (" + f.Scope + ", " + f.BackingComponent + ")", Acknowledgement: f.Acknowledgement);
    }
}
