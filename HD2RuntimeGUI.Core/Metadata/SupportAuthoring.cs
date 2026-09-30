using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record SupportBranchLabel(string Name, string Kind, string State);
public sealed record SupportTarget(string Resource, string Path, string? AttackRole, string? AttackKind, string? ParentAttackRole,
    string[] Accessor, SupportBranchLabel[] CatalogBranches, SupportBranchLabel[] WritableCatalogBranches);
public sealed record SupportIdentity(string WeaponKey, string Name, string IdentityStatus);
public sealed record SupportDisplay(string Name, string Domain, string Group);
public sealed record SupportValue(string Kind, string Type, JsonElement Baseline, JsonElement Expected, string? Unit, JsonElement Reference);
public sealed record SupportBacking(string ObjectKey, string Kind, string SemanticType, string Domain, string OperationGroupingKey, string RuntimeBackingScope);
public sealed record SupportConsumer(string Weapon, string TargetPath, string? AttackRole, string? AttackKind, string? ParentAttackRole,
    SupportBranchLabel[] CatalogBranches, SupportBranchLabel[] WritableCatalogBranches, string ConsumerKey);
public sealed record SupportScope(string ScopeKey, bool Shared, bool RequiresAcknowledgement, int ReviewedConsumerCount,
    SupportConsumer[] AffectedSemanticConsumers, bool ReviewedScopeComplete, bool DynamicConsumersPossible);
public sealed record SupportOperation(string MinimumApi, bool PatchSupported, bool TransactionSupported, bool TransactionRequired,
    string TransactionGroupingKey, bool PlanSupported, bool PlanRequired, bool PlanRequiredForMultipleBackingObjects,
    string PlanGroupingKey, int Phase, string[] Dependencies, bool AllowSharedRequired,
    // 0.24.0+ per-field opt-in. Omitted from evidence hashes when absent, so unchanged fields keep their saved evidence across rebinds.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Acknowledgement = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AcknowledgementReason = null);
public sealed record SupportResolution(string[] RootChain, string Linkage, string? ParentObjectKey, string? TerminalPhase,
    int PlanPhase, string[] PlanDependencies, bool RequiresLaterPlanPhase, JsonElement TargetFrom);
public sealed record SupportProvenance(string Identity, string Semantics, string Ownership, string Baseline, string Validation, string EvidenceArtifact);
// 0.26.0: fire-mode (fire_mode.modes / burst_rounds) and third-person reticle metadata on support-weapon field instances.
public sealed record SupportFireMode(string FireModeState, IReadOnlyList<int>? NativeSlots, IReadOnlyList<string>? AllowedModes,
    Dictionary<string, int>? ModeValues, int? MaxModes, FireModeSelector? Selector, WeaponFieldEvidence? Evidence);
public sealed record SupportReticle(string ReticleState, int? NativeValue, string? NativeName, ReticleEncoding? Encoding, WeaponFieldEvidence? Evidence);
// 0.28.0 weapon composition on support-weapon field instances: rate-of-fire slots ({X, Y, Z} in menu order, the default slot, the
// selector traversal and binding), weapon-function inputs, programmable ammunition, armory presentation and the projectile a
// support weapon fires (a unified projectile host, hd2.support_weapon(name):attack(role)).
public sealed record SupportFireRate(string FireRateState, IReadOnlyList<double> NativeSlots, IReadOnlyList<string> SlotNames, string DefaultSlot,
    IReadOnlyList<string> SelectorOrder, int MaxModes, bool SelectorBound, string? SelectorInput, IReadOnlyList<string> BindableInputs,
    double Min, double Max, IReadOnlyList<string> OverriddenWhenEquipped);
public sealed record SupportFunctionAmmo(string FunctionAmmoState, bool SelectorBound, string? SelectorInput, IReadOnlyList<string> BindableInputs, string? CompatibilityClass);
public sealed record SupportPresentation(JsonElement? Labels, int? MaxTraits, IReadOnlyList<string>? AllowedValues, string? ArmorPenetrationState);
public sealed record SupportWeaponFunction(string Input, IReadOnlyList<string> AllowedValues);
public sealed record SupportProjectileReference(string ReferenceKind, string CompatibilityClass, string ReferenceRole, WeaponProjectileSource ProjectileSource,
    ProjectileResidency? Residency, JsonElement? Effect);
public sealed record SupportField(string InstanceKey, string SupportWeapon, SupportIdentity SupportWeaponIdentity, SupportTarget Target,
    string SemanticFieldId, string QualifiedSemanticFieldId, string ApiFieldConstant, SupportDisplay Display, SupportValue Value,
    bool Writable, bool ReadOnly, string? BlockedReason, SupportBacking Backing, SupportScope SharedScope,
    SupportOperation Operation, SupportResolution Resolution, SupportProvenance Provenance,
    SupportFireMode? FireMode = null, SupportReticle? Reticle = null, FieldLiveEvidence? LiveEvidence = null,
    SupportProjectileReference? ProjectileReference = null, SupportFireRate? FireRate = null, SupportFunctionAmmo? FunctionAmmo = null,
    SupportPresentation? Presentation = null, SupportWeaponFunction? WeaponFunction = null, JsonElement? Effect = null)
{
    // A typed status reference (value kind "reference"): a status key, or 'none'.
    [JsonIgnore] public bool IsStatusReference => Value.Kind == "reference" && Value.Type == WeaponCapability.StatusReference;
    // 0.28.0 typed references: the projectile an attack fires ({weapon, attack} baseline) and the programmable-ammunition projectile.
    [JsonIgnore] public bool IsProjectileReference => Value.Kind == "reference" && Value.Type == "projectile_reference";
    [JsonIgnore] public bool IsFunctionProjectile => Value.Kind == "reference" && Value.Type == WeaponCapability.FunctionProjectileReference;
    // Plain scalars (numbers, integers, booleans) and the published list / label types.
    [JsonIgnore] public bool IsScalar => Value.Kind == "scalar";
}
// 0.26.0: support weapons whose ammunition is stored in their backpack (support weapon -> ammoBackpack).
public sealed record SupportAmmoBackpackBaseline(int Capacity, int StartAmount, int RefillAmount);
public sealed record SupportAmmoBackpack(string Backpack, string SemanticId, string Accessor, string Owner, bool WeaponOwnsMagazine,
    string[] Fields, SupportAmmoBackpackBaseline Baseline, string Catalog);
public sealed record SupportBlock(string Field, string Reason);
// Forward call-in link. 0.22.0 publishes only known/kind; 0.22.1+ adds state, the linked stratagem semantic ID, relationship ID and blocker.
public sealed record SupportLinkedStratagem(bool Known, string? Kind = null, string? State = null, string? SemanticId = null, string? RelationshipId = null,
    string? Relationship = null, string? StratagemName = null, string? DeliveryObject = null, string? Special = null, string? Confidence = null, string? Blocker = null,
    SupportNoCallIn? NoCallIn = null)
{
    // 0.25.0+: Runtime proves the game defines no call-in for this equipment (state no_call_in); it is standalone, not unresolved.
    public bool IsNoCallIn => this is { Known: false, State: "no_call_in", SemanticId: null, RelationshipId: null } && !string.IsNullOrWhiteSpace(NoCallIn?.Reason);
}
public sealed record SupportAcquisition(string Kind, string? Source = null, bool NativeDeliveryProven = false, string? Note = null);
public sealed record SupportNoCallIn(string Reason, SupportAcquisition? Acquisition = null, string[]? LoadoutItemTypes = null, bool? SharedLoadoutPackage = null);
// 0.24.0+: DELIVERY_RESOLVED identities are proven through their call-in delivery chain (identityResolution), not by name.
public sealed record SupportIdentityResolution(string Basis, string[] Evidence, int NonDeliveredNativeRoots, bool NonDeliveredRootsAffected, string Note, string EvidenceArtifact);
public sealed record SupportAuthoringWeapon(string Name, string IdentityStatus, string Confidence, string[] Family,
    bool Writable, int WritableFieldCount, string[] FieldInstanceKeys, SupportBlock[] BlockedFields, SupportLinkedStratagem? LinkedStratagem = null, string? SemanticId = null,
    SupportIdentityResolution? IdentityResolution = null, SupportAmmoBackpack? AmmoBackpack = null)
{
    public const string Unique = "UNIQUE", DeliveryResolved = "DELIVERY_RESOLVED";
    // A writable identity: unique, or resolved by Runtime through its call-in delivery chain.
    public static bool Resolved(string status) => status is Unique or DeliveryResolved;
}
public sealed record SupportObject(string ObjectKey, string Kind, string SemanticType, string[] Domains, bool Shared,
    bool RequiresSharedAcknowledgement, string SharedScopeKey, int ReviewedConsumerCount, SupportConsumer[] AffectedSemanticConsumers,
    bool ReviewedScopeComplete, bool DynamicConsumersPossible, string[] FieldInstanceKeys);
public sealed record SupportOperationGroup(string OperationGroupingKey, string BackingObjectKey, string RuntimeBackingScope,
    SupportTarget Target, string[] FieldInstanceKeys, string RecommendedApi, bool AllowSharedRequired, string PlanGroupingKey, int Phase, string[] Dependencies);
public sealed record SupportAuthoringSummary(int CatalogWeapons, int WritableSupportWeapons, int WritableFieldInstances,
    int PublishedSupportFieldInstances, int BackingObjectCount, int OperationGroupingCount, int SharedConsumerScopeCount, int IntentionallyOmittedInstances);
public sealed record SupportPlanContract(string Api, int CurrentPhase, bool CurrentInstancesRequireTargetFrom,
    bool OneOperationPerOperationGroupingKey, bool MultipleBackingObjectsRequirePlan);
public sealed record SupportAuthoringCatalog(int SchemaVersion, string Contract, string Hd2RuntimeVersion, SupportAuthoringSummary Summary,
    SupportAuthoringWeapon[] Weapons, SupportField[] FieldInstances, SupportObject[] BackingObjects,
    SupportOperationGroup[] OperationGroups, SupportPlanContract PlanContract, SupportCallInLinkage? SupportCallInLinks = null)
{
    public SupportField Field(string key) => FieldInstances.SingleOrDefault(f => f.InstanceKey == key)
        ?? throw new InvalidDataException("Support capability is missing from this SDK. Reset or review the saved modification.");
}
public interface ISupportAuthoringReader { SupportAuthoringCatalog Read(byte[] bytes, string version); }
public sealed class SupportAuthoringReader : ISupportAuthoringReader
{
    public const string FileName = "SupportWeaponAuthoringCapabilities.json";
    public const int MaxBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    public SupportAuthoringCatalog Read(byte[] bytes, string version)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Support capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 }); MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 2 || doc.RootElement.GetProperty("contract").GetString() != "hd2runtime.support_weapon.guarded_authoring.v2")
                throw new UnsupportedSdkException("Support authoring requires the canonical schema-v2 instance contract.");
            var c = JsonSerializer.Deserialize<SupportAuthoringCatalog>(bytes, Options)!;
            void Check(bool valid) { if (!valid) throw new InvalidDataException("Inconsistent or unsupported support authoring metadata."); }
            Check(c.Hd2RuntimeVersion == version && c.FieldInstances.Length <= 4000 && c.Weapons.Length <= 200);
            var safety = doc.RootElement.GetProperty("safety");
            Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && !safety.GetProperty("rawResourceIdentifiers").GetBoolean());
            Check(safety.GetProperty("writesDuringGeneration").GetInt32() == 0 && safety.GetProperty("protectionChangesDuringGeneration").GetInt32() == 0 && safety.GetProperty("fixtureFallback").GetString() == "disabled");
            Check(c.PlanContract is { Api: "hd2.plan", CurrentPhase: 1, CurrentInstancesRequireTargetFrom: false, OneOperationPerOperationGroupingKey: true, MultipleBackingObjectsRequirePlan: true });
            var fields = c.FieldInstances.ToDictionary(f => f.InstanceKey, StringComparer.Ordinal);
            var weapons = c.Weapons.ToDictionary(w => w.Name, StringComparer.Ordinal);
            var objects = c.BackingObjects.ToDictionary(o => o.ObjectKey, StringComparer.Ordinal);
            var operations = c.OperationGroups.ToDictionary(o => o.OperationGroupingKey, StringComparer.Ordinal);
            Check(c.Summary.CatalogWeapons == weapons.Count && c.Summary.WritableSupportWeapons == weapons.Values.Count(w => w.Writable)
                && c.Summary.WritableFieldInstances == fields.Values.Count(f => f.Writable) && c.Summary.PublishedSupportFieldInstances == fields.Count
                && c.Summary.BackingObjectCount == objects.Count && c.Summary.OperationGroupingCount == operations.Count && c.Summary.IntentionallyOmittedInstances == 0);
            // Delivery-resolved identities (0.24.0+) must carry Runtime's structural evidence; nothing else lifts a duplicate block.
            var deliveryResolved = Models.SemVersion.Parse(version).CompareTo(Models.SemVersion.Parse("0.24.0")) >= 0;
            foreach (var w in c.Weapons)
                Check(w.FieldInstanceKeys.Length == w.WritableFieldCount && w.FieldInstanceKeys.Distinct().Count() == w.FieldInstanceKeys.Length
                    && (!w.Writable || w.IdentityStatus == SupportAuthoringWeapon.Unique
                        || deliveryResolved && w.IdentityStatus == SupportAuthoringWeapon.DeliveryResolved && w.LinkedStratagem is { Known: true, State: "linked" }
                            && w.IdentityResolution is { } r && !string.IsNullOrWhiteSpace(r.Basis) && r.Evidence.Length > 0 && !r.NonDeliveredRootsAffected)
                    && (w.IdentityStatus == SupportAuthoringWeapon.DeliveryResolved) == (w.IdentityResolution != null)
                    && w.FieldInstanceKeys.All(k => fields[k].SupportWeapon == w.Name));
            foreach (var f in fields.Values)
            {
                Check(f.InstanceKey.StartsWith("support-field/v1/", StringComparison.Ordinal) && f.InstanceKey.Length < 512
                    && f.Display != null && !string.IsNullOrWhiteSpace(f.Display.Name) && f.Provenance != null
                    && weapons[f.SupportWeapon].Writable && weapons[f.SupportWeapon].FieldInstanceKeys.Contains(f.InstanceKey)
                    && f.SupportWeaponIdentity.Name == f.SupportWeapon && f.SupportWeaponIdentity.IdentityStatus == weapons[f.SupportWeapon].IdentityStatus
                    && SupportAuthoringWeapon.Resolved(f.SupportWeaponIdentity.IdentityStatus)
                    && (f.Operation.Acknowledgement == null ? f.Operation.AcknowledgementReason == null
                        : f.Operation.Acknowledgement == "allow_unverified_effect" && !string.IsNullOrWhiteSpace(f.Operation.AcknowledgementReason))
                    && f.Writable && !f.ReadOnly && ValidValue(f)
                    && (f.Value.Type == WeaponCapability.FireModeSet) == (f.FireMode != null && f.SemanticFieldId == FireModes.ModesField)
                    && (f.Reticle == null) == (f.SemanticFieldId != FireModes.ReticleField) && (f.Reticle == null || f.Value.Type == "boolean" && f.Reticle.Encoding != null)
                    && JsonElement.DeepEquals(f.Value.Baseline, f.Value.Expected));
                if (f.FireMode is { } fm && f.Value.Type == WeaponCapability.FireModeSet)
                    FireModes.ValidateCapability(fm.FireModeState, f.Value.Baseline, f.Writable, fm.AllowedModes, fm.ModeValues, fm.MaxModes, fm.NativeSlots?.Select(x => (double)x).ToList());
                if (f.IsScalar && f.Value.Type is "number" or "integer" or "boolean" or WeaponCapability.FireModeSet) _ = Generation.SupportScalar.Normalize(f, f.Value.Baseline);
                Check(Regex.IsMatch(f.ApiFieldConstant, @"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z"));
                ValidateTarget(f.Target);
                var o = objects[f.Backing.ObjectKey]; var g = operations[f.Backing.OperationGroupingKey];
                // 0.28.0 weapon_selector groups write a weapon's rates, function bindings and function projectile together in one
                // transaction across its projectile-weapon and weapon-data components; every other group has a single backing object.
                var selector = g.RecommendedApi == "hd2.transaction" && g.FieldInstanceKeys.All(k => fields[k].SemanticFieldId.Split('.')[0] is "fire_rate" or "function_ammo" or "weapon_function")
                    && g.FieldInstanceKeys.Any(k => fields[k].Backing.ObjectKey == g.BackingObjectKey);
                Check(o.FieldInstanceKeys.Contains(f.InstanceKey) && g.FieldInstanceKeys.Contains(f.InstanceKey)
                    && (selector || g.BackingObjectKey == o.ObjectKey && g.RuntimeBackingScope == f.Backing.RuntimeBackingScope)
                    && f.Operation.TransactionGroupingKey == g.OperationGroupingKey
                    && f.Operation.PlanGroupingKey == g.PlanGroupingKey && SameTarget(f.Target, g.Target));
                Check(f.Operation.Phase == g.Phase && g.Phase == 1 && f.Resolution.PlanPhase == 1
                    && f.Operation.Dependencies.Length == 0 && g.Dependencies.Length == 0 && f.Resolution.PlanDependencies.Length == 0
                    && !f.Resolution.RequiresLaterPlanPhase && f.Resolution.TargetFrom.ValueKind == JsonValueKind.Null);
                Check(f.SharedScope.ScopeKey == o.SharedScopeKey && f.SharedScope.Shared == o.Shared
                    && f.SharedScope.RequiresAcknowledgement == o.RequiresSharedAcknowledgement
                    && f.Operation.AllowSharedRequired == o.RequiresSharedAcknowledgement && g.AllowSharedRequired == o.RequiresSharedAcknowledgement
                    && f.SharedScope.ReviewedConsumerCount == f.SharedScope.AffectedSemanticConsumers.Length
                    && f.SharedScope.ReviewedConsumerCount == o.ReviewedConsumerCount
                    && f.SharedScope.ReviewedScopeComplete == o.ReviewedScopeComplete && f.SharedScope.DynamicConsumersPossible == o.DynamicConsumersPossible
                    && JsonSerializer.Serialize(f.SharedScope.AffectedSemanticConsumers) == JsonSerializer.Serialize(o.AffectedSemanticConsumers));
                Check(f.Operation.PatchSupported && f.Operation.TransactionSupported && f.Operation.PlanSupported);
            }
            foreach (var o in objects.Values) Check(o.FieldInstanceKeys.Distinct().Count() == o.FieldInstanceKeys.Length && o.FieldInstanceKeys.All(k => fields[k].Backing.ObjectKey == o.ObjectKey));
            foreach (var g in operations.Values) Check(g.FieldInstanceKeys.Distinct().Count() == g.FieldInstanceKeys.Length && g.FieldInstanceKeys.All(k => fields[k].Backing.OperationGroupingKey == g.OperationGroupingKey));
            Check(c.Summary.SharedConsumerScopeCount == fields.Values.Where(f => f.SharedScope.Shared).Select(f => f.SharedScope.ScopeKey).Distinct().Count());
            // Value types this build does not author yet stay visible with Runtime's baseline, read-only (AuthoredTypes).
            return c.FieldInstances.Any(f => !AuthoredTypes.SupportField(f))
                ? c with { FieldInstances = c.FieldInstances.Select(f => AuthoredTypes.SupportField(f) ? f : f with { Writable = false, ReadOnly = true, BlockedReason = AuthoredTypes.NotAuthoredReason }).ToArray() }
                : c;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed support authoring metadata.", e); }
    }
    // Each published value kind carries its own metadata: rate sets their slots, weapon functions their input, labels their
    // allowed names, and typed references their source class.
    private static bool ValidValue(SupportField f)
    {
        var baseline = f.Value.Baseline;
        static bool Names(IReadOnlyList<string>? names, JsonElement v) => names is { Count: > 0 } && v.ValueKind == JsonValueKind.String && names.Contains(v.GetString()!);
        return (f.Value.Kind, f.Value.Type) switch
        {
            ("reference", WeaponCapability.StatusReference) => baseline.ValueKind == JsonValueKind.String,
            ("reference", "projectile_reference") => f.SemanticFieldId == "attack.projectile" && f.Target.Path == "attack" && f.Target.AttackRole != null
                && baseline.ValueKind == JsonValueKind.Object && f.ProjectileReference is { ReferenceKind: "projectile" } r && r.ReferenceRole == f.Target.AttackRole
                && !string.IsNullOrWhiteSpace(r.CompatibilityClass) && r.ProjectileSource.Status == WeaponProjectileSource.ActiveDirect,
            ("reference", WeaponCapability.FunctionProjectileReference) => f.SemanticFieldId == "function_ammo.projectile" && f.FunctionAmmo != null
                && baseline.ValueKind == JsonValueKind.String,
            ("scalar", "number" or "integer" or "boolean" or WeaponCapability.FireModeSet) => true,
            ("scalar", WeaponCapability.FireRateSet) => f.FireRate is { MaxModes: 3, SlotNames.Count: 3, NativeSlots.Count: 3 } fr && fr.SlotNames.Contains(fr.DefaultSlot)
                && baseline.ValueKind == JsonValueKind.Array && baseline.GetArrayLength() == 3 && baseline.EnumerateArray().Select(x => x.GetDouble()).SequenceEqual(fr.NativeSlots),
            ("scalar", WeaponCapability.WeaponFunction) => f.WeaponFunction is { Input: "left" or "right" } wf && f.SemanticFieldId == "weapon_function." + wf.Input && Names(wf.AllowedValues, baseline),
            ("scalar", WeaponCapability.ArmorPenetrationLabel) => Names(f.Presentation?.AllowedValues, baseline),
            ("scalar", WeaponCapability.TraitSet) => f.Presentation is { MaxTraits: > 0 } p && baseline.ValueKind == JsonValueKind.Array
                && baseline.GetArrayLength() <= p.MaxTraits && baseline.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),
            _ => false,
        };
    }
    public static bool SameTarget(SupportTarget a, SupportTarget b) => a.Resource == b.Resource && a.Path == b.Path && a.AttackRole == b.AttackRole && a.Accessor.SequenceEqual(b.Accessor);
    public static void ValidateTarget(SupportTarget t)
    {
        var expected = t.Path switch { "weapon" => new[] { "support_weapon" }, "attack" => ["support_weapon", "attack"],
            "projectile_reference" => ["support_weapon", "attack", "projectile"], "explosion" => ["support_weapon", "attack", "explosion"], _ => [] };
        if (t.Resource != "support_weapon" || expected.Length == 0 || !t.Accessor.SequenceEqual(expected)
            || (t.Path != "weapon" && (t.AttackRole == null || !Regex.IsMatch(t.AttackRole, @"\A[a-z][a-z_0-9]{0,63}\z"))))
            throw new InvalidDataException("Unsupported semantic support target path.");
    }
}
