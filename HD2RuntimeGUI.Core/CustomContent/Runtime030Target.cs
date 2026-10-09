using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>The HD2Runtime 0.30.0 custom stratagem API: custom stratagems through hd2.custom_stratagem.register, described by
/// the schema hd2runtime-custom-stratagems/1 (docs/custom-stratagem-api.md, docs/custom-stratagem-builder.md); custom projectiles through
/// the internal modules runtime/custom_projectiles and api/actions (docs/custom-projectile-rows.md), which hd2 does not export (solo / host only).
///
/// A custom stratagem goes through the Runtime's own project form (RuntimeProjectForm): the Runtime SDK's validator (RuntimeProjectValidator,
/// a port of sdk/tools/custom_stratagem_project.py) checks it and the SDK's compiler (RuntimeRegistrationLua) writes its register call, so a
/// build registers what the Runtime's own tools would. ModBuilder adds what the project format has no place for: the Lua callbacks and extra
/// fields of the advanced section, and a scripted family. This is the only place that knows the Runtime's field names.</summary>
public sealed class Runtime030Target : ICustomContentTarget
{
    public const string TargetId = "hd2runtime-0.30", Format = "hd2runtime-custom-stratagems/1";
    // The carrier a weapon fires so each shot can be replaced: the TD-110 Maelstrom's slot 2 projectile (no effect, no unit, damage type 0,
    // no explosion). The one carrier Runtime has live-proven (SMG-32 Reprimand, EXO-45 Patriot); Runtime binds one weapon per carrier.
    public const string Carrier = "output/v1/projectile/td-110-maelstrom-slot-2";
    private readonly CustomSchema schema;
    private readonly JsonDocument document;
    public Runtime030Target(CustomSchema schema, byte[] json)
    {
        this.schema = schema;
        document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
    }
    public string Id => TargetId;
    public string RuntimeLabel => "HD2Runtime " + schema.RuntimeVersion;
    // The custom stratagem API was released with HD2Runtime 0.30.0. A later 0.30.x schema that only grows its catalogues or ranges (0.30.2: two
    // sound names; 0.30.3: uses 0 and -1, unlimited) keeps that floor; what it adds raises an export's requirement where it is used (RuntimeRequirement.Floors). A development schema
    // (0.30.0-dev) is its own floor.
    public string MinimumRuntime => SemVersion.Parse(schema.RuntimeVersion).CompareTo(SemVersion.Parse(Generation.RuntimeRequirement.Release030)) > 0
        ? Generation.RuntimeRequirement.Release030 : schema.RuntimeVersion;
    public CustomSchema Schema => schema;
    public string ProjectileCarrier => Carrier;

    // ---- families, carriers and the project form ------------------------------------------------------------------------------------------

    /// <summary>The Runtime's name of a ModBuilder delivery family (the scripted family is its Lua-only "script").</summary>
    public static string RuntimeFamily(string family) => family == DeliveryFamily.Scripted ? RuntimeProjectForm.ScriptFamily : family;
    // The payload key carrier groups are keyed by (carrierGroups[].payloads, .defaults).
    private static string PayloadKey(CustomStratagem s) => s.Delivery.Family switch
    {
        DeliveryFamily.Orbital => s.Delivery.Orbital?.Mode == OrbitalDelivery.NativeBarrage ? "orbital_native" : "orbital",
        DeliveryFamily.Scripted => "runtime",
        var f => f,
    };
    public JsonObject ProjectForm(CustomStratagem stratagem) => RuntimeProjectForm.Stratagem(stratagem, schema.Catalogs.PodItems, SharedHost);
    /// <summary>Whether a weapon-variant host's type is shared: its project form always carries allow_shared = true (a warning says why).</summary>
    public bool SharedHost(string weapon) => schema.VariantDonor(weapon) is { IsShared: true };
    public FamilyEvidence? Evidence(string family) => schema.EvidenceOf(RuntimeFamily(family));
    public IReadOnlyList<CarrierGroup> CarrierGroups(CustomStratagem s)
    {
        var key = PayloadKey(s);
        var only = schema.Family(RuntimeFamily(s.Delivery.Family))?.Groups ?? [];
        return [.. schema.Catalogs.CarrierGroups.Where(g => g.Payloads.Contains(key) && (only.Count == 0 || only.Contains(g.Name)))];
    }
    public string? DefaultCarrierGroup(CustomStratagem s) => schema.Catalogs.CarrierGroups.FirstOrDefault(g => g.Defaults.Contains(PayloadKey(s)))?.Name;
    /// <summary>The beacons a family can throw (a support delivery or sentry: support; an Eagle: offensive; the others: any).</summary>
    public IReadOnlyList<string> Beacons(string family) => schema.Family(RuntimeFamily(family))?.Beacons is { Count: > 0 } b ? b : schema.Catalogs.Beacons;
    /// <summary>The only carrier families a delivery family may take (sentry and Eagle), or none.</summary>
    public IReadOnlyList<string> OnlyFamilies(string family) => schema.Family(RuntimeFamily(family))?.AllowFamilies ?? [];
    public void ApplyFamilyDefaults(CustomStratagem s)
    {
        // The carrier group is the Runtime's structural carrier pool: the family's default group, its beacon implied. Sentries and Eagles
        // also name the only carrier family they may take (the Runtime requires it even with a group).
        s.Carrier = new CustomCarrier { Group = DefaultCarrierGroup(s), Allow = [.. OnlyFamilies(s.Delivery.Family)], Exclude = s.Carrier.Exclude };
        if (s.Carrier.Group == null) s.Carrier.Beacon = Beacons(s.Delivery.Family)[0];
        if (s.Delivery.Family == DeliveryFamily.Eagle) { s.Cooldown = null; s.Uses = null; }
        if (s.Callbacks != null && !DeliversSomething(s)) s.Callbacks.OnDelivered = null;
    }
    /// <summary>Whether the call brings a delivery on_delivered can see (everything but a Runtime bombardment, a Pelican and a scripted call
    /// without a vanilla pod).</summary>
    public static bool DeliversSomething(CustomStratagem s) => s.Delivery.Family switch
    {
        DeliveryFamily.Pelican => false,
        DeliveryFamily.Orbital => s.Delivery.Orbital?.Mode == OrbitalDelivery.NativeBarrage,
        DeliveryFamily.Scripted => s.Delivery.Scripted?.VanillaPod != null,
        _ => true,
    };

    // ---- field descriptions -------------------------------------------------------------------------------------------------------------

    private static readonly Regex Index = new(@"\[\d+\]", RegexOptions.CultureInvariant);
    // A project form path -> the schema's own path (metadata.cooldown, carrier.group, pelican.gun.rpm, expendable.pod[].count).
    private static string SchemaPath(string path, string family) =>
        path.StartsWith("carrier.", StringComparison.Ordinal) ? path
        : path.StartsWith("payload.", StringComparison.Ordinal) ? RuntimeFamily(family) + "." + Index.Replace(path["payload.".Length..], "[]")
        : "metadata." + path;

    public CustomFieldInfo Describe(string path, CustomStratagem? s = null)
    {
        if (schema.Refusal(path) is { } refusal) return new(path, CustomFieldStatus.Refused, refusal.Reason);
        if (path.EndsWith(".projectile#custom", StringComparison.Ordinal)) return new(path, CustomFieldStatus.Refused, CoreText.Get("CustomContent.Field.CustomRound"));
        var family = s?.Delivery.Family ?? DeliveryFamily.Orbital;
        if (schema.Field(SchemaPath(path, family)) is not { } f) return new(path, CustomFieldStatus.Unavailable, CoreText.Get("CustomContent.Field.NotDescribed"));
        var info = new CustomFieldInfo(path, CustomFieldStatus.Supported, null, f.Unit, f.Min, f.Max, f.ExclusiveMin, f.Default, f.Required, f.Doc, Choices(path, f, s));
        if (s == null) return info;
        CustomFieldInfo Refused(string reason) => info with { Status = CustomFieldStatus.Refused, Reason = reason };
        CustomFieldInfo Unused(string reason) => info with { Status = CustomFieldStatus.Unavailable, Reason = reason };
        if (family == DeliveryFamily.Eagle && path is "cooldown" or "uses")
            return Refused(path == "cooldown" ? schema.Refusal("eagle cooldown")?.Reason ?? CoreText.Get("CustomContent.Field.RefusedDefault") : CoreText.Get("CustomContent.Field.EagleUses"));
        switch (family)
        {
            case DeliveryFamily.Support when path.StartsWith("payload.items[].modify.", StringComparison.Ordinal):
            {
                var donor = schema.SupportDonor(s.Delivery.Support?.Item);
                if (donor is { Modifiable: false }) return Unused(CoreText.Format("CustomContent.Field.NoWeapon", donor.Name));
                if (path.EndsWith(".rpm", StringComparison.Ordinal) && donor is { RateSelector: true }) return Refused(CoreText.Format("CustomContent.Field.RateSelector", donor.Name));
                if (path.EndsWith(".impact_explosion", StringComparison.Ordinal) || path.EndsWith(".rounds", StringComparison.Ordinal))
                    if (donor is { FiresProjectiles: false }) return Unused(CoreText.Format("CustomContent.Field.NoRound", donor.Name));
                return info;
            }
            case DeliveryFamily.Support when path == "payload.items[].count" && schema.SupportDonor(s.Delivery.Support?.Item) is { Count: { } count }:
                return info with { Min = count, Max = count, Default = count.ToString(CultureInfo.InvariantCulture) };
            case DeliveryFamily.Eagle when path == "payload.uses" && schema.Eagle(s.Delivery.Eagle?.Strike) is { Uses: { } uses }:
                return info with { Default = uses.ToString(CultureInfo.InvariantCulture) };
            case DeliveryFamily.Eagle when path == "payload.payload.impact_explosion" && schema.Eagle(s.Delivery.Eagle?.Strike) is { ImpactExplosionReplaceable: false } eagle:
                return Refused(CoreText.Format("CustomContent.Field.EagleImpact", eagle.Name));
            case DeliveryFamily.Expendable when path == "payload.round" && s.Delivery.Expendable?.Modify?.ImpactPayload != null:
                return Unused(CoreText.Get("CustomContent.Field.RoundWithImpact"));
            case DeliveryFamily.Expendable when path == "payload.round" && s.Delivery.Expendable?.Modify?.DirectDamage != null:
                return Unused(CoreText.Get("CustomContent.Field.RoundWithDirectDamage"));
            case DeliveryFamily.Expendable when path == "payload.modify.direct_damage" && s.Delivery.Expendable?.Round != null:
                return Unused(CoreText.Get("CustomContent.Field.DirectDamageWithRound"));
            case DeliveryFamily.Expendable when path == "payload.modify.direct_damage" && schema.CloneDonor(s.Delivery.Expendable?.Weapon) is { DirectDamage.Count: 0 } noDamage:
                return Unused(CoreText.Format("CustomContent.Field.DirectDamageNone", noDamage.Name));
            case DeliveryFamily.Expendable when path == "payload.modify.direct_damage" && s.Delivery.Expendable?.Modify?.ImpactPayload == null:
                return Unused(CoreText.Get("CustomContent.Field.DirectDamageNeedsImpact"));
            case DeliveryFamily.Variant when path == "payload.round" && schema.VariantDonor(s.Delivery.Variant?.Weapon) is { FiresRound: false } beam:
                return Unused(CoreText.Format("CustomContent.Field.VariantNoRound", beam.Name));
            case DeliveryFamily.Variant when path == "payload.round" && schema.VariantDonor(s.Delivery.Variant?.Weapon) is { Rounds.Count: 0 } alone:
                return Unused(CoreText.Format("CustomContent.Field.VariantNoOtherRound", alone.Name));
            case DeliveryFamily.Expendable when path is "payload.modify.impact_explosion" or "payload.modify.rounds" && s.Delivery.Expendable?.Round != null:
                return Unused(CoreText.Get("CustomContent.Field.ImpactWithRound"));
            case DeliveryFamily.Orbital when path.StartsWith("payload.", StringComparison.Ordinal):
            {
                var orbital = s.Delivery.Orbital;
                var native = orbital?.Mode == OrbitalDelivery.NativeBarrage;
                if (native && path is not ("payload.native" or "payload.pattern" or "payload.impact_explosion")) return Unused(CoreText.Get("CustomContent.Field.NativeOnly"));
                var pattern = schema.Orbital(orbital?.Pattern ?? DefaultPattern)?.Pattern;
                string? Vanilla(double? v) => v?.ToString("R", CultureInfo.InvariantCulture);
                return path switch
                {
                    "payload.salvos" => info with { Default = Vanilla(pattern?.Salvos) },
                    "payload.shells_per_salvo" => info with { Default = Vanilla(pattern?.ShellsPerSalvo) },
                    "payload.shell_interval" => info with { Default = Vanilla(pattern?.ShellInterval) },
                    "payload.salvo_interval" => info with { Default = Vanilla(pattern?.SalvoInterval) },
                    "payload.scatter" => info with { Default = Vanilla(pattern?.Scatter) },
                    "payload.salvo_scatter" => info with { Default = Vanilla(pattern?.SalvoScatter) },
                    "payload.impact_explosion" when native => info with { Required = true },
                    "payload.shell" when !native => info with { Required = true },
                    _ => info,
                };
            }
            case DeliveryFamily.Pelican when path.StartsWith("payload.", StringComparison.Ordinal):
            {
                var gun = s.Delivery.Pelican?.Gun;
                var gatling = gun?.BehaveAs == "gatling_sentry";
                if (!gatling && path is "payload.gun.rpm" or "payload.gun.face_target" or "payload.gun.aim_height" or "payload.gun.rate_multiplier")
                    return Unused(CoreText.Get("CustomContent.Field.NeedsGatling"));
                if (gatling && path == "payload.gun.rpm" && gun?.RateMultiplier is { } m && m != 1) return Unused(CoreText.Get("CustomContent.Field.RpmWithMultiplier"));
                if (gun?.Round == "native" && path.StartsWith("payload.gun.", StringComparison.Ordinal) && path != "payload.gun.round")
                    return Unused(CoreText.Get("CustomContent.Field.NativeGun"));
                return info;
            }
        }
        return info;
    }
    private const string DefaultPattern = "Orbital 120mm HE Barrage";

    private IReadOnlyList<string> Choices(string path, SchemaField f, CustomStratagem? s)
    {
        switch (path)
        {
            case "carrier.group": return s == null ? [.. schema.Catalogs.CarrierGroups.Select(g => g.Name)] : [.. CarrierGroups(s).Select(g => g.Name)];
            case "carrier.beacon": return schema.Catalogs.Beacons;
            case "carrier.prefer_families" or "carrier.allow_families": return schema.Catalogs.CarrierFamilies;
            case "carrier.exclude" or "assets": return schema.Catalogs.Stratagems;
            case "code": return schema.Catalogs.Directions;
            case "payload.round" when s?.Delivery.Family == DeliveryFamily.Expendable: return schema.CloneDonor(s.Delivery.Expendable?.Weapon)?.Rounds ?? [];
            case "payload.round" when s?.Delivery.Family == DeliveryFamily.Variant: return [.. schema.VariantDonor(s.Delivery.Variant?.Weapon)?.Rounds.Select(r => r.Output) ?? []];
            case "payload.modify.direct_damage" when s?.Delivery.Family == DeliveryFamily.Expendable:
                return [.. (schema.CloneDonor(s.Delivery.Expendable?.Weapon)?.DirectDamage ?? []).Select(v => v.ToString(CultureInfo.InvariantCulture)).Where(f.Values.Contains)];
        }
        return f.Catalog switch
        {
            "supportDonors" => [.. schema.Catalogs.SupportDonors.Where(d => d.Deliverable).Select(d => d.Name)],
            "projectileDonors" => [.. schema.Catalogs.Rounds.Select(d => d.Name)],
            "explosionDonors" => [.. schema.Catalogs.Explosions.Select(d => d.Name)],
            "sentryDonors" => [.. schema.Catalogs.Sentries.Where(d => d.Supported).Select(d => d.Name)],
            "eagleDonors" => [.. schema.Catalogs.Eagles.Select(d => d.Name)],
            "orbitals" when path == "payload.pattern" => [.. schema.Catalogs.Orbitals.Where(d => d.Pattern != null).Select(d => d.Name)],
            "orbitals" => [.. schema.Catalogs.Orbitals.Select(d => d.Name)],
            "cloneDonors" => [.. schema.Catalogs.CloneDonors.Select(d => d.Name)],
            "variantDonors" => [.. schema.Catalogs.VariantDonors.Where(d => d.Deliverable).Select(d => d.Name)],
            "pelicanSounds" => schema.Catalogs.Sounds,
            "pelicanExplosionDonors" => [.. schema.Catalogs.PelicanExplosions.Select(d => d.Name)],
            "siloDonors" => [.. schema.Catalogs.Silos.Where(d => d.PackageKnown).Select(d => d.Name)],
            "blastExplosions" => [.. schema.Catalogs.Blasts.Select(d => d.Name)],
            "podItems" => [.. schema.Catalogs.PodItems.Select(d => d.Key)],
            _ => f.Values,
        };
    }
    public string ChoiceLabel(string path, string value)
    {
        if (path.EndsWith(".item", StringComparison.Ordinal) && schema.PodItem(value) is { } item) return item.Name;
        if (path == "payload.round" && schema.Catalogs.VariantDonors.SelectMany(d => d.Rounds).FirstOrDefault(r => r.Output == value) is { } round) return round.Label;
        return value;
    }
    // A saved sound the Runtime renamed (0.30.2: primary/smg37/seaf is seaf/3, secondary/gp31/alt is other/wep_grenadier_rifle/1). The Runtime
    // still plays the old name, but this schema (and so its validator) lists only the new one: the field offers the new name in place.
    public string? RenamedChoice(string path, string value) =>
        path.EndsWith(".sound", StringComparison.Ordinal) && !schema.Catalogs.Sounds.Contains(value)
            && Scripting.WeaponSounds.Renamed.TryGetValue(value, out var renamed) && schema.Catalogs.Sounds.Contains(renamed) ? renamed : null;

    // ---- validation -----------------------------------------------------------------------------------------------------------------------

    public IReadOnlyList<CustomDiagnostic> Validate(CustomContentSection content, ModProject project, SdkMetadata? sdk)
    {
        var list = new List<CustomDiagnostic>();
        if (content.Stratagems.Count > 0)
        {
            // The Runtime's own validator over every custom stratagem at once (codes collide across them), in the Runtime's own words.
            var form = RuntimeProjectForm.Project(content.Stratagems, schema.Catalogs.PodItems, sharedHost: SharedHost);
            var problems = RuntimeProjectValidator.Validate(form, document.RootElement, allowScript: true);
            foreach (var p in problems) list.AddRange(Diagnose(p, content));
        }
        foreach (var s in content.Stratagems) Stratagem(s, content, list);
        foreach (var p in content.Projectiles) ValidateProjectile(p, content, project, sdk, list);
        var active = content.Assignments.Where(a => a.Enabled).ToArray();
        foreach (var a in content.Assignments) ValidateAssignment(a, active, content, project, sdk, list);
        return list;
    }

    // A Runtime problem as a diagnostic of its stratagem and field. ModBuilder reports a missing icon and an invalid id itself (in its own words,
    // with the fix), so the Runtime's version of those is not repeated.
    private static IEnumerable<CustomDiagnostic> Diagnose(RuntimeProblem p, CustomContentSection content)
    {
        var where = p.Where;
        var owner = CustomDiagnostic.ContentOwner;
        if (where.StartsWith("stratagems[", StringComparison.Ordinal) && where.IndexOf(']') is var end and > 0)
        {
            var key = where["stratagems[".Length..end];
            var s = content.Stratagems.FirstOrDefault(x => x.Id == key) ?? (int.TryParse(key, out var i) && i >= 0 && i < content.Stratagems.Count ? content.Stratagems[i] : null);
            if (s != null) owner = CustomDiagnostic.StratagemOwner(s.Id);
            if (s != null && (p.Field == "id" || p.Field == "icon" && s.Icon == null)) yield break;
        }
        var field = p.Field.Length > 0 ? p.Field : null;
        var area = field switch
        {
            null => CustomDiagnostic.Build,
            "name" or "name_cased" or "description" or "traits" or "icon" or "id" => CustomDiagnostic.Identity,
            "code" or "cooldown" or "uses" or "max_per_player" => CustomDiagnostic.CallIn,
            _ when field.StartsWith("carrier", StringComparison.Ordinal) => CustomDiagnostic.Carrier,
            _ when field.StartsWith("assets", StringComparison.Ordinal) => CustomDiagnostic.Assets,
            _ => CustomDiagnostic.Delivery,
        };
        yield return new(CustomSeverity.Error, area, owner, field, p.What, Runtime: true);
    }

    // What the project form cannot say (ModBuilder's own references, the Lua escape hatch, images, models) and what is worth knowing.
    private void Stratagem(CustomStratagem s, CustomContentSection content, List<CustomDiagnostic> list)
    {
        var owner = CustomDiagnostic.StratagemOwner(s.Id);
        void Add(CustomSeverity severity, string area, string? field, string message) => list.Add(new(severity, area, owner, field, message));
        var errors = list.Count(d => d.Owner == owner && d.Severity == CustomSeverity.Error && d.Runtime);
        var family = s.Delivery.Family;
        // Call-in.
        if (list.All(d => !(d.Owner == owner && d.Field == "code" && d.Severity == CustomSeverity.Error)) && s.Code.Count > 0)
            Add(CustomSeverity.Pass, CustomDiagnostic.CallIn, "code", CoreText.Get("CustomContent.Check.CodeUnique"));
        if (s.Code.Count > 0 && schema.Catalogs.NativeCodes.Where(n => !n.Catalogued && Prefix(s.Code, n.Code)).ToArray() is { Length: > 0 } guarded)
            Add(CustomSeverity.Info, CustomDiagnostic.CallIn, "code", CoreText.Format("CustomContent.Check.CodeMissionGuard", guarded.Length));
        if (s.Uses is { } uses and > 0 && family != DeliveryFamily.Eagle) Add(CustomSeverity.Info, CustomDiagnostic.CallIn, "uses", CoreText.Format("CustomContent.Check.UsesNative", uses));
        // 0.30.3: an explicit 0 or -1 (kept from an imported Runtime project) is unlimited too, but 0.30.2 refuses it (RuntimeRequirement.Release0303).
        if (Generation.RuntimeRequirement.ExplicitUnlimitedUses(s.Uses) && family != DeliveryFamily.Eagle)
            Add(CustomSeverity.Info, CustomDiagnostic.CallIn, "uses", CoreText.Format("CustomContent.Check.UsesExplicitUnlimited", s.Uses!.Value, Generation.RuntimeRequirement.Release0303));
        // Images: the icon and any delivered weapon's own icon must be images of the project.
        void Image(string? id, string field)
        {
            if (id != null && content.Images.All(i => i.Id != id)) Add(CustomSeverity.Error, CustomDiagnostic.Assets, field, CoreText.Format("CustomContent.Check.ImageMissing", id));
        }
        Image(s.Delivery.Expendable?.Presentation is { IconMode: DeliveredPresentation.Custom } ep ? ep.Icon ?? "" : null, "payload.presentation.icon");
        Image(s.Delivery.Variant?.Presentation is { IconMode: DeliveredPresentation.Custom } vp ? vp.Icon ?? "" : null, "payload.presentation.icon");
        if (s.Delivery.Variant?.Presentation is { IconMode: DeliveredPresentation.Donor } || s.Delivery.Variant?.Presentation is { NameMode: DeliveredPresentation.Donor })
            Add(CustomSeverity.Error, CustomDiagnostic.Delivery, "payload.presentation", CoreText.Get("CustomContent.Check.VariantDonorPresentation"));
        foreach (var presentation in new[] { s.Delivery.Expendable?.Presentation, s.Delivery.Variant?.Presentation })
            if (presentation is { NameMode: DeliveredPresentation.Custom, Name: null or { Length: 0 } })
                Add(CustomSeverity.Error, CustomDiagnostic.Delivery, "payload.presentation.name", CoreText.Get("CustomContent.Check.PresentationNameRequired"));
        // Models: a weapon variant's model is one of the project's models, derived from its own base weapon.
        if (s.Delivery.Variant is { Model: { } modelId } variant)
        {
            if (content.Models.FirstOrDefault(m => m.Id == modelId) is not { } model) Add(CustomSeverity.Error, CustomDiagnostic.Assets, "payload.model", CoreText.Format("CustomContent.Check.ModelMissing", modelId));
            else if (model.Base != variant.Weapon) Add(CustomSeverity.Error, CustomDiagnostic.Assets, "payload.model", CoreText.Format("CustomContent.Check.ModelBase", modelId, model.Base, variant.Weapon));
            else Add(CustomSeverity.Info, CustomDiagnostic.Assets, "payload.model", CoreText.Format("CustomContent.Check.ModelBuild", modelId, model.Base));
        }
        // Delivered weapons fire vanilla rounds only: a custom projectile has no place in the Runtime's custom stratagem payloads.
        foreach (var (field, weapon) in Weapons(s))
            if (weapon.Projectile is { IsCustom: true })
                Add(CustomSeverity.Error, CustomDiagnostic.Delivery, field + ".projectile", CoreText.Get("CustomContent.Check.CustomRoundUnsupported"));
        // Pod items the Runtime has not live-tested in a pod: the opt-in is written for them, the risk is shown here.
        foreach (var (field, items) in new[] { ("payload.items", s.Delivery.Pod?.Items), ("payload.pod", s.Delivery.Expendable?.Pod) })
            for (var k = 0; k < (items?.Count ?? 0); k++)
                if (schema.PodItem(items![k].Item) is { Unverified: true } entry)
                    Add(CustomSeverity.Warning, CustomDiagnostic.Delivery, field + "[" + k + "].item", CoreText.Format("CustomContent.Check.PodUnverified", entry.Name));
        if (family == DeliveryFamily.Pod) Add(CustomSeverity.Warning, CustomDiagnostic.Multiplayer, null, CoreText.Get("CustomContent.Check.PodSolo"));
        if (family == DeliveryFamily.Silo && schema.Blast(s.Delivery.Silo?.Blast) is { Objective: true } blast)
            Add(CustomSeverity.Info, CustomDiagnostic.Delivery, "payload.blast", CoreText.Format("CustomContent.Check.ObjectiveBlast", blast.Name));
        if (family == DeliveryFamily.Variant) Add(CustomSeverity.Info, CustomDiagnostic.Delivery, null, CoreText.Format("CustomContent.Check.VariantNative", s.Delivery.Variant?.Weapon ?? ""));
        // A shared host's type is also brought by world loot or other pods: those copies become the variant too. The opt-in is written; the risk is shown.
        if (family == DeliveryFamily.Variant && schema.VariantDonor(s.Delivery.Variant?.Weapon) is { IsShared: true } shared)
            Add(CustomSeverity.Warning, CustomDiagnostic.Delivery, "payload.weapon", CoreText.Format("CustomContent.Check.VariantShared", shared.Name, string.Join("; ", shared.Shared!)));
        // The escape hatch: callbacks need something to see, and every Lua body must parse.
        var callbacks = s.Callbacks;
        if (!string.IsNullOrWhiteSpace(callbacks?.OnDelivered) && !DeliversSomething(s))
            Add(CustomSeverity.Error, CustomDiagnostic.Advanced, "callbacks.on_delivered", CoreText.Get("CustomContent.Check.DeliveredNeedsDelivery"));
        if (family == DeliveryFamily.Scripted && callbacks is not { HasBehaviour: true } && s.Delivery.Scripted?.VanillaPod == null)
            Add(CustomSeverity.Error, CustomDiagnostic.Advanced, "callbacks.on_activate", CoreText.Get("CustomContent.Check.ScriptedNeedsActivate"));
        if (family == DeliveryFamily.Scripted && s.Delivery.Scripted?.VanillaPod is { } pod && !schema.Catalogs.SupportDonors.Any(d => d.Name == pod && d.Deliverable))
            Add(CustomSeverity.Error, CustomDiagnostic.Delivery, "delivery", CoreText.Format("CustomContent.Check.SupportItemUnknown", pod));
        foreach (var (name, field, body) in CallbackBodies(callbacks))
            foreach (var d in Scripting.LuaScriptAnalyzer.Analyze("local hd2=nil\nlocal f=function(ctx)\n" + body + "\nend\n", null).Where(d => d.Severity == Scripting.LuaDiagnostic.Error))
                Add(CustomSeverity.Error, CustomDiagnostic.Advanced, field, CoreText.Format("CustomContent.Check.CallbackSyntax", name, Math.Max(1, d.Line - 2), d.Message));
        if (!string.IsNullOrWhiteSpace(callbacks?.Setup))
            foreach (var d in Scripting.LuaScriptAnalyzer.Analyze("local hd2=nil\ndo\n" + callbacks.Setup + "\nend\n", null).Where(d => d.Severity == Scripting.LuaDiagnostic.Error))
                Add(CustomSeverity.Error, CustomDiagnostic.Advanced, "callbacks.setup", CoreText.Format("CustomContent.Check.CallbackSyntax", "setup", Math.Max(1, d.Line - 2), d.Message));
        if (!string.IsNullOrWhiteSpace(callbacks?.ExtraFields))
        {
            foreach (var d in Scripting.LuaScriptAnalyzer.Analyze("local hd2=nil\nlocal t={\n" + callbacks.ExtraFields + "\n}\n", null).Where(d => d.Severity == Scripting.LuaDiagnostic.Error))
                Add(CustomSeverity.Error, CustomDiagnostic.Advanced, "callbacks.extra", CoreText.Format("CustomContent.Check.CallbackSyntax", "fields", Math.Max(1, d.Line - 2), d.Message));
            Add(CustomSeverity.Warning, CustomDiagnostic.Advanced, "callbacks.extra", CoreText.Get("CustomContent.Check.ExtraFields"));
        }
        // Multiplayer and what Runtime has shown in play (informational: networking is the Runtime's).
        Add(callbacks is { HasBehaviour: true } ? CustomSeverity.Warning : CustomSeverity.Pass, CustomDiagnostic.Multiplayer, callbacks is { HasBehaviour: true } ? "callbacks" : null,
            CoreText.Get(callbacks is { HasBehaviour: true } ? "CustomContent.Multiplayer.Callback" : "CustomContent.Multiplayer.Declarative"));
        Add(CustomSeverity.Pass, CustomDiagnostic.Multiplayer, "id", CoreText.Format("CustomContent.Multiplayer.StableId", s.Id));
        if (Evidence(family) is { } evidence)
            Add(evidence.Status == "live" ? CustomSeverity.Info : CustomSeverity.Experimental, CustomDiagnostic.Multiplayer, null,
                CoreText.Format("CustomContent.Check.Evidence", evidence.Status, evidence.Multiplayer, evidence.Doc));
        if (errors == 0 && list.All(d => !(d.Owner == owner && d.Severity == CustomSeverity.Error)))
            Add(CustomSeverity.Pass, CustomDiagnostic.Build, null, CoreText.Get("CustomContent.Check.RuntimeAccepts"));
    }
    private static bool Prefix(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = Math.Min(a.Count, b.Count);
        return a.Take(n).SequenceEqual(b.Take(n)) && a.Count != b.Count;
    }
    private static IEnumerable<(string Field, WeaponOverrides Weapon)> Weapons(CustomStratagem s)
    {
        var d = s.Delivery;
        if (d.Support?.Weapon is { } sw) yield return ("payload.items[0].modify", sw);
        if (d.Sentry?.Weapon is { } tw) yield return ("payload.weapon", tw);
        if (d.Expendable?.Modify is { } ew) yield return ("payload.modify", ew);
        for (var k = 0; k < (d.Pod?.Items.Count ?? 0); k++) if (d.Pod!.Items[k].Weapon is { } pw) yield return ("payload.items[" + k + "].modify", pw);
        for (var k = 0; k < (d.Expendable?.Pod?.Count ?? 0); k++) if (d.Expendable!.Pod![k].Weapon is { } xw) yield return ("payload.pod[" + k + "].modify", xw);
    }
    public static IEnumerable<(string Name, string Field, string Body)> CallbackBodies(CustomCallbacks? c)
    {
        if (c == null) yield break;
        foreach (var (name, field, body) in new[] { ("on_called", "callbacks.on_called", c.OnCalled), ("on_beacon_created", "callbacks.on_beacon_created", c.OnBeaconCreated),
                     ("on_beacon_landed", "callbacks.on_beacon_landed", c.OnBeaconLanded), ("on_activate", "callbacks.on_activate", c.OnActivate),
                     ("on_delivered", "callbacks.on_delivered", c.OnDelivered) })
            if (!string.IsNullOrWhiteSpace(body)) yield return (name, field, body);
    }

    private void ValidateProjectile(CustomProjectile p, CustomContentSection content, ModProject project, SdkMetadata? sdk, List<CustomDiagnostic> list)
    {
        var owner = CustomDiagnostic.ProjectileOwner(p.Id);
        void Add(CustomSeverity severity, string? field, string message, string area = CustomDiagnostic.Projectile) => list.Add(new(severity, area, owner, field, message));
        Add(CustomSeverity.Experimental, null, CoreText.Get("CustomContent.Projectile.Experimental"));
        var catalog = sdk?.AttackOutputs;
        if (catalog == null) { Add(CustomSeverity.Error, null, CoreText.Format("CustomContent.Projectile.NeedsCatalog", sdk?.Version ?? project.SdkVersion)); return; }
        bool Output(string field, string? id, bool package, bool required)
        {
            if (string.IsNullOrEmpty(id))
            {
                if (required) Add(CustomSeverity.Error, field, CoreText.Get("CustomContent.Projectile.BaseRequired"));
                return !required;
            }
            var issue = OutputIssue(catalog, id, package);
            if (issue != null) Add(CustomSeverity.Error, field, issue);
            return issue == null;
        }
        var ok = Output("base", p.Base, true, true);
        ok &= Output("visual", p.Visual, true, false);
        ok &= Output("damage", p.Damage, true, false);
        ok &= Output("impactExplosion", p.ImpactExplosion, true, false);
        ok &= Output("ballistics", p.Ballistics, false, false);
        if (p.Visual is null && p.Damage is null && p.ImpactExplosion is null && p.Ballistics is null && !string.IsNullOrEmpty(p.Base))
            // An exact copy of its base (a clone): valid, Runtime defines it as the base row; worth knowing, not a warning.
            Add(CustomSeverity.Info, null, CoreText.Format("CustomContent.Projectile.NoComponents", catalog.Output(p.Base)?.Label ?? p.Base));
        if (p.Visual != null) Add(CustomSeverity.Info, "visual", CoreText.Get("CustomContent.Projectile.VisualUnit"));
        if (ok) Add(CustomSeverity.Pass, null, CoreText.Get("CustomContent.Projectile.Ready"));
        Add(CustomSeverity.Pass, null, CoreText.Format("CustomContent.Projectile.StableId", p.Id, CustomIds.RuntimeProjectileId(project.ResourceId, p.Id)), CustomDiagnostic.Multiplayer);
        Add(CustomSeverity.Info, null, CoreText.Get("CustomContent.Projectile.Multiplayer"), CustomDiagnostic.Multiplayer);
        if (!content.Assignments.Any(a => a.Enabled && a.Projectile == ContentRef.Custom(p.Id)))
            Add(CustomSeverity.Info, null, CoreText.Get("CustomContent.Projectile.Unused"));
    }

    // Why a catalogued output cannot be a custom projectile's base or component: Runtime composes whole projectile rows that are not a spare
    // twin, and loads the package of every component it draws (all but ballistics). It finds that package by the row's dependencyKey, which
    // 0.30's projectile donors do not have (the SDK says their package is known: it auto-loads with the owner, which Runtime does not use here).
    private static string? OutputIssue(AttackOutputCatalog catalog, string id, bool package) => catalog.Output(id) is not { } o ? CoreText.Format("CustomContent.Projectile.OutputUnknown", id)
        : o.Family != "projectile" ? CoreText.Format("CustomContent.Projectile.OutputNotProjectile", o.Label, o.Family)
        : o.SpareTwin != null || o.Owner.Kind == "spare_row" ? CoreText.Format("CustomContent.Projectile.OutputSpare", o.Label)
        : package && (!o.Package.Known || o.IsDonor) ? CoreText.Format("CustomContent.Projectile.OutputPackage", o.Label) : null;
    public string? ProjectileOutputIssue(string outputId, bool package, SdkMetadata? sdk) =>
        sdk?.AttackOutputs is { } catalog ? OutputIssue(catalog, outputId, package) : CoreText.Format("CustomContent.Projectile.NeedsCatalog", sdk?.Version ?? "");

    // Runtime binds one weapon per carrier type, and this target has one carrier.
    public int? ProjectileBindingLimit => 1;

    // replace_projectiles selects the carrier's shots by the firing weapon's catalogued name: player weapons only. Runtime's internal
    // source-bound binding for mounted weapons is not offered by this target.
    public CustomFieldInfo DescribeProjectileHost(CustomProjectileHost host, ModProject project, SdkMetadata? sdk)
    {
        const string path = "projectileHost";
        if (host.Kind == CustomProjectileHost.AmmoMode) return new(path, CustomFieldStatus.Unavailable, CoreText.Format("CustomContent.Host.AmmoMode", RuntimeLabel));
        if (host.Kind != AttackOutputChange.PlayerHost) return new(path, CustomFieldStatus.Unavailable, CoreText.Format("CustomContent.Host.NotPlayer", RuntimeLabel));
        var (issue, _) = PlayerHost(host.Weapon, host.Role, host.Label, project, sdk);
        return issue == null ? new(path, CustomFieldStatus.Supported) : new(path, CustomFieldStatus.Unavailable, issue);
    }
    // A player weapon attack that can fire the carrier: Runtime writes the projectile it fires, and offers the carrier as its donor.
    private static (string? Issue, AttackOutputDonor? Donor) PlayerHost(string weapon, string role, string label, ModProject project, SdkMetadata? sdk)
    {
        if (sdk?.AttackOutputs is not { } catalog || sdk.PlayerWeapons is not { } weapons) return (CoreText.Format("CustomContent.Projectile.NeedsCatalog", sdk?.Version ?? project.SdkVersion), null);
        var host = catalog.Host(weapons, weapon, role, out var reason);
        if (host == null) return (CoreText.Format("CustomContent.Assignment.NotHost", label, reason), null);
        var donor = catalog.Donors(host).FirstOrDefault(d => d.Output.SemanticId == Carrier);
        return donor is { Allowed: true } ? (null, donor)
            : (CoreText.Format("CustomContent.Assignment.Carrier", label, donor?.Refusal ?? CoreText.Get("CustomContent.Assignment.CarrierNotOffered")), null);
    }

    private void ValidateAssignment(CustomProjectileAssignment a, CustomProjectileAssignment[] active, CustomContentSection content, ModProject project, SdkMetadata? sdk,
        List<CustomDiagnostic> list)
    {
        var owner = CustomDiagnostic.AssignmentOwner(a.Id);
        void Add(CustomSeverity severity, string message) => list.Add(new(severity, CustomDiagnostic.Assignment, owner, null, message));
        var label = CustomProjectileHost.Of(a).Label;
        if (!a.Projectile.IsCustom) { Add(CustomSeverity.Error, CoreText.Get("CustomContent.Assignment.NotCustom")); return; }
        if (content.Projectiles.All(p => p.Id != a.Projectile.Id)) { Add(CustomSeverity.Error, CoreText.Format("CustomContent.Assignment.ProjectileMissing", label, a.Projectile.Id)); return; }
        if (a.HostKind != AttackOutputChange.PlayerHost) { Add(CustomSeverity.Error, CoreText.Format("CustomContent.Assignment.HostKind", label)); return; }
        if (!a.Enabled) return;
        if (active.Length > 1) Add(CustomSeverity.Error, CoreText.Format("CustomContent.Assignment.OnePerCarrier", active.Length));
        var (issue, donor) = PlayerHost(a.Weapon, a.AttackRole, label, project, sdk);
        if (issue != null || donor == null) { Add(CustomSeverity.Error, issue!); return; }
        if ((project.AttackOutputChanges ?? []).Any(c => c.IsPlayer && c.Weapon == a.Weapon && c.AttackRole == a.AttackRole) || project.ProjectileChanges.Any(c => c.Weapon == a.Weapon && c.AttackRole == a.AttackRole))
            Add(CustomSeverity.Error, CoreText.Format("CustomContent.Assignment.ConflictSwap", label));
        if (project.CompositionChanges.Any(c => c.Weapon == a.Weapon && c.AttackRole == a.AttackRole))
            Add(CustomSeverity.Error, CoreText.Format("CustomContent.Assignment.ConflictEdits", label));
        if (donor.Acknowledgements.Length > 0) Add(CustomSeverity.Info, CoreText.Format("CustomContent.Assignment.OptIns", string.Join(", ", donor.Acknowledgements)));
        list.Add(new(CustomSeverity.Experimental, CustomDiagnostic.Assignment, owner, null, CoreText.Get("CustomContent.Assignment.Live")));
        Add(CustomSeverity.Pass, CoreText.Format("CustomContent.Assignment.Ready", label, a.Projectile.Id, donor.Output.Label));
    }

    // ---- compiling -------------------------------------------------------------------------------------------------------------------------

    public CompiledCustomContent Compile(CustomContentSection content, ModProject project, SdkMetadata sdk, bool images)
    {
        var lines = new List<string>
        {
            "-- Custom content (EXPERIMENTAL) for the " + RuntimeLabel + " API: custom stratagems (hd2.custom_stratagem) and custom projectiles.",
            "-- Generated by " + BuildInfo.ProductName + " from the project's custom content (target " + TargetId + ", schema " + schema.Format + ").",
            "-- Edit it in " + BuildInfo.ProductName + "; this section is written again by every build.",
            "do",
            "    local function custom_content()",
        };
        void Line(string text) => lines.Add(text.Length == 0 ? "" : "        " + text);
        if (content.Stratagems.Count > 0)
        {
            Line("if type(hd2.custom_stratagem) ~= 'table' then");
            Line("    print('[ModBuilder] custom stratagems need HD2Runtime " + MinimumRuntime + " or newer; they are not registered')");
            Line("else");
            Line("    -- Each custom stratagem on its own: a problem in one (its setup or Runtime's refusal at registration) never stops the others.");
            Line("    local function register_custom_stratagem(id, build)");
            Line("        local built, definition = pcall(build)");
            Line("        if not built then print('[ModBuilder] custom stratagem ' .. id .. ' not registered: ' .. tostring(definition)) return end");
            Line("        local ok, problem = pcall(hd2.custom_stratagem.register, definition)");
            Line("        if not ok then print('[ModBuilder] custom stratagem ' .. id .. ' not registered: ' .. tostring(problem)) end");
            Line("    end");
            foreach (var s in content.Stratagems)
            {
                Line("");
                foreach (var l in Registration(s).Split('\n')) Line("    " + l);
            }
            Line("end");
        }
        var operations = new List<string>();
        var assigned = content.Assignments.Where(a => a.Enabled).ToArray();
        var projectiles = content.Projectiles.Where(p => assigned.Any(a => a.Projectile == ContentRef.Custom(p.Id))).ToArray();
        if (assigned.Length > 0)
        {
            if (content.Stratagems.Count > 0) Line("");
            Line("-- Custom projectiles: HD2Runtime 0.30.0 or newer. Runtime publishes no API for them yet, so this calls its internal");
            Line("-- modules (runtime/custom_projectiles, api/actions), which may change without notice. The weapon fires the carrier projectile");
            Line("-- (an ordinary guarded projectile swap); at each mission start Runtime replaces each of its shots by the custom projectile.");
            Line("-- Host only: other players' machines do not see custom projectiles.");
            Line("local ok_projectiles, custom_projectiles = pcall(require, 'hd2runtime/runtime/custom_projectiles')");
            Line("local ok_actions, actions = pcall(require, 'hd2runtime/api/actions')");
            Line("if not (ok_projectiles and ok_actions and type(custom_projectiles) == 'table' and type(custom_projectiles.define) == 'function'");
            Line("        and type(actions) == 'table' and type(actions.replace_projectiles) == 'function') then");
            Line("    print('[ModBuilder] custom projectiles need an HD2Runtime with custom projectile rows (0.30.0 or newer); they are not active')");
            Line("else");
            var definitions = new LuaTable();
            foreach (var p in projectiles) definitions.Add(p.Id, Definition(p, project));
            foreach (var l in ("local definitions = " + definitions.Write()).Split('\n')) Line("    " + l);
            var bindings = assigned.Select(a => (object)new LuaTable().Add("projectile", a.Projectile.Id).Add("carrier", Carrier).Add("weapon", a.Weapon)).ToList();
            foreach (var l in ("local bindings = " + new LuaList(bindings).Write(0)).Split('\n')) Line("    " + l);
            Line("    local function bind(reason)");
            Line("        for _, binding in ipairs(bindings) do");
            Line("            local definition, code, why = custom_projectiles.define(definitions[binding.projectile])");
            Line("            if not definition then");
            Line("                print('[ModBuilder] ' .. reason .. ': custom projectile ' .. binding.projectile .. ' not defined: ' .. tostring(code) .. ': ' .. tostring(why))");
            Line("            else");
            Line("                local action = actions.replace_projectiles(definition, { carrier = binding.carrier, weapon = binding.weapon })");
            Line("                print('[ModBuilder] ' .. reason .. ': ' .. binding.weapon .. ' fires custom projectile ' .. binding.projectile .. ': '");
            Line("                    .. tostring(action.status) .. (action.code and (' ' .. tostring(action.code) .. ': ' .. tostring(action.reason)) or ''))");
            Line("            end");
            Line("        end");
            Line("    end");
            Line("    hd2.events.on('mission_started', function() bind('mission started') end, { id = " + LuaTable.Quote("modbuilder-custom-projectiles") + " })");
            Line("end");
            foreach (var a in assigned) operations.Add(CarrierSwap(a, project, sdk));
        }
        lines.Add("    end");
        lines.Add("    local run_as = type(hd2.events) == 'table' and hd2.events.run_as");
        lines.Add("    local ok, problem");
        lines.Add("    if type(run_as) == 'function' then ok, problem = pcall(run_as, " + LuaTable.Quote(project.ResourceId) + ", custom_content) else ok, problem = pcall(custom_content) end");
        lines.Add("    if not ok then print('[ModBuilder] custom content not loaded: ' .. tostring(problem)) end");
        lines.Add("end");
        var compiled = images ? CompileImages(content, project) : [];
        var models = content.Stratagems.Select(s => s.Delivery.Variant?.Model).OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(id => content.Models.First(m => m.Id == id)).OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
        return new(TargetId, MinimumRuntime, operations, string.Join("\n", lines) + "\n", compiled,
            content.Stratagems.Select(s => s.Id).ToArray(), projectiles.Select(p => p.Id).ToArray(), schema.Sha256, models);
    }

    public string RegistrationPreview(CustomStratagem stratagem, CustomContentSection content) => Registration(stratagem);
    public string ProjectilePreview(CustomProjectile projectile, ModProject project) => "custom_projectiles.define " + Definition(projectile, project).Write();
    // docs/custom-projectile-rows.md, live results (2026-09-30): the impact explosion on a Runtime-owned row is VERIFIED; the visual, damage and
    // ballistics donors are OBSERVED (seen working, not measured).
    public IReadOnlyList<(string Component, string? Evidence)> ProjectileComponents { get; } =
        [("visual", "observed"), ("damage", "observed"), ("impactExplosion", "verified"), ("ballistics", "observed")];

    /// <summary>One custom stratagem as the build writes it: its setup, then the register call's fields exactly as the Runtime SDK's compiler
    /// writes them (RuntimeRegistrationLua), then the callbacks and extra fields of the advanced section.</summary>
    public string Registration(CustomStratagem s)
    {
        var b = new List<string> { "register_custom_stratagem(" + RuntimeRegistrationLua.LuaString(s.Id) + ", function()" };
        if (!string.IsNullOrWhiteSpace(s.Callbacks?.Setup)) b.AddRange(Body(s.Callbacks.Setup, "    "));
        b.Add("    return {");
        b.AddRange(RuntimeRegistrationLua.FieldLines(ProjectForm(s), s.Delivery.Scripted?.VanillaPod).Select(l => "    " + l));
        foreach (var (name, _, body) in CallbackBodies(s.Callbacks))
        {
            b.Add("        " + name + "=function(ctx)");
            b.AddRange(Body(body, "            "));
            b.Add("        end,");
        }
        if (!string.IsNullOrWhiteSpace(s.Callbacks?.ExtraFields))
        {
            b.Add("        -- Extra fields (advanced), as written:");
            b.AddRange(Body(s.Callbacks.ExtraFields, "        "));
        }
        b.Add("    }");
        b.Add("end)");
        return string.Join("\n", b);
    }
    private static IEnumerable<string> Body(string text, string indent) =>
        text.Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t').Split('\n').Select(l => l.Trim().Length == 0 ? "" : indent + l.TrimEnd());

    // A custom projectile as runtime/custom_projectiles.define reads it: {id, base, components = {visual, damage, impact_explosion, ballistics}}.
    private static LuaTable Definition(CustomProjectile p, ModProject project) => new LuaTable()
        .Add("id", CustomIds.RuntimeProjectileId(project.ResourceId, p.Id)).Add("base", p.Base)
        .Add("components", new LuaTable().Add("visual", p.Visual).Add("damage", p.Damage).Add("impact_explosion", p.ImpactExplosion).Add("ballistics", p.Ballistics));
    // The carrier swap: the same guarded patch as any attack output swap (expect the host's current projectile, value the carrier), with the
    // opt-ins the published donor rules give this pair.
    private static string CarrierSwap(CustomProjectileAssignment a, ModProject project, SdkMetadata sdk)
    {
        var change = AttackOutputChangeService.Create(sdk, a.HostKind, a.Weapon, a.AttackRole, Carrier);
        return AttackOutputChangeService.Operation(change, sdk, "custom-carrier-" + SupportChangeService.Hash(project.ResourceId + "\n" + a.HostKind + "\n" + a.Weapon + "\n" + a.AttackRole)[..24]);
    }
    /// <summary>Every image a custom stratagem shows: its icon and its delivered weapon's own icon.</summary>
    public static IEnumerable<string> UsedImages(CustomContentSection content) => UsedImageReferences(content).Distinct(StringComparer.Ordinal);
    /// <summary>Every reference to an image (an image two stratagems show is listed twice).</summary>
    public static IEnumerable<string> UsedImageReferences(CustomContentSection content) => content.Stratagems.SelectMany(s => new[]
    {
        s.Icon,
        s.Delivery.Expendable?.Presentation is { IconMode: DeliveredPresentation.Custom } ep ? ep.Icon : null,
        s.Delivery.Variant?.Presentation is { IconMode: DeliveredPresentation.Custom } vp ? vp.Icon : null,
    }).OfType<string>();
    private static IReadOnlyList<CompiledImage> CompileImages(CustomContentSection content, ModProject project)
    {
        var used = UsedImages(content).ToHashSet(StringComparer.Ordinal);
        return content.Images.Where(i => used.Contains(i.Id)).OrderBy(i => i.Id, StringComparer.Ordinal).Select(i => CompileImage(i, project)).ToArray();
    }
    /// <summary>One project image as the SDK build compiles images/&lt;id&gt;.png (also a plain stratagem's presentation_icon image).</summary>
    public static CompiledImage CompileImage(CustomImage i, ModProject project)
    {
        var png = Convert.FromBase64String(i.Png);
        var name = project.ResourceId + "/images/" + i.Id;
        var hash = GameplayArchive.ResourceHash(name);
        var (main, gpu, label) = CustomIconCompiler.Texture(png, i.Preparation);
        return new CompiledImage(i.Id, name, hash, i.Sha256, label, png, main, gpu, CustomIconCompiler.Material(hash));
    }
}
