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
    // 0.28.0: the weapon a backpack's drone carries (Guard Dogs), keyed by the backpack name.
    public VehicleWeaponVehicle? CarriedBy(string backpack) => Vehicles.FirstOrDefault(v => v.Carrier?.Backpack == backpack);
    public static readonly IReadOnlyDictionary<string, string> Tiers = new Dictionary<string, string>
    {
        ["gameplay_proven"] = "Changed in game by a working reference mod (listed); no acknowledgement needed.",
        ["structural_reference"] = "Owner and bytes proven; the in-game effect of changing it has not been tested.",
    };
}

// Typed status slots of a mounted weapon's damage row (Runtime 0.28.0, docs/status-effects.md): damage.<attack>.status_<k>_type for every
// used slot and the first empty one. A slot takes an attachable status (StatusEffectCatalog) or keeps its current one; 'none' clears only
// the last used slot, and the slots stay packed from slot 1.
public static class MountedStatusSlots
{
    private static readonly Regex TypeField = new(@"\A(?<row>[a-z_.0-9]+\.status_)(?<k>[1-4])_type\z", RegexOptions.CultureInvariant);
    private static readonly Regex StrengthField = new(@"\A(?<row>[a-z_.0-9]+\.status_)(?<k>[1-4])_strength\z", RegexOptions.CultureInvariant);
    // The slot number (1-4) of a status type or strength field.
    public static int? Slot(EntityField f) => (TypeField.Match(f.SemanticFieldId) is { Success: true } t ? t : StrengthField.Match(f.SemanticFieldId)) is { Success: true } m
        ? m.Groups["k"].Value[0] - '0' : null;
    public static bool IsStrength(EntityField f) => StrengthField.IsMatch(f.SemanticFieldId);
    private static string? Row(EntityField f) => TypeField.Match(f.SemanticFieldId) is { Success: true } m ? m.Groups["row"].Value : null;
    // The status type fields of one damage row (same target and row), in slot order.
    public static EntityField[] Siblings(EntityAuthoring catalog, EntityField f) => Row(f) is not { } row ? []
        : (catalog.VehicleWeapons?.FieldInstances ?? []).Where(x => x.IsStatusReference && x.Target == f.Target && Row(x) == row).OrderBy(x => Slot(x)).ToArray();
    public sealed record Choices(IReadOnlyList<string> Statuses, bool AllowNone);
    public static Choices For(SdkMetadata sdk, EntityField f)
    {
        var current = f.CurrentDefault.ValueKind == JsonValueKind.String ? f.CurrentDefault.GetString()! : StatusReference.None;
        var statuses = (sdk.StatusEffects?.Statuses.Values.Where(s => s.Attachable).Select(s => s.SemanticId) ?? []).ToList();
        if (current != StatusReference.None) statuses.Add(current);
        var next = sdk.Entities is { } e ? Siblings(e, f).FirstOrDefault(x => Slot(x) == Slot(f) + 1) : null;
        var allowNone = current == StatusReference.None || next == null || next.CurrentDefault.GetString() == StatusReference.None;
        return new(statuses.Distinct().Order(StringComparer.Ordinal).ToArray(), allowNone);
    }
    // The first packing violation involving this slot, given each sibling's effective value (saved desired value, else the baseline):
    // a status after an empty slot. Returns the (empty slot, slot with a status) pair.
    public static (int Empty, int Used)? Gap(IReadOnlyList<EntityField> siblings, Func<EntityField, string> effective, EntityField f)
    {
        var values = siblings.Select(x => (Slot: Slot(x)!.Value, Value: effective(x))).ToArray();
        foreach (var used in values.Where(v => v.Value != StatusReference.None))
            if (values.FirstOrDefault(v => v.Slot < used.Slot && v.Value == StatusReference.None) is { Slot: > 0 } empty && (Slot(f) == used.Slot || Slot(f) == empty.Slot))
                return (empty.Slot, used.Slot);
        return null;
    }
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
    // "<vehicle name> / <mount label>" as published. A carrier (0.28.0 Guard Dog drones) is named by its backpack, which may itself contain
    // a slash ("AX/AR-23 Guard Dog"); only the last " / " separates the mount label.
    public static bool ValidKey(string key) => key.Length <= 256 && Regex.IsMatch(key, @"\A(?:(?! / )[^\p{C}]){1,200} / [a-z][a-z_0-9]{0,63}\z");
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
                // A carrier's published accessor must be exactly the one ModBuilder writes (hd2.backpack(name):drone():weapon()), with one weapon.
                Check((v.Carrier == null ? vehicles.Find(v.Vehicle) != null
                        : v.Carrier is { Kind: VehicleWeaponCarrier.BackpackDrone } k && k.Backpack == v.Vehicle && backpacks?.Find(k.Backpack)?.Linked(BackpackLinkedEntity.Drone) != null
                            && k.Api == CarrierAccessor(k.Backpack) && v.Mounts.Count(m => m.Weapon != null) == 1)
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
            // Consumers name other mounts as "<vehicle> (slot N)": the weapon key of that mount, so one settings row gets one identity
            // whichever mount reaches it.
            var mountKeys = c.Vehicles.SelectMany(v => v.Mounts.Where(m => m.Weapon != null).Select(m => (Name: v.Vehicle + " (slot " + m.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", m.Weapon!.Key)))
                .ToDictionary(x => x.Name, x => x.Key, StringComparer.Ordinal);
            return new() { Vehicles = c.Vehicles, Scopes = c.Scopes, Published = published,
                FieldInstances = all.Select(f => Adapt(f, carried.Contains(Split(f.Weapon).Vehicle), SharedRow(f, mountKeys))).ToArray() };
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

    // 0.28.0 Guard Dog drones: the weapon a backpack's drone carries is reached through the backpack, never through hd2.vehicle.
    public static string CarrierAccessor(string backpack) => "hd2.backpack(" + Generation.LuaGenerator.Quote(backpack) + "):drone():weapon()";
    // A shared settings row identified by every weapon that fires it (scope, component, attack role and the canonical consumer set). Only
    // used to find one row edited through two targets; OwnerKey (and so every saved evidence hash) is unchanged.
    private static string? SharedRow(VehicleWeaponFieldInstance f, IReadOnlyDictionary<string, string> mountKeys) => f.Scope is "weapon_local" or "shared_mounted_weapon" ? null
        : "vehicle-weapon-row:" + f.Scope + "|" + f.BackingComponent + "|" + (f.Target.Attack ?? "") + "|"
            + string.Join("|", f.OtherConsumers.Select(o => mountKeys.GetValueOrDefault(o) ?? o).Append(f.Weapon).Distinct().Order(StringComparer.Ordinal));

    private static EntityField Adapt(VehicleWeaponFieldInstance f, bool carried, string? sharedRow)
    {
        var owner = OwnerKey(f); var (vehicle, _) = Split(f.Weapon);
        // A drone weapon's target names the drone (Linked), which only a carried weapon has: vehicle-mounted targets (and their evidence) are unchanged.
        var target = new EntityTarget("vehicle_weapon", f.Target.Path, Weapon: f.Weapon, Attack: f.Target.Attack, Linked: carried ? BackpackLinkedEntity.Drone : null);
        // One transaction per target and native component; all of a vehicle's operations form one plan.
        var operation = "vehicle-weapon-op:" + f.Weapon + "|" + f.Target.Path + "|" + (f.Target.Attack ?? "") + "|" + f.BackingComponent;
        var reason = !AuthoredTypes.VehicleWeapon(f, carried) ? AuthoredTypes.NotAuthoredReason : f.ApiFieldConstant == null ? NoApiConstantReason : null;
        return new EntityField(f.InstanceKey, f.SemanticFieldId, f.DisplayName, f.Type, f.Unit, f.Baseline.Clone(), f.Writable && reason == null, reason, target,
            owner, operation, "vehicle-weapon-plan:" + vehicle, "patch_or_transaction", f.AllowSharedRequired, f.Scope != "weapon_local", [], owner,
            ReviewedScopeComplete: f.Scope == "weapon_local", DynamicConsumersPossible: f.Scope is "shared_projectile" or "shared_damage" or "shared_explosion" or "shared_beam" or "shared_arc",
            BackingObjectKind: f.BackingComponent, Domain: f.SemanticFieldId.Split('.')[0], ApiFieldConstant: f.ApiFieldConstant ?? "", PlanPhase: 1, DependsOn: [],
            Evidence: new EntityEvidence(f.GameplayEvidence != null ? "gameplay_proven" : "structural_reference", ReferenceMod: f.GameplayEvidence),
            Provenance: "VehicleWeaponCapabilities (" + f.Scope + ", " + f.BackingComponent + ")", Acknowledgement: f.Acknowledgement,
            LiveProvenValues: f.LiveProvenValues, SharedRow: sharedRow);
    }
}
