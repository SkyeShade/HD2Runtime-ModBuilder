using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

// The programmable-ammunition editor as two modes (function_ammo.projectile, docs/weapon-feeds.md), for player and support weapons alike.
// The base mode is always there: what the weapon fires while the function is off, the row its attack fires in this project (its own, or a
// projectile swap's donor). The alternate mode is what the ProgrammableAmmo function fires while it is on: nothing ('none'), the weapon's
// native programmable projectile ('native') or a donor row. The actions a mode offers follow Runtime's rules (FunctionProjectile.Check): an
// added alternate is removed back to 'none'; a native one is replaced and restored, never removed. Everything is read from the selector view
// and the SDK; the edits themselves go through SetSelectorAsync (one weapon_selector transaction).

// Row: the row the base mode fires; Own: the weapon's own row; Native: the base feed's published presentation.
public sealed record BaseAmmoMode(AttackOutput? Row, AttackOutput? Own, FeedPresentation? Native)
{
    // The weapon fires its own projectile (no projectile swap in this project).
    public bool Vanilla => Row == null || Own == null || Row.SemanticId == Own.SemanticId;
}
// Mode: absent, native or donor. Donor / Row: the donor pair and its row; Native: the native programmable feed's published presentation.
public sealed record AlternateAmmoMode(string Mode, string Token, FunctionDonor? Donor, AttackOutput? Row, FeedPresentation? Native)
{
    public const string Absent = "absent", NativeMode = "native", DonorMode = "donor";
    public bool Configured => Mode != Absent;
    // A donor pair a live test proved on this weapon (it carries no allow_unverified_* opt-in).
    public bool LiveProven => Donor?.LiveProven == true;
}
// The weapon-function input that switches the alternate mode on. State: native (the weapon's own binding), bound (this project binds it),
// pending (adding the alternate binds Input; Choices when there are several), held (the only input it can use holds the rate-of-fire selector:
// resolved where the alternate is chosen) or unavailable.
public sealed record AmmoSelectorInput(string State, string? Input, IReadOnlyList<string> Choices)
{
    public const string Native = "native", Bound = "bound", Pending = "pending", Held = "held", Unavailable = "unavailable";
}
public sealed record AmmoModes(string Kind, string Weapon, SelectorProjectile Source, BaseAmmoMode Base, AlternateAmmoMode Alternate, AmmoSelectorInput Input,
    SelectorOptIns OptIns)
{
    public bool Writable => Source.Writable;
    public string? Reason => Writable ? null : Source.Reason;
    public IReadOnlyList<FunctionDonor> Donors => Source.Donors;
    // Add: no alternate yet and Runtime accepts one.
    public bool CanAdd => Writable && !Alternate.Configured && Source.Baseline == FunctionProjectile.None;
    // Remove: an alternate this project added (back to 'none').
    public bool CanRemove => Writable && Alternate.Mode == AlternateAmmoMode.DonorMode && Source.Baseline == FunctionProjectile.None;
    // Restore: a native alternate this project replaced.
    public bool CanRestoreNative => Writable && Source.Baseline == FunctionProjectile.Native && Source.Current != FunctionProjectile.Native;
    // The opt-ins choosing a donor writes (warnings only): the field's allow_unverified_effect and allow_unverified_reference, except for a
    // pair a live test proved on this weapon.
    public IReadOnlyList<string> DonorOptIns(FunctionDonor donor) => donor.LiveProven ? []
        : [.. new[] { Source.AcknowledgementReason != null ? CompositionOptIns.Effect : null, CompositionOptIns.Reference }.OfType<string>()];
}

public sealed partial class BuilderWorkspace
{
    /// <summary>One weapon's programmable ammunition as its base and alternate modes, or null when it publishes none.</summary>
    public AmmoModes? AmmoModes(string kind, string weapon)
    {
        if (Metadata == null || Selector(kind, weapon) is not { Projectile: { } ammo } view) return null;
        var feeds = Metadata.Feeds?.Of(kind, weapon)?.Feeds ?? [];
        var primary = feeds.FirstOrDefault(f => f.Mechanism == "projectile");
        var host = kind == CompositionKind.Support ? AttackOutputChange.SupportHost : AttackOutputChange.PlayerHost;
        var baseMode = new BaseAmmoMode(FiredRow(host, weapon, primary?.AttackRole ?? "primary") ?? ammo.Base, ammo.Base, primary?.Presentation);

        var native = ammo.Feed?.Presentation;
        var alternate = ammo.Current switch
        {
            FunctionProjectile.None => new AlternateAmmoMode(AlternateAmmoMode.Absent, ammo.Current, null, null, null),
            FunctionProjectile.Native => new AlternateAmmoMode(AlternateAmmoMode.NativeMode, ammo.Current, null, null, native),
            var token => new AlternateAmmoMode(AlternateAmmoMode.DonorMode, token, ammo.Donors.FirstOrDefault(d => d.Output.SemanticId == token),
                Metadata.AttackOutputs?.Output(token), null),
        };

        AmmoSelectorInput input;
        var free = view.Candidates(WeaponFunctions.ProgrammableAmmo).Where(i => i != view.RatesHold).ToArray();
        if (ammo.SelectorBound) input = new(AmmoSelectorInput.Native, ammo.SelectorInput, []);
        else if (view.BoundInput(WeaponFunctions.ProgrammableAmmo) is { } bound) input = new(AmmoSelectorInput.Bound, bound, free);
        else if (free.Length > 0) input = new(AmmoSelectorInput.Pending, free[0], free);
        else if (view.RatesHold is { } held && view.Candidates(WeaponFunctions.ProgrammableAmmo).Contains(held)) input = new(AmmoSelectorInput.Held, held, []);
        else input = new(AmmoSelectorInput.Unavailable, null, []);
        return new(kind, weapon, ammo, baseMode, alternate, input, view.AmmoOptIns);
    }

    /// <summary>The mode label and HUD icon edits saved on a row (hd2.fields.presentation.mode_label / mode_icon).</summary>
    public IReadOnlyList<OutputRowChange> ModePresentationChanges(string output) =>
        RowChanges(output).Where(c => c.Field is OutputRowChangeService.ModeLabel or OutputRowChangeService.ModeIcon).ToArray();
    /// <summary>Removes a row's mode label and HUD icon edits together (its slot edits stay).</summary>
    public Task ResetModePresentationAsync(string output) => EditRowsAsync(project =>
        project.OutputRowChanges?.RemoveAll(c => c.Output == output && c.Field is OutputRowChangeService.ModeLabel or OutputRowChangeService.ModeIcon), output);
}
