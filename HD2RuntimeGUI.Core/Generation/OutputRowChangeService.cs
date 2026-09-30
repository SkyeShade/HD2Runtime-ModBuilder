using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>One donor slot a row slot may reference (hd2.attack_output(donor):direct_damage() / :impact_explosion() / :expiry_explosion()),
/// or null Output for "none". Refused donors keep Runtime's reason; Acknowledgements are exactly what the write carries for this value.</summary>
public sealed record SlotDonor(AttackOutput? Output, string? Slot, string Value, bool Allowed, string? Refusal, string[] Acknowledgements, bool LiveProven);

// Projectile-builder writes on catalogued rows (format 11, attack-outputs.md "Projectile builder" and weapon-feeds.md "Mode labels and icons").
// A row write targets hd2.attack_output(row), never a host: it changes every entity that fires the row and does not follow a host swap.
//   slots         hd2.fields.projectile.direct_damage / impact_explosion / expiry_explosion
//                 expect: the row's own handle hd2.attack_output(row):<slot>(), or 'none' where it has none
//                 value:  a donor's handle of the same type (a damage slot takes direct_damage(), an explosion slot either explosion), or 'none'
//                         (explosions only: a projectile always has a direct hit)
//                 opt-ins: the slot's own (allow_shared when more than one entity fires the row; allow_unverified_effect unless the exact
//                         '<donor>#<slot>' / 'none' value is in the slot's liveProvenValues). allow_shared never drops.
//   presentation  hd2.fields.presentation.mode_label (a native label) / mode_icon (a native icon, or 'auto': the label's exact native icon,
//                 else the generic fallback). 'default' is never written: it is the unlabelled placeholder, restored by removing the edit.
//                 opt-ins: the row presentation's own; a live-proven label or (resolved) icon drops allow_unverified_effect.
// Each row gets at most two guarded transactions, like builder:operations(): its slots, and its label and icon (so 'auto' resolves with the
// label written in the same operation). Runtime refuses row writes carrying allow_unverified_reference, so none is ever emitted.
public static class OutputRowChangeService
{
    public const string DirectDamage = "projectile.direct_damage", ImpactExplosion = "projectile.impact_explosion", ExpiryExplosion = "projectile.expiry_explosion";
    public const string ModeLabel = "presentation.mode_label", ModeIcon = "presentation.mode_icon", None = "none";
    public static readonly string[] SlotFields = [DirectDamage, ImpactExplosion, ExpiryExplosion];
    public static readonly string[] PresentationFields = [ModeLabel, ModeIcon];
    public static readonly string[] Fields = [.. SlotFields, .. PresentationFields];
    private static readonly string[] Flags = ["allow_shared", "allow_unverified_effect"];
    public static bool IsSlot(string field) => SlotFields.Contains(field);
    public static string SlotKey(string field) => field switch
    {
        DirectDamage => AttackOutputSlots.DirectDamageKey, ImpactExplosion => AttackOutputSlots.ImpactExplosionKey, ExpiryExplosion => AttackOutputSlots.ExpiryExplosionKey,
        _ => throw new InvalidDataException(CoreText.Format("Messages.Build.Row.UnknownField", field)),
    };
    // The Lua method of a slot handle.
    public static string Method(string slotKey) => slotKey switch
    {
        AttackOutputSlots.DirectDamageKey => "direct_damage", AttackOutputSlots.ImpactExplosionKey => "impact_explosion", AttackOutputSlots.ExpiryExplosionKey => "expiry_explosion",
        _ => throw new InvalidDataException(CoreText.Format("Messages.Build.Row.UnknownField", slotKey)),
    };
    public static string Handle(string output, string slotKey) => output + "#" + slotKey;
    public static (string Output, string Slot)? ParseHandle(string value) =>
        value != None && value.Split('#') is [var output, var slot] && AttackOutputSlots.Keys.Contains(slot) ? (output, slot) : null;

    public static AttackOutput Row(SdkMetadata sdk, string output)
    {
        var o = AttackOutputChangeService.Catalog(sdk).Output(output) ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Row.Unknown", output));
        if (o.Slots == null || o.Presentation == null) throw new InvalidDataException(CoreText.Format("Messages.Build.Row.NotSelectable", o.Label, o.BlockedReason ?? o.Family));
        return o;
    }
    // The row's reviewed value of a field: what a write expects, and what removing the edit restores.
    public static string Baseline(AttackOutput row, string field) => IsSlot(field)
        ? row.Slots![SlotKey(field)].Current ? Handle(row.SemanticId, SlotKey(field)) : None
        : field == ModeLabel ? row.Presentation!.Label : field == ModeIcon ? row.Presentation!.Icon : throw new InvalidDataException(CoreText.Format("Messages.Build.Row.UnknownField", field));

    // ---- slots ---------------------------------------------------------------------------------------------------------------------------
    public static string[] SlotAcknowledgements(AttackOutputSlot slot, string value) =>
        [.. slot.Acknowledgements.Where(a => a != "allow_unverified_effect" || !slot.LiveProvenValues.Contains(value)).Distinct().Order(StringComparer.Ordinal)];
    // Every value one slot of a row may take: 'none' for an explosion slot, then each catalogued donor slot of the same type that exists (the
    // row's own current handle is its baseline). Donors without a catalogued package are refused (ASSET_UNAVAILABLE).
    public static IReadOnlyList<SlotDonor> SlotDonors(SdkMetadata sdk, string output, string field)
    {
        var catalog = AttackOutputChangeService.Catalog(sdk); var row = Row(sdk, output); var key = SlotKey(field); var slot = row.Slots![key];
        var baseline = Baseline(row, field); var list = new List<SlotDonor>();
        if (slot.AllowNone && baseline != None) list.Add(new(null, null, None, true, null, SlotAcknowledgements(slot, None), slot.LiveProvenValues.Contains(None)));
        string[] kinds = key == AttackOutputSlots.DirectDamageKey ? [AttackOutputSlots.DirectDamageKey] : [AttackOutputSlots.ImpactExplosionKey, AttackOutputSlots.ExpiryExplosionKey];
        foreach (var o in catalog.Outputs.Where(o => o.Slots != null))
            foreach (var k in kinds.Where(k => o.Slots![k].Current))
            {
                var value = Handle(o.SemanticId, k);
                if (value == baseline) continue;
                var refusal = o.SemanticId != row.SemanticId && !o.Package.AutoLoad ? AttackOutputCatalog.DonorAssetRefusal(o) : null;
                list.Add(new(o, k, value, refusal == null, refusal, SlotAcknowledgements(slot, value), slot.LiveProvenValues.Contains(value)));
            }
        return list.OrderBy(d => d.Output == null ? 0 : 1).ThenBy(d => d.Allowed ? 0 : 1).ThenBy(d => d.LiveProven ? 0 : 1)
            .ThenBy(d => d.Output?.Label, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Slot, StringComparer.Ordinal).ToArray();
    }

    // ---- presentation --------------------------------------------------------------------------------------------------------------------
    public static ModePresentation Modes(SdkMetadata sdk) => AttackOutputChangeService.Catalog(sdk).ModePresentation
        ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Output.SdkMissing", AttackOutputReader.FileName));
    // The labels a mode may be given (native strings only; custom text is not supported).
    public static IReadOnlyList<ModeLabel> Labels(SdkMetadata sdk) => Modes(sdk).Labels;
    // The icons a write may use: every native weapon-function icon and 'auto'; 'default' (the empty placeholder) only by restoring vanilla.
    public static IReadOnlyList<ModeIcon> Icons(SdkMetadata sdk) => Modes(sdk).Icons.Where(i => i.Value != ModePresentation.Default).ToArray();
    // What 'auto' writes: the exact native icon of the label written in the same operation (else the row's current label), or the generic fallback.
    public static (string Icon, string Source) AutoIcon(ModePresentation modes, AttackOutput row, string? label)
    {
        var entry = modes.Labels.FirstOrDefault(l => l.Value == (label ?? row.Presentation!.Label));
        return entry != null ? (entry.Icon, entry.IconSource) : (modes.GenericFallbackIcon, "generic_fallback");
    }
    public static string[] PresentationAcknowledgements(ModePresentation modes, AttackOutput row, string field, string value, string? label)
    {
        var p = row.Presentation!;
        var proven = field == ModeLabel ? p.LiveProven.Labels.Contains(value)
            : p.LiveProven.Icons.Contains(value == ModePresentation.Auto ? AutoIcon(modes, row, label).Icon : value);
        return [.. p.Acknowledgements.Where(a => a != "allow_unverified_effect" || !proven).Distinct().Order(StringComparer.Ordinal)];
    }
    // The label an 'auto' icon of this row resolves with in the project: an enabled label edit (written in the same operation), else none.
    public static string? LabelContext(ModProject? project, string output) =>
        project?.OutputRowChanges?.FirstOrDefault(c => c.Enabled && c.Output == output && c.Field == ModeLabel)?.Value;

    // ---- changes -------------------------------------------------------------------------------------------------------------------------
    public static OutputRowChange Create(SdkMetadata sdk, ModProject? project, string output, string field, string value)
    {
        var row = Row(sdk, output); var baseline = Baseline(row, field);
        if (value == baseline) throw new InvalidDataException(CoreText.Get("Messages.Build.Row.Baseline"));
        if (IsSlot(field))
        {
            var damage = SlotKey(field) == AttackOutputSlots.DirectDamageKey;
            // Runtime's refusals, in its order: removing the direct hit, a cross slot type, a slot the donor lacks, anything uncatalogued.
            var donor = SlotDonors(sdk, output, field).FirstOrDefault(d => d.Value == value)
                ?? throw new InvalidDataException(value == None ? CoreText.Get("Messages.Build.Row.NoneRefused")
                    : ParseHandle(value) is not { } h ? CoreText.Format("Messages.Build.Row.DonorRefused", value, row.Label)
                    : (h.Slot == AttackOutputSlots.DirectDamageKey) != damage ? CoreText.Get(damage ? "Messages.Build.Row.DamageSlotType" : "Messages.Build.Row.ExplosionSlotType")
                    : AttackOutputChangeService.Catalog(sdk).Output(h.Output)?.Slots?[h.Slot] is { Current: false } ? CoreText.Format("Messages.Build.Row.DonorHasNoSlot", h.Output, Method(h.Slot))
                    : CoreText.Format("Messages.Build.Row.DonorRefused", value, row.Label));
            if (!donor.Allowed) throw new InvalidDataException(donor.Refusal);
            return new() { Output = output, OutputName = row.Label, Field = field, Expect = baseline, Value = value, ValueName = donor.Output?.Label, Acknowledgements = donor.Acknowledgements,
                Evidence = SlotEvidence(row, field, donor), BaselineSdkVersion = sdk.Version };
        }
        var modes = Modes(sdk);
        if (!row.Presentation!.Writable) throw new InvalidDataException(CoreText.Format("Messages.Build.Row.PresentationReadOnly", row.Label));
        if (field == ModeLabel ? !modes.Labels.Any(l => l.Value == value) : !Icons(sdk).Any(i => i.Value == value))
            throw new InvalidDataException(CoreText.Format(field == ModeLabel ? "Messages.Build.Row.LabelRefused" : "Messages.Build.Row.IconRefused", value));
        var label = field == ModeIcon ? LabelContext(project, output) : null;
        var acks = PresentationAcknowledgements(modes, row, field, value, label);
        return new() { Output = output, OutputName = row.Label, Field = field, Expect = baseline, Value = value, Acknowledgements = acks,
            Evidence = PresentationEvidence(modes, row, field, value, label, acks), BaselineSdkVersion = sdk.Version };
    }
    private static string SlotEvidence(AttackOutput row, string field, SlotDonor donor) => SupportChangeService.Hash(JsonSerializer.Serialize(new
    {
        row.SemanticId, row.Owner, row.SpareTwin?.TwinOf, Field = field, RowSlot = row.Slots![SlotKey(field)], donor.Value, Donor = donor.Output?.SemanticId, DonorKey = donor.Slot,
        DonorSlot = donor.Output?.Slots![donor.Slot!], DonorPackage = donor.Output?.Package, donor.Acknowledgements,
    }));
    private static string PresentationEvidence(ModePresentation modes, AttackOutput row, string field, string value, string? label, string[] acks) => SupportChangeService.Hash(JsonSerializer.Serialize(new
    {
        row.SemanticId, row.Owner, Field = field, row.Presentation, Value = value,
        Label = field == ModeLabel ? modes.Labels.FirstOrDefault(l => l.Value == value) : null,
        Icon = field == ModeIcon ? value == ModePresentation.Auto ? AutoIcon(modes, row, label).Icon : value : null, Acknowledgements = acks,
    }));
    public static void Validate(SdkMetadata sdk, ModProject? project, OutputRowChange c)
    {
        if (!Fields.Contains(c.Field)) throw new InvalidDataException(CoreText.Format("Messages.Build.Row.UnknownField", c.Field));
        var current = Create(sdk, project, c.Output, c.Field, c.Value);
        if (current.Expect != c.Expect || current.Evidence != c.Evidence || !current.Acknowledgements.SequenceEqual(c.Acknowledgements.Order(StringComparer.Ordinal)))
            throw new InvalidDataException(CoreText.Format("Messages.Build.Row.Changed", c.OutputName, Label(c.Field)));
    }
    // The field's name in messages (Runtime identifiers stay as published; only the wording around them is translated).
    public static string Label(string field) => field switch
    {
        DirectDamage => CoreText.Get("Messages.Row.Field.DirectDamage"), ImpactExplosion => CoreText.Get("Messages.Row.Field.ImpactExplosion"),
        ExpiryExplosion => CoreText.Get("Messages.Row.Field.ExpiryExplosion"), ModeLabel => CoreText.Get("Messages.Row.Field.ModeLabel"),
        ModeIcon => CoreText.Get("Messages.Row.Field.ModeIcon"), _ => field,
    };
    // Player terminal-explosion edits write the same member of the same row as an impact / expiry slot write (a projectile's terminal action
    // is its row's explosion reference): both cannot own it. Only a weapon's own primary projectile is mapped to its catalogued row.
    public static CompositionChange? TerminalConflict(SdkMetadata sdk, ModProject project, OutputRowChange c)
    {
        if (c.Field is not (ImpactExplosion or ExpiryExplosion) || sdk.AttackOutputs is not { } catalog) return null;
        var phase = c.Field == ImpactExplosion ? "impact" : "expiry";
        return project.CompositionChanges.FirstOrDefault(t => t.Enabled && t.Kind == "terminal" && t.Phase == phase && t.Target.AttackRole == "primary"
            && catalog.OwnedBy(t.Target.Weapon) is { Owner.Kind: AttackProjectileSource.PlayerKind } o && o.SemanticId == c.Output);
    }
    // The (row, field) pairs this project writes: other writers of rows (a programmable-ammunition builder) must not write the same pair.
    public static IReadOnlyList<(string Output, string Field)> Writes(ModProject project) =>
        [.. (project.OutputRowChanges ?? []).Where(c => c.Enabled).Select(c => (c.Output, c.Field))];

    public static IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk)
    {
        var active = (project.OutputRowChanges ?? []).Where(c => c.Enabled).ToArray();
        if (active.Length == 0) return [];
        if (active.GroupBy(c => (c.Output, c.Field)).Any(g => g.Count() > 1)) throw new InvalidDataException(CoreText.Get("Messages.Build.Row.OnePerField"));
        var output = new List<string>();
        foreach (var group in active.GroupBy(c => (c.Output, Slots: IsSlot(c.Field), c.EnsureEnabled)).OrderBy(g => g.Key.Output, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Slots ? 0 : 1).ThenBy(g => g.Key.EnsureEnabled ? 0 : 1))
        {
            var changes = group.OrderBy(c => Array.IndexOf(Fields, c.Field)).ToArray();
            foreach (var c in changes)
            {
                Validate(sdk, project, c);
                if (TerminalConflict(sdk, project, c) is { } t)
                    throw new InvalidDataException(CoreText.Format("Messages.Build.Row.TerminalConflict", c.OutputName, Label(c.Field), t.Weapon));
            }
            var id = "row-" + SupportChangeService.Hash(project.ResourceId + "\nrow\n" + group.Key.Output + "\n" + (group.Key.Slots ? "slots" : "presentation")
                + (group.Key.EnsureEnabled ? "" : "\nonce"))[..24];
            var body = "{\n    id=" + LuaGenerator.Quote(id) + ",\n    target=hd2.attack_output(" + LuaGenerator.Quote(group.Key.Output) + "),\n";
            foreach (var flag in Flags.Where(f => changes.Any(c => c.Acknowledgements.Contains(f)))) body += "    " + flag + "=true,\n";
            body += "    changes={\n";
            foreach (var c in changes) body += "        {field=hd2.fields." + c.Field + ",expect=" + ValueLua(c.Field, c.Expect) + ",value=" + ValueLua(c.Field, c.Value) + "},\n";
            body += "    },\n}";
            output.Add(OptionBindings.Wrap(null, "transaction", body, group.Key.EnsureEnabled, []));
        }
        return output;
    }
    public static string ValueLua(string field, string value) => IsSlot(field) && ParseHandle(value) is { } h
        ? "hd2.attack_output(" + LuaGenerator.Quote(h.Output) + "):" + Method(h.Slot) + "()" : LuaGenerator.Quote(value);
}
