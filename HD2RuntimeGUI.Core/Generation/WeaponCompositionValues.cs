using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Generation;

// 1.4.0 weapon composition values (HD2Runtime 0.28.0), for player and support weapons alike: how each published value type is validated,
// compared and written as a Lua literal. Only what the SDK publishes is accepted; everything else is refused with Runtime's rule.

// hd2.fields.fire_rate.modes: the three rate slots {X, Y, Z} in weapon-menu order (rpm, 0 = no mode in that slot). Y is the rate the
// weapon is built on and is never 0; the selector visits Y -> Z -> X, skipping empty slots (docs/fire-rate-modes.md).
public static class FireRateModes
{
    public const string Field = "fire_rate.modes", LegacyField = "weapon.fire_rate";
    public static readonly string[] SlotNames = ["x", "y", "z"];
    public static float[] Rates(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3
            || value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number || !double.IsFinite(x.GetDouble()) || !float.IsFinite((float)x.GetDouble())))
            throw new InvalidDataException(CoreText.Get("Messages.Build.FireRate.ThreeSlots"));
        return value.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
    }
    // Three finite slots, each 0 or within the published range, the default (Y) filled, and no more filled slots than the weapon can hold.
    public static JsonElement Normalize(JsonElement value, double min, double max, int maxModes)
    {
        var rates = Rates(value);
        if (rates.Any(r => r < 0)) throw new InvalidDataException(CoreText.Format("Messages.Build.FireRate.Range", Number((float)min), Number((float)max)));
        if (rates[1] == 0) throw new InvalidDataException(CoreText.Get("Messages.Build.FireRate.DefaultRequired"));
        if (rates.Any(r => r != 0 && (r < min || r > max))) throw new InvalidDataException(CoreText.Format("Messages.Build.FireRate.Range", Number((float)min), Number((float)max)));
        if (rates.Count(r => r != 0) > maxModes) throw new InvalidDataException(CoreText.Get("Messages.Build.FireRate.SingleRate"));
        return JsonSerializer.SerializeToElement(rates);
    }
    public static int Filled(JsonElement value) => Rates(value).Count(r => r != 0);
    public static bool Equal(JsonElement a, JsonElement b)
    {
        try { return Rates(a).SequenceEqual(Rates(b)); } catch (InvalidDataException) { return false; }
    }
    public static string Number(float rate) => rate.ToString("R", CultureInfo.InvariantCulture);
    public static string Lua(JsonElement value) => "{" + string.Join(",", Rates(value).Select(Number)) + "}";
    // The filled slots in the order the selector visits them from the default: y, z, x.
    public static IReadOnlyList<string> SelectorOrder(IReadOnlyList<float> rates) => new[] { 1, 2, 0 }.Where(i => rates[i] != 0).Select(i => SlotNames[i]).ToArray();
    // The filled slots in weapon-menu order: x, y, z.
    public static IReadOnlyList<string> MenuOrder(IReadOnlyList<float> rates) => new[] { 0, 1, 2 }.Where(i => rates[i] != 0).Select(i => SlotNames[i]).ToArray();
}

// hd2.fields.weapon_function.left / .right: the WeaponFunctionType bound to one weapon-function input, a name from the field's allowed list.
public static class WeaponFunctions
{
    public const string None = "none", RateOfFire = "rate_of_fire", ProgrammableAmmo = "programmable_ammo";
    public static readonly string[] Inputs = ["left", "right"];
    public static string Field(string input) => "weapon_function." + input;
    public static string? Input(string semanticFieldId) => semanticFieldId.StartsWith("weapon_function.", StringComparison.Ordinal) ? semanticFieldId["weapon_function.".Length..] : null;
    public static string Normalize(JsonElement value, IReadOnlyList<string>? allowed)
    {
        var name = value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        if (allowed?.Contains(name) != true) throw new InvalidDataException(CoreText.Format("Messages.Build.WeaponFunction.NotAllowed", name, string.Join(", ", allowed ?? [])));
        return name;
    }
}

// hd2.fields.function_ammo.projectile: what the ProgrammableAmmo weapon function fires. Saved as a token: 'none' (no second feed),
// 'native' (the weapon's own native function projectile: weapon:feed('programmable'):projectile()), or an attack-output semantic ID.
public sealed record FunctionDonor(AttackOutput Output, bool CrossClass, bool LiveProven);
public static class FunctionProjectile
{
    public const string Field = "function_ammo.projectile", None = "none", Native = "native";
    // Player baselines are {projectileType: N} (0 = none); support baselines are already 'none' / 'native'.
    public static string Token(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Object when value.TryGetProperty("projectileType", out var type) && type.TryGetInt64(out var n) => n == 0 ? None : Native,
        _ => throw new InvalidDataException(CoreText.Get("Messages.Build.FunctionAmmo.Invalid")),
    };
    public static string? TokenOrNull(JsonElement value) { try { return Token(value); } catch (InvalidDataException) { return null; } }
    public static bool IsOutput(string token) => token.StartsWith("output/v1/projectile/", StringComparison.Ordinal);
    public static bool Equal(JsonElement a, JsonElement b)
    {
        try { return Token(a) == Token(b); } catch (InvalidDataException) { return false; }
    }
    public static string Lua(string token, string weaponTarget) => token switch
    {
        None => "'none'",
        Native => weaponTarget + ":feed('programmable'):projectile()",
        _ => "hd2.attack_output(" + LuaGenerator.Quote(token) + ")",
    };
    // Every attack output a programmable mode can fire: a selectable projectile output whose reference scope (if any) names this field,
    // other than the weapon's own normal projectile. Live-proven pairs (the SDK's proven compositions and the field's proven values) first.
    public static IReadOnlyList<FunctionDonor> Donors(SdkMetadata sdk, string weapon, IReadOnlyList<string>? liveValues)
    {
        if (sdk.AttackOutputs is not { } catalog) return [];
        var own = catalog.Own(weapon);
        return catalog.Outputs
            .Where(o => o.Family == "projectile" && o.SelectableAsProjectileReference && (o.ReferenceScope is not { Length: > 0 } scope || scope.Contains(Field)) && o.SemanticId != own?.SemanticId)
            .Select(o => new FunctionDonor(o, own?.CompatibilityClass != null && o.CompatibilityClass != own.CompatibilityClass,
                liveValues?.Contains(o.SemanticId) == true || catalog.ProvenCompositions.Any(p => p.Host == weapon && p.Output == o.SemanticId && p.Mechanism == AttackOutputHost.ProgrammableAmmo)))
            .OrderBy(d => d.LiveProven ? 0 : 1).ThenBy(d => d.Output.ReferenceScope != null ? 0 : 1).ThenBy(d => d.Output.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // 'none' only where the weapon has no native function projectile (a native one cannot be removed, only restored); 'native' only where
    // it has one; an output only when it is one of Donors.
    public static void Check(SdkMetadata sdk, string weapon, string baseline, string token)
    {
        if (token == None) { if (baseline != None) throw new InvalidDataException(CoreText.Format("Messages.Build.FunctionAmmo.NativeKept", weapon)); return; }
        if (token == Native) { if (baseline != Native) throw new InvalidDataException(CoreText.Format("Messages.Build.FunctionAmmo.NoNative", weapon)); return; }
        if (!IsOutput(token) || Donors(sdk, weapon, null).All(d => d.Output.SemanticId != token))
            throw new InvalidDataException(CoreText.Format("Messages.Build.FunctionAmmo.NotADonor", token, weapon));
    }
}

// hd2.fields.presentation.traits (an ordered list of up to maxTraits trait IDs) and hd2.fields.presentation.armor_penetration (one label).
// Presentation only: they never change gameplay, and they are the same five slots, so a weapon edits one or the other.
public static class TraitSets
{
    public const string Field = "presentation.traits", PenetrationField = "presentation.armor_penetration";
    public static IReadOnlyList<string> Traits(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
        ? value.EnumerateArray().Select(x => x.GetString()!).ToArray() : throw new InvalidDataException(CoreText.Get("Messages.Build.Traits.Invalid"));
    public static JsonElement Normalize(JsonElement value, int maxTraits, IReadOnlyCollection<string>? allowed)
    {
        var traits = Traits(value);
        if (traits.Count > maxTraits) throw new InvalidDataException(CoreText.Plural("Messages.Build.Traits.TooMany", maxTraits));
        if (traits.Distinct().Count() != traits.Count) throw new InvalidDataException(CoreText.Get("Messages.Build.Traits.Duplicate"));
        if (allowed != null && traits.FirstOrDefault(t => !allowed.Contains(t)) is { } unknown) throw new InvalidDataException(CoreText.Format("Messages.Build.Traits.Unknown", unknown));
        return JsonSerializer.SerializeToElement(traits);
    }
    public static bool Equal(JsonElement a, JsonElement b)
    {
        try { return Traits(a).SequenceEqual(Traits(b)); } catch (InvalidDataException) { return false; }
    }
    public static string Lua(JsonElement value) => "{" + string.Join(",", Traits(value).Select(LuaGenerator.Quote)) + "}";
    public static string Label(string value, IReadOnlyList<string>? allowed)
    {
        if (allowed?.Contains(value) != true) throw new InvalidDataException(CoreText.Format("Messages.Build.Traits.PenetrationNotAllowed", value));
        return value;
    }
}

// Status references on weapon damage rows (damage.status_<k>_type, explosion.damage.status_<k>_type, heat.level_<n>_self_status).
public static class StatusReferences
{
    private static readonly Regex SlotPattern = new(@"status_(\d)_type\z", RegexOptions.CultureInvariant);
    public static int? Slot(string semanticFieldId) => SlotPattern.Match(semanticFieldId) is { Success: true } m ? m.Groups[1].Value[0] - '0' : null;
    // The support catalog publishes no per-field list: Runtime accepts the attachable statuses (sdk/StatusEffectCatalog.json) plus the slot's
    // current status, and 'none' only on the last used slot of the row (or its first empty slot), so slots stay packed. The player catalog
    // publishes exactly this per field (allowedValues / allowNone).
    public static (IReadOnlyList<string> Allowed, bool AllowNone) Support(SdkMetadata sdk, SupportField f)
    {
        var current = f.Value.Baseline.ValueKind == JsonValueKind.String ? f.Value.Baseline.GetString()! : StatusReference.None;
        var attachable = sdk.StatusEffects?.Statuses.Values.Where(s => s.Attachable).Select(s => s.SemanticId) ?? [];
        var allowed = attachable.Concat(current == StatusReference.None ? [] : [current]).Distinct(StringComparer.Ordinal).ToArray();
        var slot = Slot(f.SemanticFieldId);
        var last = (sdk.SupportAuthoring?.FieldInstances ?? []).Where(x => x.IsStatusReference && x.Backing.ObjectKey == f.Backing.ObjectKey && Slot(x.SemanticFieldId) != null
                && x.Value.Baseline.ValueKind == JsonValueKind.String && x.Value.Baseline.GetString() != StatusReference.None)
            .Select(x => Slot(x.SemanticFieldId)!.Value).DefaultIfEmpty(0).Max();
        return (allowed, current == StatusReference.None || slot == null || slot == last);
    }
}

// The Lua literal of a 1.4.0 value type; null for older types (their own spelling applies). weaponTarget is the weapon's handle, used by
// a native function projectile's restore handle.
public static class CompositionLua
{
    public static string? Value(string type, JsonElement value, string weaponTarget) => type switch
    {
        WeaponCapability.FireRateSet => FireRateModes.Lua(value),
        WeaponCapability.TraitSet => TraitSets.Lua(value),
        WeaponCapability.FunctionProjectileReference => FunctionProjectile.Lua(FunctionProjectile.Token(value), weaponTarget),
        WeaponCapability.WeaponFunction or WeaponCapability.ArmorPenetrationLabel or WeaponCapability.StatusReference when value.ValueKind == JsonValueKind.String => LuaGenerator.Quote(value.GetString()!),
        _ => null,
    };
}

// The Runtime opt-ins a write carries. allow_unverified_effect follows the field's published acknowledgement, except for the exact values a
// live test proved on this target (liveProvenValues / liveEvidence.values); allow_unverified_reference is required by every donor function
// projectile except a live-proven pair. allow_shared is decided by the shared scope and never drops.
public static class CompositionOptIns
{
    public const string Shared = "allow_shared", Effect = "allow_unverified_effect", Reference = "allow_unverified_reference";
    public static readonly string[] Order = [Shared, Effect, Reference];
    // The value a live test's values name: the value itself for names, the output ID for a function projectile.
    private static string? Compared(string type, JsonElement value)
    {
        if (type == WeaponCapability.FunctionProjectileReference) { try { return FunctionProjectile.Token(value); } catch (InvalidDataException) { return null; } }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    public static bool LiveValue(IReadOnlyList<string>? live, string type, JsonElement baseline, JsonElement value) => live != null
        && (value.ValueKind == JsonValueKind.String && baseline.ValueKind == JsonValueKind.String && value.GetString() == baseline.GetString()
            || Compared(type, value) is { } token && live.Contains(token));
    public static bool NeedsEffect(string? acknowledgement, IReadOnlyList<string>? live, string type, JsonElement baseline, JsonElement value) =>
        acknowledgement == Effect && !LiveValue(live, type, baseline, value);
    public static bool NeedsReference(IReadOnlyList<string>? live, string type, JsonElement baseline, JsonElement value) =>
        type == WeaponCapability.FunctionProjectileReference && Compared(type, value) is { } token && FunctionProjectile.IsOutput(token) && !LiveValue(live, type, baseline, value);
    public static IReadOnlyList<string>? LiveValues(WeaponCapability f) => f.LiveProvenValues ?? f.LiveEvidence?.Values;
    public static IReadOnlyList<string>? LiveValues(SupportField f) => f.LiveEvidence?.Values;
    public static bool EffectFor(WeaponCapability f, JsonElement value) => NeedsEffect(f.Acknowledgement, LiveValues(f), f.Type, f.CurrentDefault, value);
    public static bool ReferenceFor(WeaponCapability f, JsonElement value) => NeedsReference(LiveValues(f), f.Type, f.CurrentDefault, value);
    public static bool EffectFor(SupportField f, JsonElement value) => NeedsEffect(f.Operation.Acknowledgement, LiveValues(f), f.Value.Type, f.Value.Baseline, value);
    public static bool ReferenceFor(SupportField f, JsonElement value) => NeedsReference(LiveValues(f), f.Value.Type, f.Value.Baseline, value);
}

// The Lua handle of a player-weapon authoring target: a weapon, or a sub-target reached through its parent
// (hd2.weapon('AR/GL-21 One-Two'):underbarrel(), docs/player-weapon-composition.md).
public static class WeaponTargets
{
    public static string Lua(SdkMetadata sdk, string weapon) => sdk.PlayerWeapons?.FindSubweapon(weapon) is { } sub
        ? "hd2.weapon(" + LuaGenerator.Quote(sub.SubweaponOf) + "):" + sub.Kind + "()"
        : "hd2.weapon(" + LuaGenerator.Quote(weapon) + ")";
    public static string Support(string weapon) => "hd2.support_weapon(" + LuaGenerator.Quote(weapon) + ")";
}
