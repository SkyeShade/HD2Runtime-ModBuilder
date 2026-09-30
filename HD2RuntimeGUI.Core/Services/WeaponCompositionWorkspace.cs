using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

// 1.4.0 weapon composition (HD2Runtime 0.28.0) for player weapons (hd2.weapon) and support weapons (hd2.support_weapon): the rate-of-fire
// selector group, armory presentation and status references, as views built from the published fields and the saved edits, and the
// setters that save one weapon's related fields together. Everything a view offers comes from the SDK; Runtime's pairing rules are
// resolved here, where the edit happens, so an inconsistent state is never saved.
public static class CompositionKind
{
    public const string Player = "player", Support = "support";
}

// The three rate slots of one weapon as published and as edited. LegacyRate: a saved weapon.fire_rate edit (the Y slot), which a rate-mode
// edit takes over.
public sealed record SelectorRates(bool Writable, string State, string? Reason, float[] Baseline, float[] Current, IReadOnlyList<string> SlotNames, string DefaultSlot,
    bool SelectorBound, string? SelectorInput, IReadOnlyList<string> BindableInputs, double Min, double Max, int MaxModes, IReadOnlyList<string> OverriddenWhenEquipped,
    string? AcknowledgementReason, FieldLiveEvidence? LiveEvidence, string? EffectReason, float? LegacyRate)
{
    public bool Modified => !Baseline.SequenceEqual(Current);
    public int Filled => Current.Count(r => r != 0);
}
// One weapon-function input: its published binding, the edited one, and the functions it may take.
public sealed record SelectorInput(string Input, string Field, string Baseline, string Current, bool Editable, IReadOnlyList<string> Allowed, string? Reason)
{
    public bool Modified => Baseline != Current;
}
// The programmable-ammunition feed: the weapon's own projectile (the base) and what the ProgrammableAmmo function fires (the alternate).
public sealed record SelectorProjectile(bool Writable, string State, string? Reason, string Baseline, string Current, bool SelectorBound, string? SelectorInput,
    IReadOnlyList<string> BindableInputs, AttackOutput? Base, IReadOnlyList<FunctionDonor> Donors, string? AcknowledgementReason, WeaponFeed? Feed, string? EffectReason,
    IReadOnlyList<string> LiveValues)
{
    public bool Modified => Baseline != Current;
}
// The Runtime opt-ins one part of a saved group is written with (warnings only), each with its reason.
public sealed record SelectorOptIns(IReadOnlyList<string> Flags, IReadOnlyDictionary<string, string> Reasons)
{
    public static readonly SelectorOptIns None = new([], new Dictionary<string, string>());
}
// One weapon's weapon_selector group. RatesOptIns / AmmoOptIns: the opt-ins its rates and its programmable ammunition (each with the binding
// it needs) are written with, shown next to that part; the group's one transaction carries all of them (OptIns).
public sealed record WeaponSelector(string Kind, string Weapon, SelectorRates? Rates, IReadOnlyList<SelectorInput> Inputs, SelectorProjectile? Projectile,
    SelectorOptIns RatesOptIns, SelectorOptIns AmmoOptIns, bool Enabled, bool Saved, string? Issue)
{
    public IReadOnlyList<string> OptIns => [.. CompositionOptIns.Order.Where(f => RatesOptIns.Flags.Contains(f) || AmmoOptIns.Flags.Contains(f))];
    public bool Writable => Rates?.Writable == true || Projectile?.Writable == true;
    public bool Modified => Rates?.Modified == true || Projectile?.Modified == true || Inputs.Any(i => i.Modified);
    // Inputs that can take a selector this weapon hosts: unbound, editable, and published as bindable for it.
    public IReadOnlyList<string> Candidates(string function) => Inputs.Where(i => i.Editable && i.Baseline == WeaponFunctions.None && i.Allowed.Contains(function)
        && (function == WeaponFunctions.RateOfFire ? Rates?.BindableInputs : Projectile?.BindableInputs)?.Contains(i.Input) == true).Select(i => i.Input).ToArray();
    // The edited input bound to a function, if any.
    public string? BoundInput(string function) => Inputs.FirstOrDefault(i => i.Modified && i.Current == function)?.Input;
    // The input one selector holds in the saved group when the other would need it (null when it holds none): the rate-of-fire selector
    // this project binds for extra rates, or the programmable-ammunition selector it binds for a donor projectile.
    public string? RatesHold => Rates is { SelectorBound: false } r && r.Filled > 1 ? BoundInput(WeaponFunctions.RateOfFire) : null;
    public string? AmmoHolds => Projectile is { SelectorBound: false } a && FunctionProjectile.IsOutput(a.Current) ? BoundInput(WeaponFunctions.ProgrammableAmmo) : null;
}
// A desired selector state: rates (null = the published slots), a function projectile token (null = the published one), and the inputs to
// bind (null = the first free input that can take the selector).
public sealed record SelectorEdit(IReadOnlyList<float>? Rates, string? RatesInput, string? Projectile, string? ProjectileInput);

// Armory presentation (presentation.traits / presentation.armor_penetration): presentation only, never gameplay.
public sealed record PresentationTraits(bool Writable, string? Reason, IReadOnlyList<string> Baseline, IReadOnlyList<string> Current, int MaxTraits, IReadOnlyList<string> Choices,
    string? AcknowledgementReason, bool NeedsEffect, FieldLiveEvidence? LiveEvidence)
{
    public bool Modified => !Baseline.SequenceEqual(Current);
}
public sealed record PresentationPenetration(bool Writable, string? Reason, string Baseline, string Current, IReadOnlyList<string> Choices, string? State,
    string? AcknowledgementReason, bool NeedsEffect, IReadOnlyList<string>? LiveValues)
{
    public bool Modified => Baseline != Current;
}
public sealed record WeaponPresentation(string Kind, string Weapon, PresentationTraits? Traits, PresentationPenetration? Penetration, string? GameplaySeparation, string? Refresh);

public sealed partial class BuilderWorkspace
{
    // ---- Views -------------------------------------------------------------------------------------------------------------------------
    private static float[] RatesOrZero(JsonElement v) { try { return FireRateModes.Rates(v); } catch (InvalidDataException) { return [0, 0, 0]; } }
    private static string Effect(JsonElement? effect) => effect is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
    private static string? Nonempty(string s) => s.Length == 0 ? null : s;
    private static string NameOf(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private WeaponChange? SavedWeapon(string weapon, string field) => WeaponGroups.SingleOrDefault(g => g.Weapon == weapon && g.FieldId == field && g.Conflict == null)?.Representative;
    private SupportField? SupportWeaponField(string weapon, string field) => Metadata?.SupportAuthoring?.FieldInstances
        .FirstOrDefault(f => f.SupportWeapon == weapon && f.Target.Path == "weapon" && f.SemanticFieldId == field);
    private SupportChange? SavedSupport(SupportField? f) => f == null ? null : Project?.SupportChanges.SingleOrDefault(c => c.InstanceKey == f.InstanceKey);

    /// <summary>One weapon's rate-of-fire / weapon-function / programmable-ammunition group, or null when it publishes none of it.</summary>
    public WeaponSelector? Selector(string kind, string weapon) => kind == CompositionKind.Support ? SupportSelector(weapon) : PlayerSelector(weapon);
    private WeaponSelector? PlayerSelector(string name)
    {
        if (Metadata?.PlayerWeapons?.Find(name) is not { } w) return null;
        WeaponCapability? Field(string id) => w.Fields.FirstOrDefault(f => f.SemanticFieldId == id);
        bool Writable(WeaponCapability f) => Project != null && f.Editable && f.WriteAccepted && f.IsPreferred && !w.OrdinaryWritesBlocked;
        string? Reason(WeaponCapability f) => w.OrdinaryWritesBlocked ? w.BlockReason : f.Reason;
        var saved = new List<(WeaponChange Change, WeaponCapability Field)>();
        SelectorRates? rates = null;
        if (Field(FireRateModes.Field) is { } rf)
        {
            var change = SavedWeapon(name, rf.SemanticFieldId); if (change != null) saved.Add((change, rf));
            var legacy = SavedWeapon(name, FireRateModes.LegacyField)?.DesiredValue;
            rates = new(Writable(rf), rf.FireRateState ?? "blocked", Reason(rf), RatesOrZero(rf.CurrentDefault), RatesOrZero(change?.DesiredValue ?? rf.CurrentDefault),
                rf.SlotNames ?? FireRateModes.SlotNames, rf.DefaultSlot ?? "y", rf.SelectorBound == true, rf.SelectorInput, rf.BindableInputs ?? [], rf.Min ?? 1, rf.Max ?? 3000,
                rf.MaxModes ?? 1, rf.OverriddenWhenEquipped ?? [], rf.Acknowledgement == CompositionOptIns.Effect ? rf.AcknowledgementReason : null, rf.LiveEvidence, Nonempty(Effect(rf.Effect)),
                change == null && legacy is { ValueKind: JsonValueKind.Number } l ? (float)l.GetDouble() : null);
        }
        var inputs = new List<SelectorInput>();
        foreach (var input in WeaponFunctions.Inputs)
            if (Field(WeaponFunctions.Field(input)) is { } f)
            {
                var change = SavedWeapon(name, f.SemanticFieldId); if (change != null) saved.Add((change, f));
                inputs.Add(new(input, f.SemanticFieldId, NameOf(f.CurrentDefault), NameOf(change?.DesiredValue ?? f.CurrentDefault), Writable(f), f.AllowedNames ?? [], Reason(f)));
            }
        SelectorProjectile? projectile = null;
        if (Field(FunctionProjectile.Field) is { } pf && pf.CurrentDefault.ValueKind == JsonValueKind.Object)
        {
            var change = SavedWeapon(name, pf.SemanticFieldId); if (change != null) saved.Add((change, pf));
            var live = CompositionOptIns.LiveValues(pf) ?? [];
            projectile = new(Writable(pf), pf.FunctionAmmoState ?? "blocked", Reason(pf), FunctionProjectile.Token(pf.CurrentDefault),
                FunctionProjectile.Token(change?.DesiredValue ?? pf.CurrentDefault), pf.SelectorBound == true, pf.SelectorInput, pf.BindableInputs ?? [],
                Metadata.AttackOutputs?.Own(name), Writable(pf) ? FunctionProjectile.Donors(Metadata, name, live) : [],
                pf.Acknowledgement == CompositionOptIns.Effect ? pf.AcknowledgementReason : null,
                Metadata.Feeds?.Of(CompositionKind.Player, name)?.Feeds.FirstOrDefault(f => f.Mechanism == AttackOutputHost.ProgrammableAmmo), Nonempty(Effect(pf.Effect)), live);
        }
        if (rates == null && inputs.Count == 0 && projectile == null) return null;
        var needs = saved.Select(s => new SelectorNeed(IsAmmoPart(s.Field.SemanticFieldId, s.Change.DesiredValue),
            CompositionOptIns.EffectFor(s.Field, s.Change.DesiredValue) ? s.Field.AcknowledgementReason ?? "" : null, CompositionOptIns.ReferenceFor(s.Field, s.Change.DesiredValue))).ToArray();
        return Finish(CompositionKind.Player, name, rates, inputs, projectile, needs,
            saved.Select(s => s.Change.Enabled).DefaultIfEmpty(true).All(e => e), saved.Count > 0, Issue(saved.Select(s => s.Change.Id)));
    }
    private WeaponSelector? SupportSelector(string name)
    {
        if (Metadata?.SupportAuthoring == null) return null;
        bool Writable(SupportField f) => Project != null && f.Writable && !f.ReadOnly;
        var saved = new List<(SupportChange Change, SupportField Field)>();
        SelectorRates? rates = null;
        if (SupportWeaponField(name, FireRateModes.Field) is { FireRate: { } fr } rf)
        {
            var change = SavedSupport(rf); if (change != null) saved.Add((change, rf));
            var legacy = SavedSupport(SupportWeaponField(name, FireRateModes.LegacyField))?.DesiredValue;
            rates = new(Writable(rf), fr.FireRateState, rf.BlockedReason, RatesOrZero(rf.Value.Baseline), RatesOrZero(change?.DesiredValue ?? rf.Value.Baseline), fr.SlotNames,
                fr.DefaultSlot, fr.SelectorBound, fr.SelectorInput, fr.BindableInputs, fr.Min, fr.Max, fr.MaxModes, fr.OverriddenWhenEquipped,
                rf.Operation.Acknowledgement == CompositionOptIns.Effect ? rf.Operation.AcknowledgementReason : null, rf.LiveEvidence, Nonempty(Effect(rf.Effect)),
                change == null && legacy is { ValueKind: JsonValueKind.Number } l ? (float)l.GetDouble() : null);
        }
        var inputs = new List<SelectorInput>();
        foreach (var input in WeaponFunctions.Inputs)
            if (SupportWeaponField(name, WeaponFunctions.Field(input)) is { } f)
            {
                var change = SavedSupport(f); if (change != null) saved.Add((change, f));
                inputs.Add(new(input, f.SemanticFieldId, NameOf(f.Value.Baseline), NameOf(change?.DesiredValue ?? f.Value.Baseline), Writable(f), f.WeaponFunction?.AllowedValues ?? [], f.BlockedReason));
            }
        SelectorProjectile? projectile = null;
        if (SupportWeaponField(name, FunctionProjectile.Field) is { FunctionAmmo: { } fa } pf)
        {
            var change = SavedSupport(pf); if (change != null) saved.Add((change, pf));
            var live = CompositionOptIns.LiveValues(pf) ?? [];
            projectile = new(Writable(pf), fa.FunctionAmmoState, pf.BlockedReason, FunctionProjectile.Token(pf.Value.Baseline), FunctionProjectile.Token(change?.DesiredValue ?? pf.Value.Baseline),
                fa.SelectorBound, fa.SelectorInput, fa.BindableInputs, Metadata.AttackOutputs?.Own(name), Writable(pf) ? FunctionProjectile.Donors(Metadata, name, live) : [],
                pf.Operation.Acknowledgement == CompositionOptIns.Effect ? pf.Operation.AcknowledgementReason : null,
                Metadata.Feeds?.Of(CompositionKind.Support, name)?.Feeds.FirstOrDefault(f => f.Mechanism == AttackOutputHost.ProgrammableAmmo), Nonempty(Effect(pf.Effect)), live);
        }
        if (rates == null && inputs.Count == 0 && projectile == null) return null;
        var needs = saved.Select(s => new SelectorNeed(IsAmmoPart(s.Field.SemanticFieldId, s.Change.DesiredValue),
            CompositionOptIns.EffectFor(s.Field, s.Change.DesiredValue) ? s.Field.Operation.AcknowledgementReason ?? "" : null, CompositionOptIns.ReferenceFor(s.Field, s.Change.DesiredValue))).ToArray();
        return Finish(CompositionKind.Support, name, rates, inputs, projectile, needs,
            saved.Select(s => s.Change.Enabled).DefaultIfEmpty(true).All(e => e), saved.Count > 0, SupportSelectorIssue(saved.Select(s => s.Change.Id)));
    }
    // One saved field of a selector group and the opt-ins it is written with (Effect: the reason when it needs allow_unverified_effect).
    private sealed record SelectorNeed(bool Ammo, string? Effect, bool Reference);
    // A field belongs to the programmable ammunition when it is the function projectile or the binding that selects it; else to the rates.
    private static bool IsAmmoPart(string field, JsonElement value) => field == FunctionProjectile.Field || NameOf(value) == WeaponFunctions.ProgrammableAmmo;
    private static WeaponSelector Finish(string kind, string weapon, SelectorRates? rates, IReadOnlyList<SelectorInput> inputs, SelectorProjectile? projectile,
        IReadOnlyList<SelectorNeed> needs, bool enabled, bool saved, string? issue)
    {
        static SelectorOptIns OptIns(IEnumerable<SelectorNeed> part)
        {
            var list = part.ToArray(); var flags = new List<string>(); var reasons = new Dictionary<string, string>();
            if (list.Where(n => n.Effect != null).Select(n => n.Effect!).Distinct().ToArray() is { Length: > 0 } effect)
            { flags.Add(CompositionOptIns.Effect); reasons[CompositionOptIns.Effect] = string.Join(" ", effect.Where(r => r.Length > 0)); }
            if (list.Any(n => n.Reference)) { flags.Add(CompositionOptIns.Reference); reasons[CompositionOptIns.Reference] = CoreText.Get("Composition.Selector.ReferenceReason"); }
            return flags.Count == 0 ? SelectorOptIns.None : new(flags, reasons);
        }
        return new(kind, weapon, rates, inputs, projectile, OptIns(needs.Where(n => !n.Ammo)), OptIns(needs.Where(n => n.Ammo)), enabled, saved, issue);
    }
    private string? Issue(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        return Project == null || Metadata == null ? null : WeaponIssues.FirstOrDefault(i => set.Contains(i.ChangeId))?.Message;
    }
    private string? SupportSelectorIssue(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        if (Project == null || Metadata == null) return null;
        foreach (var c in Project.SupportChanges.Where(c => set.Contains(c.Id))) if (SupportIssue(c) is { } issue) return issue;
        return WeaponSelectorRules.SupportIssues(Metadata, Project.SupportChanges).FirstOrDefault(i => set.Contains(i.Id)).Message;
    }

    // ---- Selector setters ----------------------------------------------------------------------------------------------------------------
    /// <summary>Saves one weapon's rate slots, function projectile and the weapon-function bindings they need, together (Runtime's
    /// weapon_selector transaction). Rates beyond the default bind the rate-of-fire selector and a donor projectile binds ProgrammableAmmo on
    /// the chosen (or first free) input; nothing is saved when the state would be refused.</summary>
    public Task SetSelectorAsync(string kind, string weapon, SelectorEdit edit) => EditSelectorAsync(kind, weapon, view => Desired(view, edit));
    /// <summary>Removes one weapon's whole selector group.</summary>
    public Task ResetSelectorAsync(string kind, string weapon) => SetSelectorAsync(kind, weapon, new(null, null, null, null));
    // The edit that keeps a group as saved except the part a field belongs to (its rates and their binding, or its projectile and its binding).
    private static SelectorEdit Without(WeaponSelector view, string field)
    {
        var binding = view.Inputs.FirstOrDefault(i => i.Field == field)?.Current;
        var dropRates = field == FireRateModes.Field || binding == WeaponFunctions.RateOfFire;
        var dropProjectile = field == FunctionProjectile.Field || binding == WeaponFunctions.ProgrammableAmmo;
        return new(dropRates || view.Rates?.Modified != true ? null : view.Rates.Current, view.BoundInput(WeaponFunctions.RateOfFire),
            dropProjectile || view.Projectile?.Modified != true ? null : view.Projectile.Current, view.BoundInput(WeaponFunctions.ProgrammableAmmo));
    }
    private Task ResetSelectorFieldAsync(string kind, string weapon, string field) =>
        Selector(kind, weapon) is { } view ? SetSelectorAsync(kind, weapon, Without(view, field)) : Task.CompletedTask;
    // Desired values of every selector field (null = the published value) for an edit, with Runtime's pairing resolved.
    private static Dictionary<string, JsonElement?> Desired(WeaponSelector view, SelectorEdit edit)
    {
        var rates = edit.Rates?.ToArray();
        if (rates != null && view.Rates != null && rates.SequenceEqual(view.Rates.Baseline)) rates = null;
        if (rates != null && view.Rates?.Writable != true) throw new InvalidDataException(view.Rates?.Reason ?? CoreText.Get("Messages.Build.FireRate.ReadOnly"));
        var projectile = edit.Projectile == view.Projectile?.Baseline ? null : edit.Projectile;
        if (projectile != null && view.Projectile?.Writable != true) throw new InvalidDataException(view.Projectile?.Reason ?? CoreText.Get("Messages.Build.FunctionAmmo.ReadOnly"));
        var bindings = new Dictionary<string, string>();
        if (rates != null && rates.Count(r => r != 0) > 1 && !view.Rates!.SelectorBound)
        {
            var candidates = view.Candidates(WeaponFunctions.RateOfFire);
            var input = edit.RatesInput != null && candidates.Contains(edit.RatesInput) ? edit.RatesInput
                : candidates.FirstOrDefault(i => i != edit.ProjectileInput) ?? candidates.FirstOrDefault();
            bindings[input ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Selector.NoRateInput", view.Weapon))] = WeaponFunctions.RateOfFire;
        }
        if (projectile != null && FunctionProjectile.IsOutput(projectile) && !view.Projectile!.SelectorBound)
        {
            var candidates = view.Candidates(WeaponFunctions.ProgrammableAmmo).Where(i => !bindings.ContainsKey(i)).ToArray();
            if (edit.ProjectileInput != null && bindings.ContainsKey(edit.ProjectileInput))
                throw new InvalidDataException(CoreText.Format("Messages.Build.Selector.InputTaken", view.Weapon, edit.ProjectileInput));
            var input = edit.ProjectileInput != null && candidates.Contains(edit.ProjectileInput) ? edit.ProjectileInput : candidates.FirstOrDefault();
            bindings[input ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Selector.NoAmmoInput", view.Weapon))] = WeaponFunctions.ProgrammableAmmo;
        }
        if (WeaponSelectorRules.Problem(view.Weapon, rates, view.Rates?.SelectorBound == true, projectile, view.Projectile?.SelectorBound == true, bindings) is { } problem)
            throw new InvalidDataException(problem);
        var desired = new Dictionary<string, JsonElement?> { [FireRateModes.Field] = rates == null ? null : JsonSerializer.SerializeToElement(rates),
            [FunctionProjectile.Field] = projectile == null ? null : JsonSerializer.SerializeToElement(projectile) };
        foreach (var input in view.Inputs) desired[input.Field] = bindings.TryGetValue(input.Input, out var function) ? JsonSerializer.SerializeToElement(function) : null;
        return desired;
    }
    private async Task EditSelectorAsync(string kind, string weapon, Func<WeaponSelector, Dictionary<string, JsonElement?>> desired)
    {
        var view = Selector(kind, weapon) ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Selector.None", weapon));
        var values = desired(view);
        if (kind == CompositionKind.Support) await EditSupportAsync(p => ApplySupport(p, weapon, values, view));
        else await EditPlayerAsync(p => ApplyPlayer(p, weapon, values, view));
    }
    // Replaces a weapon's saved edits of the given fields with the desired values (null or the published value removes the edit). New edits
    // join the group's enabled / ensure state, so one transaction never mixes them; a rate-mode edit takes over a weapon.fire_rate edit.
    private void ApplyPlayer(ModProject p, string weapon, IReadOnlyDictionary<string, JsonElement?> values, WeaponSelector? view)
    {
        var existing = p.WeaponChanges.Where(c => c.Weapon == weapon && values.ContainsKey(Metadata!.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)?.SemanticFieldId ?? c.SemanticFieldId)).ToList();
        var enabled = existing.Select(c => c.Enabled).DefaultIfEmpty(true).All(e => e); var ensure = existing.Select(c => c.EnsureEnabled).DefaultIfEmpty(true).All(e => e);
        foreach (var (field, value) in values)
        {
            var old = existing.FirstOrDefault(c => (Metadata!.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)?.SemanticFieldId ?? c.SemanticFieldId) == field);
            p.WeaponChanges.RemoveAll(c => c.Weapon == weapon && (Metadata!.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)?.SemanticFieldId ?? c.SemanticFieldId) == field);
            if (value is not { } v) continue;
            var next = weaponChanges.Create(Metadata!, weapon, field, v.GetRawText(), false);
            if (WeaponScalar.IsNoOp(Metadata!, next)) continue;
            if (old != null) { next.Id = old.Id; next.ExpectedValue = old.ExpectedValue; next.BaselineSdkVersion = old.BaselineSdkVersion; next.Group = old.Group; next.Notes = old.Notes; }
            next.Enabled = enabled; next.EnsureEnabled = ensure;
            p.WeaponChanges.Add(next);
        }
        if (values.GetValueOrDefault(FireRateModes.Field) is not null) p.WeaponChanges.RemoveAll(c => c.Weapon == weapon && c.SemanticFieldId == FireRateModes.LegacyField);
        // Only the edited fields are checked here: an unrelated saved edit that needs review (a rebind) never blocks this one.
        var edited = p.WeaponChanges.Where(c => c.Weapon == weapon && values.ContainsKey(Metadata!.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)?.SemanticFieldId ?? c.SemanticFieldId)).ToArray();
        foreach (var c in edited.Where(c => c.Enabled)) weaponChanges.Validate(Metadata!, c);
        var ids = edited.Select(c => c.Id).ToHashSet();
        if (WeaponSelectorRules.PlayerIssues(Metadata!, p.WeaponChanges.Where(c => c.Weapon == weapon).ToArray()).FirstOrDefault(i => ids.Contains(i.Id)) is { Message: { } issue })
            throw new InvalidDataException(issue);
    }
    private void ApplySupport(ModProject p, string weapon, IReadOnlyDictionary<string, JsonElement?> values, WeaponSelector? view)
    {
        var fields = values.Keys.Select(id => SupportWeaponField(weapon, id)).Where(f => f != null).Select(f => f!).ToArray();
        var existing = p.SupportChanges.Where(c => fields.Any(f => f.InstanceKey == c.InstanceKey)).ToList();
        var enabled = existing.Select(c => c.Enabled).DefaultIfEmpty(true).All(e => e); var ensure = existing.Select(c => c.EnsureEnabled).DefaultIfEmpty(true).All(e => e);
        foreach (var f in fields)
        {
            var old = existing.FirstOrDefault(c => c.InstanceKey == f.InstanceKey);
            p.SupportChanges.RemoveAll(c => c.InstanceKey == f.InstanceKey);
            if (values[f.SemanticFieldId] is not { } v) continue;
            var next = supportChanges.Create(Metadata!, f.InstanceKey, v.GetRawText());
            if (SupportScalar.Equal(f, next.DesiredValue, f.Value.Baseline)) continue;
            if (old != null) next = next with { Id = old.Id, ExpectedValue = old.ExpectedValue, CapabilityEvidence = old.CapabilityEvidence, BaselineSdkVersion = old.BaselineSdkVersion,
                Group = old.Group, Notes = old.Notes, EffectAcknowledgement = old.EffectAcknowledgement };
            p.SupportChanges.Add(next with { Enabled = enabled, EnsureEnabled = ensure });
        }
        if (values.GetValueOrDefault(FireRateModes.Field) is not null && SupportWeaponField(weapon, FireRateModes.LegacyField) is { } legacy)
            p.SupportChanges.RemoveAll(c => c.InstanceKey == legacy.InstanceKey);
        var ids = p.SupportChanges.Where(c => fields.Any(f => f.InstanceKey == c.InstanceKey)).Select(c => c.Id).ToHashSet();
        if (WeaponSelectorRules.SupportIssues(Metadata!, p.SupportChanges.Where(c => c.Weapon == weapon).ToArray()).FirstOrDefault(i => ids.Contains(i.Id)) is { Message: { } issue })
            throw new InvalidDataException(issue);
    }
    private async Task EditPlayerAsync(Action<ModProject> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.WeaponChanges.ToList();
            try { edit(project); await SaveChangesAsync(); } catch { project.WeaponChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    // A weapon.fire_rate edit on a weapon whose rate modes are edited goes into their default (Y) slot: the two cover the same bytes.
    private async Task<bool> FoldLegacyFireRateAsync(string kind, string weapon, string field, string value)
    {
        if (field != FireRateModes.LegacyField || Selector(kind, weapon) is not { Rates: { Modified: true } rates } view) return false;
        float y;
        try { using var doc = JsonDocument.Parse(value); y = (float)doc.RootElement.GetDouble(); }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw new InvalidDataException(CoreText.Get("Messages.Build.Value.CompleteNumeric")); }
        var next = rates.Current.ToArray(); next[1] = y;
        await SetSelectorAsync(kind, weapon, new(next, view.BoundInput(WeaponFunctions.RateOfFire), view.Projectile?.Modified == true ? view.Projectile.Current : null,
            view.BoundInput(WeaponFunctions.ProgrammableAmmo)));
        return true;
    }
    /// <summary>Enables or disables a weapon's whole saved selector group.</summary>
    public Task<bool> ToggleSelectorGroupAsync(string kind, string weapon) => ToggleSelectorAsync(kind, weapon, FireRateModes.Field);
    // Enables or disables a weapon's whole selector group (Runtime writes it as one transaction).
    private async Task<bool> ToggleSelectorAsync(string kind, string weapon, string field)
    {
        if (!WeaponSelectorRules.IsSelectorField(field) || Selector(kind, weapon) is not { Saved: true } view) return false;
        var enabled = !view.Enabled;
        if (kind == CompositionKind.Support)
            await EditSupportAsync(p => p.SupportChanges = p.SupportChanges.Select(c => c.Weapon == weapon && WeaponSelectorRules.IsSelectorField(c.SemanticFieldId)
                && SupportWeaponField(weapon, c.SemanticFieldId)?.InstanceKey == c.InstanceKey ? c with { Enabled = enabled } : c).ToList());
        else
            await EditPlayerAsync(p => { foreach (var c in p.WeaponChanges.Where(c => c.Weapon == weapon && WeaponSelectorRules.IsSelectorField(c.SemanticFieldId))) c.Enabled = enabled; });
        return true;
    }

    // ---- Presentation ------------------------------------------------------------------------------------------------------------------
    public WeaponPresentation? Presentation(string kind, string weapon)
    {
        if (Metadata == null) return null;
        var catalog = Metadata.Presentation;
        PresentationTraits? traits = null; PresentationPenetration? penetration = null;
        var choices = catalog?.Traits.Keys.OrderBy(k => catalog.TraitLabel(k), StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (kind == CompositionKind.Support)
        {
            if (SupportWeaponField(weapon, TraitSets.Field) is { } tf)
            {
                var saved = SavedSupport(tf);
                traits = new(Project != null && tf.Writable, tf.BlockedReason, TraitSets.Traits(tf.Value.Baseline), Effective(kind, weapon, saved?.DesiredValue, tf.Value.Baseline),
                    tf.Presentation?.MaxTraits ?? 5, choices, tf.Operation.AcknowledgementReason, saved != null && CompositionOptIns.EffectFor(tf, saved.DesiredValue), tf.LiveEvidence);
            }
            if (SupportWeaponField(weapon, TraitSets.PenetrationField) is { } pf)
            {
                var saved = SavedSupport(pf);
                penetration = new(Project != null && pf.Writable, pf.BlockedReason, NameOf(pf.Value.Baseline), NameOf(saved?.DesiredValue ?? pf.Value.Baseline),
                    pf.Presentation?.AllowedValues ?? [], pf.Presentation?.ArmorPenetrationState, pf.Operation.AcknowledgementReason,
                    saved != null && CompositionOptIns.EffectFor(pf, saved.DesiredValue), CompositionOptIns.LiveValues(pf));
            }
        }
        else if (Metadata.PlayerWeapons?.Find(weapon) is { } w)
        {
            bool Writable(WeaponCapability f) => Project != null && f.Editable && f.WriteAccepted && !w.OrdinaryWritesBlocked;
            if (w.Fields.FirstOrDefault(f => f.SemanticFieldId == TraitSets.Field) is { } tf)
            {
                var saved = SavedWeapon(weapon, tf.SemanticFieldId);
                traits = new(Writable(tf), w.OrdinaryWritesBlocked ? w.BlockReason : tf.Reason, TraitsOrEmpty(tf.CurrentDefault), Effective(kind, weapon, saved?.DesiredValue, tf.CurrentDefault),
                    tf.MaxTraits ?? 5, tf.TraitValues?.Keys.OrderBy(k => catalog?.TraitLabel(k) ?? k, StringComparer.OrdinalIgnoreCase).ToArray() ?? choices,
                    tf.AcknowledgementReason, saved != null && CompositionOptIns.EffectFor(tf, saved.DesiredValue), tf.LiveEvidence);
            }
            if (w.Fields.FirstOrDefault(f => f.SemanticFieldId == TraitSets.PenetrationField) is { } pf)
            {
                var saved = SavedWeapon(weapon, pf.SemanticFieldId);
                penetration = new(Writable(pf), w.OrdinaryWritesBlocked ? w.BlockReason : pf.Reason, NameOf(pf.CurrentDefault), NameOf(saved?.DesiredValue ?? pf.CurrentDefault),
                    pf.AllowedNames ?? [], pf.ArmorPenetrationState, pf.AcknowledgementReason, saved != null && CompositionOptIns.EffectFor(pf, saved.DesiredValue), CompositionOptIns.LiveValues(pf));
            }
        }
        return traits == null && penetration == null ? null : new(kind, weapon, traits, penetration, catalog?.GameplaySeparation, catalog?.Refresh);
    }
    private static IReadOnlyList<string> TraitsOrEmpty(JsonElement v) { try { return TraitSets.Traits(v); } catch (InvalidDataException) { return []; } }
    // The trait list the armory shows after the saved edit: the edited list, or the published one with an edited penetration label applied.
    private IReadOnlyList<string> Effective(string kind, string weapon, JsonElement? savedTraits, JsonElement baseline)
    {
        if (savedTraits is { } t) return TraitsOrEmpty(t);
        var penetration = kind == CompositionKind.Support ? SavedSupport(SupportWeaponField(weapon, TraitSets.PenetrationField))?.DesiredValue : SavedWeapon(weapon, TraitSets.PenetrationField)?.DesiredValue;
        var list = TraitsOrEmpty(baseline);
        return penetration is { } p ? ApplyPenetration(list, NameOf(p), int.MaxValue) : list;
    }
    // A penetration choice's trait: the trait whose published label is the choice's label.
    private IReadOnlyDictionary<string, string> PenetrationTraits() => Metadata?.Presentation is { } c
        ? c.PenetrationChoices.Where(p => p.Label != null).Select(p => (p.Value, Trait: c.Traits.Values.FirstOrDefault(t => t.Label == p.Label)?.SemanticId))
            .Where(x => x.Trait != null).ToDictionary(x => x.Value, x => x.Trait!, StringComparer.Ordinal)
        : new Dictionary<string, string>();
    // Runtime's rule for one penetration label on a trait list: replace the penetration trait in place, add it in the first empty slot, or
    // (none) remove it and keep the others packed.
    private IReadOnlyList<string> ApplyPenetration(IReadOnlyList<string> traits, string penetration, int maxTraits)
    {
        var map = PenetrationTraits(); var list = traits.ToList();
        var index = list.FindIndex(t => map.Values.Contains(t));
        if (penetration == StatusReference.None) { if (index >= 0) list.RemoveAt(index); return list; }
        var trait = map.GetValueOrDefault(penetration) ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Traits.PenetrationNotAllowed", penetration));
        if (index >= 0) list[index] = trait;
        else if (list.Count < maxTraits) list.Add(trait);
        else throw new InvalidDataException(CoreText.Plural("Messages.Build.Traits.TooMany", maxTraits));
        return list;
    }
    /// <summary>Saves a weapon's armory traits (the whole ordered list) or its penetration label. Both are the same five native slots, so an
    /// edit to one while the other is edited is folded into the trait list instead of being saved beside it.</summary>
    public async Task SetPresentationAsync(string kind, string weapon, IReadOnlyList<string>? traits, string? penetration)
    {
        var view = Presentation(kind, weapon) ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Traits.None", weapon));
        var values = new Dictionary<string, JsonElement?>();
        if (traits != null)
        {
            if (view.Traits?.Writable != true) throw new InvalidDataException(view.Traits?.Reason ?? CoreText.Get("Messages.Build.Traits.ReadOnly"));
            values[TraitSets.Field] = traits.SequenceEqual(view.Traits.Baseline) ? null : JsonSerializer.SerializeToElement(traits);
            values[TraitSets.PenetrationField] = null;
        }
        else if (penetration != null)
        {
            var traitsEdited = view.Traits?.Modified == true && (kind == CompositionKind.Support ? SavedSupport(SupportWeaponField(weapon, TraitSets.Field)) != null : SavedWeapon(weapon, TraitSets.Field) != null);
            if (traitsEdited)
            {
                var list = ApplyPenetration(view.Traits!.Current, penetration, view.Traits.MaxTraits);
                values[TraitSets.Field] = list.SequenceEqual(view.Traits.Baseline) ? null : JsonSerializer.SerializeToElement(list);
            }
            else
            {
                if (view.Penetration?.Writable != true) throw new InvalidDataException(view.Penetration?.Reason ?? CoreText.Get("Messages.Build.Traits.ReadOnly"));
                values[TraitSets.PenetrationField] = penetration == view.Penetration.Baseline ? null : JsonSerializer.SerializeToElement(penetration);
            }
        }
        else { values[TraitSets.Field] = null; values[TraitSets.PenetrationField] = null; }
        if (kind == CompositionKind.Support) await EditSupportAsync(p => ApplySupport(p, weapon, values, null));
        else await EditPlayerAsync(p => ApplyPlayer(p, weapon, values, null));
    }

    // ---- Status references ---------------------------------------------------------------------------------------------------------------
    /// <summary>The statuses one status reference may take and whether 'none' clears it (published per field on player weapons; derived from
    /// the status catalog on support weapons).</summary>
    public (IReadOnlyList<string> Allowed, bool AllowNone) StatusChoices(WeaponCapability f) => (f.AllowedReferences ?? [], f.AllowNone == true);
    public (IReadOnlyList<string> Allowed, bool AllowNone) StatusChoices(SupportField f) => Metadata == null ? ([], false) : StatusReferences.Support(Metadata, f);
    // The published status name; statuses that share a name (gas / gas_2, the three hotshot levels) also show their key.
    public string StatusLabel(string key)
    {
        if (key == StatusReference.None) return CoreText.Get("Common.None");
        if (Metadata?.StatusEffects is not { } catalog || catalog.Find(key) is not { } status) return key;
        return catalog.Statuses.Values.Count(s => s.Name == status.Name) > 1 ? catalog.Label(key) + " · " + key : catalog.Label(key);
    }

    // ---- Sub-targets ---------------------------------------------------------------------------------------------------------------------
    /// <summary>The sub-targets (underbarrels) a weapon names, as authoring targets with their own fields.</summary>
    public IReadOnlyList<(Subweapon Info, PlayerWeapon Target)> Subweapons(string weapon) => Metadata?.PlayerWeapons is { } c && c.Find(weapon)?.Subweapons is { } links
        ? links.Select(l => c.FindSubweapon(l.Name)).Where(s => s != null).Select(s => (s!, c.Find(s!.Name)!)).ToArray() : [];
}
