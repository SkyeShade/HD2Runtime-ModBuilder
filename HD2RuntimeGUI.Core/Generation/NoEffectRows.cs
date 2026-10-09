using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>Where a backpack-fed weapon's ammunition really is: the backpack its call-in delivers (stratagem, hellpod rack and slot).
/// Authorable: the SDK publishes that backpack's ammunition (support weapon -> ammoBackpack, edited in Support › backpack ammunition).
/// The team-reload backpacks of HD2Runtime 0.30.2 are not: Runtime keeps their deposits read-only (sdk/docs/backpack-ammo.md "Not covered").</summary>
public sealed record AmmoHome(string Weapon, string Stratagem, string Backpack, string? Rack, int Slot, bool Authorable)
{
    /// <summary>The element that shows the backpack on its stratagem's page: its ammunition panel, or the drop-pod slot that delivers it.</summary>
    public string? Reveal => Authorable ? $"[data-backpack-ammo=\"{Backpack}\"]" : Rack == null ? null : $"[data-pod-rack=\"{Rack}\"] [data-pod-slot=\"{Slot}\"]";
}

/// <summary>HD2Runtime 0.30.2 noEffect (SupportWeaponAuthoringCapabilities fieldInstances[].noEffect.reason): the team-reload weapons' own
/// ammunition rows (AC-8 rounds.*, GR-8 / FAF-14 / RL-77 / StA-X3 magazine.starting_magazines and spare_magazines) are 0 natively because
/// their backpack holds the ammunition. Runtime still accepts a write, so older mods keep working, and asks editors to show the rows read-only
/// with the reason. A new edit is refused; an edit saved before (ModBuilder 1.7.0) is kept and exported unchanged until the user removes it.</summary>
public static class NoEffectRows
{
    public static bool Is(SupportField f) => f.NoEffect != null;
    public static SupportField? Field(SdkMetadata sdk, SupportChange c) => sdk.SupportAuthoring?.FieldInstances.FirstOrDefault(f => f.InstanceKey == c.InstanceKey);
    /// <summary>The saved edits of no-effect rows (kept and exported; each row offers to remove it).</summary>
    public static SupportChange[] Saved(ModProject? project, SdkMetadata? sdk) => project == null || sdk?.SupportAuthoring == null ? []
        : project.SupportChanges.Where(c => Field(sdk, c)?.NoEffect != null).ToArray();

    /// <summary>Refuses a new edit of a no-effect row with Runtime's reason. Re-saving the value an edit already holds (accepting a changed
    /// baseline) is allowed: that edit predates the notice and stays until it is removed.</summary>
    public static void RefuseNew(SupportField f, SupportChange? saved, JsonElement desired)
    {
        if (f.NoEffect is { } inert && (saved == null || !SupportScalar.Equal(f, saved.DesiredValue, desired)))
            throw new InvalidDataException(CoreText.Format("Messages.Build.Support.NoEffect", f.Display.Name, inert.Reason));
    }

    /// <summary>The backpack that holds a weapon's ammunition, found through published links only: the weapon's call-in (supportCallInLinks),
    /// that stratagem's hellpod rack (PodPayloadCapabilities consumers) and the active rack slot whose pickup is a backpack. Null when the
    /// weapon has no linked call-in or its rack delivers no backpack.</summary>
    public static AmmoHome? Home(SdkMetadata? sdk, string weapon)
    {
        if (sdk?.SupportLinks?.ForWeapon(weapon) is not { } link) return null;
        var ammo = sdk.SupportAuthoring?.Weapons.FirstOrDefault(w => w.Name == weapon)?.AmmoBackpack;
        var authorable = ammo != null && sdk.Entities?.Backpacks.Find(ammo.Backpack) != null;
        foreach (var rack in sdk.Entities?.Pods?.ForStratagem(link.Stratagem.SemanticId) ?? [])
            foreach (var slot in rack.AuthoredSlots.Where(s => s.Active && s.Current is { Category: "backpack", Name: not null }))
                return new(weapon, link.Stratagem.Name, authorable ? ammo!.Backpack : slot.Current!.Name!, rack.Name, slot.Slot, authorable);
        return authorable ? new(weapon, link.Stratagem.Name, ammo!.Backpack, null, 0, true) : null;
    }
}
