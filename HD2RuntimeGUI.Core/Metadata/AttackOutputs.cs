using HD2RuntimeGUI.Core.Localization;
using System.Text.Json.Serialization;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Attack outputs and active projectile sources (hd2runtime.attack_outputs.v1 schema 2, sdk/AttackOutputCapabilities.json; unreleased
// HD2Runtime 0.28.0 development SDKs). For every player attack Runtime publishes which member its shot actually fires (ACTIVE_DIRECT: the
// attack's own reference; INDIRECT: the default ammunition's projectile, patched in when the weapon is built; AMBIGUOUS / BLOCKED:
// nothing writable), and which catalogued outputs a projectile host can fire instead, with their package, proof and opt-ins.
public sealed record AttackOutputOwner(string Kind, string Name);
public sealed record AttackOutputPackage(string? Name, bool Known, bool AutoLoad);
public sealed record AttackOutputAcknowledgements(string[] SameClass, string[] CrossClass);
public sealed record AttackOutputLiveProof(string DonorOutput, string[] Tests, string[] ProvenOnHosts);
public sealed record AttackOutputChain(bool ImpactExplosion, bool ExpiryExplosion, bool Submunition, bool ArcOnImpact);
// 0.28.0 projectile builder: a row's three reference slots. A slot write targets the row (hd2.attack_output(row)), never a host, and
// changes every entity that fires that row (allow_shared when more than one does). Its value is a donor's slot handle or 'none'.
public sealed record AttackOutputSlot(string Field, bool Current, bool AllowNone, bool Shared, int SharedConsumerCount, int ConsumerReferences,
    string[] NamedSharedConsumers, string[] Acknowledgements, string[] LiveProvenValues, string ValueHandle);
public sealed record AttackOutputSlots(AttackOutputSlot DirectDamage, AttackOutputSlot ImpactExplosion, AttackOutputSlot ExpiryExplosion)
{
    public const string DirectDamageKey = "directDamage", ImpactExplosionKey = "impactExplosion", ExpiryExplosionKey = "expiryExplosion";
    public static readonly string[] Keys = [DirectDamageKey, ImpactExplosionKey, ExpiryExplosionKey];
    public AttackOutputSlot this[string key] => key switch
    {
        DirectDamageKey => DirectDamage, ImpactExplosionKey => ImpactExplosion, ExpiryExplosionKey => ExpiryExplosion,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}
// 0.28.0: the short label and HUD icon a weapon-function mode shows for this row (hd2.fields.presentation.mode_label / mode_icon).
public sealed record AttackOutputPresentationProof(string[] Labels, string[] Icons);
public sealed record AttackOutputPresentation(string Label, string Icon, bool Writable, bool Shared, string[] Acknowledgements, AttackOutputPresentationProof LiveProven);
// 0.28.0: an independent native row identical to its twin except its references (an interim borrowed vanilla row, re-proven before every write).
public sealed record AttackOutputSpareTwin(string TwinOf, string TwinName, bool Independent, int Consumers, bool BorrowedVanillaRow, bool Interim, bool BuildScoped,
    string[] DifferingReferences, string Reason);
public sealed record AttackOutput(string SemanticId, string Family, string Kind, AttackOutputOwner Owner, string Emitter, string? CompatibilityClass,
    bool SelectableAsProjectileReference, string[] CompatibleHostFamilies, string[]? RequiredCoordinatedReferences, string? BlockedReason,
    AttackOutputChain? Chain, string[] OwnerFireResource, AttackOutputPackage Package, AttackOutputAcknowledgements? Acknowledgements,
    string? OwnerFiresThisProjectile, AttackOutputLiveProof? LiveProof,
    // 0.28.0: builder slots and mode presentation of a selectable row; referenceScope limits where a row may be referenced (the spare
    // twin and the EMS Mortar donor only as a programmable-ammunition projectile); fieldEffect describes a donor's lingering field.
    AttackOutputPresentation? Presentation = null, AttackOutputSlots? Slots = null, string[]? ReferenceScope = null,
    System.Text.Json.JsonElement? FieldEffect = null, AttackOutputSpareTwin? SpareTwin = null)
{
    public const string FunctionAmmoScope = "function_ammo.projectile";
    // A row with a reference scope may be referenced only where the scope names (not as an attack's projectile).
    [JsonIgnore] public bool AttackReferenceAllowed => ReferenceScope is not { Length: > 0 };
    [JsonIgnore] public string Label => Owner.Name;
    [JsonIgnore] public string KindLabel => Kind.Replace('_', ' ');
}
// Kind (0.28.0): player_weapon (absent before), support_weapon or vehicle_weapon (Weapon is then "<vehicle> / <mount>"). sharedEntity names
// other hosts that are the same weapon entity (the FRV guns), so writing one changes them all.
public sealed record AttackProjectileSource(string Weapon, string Attack, string Status, string? Mechanism, string? Member, string Reason,
    bool DirectWritable, bool PreviouslyWritable, string? Write, string? Kind = null, string[]? SharedEntity = null)
{
    public const string ActiveDirect = "ACTIVE_DIRECT", Indirect = "INDIRECT";
    public const string PlayerKind = "player_weapon", SupportKind = "support_weapon", VehicleKind = "vehicle_weapon";
    [JsonIgnore] public string HostKind => Kind ?? PlayerKind;
}
public sealed record AmmunitionSource(string Weapon, string SemanticId, string Item, string CompatibilityClass, string[] SharedWithWeapons, string SharedReason,
    string AppliesWhen, string[] Acknowledgements, string[] CrossClassAcknowledgements, string? EffectReason, string? LiveProof);
public sealed record ProvenComposition(string Host, string Output, string Mechanism, string[] AcknowledgementsRequired);
public sealed record HostLiveProof(string Test, string Result, string Family, bool HostReadsReference, bool Superseded);
public sealed record AttackOutputHostModel(string[] ComponentHosts, string[] AmmunitionHosts, string Rule, Dictionary<string, HostLiveProof[]>? HostLiveProof = null);
public sealed record AttackOutputSummary(int Outputs, int Selectable, int ProjectileHosts, int ComponentHosts, int AmmunitionHosts, int DirectWritableAttackFields,
    Dictionary<string, int>? ByFamily = null, string[]? StratagemDonors = null);
// 0.28.0 projectile builder contract (projectileBuilder): the composition classes, the three slots and their guards.
public sealed record ProjectileBuilderClass(System.Text.Json.JsonElement Supported, bool Interim, string Reason);
public sealed record ProjectileBuilderRows(int Total, int Referenced, int Unreferenced);
public sealed record ProjectileBuilderSpareTwin(string Output, string TwinOf, bool BorrowedVanillaRow, bool Interim, string[] DifferingReferences);
public sealed record ProjectileBuilderSlot(string Field, string Key, string Kind, string None, string Meaning);
public sealed record ProjectileBuilder(Dictionary<string, string> Api, Dictionary<string, ProjectileBuilderClass> Classes, ProjectileBuilderRows Rows,
    ProjectileBuilderSpareTwin[] SpareTwins, ProjectileBuilderSlot[] Slots, Dictionary<string, string> Guards, string[] UnprovenMembers, string[] Examples);
// 0.28.0 weapon-function mode presentation: the native short labels (each with its exact native icon or the generic fallback) and icons.
public sealed record ModeLabel(string Value, string? Label, bool Native, string Icon, string IconSource);
public sealed record ModeIcon(string Value, string Source, bool GenericFallback, string? Description);
public sealed record ModePresentation(Dictionary<string, string> Fields, string Target, string Model, string Fallback, ModeLabel[] Labels, ModeIcon[] Icons,
    string AutoIcon, string GenericFallbackIcon, string DefaultIcon, string Acknowledgement, string CustomText, string CustomIcons)
{
    public const string Auto = "auto", Default = "default", NoLabel = "none";
}
public sealed record AttackOutputSafety(bool RuntimeAddresses, bool NativeIdentifiers, int WritesDuringGeneration);
public sealed record AttackOutputCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, AttackProjectileSource[] ProjectileSources,
    AmmunitionSource[] AmmunitionSources, ProvenComposition[] ProvenCompositions, AttackOutputHostModel HostModel, AttackOutputSummary Summary, AttackOutput[] Outputs,
    AttackOutputSafety Safety, Dictionary<string, string>? ActiveSourceStatuses = null, ProjectileBuilder? ProjectileBuilder = null, ModePresentation? ModePresentation = null);

/// <summary>How a host attack's fired projectile is written: the attack's own reference ("component") or its default ammunition's projectile.</summary>
public sealed record AttackOutputHost(string Weapon, string Role, string Mechanism, string CompatibilityClass, string[] BaseAcknowledgements, AmmunitionSource? Ammunition,
    bool CrossClassHost)
{
    public const string Component = "component", AmmunitionMechanism = "ammunition", ProgrammableAmmo = "programmable_ammo";
}
/// <summary>One donor output for a host: whether it may be chosen (with Runtime's refusal otherwise) and the opt-ins the write needs.</summary>
public sealed record AttackOutputDonor(AttackOutput Output, bool CrossClass, bool Allowed, string? Refusal, string[] Acknowledgements, bool ProvenOnHost);

public sealed class AttackOutputCatalog
{
    public required AttackOutput[] Outputs { get; init; }
    public required AttackProjectileSource[] ProjectileSources { get; init; }
    public required AmmunitionSource[] AmmunitionSources { get; init; }
    public required ProvenComposition[] ProvenCompositions { get; init; }
    public required AttackOutputHostModel HostModel { get; init; }
    public required AttackOutputSummary Summary { get; init; }
    // 0.28.0; null on older SDKs.
    public ProjectileBuilder? Builder { get; init; }
    public ModePresentation? ModePresentation { get; init; }
    public AttackOutput? Output(string semanticId) => Outputs.FirstOrDefault(o => o.SemanticId == semanticId);
    public AttackProjectileSource? Source(string weapon, string role) => ProjectileSources.FirstOrDefault(s => s.Weapon == weapon && s.Attack == role);
    public AmmunitionSource? Ammunition(string weapon) => AmmunitionSources.FirstOrDefault(a => a.Weapon == weapon);
    // The output a player weapon's own attack fires (its own row, the baseline of a swap).
    public AttackOutput? Own(string weapon) => Outputs.FirstOrDefault(o => o.Owner.Name == weapon && o.Family == "projectile");

    // A host whose fired projectile Runtime can write, or null with Runtime's reason (AMBIGUOUS, BLOCKED, or not a projectile attack).
    public AttackOutputHost? Host(PlayerWeaponCatalog weapons, string weapon, string role, out string reason)
    {
        var source = Source(weapon, role);
        reason = source?.Reason ?? CoreText.Get("Messages.Output.NoActiveSource");
        if (source == null) return null;
        if (source.Status == AttackProjectileSource.ActiveDirect && source.DirectWritable)
        {
            var field = weapons.Weapons.FirstOrDefault(w => w.Name == weapon)?.Fields.FirstOrDefault(f => f.Domain == "attack" && f.ReferenceRole == role);
            if (field is not { Editable: true, CompatibilityClass: { } cls }) return null;
            return new(weapon, role, AttackOutputHost.Component, cls, [], null, HostModel.ComponentHosts.Contains(weapon));
        }
        if (source.Status == AttackProjectileSource.Indirect && source.Mechanism == AttackOutputHost.AmmunitionMechanism && Ammunition(weapon) is { } ammunition)
            return new(weapon, role, AttackOutputHost.AmmunitionMechanism, ammunition.CompatibilityClass, ammunition.Acknowledgements, ammunition, HostModel.AmmunitionHosts.Contains(weapon));
        return null;
    }
    // Every selectable projectile output for a host, with Runtime's rules: the same compatibility class is always allowed; another class
    // only for a published cross-class host, and then with allow_unverified_reference + allow_unverified_effect unless the exact
    // composition was proven in play. Ammunition hosts add their ammunition row's own opt-ins (allow_shared, allow_unverified_effect).
    public IReadOnlyList<AttackOutputDonor> Donors(AttackOutputHost host) => Outputs
        .Where(o => o.Family == "projectile" && o.SelectableAsProjectileReference && o.AttackReferenceAllowed && o.Owner.Name != host.Weapon)
        .Select(o =>
        {
            var cross = o.CompatibilityClass != host.CompatibilityClass;
            var proven = ProvenCompositions.Any(p => p.Host == host.Weapon && p.Output == o.SemanticId && p.Mechanism == host.Mechanism);
            string? refusal = cross && !host.CrossClassHost ? "CROSS_CLASS_HOST_REJECTED: " + host.Weapon + " is not a published cross-class projectile host; it can take only "
                + host.CompatibilityClass.Replace('_', ' ') + " outputs." : null;
            var extra = !cross ? o.Acknowledgements!.SameClass : proven ? [] : host.Ammunition?.CrossClassAcknowledgements ?? o.Acknowledgements!.CrossClass;
            return new AttackOutputDonor(o, cross, refusal == null, refusal, [.. host.BaseAcknowledgements.Concat(extra).Distinct().Order(StringComparer.Ordinal)], proven);
        })
        .OrderBy(d => d.Allowed ? 0 : 1).ThenBy(d => d.CrossClass ? 1 : 0).ThenBy(d => d.Output.Label, StringComparer.OrdinalIgnoreCase).ToArray();
}

public static class AttackOutputReader
{
    public const string FileName = "AttackOutputCapabilities.json", Contract = "hd2runtime.attack_outputs.v1";
    public const int MaxBytes = 4 * 1024 * 1024;
    public static readonly string[] Families = ["projectile", "beam", "arc", "spray", "melee"];
    public static readonly string[] Statuses = [AttackProjectileSource.ActiveDirect, AttackProjectileSource.Indirect, "DORMANT_OR_METADATA", "AMBIGUOUS", "BLOCKED"];
    private static readonly string[] Opt = ["allow_shared", "allow_unverified_effect", "allow_unverified_reference"];
    // Research annotations (Liberator cases, reachable references, live controls) are not part of the authoring surface.
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent attack output metadata (check {line})."); }

    public static AttackOutputCatalog Read(byte[] bytes, string version, PlayerWeaponCatalog weapons)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Attack output file exceeds size limit.");
            using var doc = System.Text.Json.JsonDocument.Parse(bytes, new System.Text.Json.JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 2)
                throw new UnsupportedSdkException("Unsupported attack output contract.");
            var c = System.Text.Json.JsonSerializer.Deserialize<AttackOutputCatalogJson>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && !c.Safety.RuntimeAddresses && !c.Safety.NativeIdentifiers && c.Safety.WritesDuringGeneration == 0);
            Check(c.Outputs.Select(o => o.SemanticId).Distinct().Count() == c.Outputs.Length);
            foreach (var o in c.Outputs)
            {
                Check(o.SemanticId.StartsWith("output/v1/" + o.Family + "/", StringComparison.Ordinal) && Families.Contains(o.Family) && !string.IsNullOrWhiteSpace(o.Owner.Name));
                // A selectable output is a projectile whose owner fires it, with its package and opt-ins; everything else says why not.
                Check(o.SelectableAsProjectileReference
                    ? o.Family == "projectile" && o.CompatibilityClass != null && o.Acknowledgements != null && o.CompatibleHostFamilies.Contains("projectile")
                        && o.Acknowledgements.SameClass.Concat(o.Acknowledgements.CrossClass).All(Opt.Contains) && o.BlockedReason == null
                    : o.Family != "projectile" ? !string.IsNullOrWhiteSpace(o.BlockedReason) : true);
            }
            Check(c.ProjectileSources.Select(s => (s.Weapon, s.Attack)).Distinct().Count() == c.ProjectileSources.Length
                && c.ProjectileSources.All(s => Statuses.Contains(s.Status) && !string.IsNullOrWhiteSpace(s.Reason) && (!s.DirectWritable || s.Status == AttackProjectileSource.ActiveDirect)
                    && s.HostKind is AttackProjectileSource.PlayerKind or AttackProjectileSource.SupportKind or AttackProjectileSource.VehicleKind
                    && (s.SharedEntity ?? []).All(e => c.ProjectileSources.Any(o => o.Weapon == e && o.Attack == s.Attack && o.HostKind == s.HostKind))));
            // Every component host fires a directly writable source.
            Check(c.HostModel.ComponentHosts.All(h => c.ProjectileSources.Any(s => s.Weapon == h && s.DirectWritable)));
            // A directly writable source is exactly a player attack field Runtime publishes as editable (the capability catalog agrees).
            foreach (var s in c.ProjectileSources.Where(s => s.HostKind == AttackProjectileSource.PlayerKind && weapons.Weapons.Any(w => w.Name == s.Weapon)))
            {
                var field = weapons.Weapon(s.Weapon).Fields.FirstOrDefault(f => f.Domain == "attack" && f.ReferenceRole == s.Attack);
                Check(field == null || field.Editable == s.DirectWritable);
            }
            Check(c.AmmunitionSources.Select(a => a.Weapon).Distinct().Count() == c.AmmunitionSources.Length
                && c.AmmunitionSources.All(a => c.HostModel.AmmunitionHosts.Contains(a.Weapon) && a.Acknowledgements.Concat(a.CrossClassAcknowledgements).All(Opt.Contains)
                    && a.Acknowledgements.Contains("allow_shared") && a.SemanticId.StartsWith("ammunition/v1/", StringComparison.Ordinal)
                    && c.ProjectileSources.Any(s => s.Weapon == a.Weapon && s.Status == AttackProjectileSource.Indirect && s.Mechanism == "ammunition")));
            Check(c.ProvenCompositions.All(p => c.Outputs.Any(o => o.SemanticId == p.Output && o.SelectableAsProjectileReference)
                && p.Mechanism is AttackOutputHost.Component or AttackOutputHost.AmmunitionMechanism or AttackOutputHost.ProgrammableAmmo && p.AcknowledgementsRequired.All(Opt.Contains)));
            if (c.ProjectileBuilder != null || c.ModePresentation != null) ValidateBuilder(c);
            var s0 = c.Summary;
            Check(s0.Outputs == c.Outputs.Length && s0.Selectable == c.Outputs.Count(o => o.SelectableAsProjectileReference)
                && s0.ComponentHosts == c.HostModel.ComponentHosts.Length && s0.AmmunitionHosts == c.HostModel.AmmunitionHosts.Length
                && s0.DirectWritableAttackFields == c.ProjectileSources.Count(p => p.DirectWritable));
            return new() { Outputs = c.Outputs, ProjectileSources = c.ProjectileSources, AmmunitionSources = c.AmmunitionSources, ProvenCompositions = c.ProvenCompositions,
                HostModel = c.HostModel, Summary = c.Summary, Builder = c.ProjectileBuilder, ModePresentation = c.ModePresentation };
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed attack output metadata.", e); }
    }
    // 0.28.0: the builder and mode presentation come together; every selectable projectile row publishes its three slots and its
    // presentation, a slot's opt-ins follow its sharing, and live-proven values name real donor slots.
    private static void ValidateBuilder(AttackOutputCatalogJson c)
    {
        Check(c.ProjectileBuilder != null && c.ModePresentation != null);
        var builder = c.ProjectileBuilder!; var modes = c.ModePresentation!;
        Check(builder.Slots.Select(s => s.Key).SequenceEqual(AttackOutputSlots.Keys)
            && builder.Slots.Select(s => s.Field).SequenceEqual(["hd2.fields.projectile.direct_damage", "hd2.fields.projectile.impact_explosion", "hd2.fields.projectile.expiry_explosion"]));
        var outputs = c.Outputs.ToDictionary(o => o.SemanticId, StringComparer.Ordinal);
        Check(builder.SpareTwins.All(t => outputs.TryGetValue(t.Output, out var o) && o.SpareTwin?.TwinOf == t.TwinOf && outputs.ContainsKey(t.TwinOf)));
        foreach (var o in c.Outputs)
        {
            Check((o.SelectableAsProjectileReference && o.Family == "projectile") == (o.Slots != null) && (o.Slots != null) == (o.Presentation != null));
            Check(o.ReferenceScope is null || o.ReferenceScope.SequenceEqual([AttackOutput.FunctionAmmoScope]));
            Check(o.SpareTwin == null || o.SpareTwin.TwinOf != o.SemanticId && outputs.ContainsKey(o.SpareTwin.TwinOf) && o.ReferenceScope != null);
            if (o.Slots is not { } slots) continue;
            foreach (var key in AttackOutputSlots.Keys)
            {
                var s = slots[key]; var spec = builder.Slots.Single(x => x.Key == key);
                Check(s.Field == spec.Field && s.AllowNone == (key != AttackOutputSlots.DirectDamageKey) && s.Shared == (s.SharedConsumerCount > 1)
                    && s.Acknowledgements.All(Opt.Contains) && s.Acknowledgements.Contains("allow_unverified_effect") && s.Acknowledgements.Contains("allow_shared") == s.Shared
                    && s.LiveProvenValues.All(v => v == "none" ? s.AllowNone
                        : v.Split('#') is [var donor, var slot] && AttackOutputSlots.Keys.Contains(slot) && outputs.TryGetValue(donor, out var d) && d.Slots != null
                            && (slot == AttackOutputSlots.DirectDamageKey) == (key == AttackOutputSlots.DirectDamageKey)));
            }
            var p = o.Presentation!;
            Check(p.Acknowledgements.All(Opt.Contains) && p.Acknowledgements.Contains("allow_shared") == p.Shared
                // The current label may be a native one no mode can be given (unnamed_<hash>, he_<hash>); only the writable list is closed.
                && !string.IsNullOrWhiteSpace(p.Label) && modes.Icons.Any(i => i.Value == p.Icon)
                && p.LiveProven.Labels.All(l => modes.Labels.Any(x => x.Value == l)) && p.LiveProven.Icons.All(i => modes.Icons.Any(x => x.Value == i)));
        }
        Check(modes.Labels.Select(l => l.Value).Distinct().Count() == modes.Labels.Length && modes.Icons.Select(i => i.Value).Distinct().Count() == modes.Icons.Length
            && modes.Labels.Any(l => l.Value == ModePresentation.NoLabel) && modes.Icons.Any(i => i.Value == ModePresentation.Auto) && modes.Icons.Any(i => i.Value == ModePresentation.Default)
            && modes.Icons.Any(i => i.Value == modes.GenericFallbackIcon && i.GenericFallback)
            && modes.Labels.All(l => modes.Icons.Any(i => i.Value == l.Icon) && l.IconSource is "exact_native" or "generic_fallback"));
    }
}
