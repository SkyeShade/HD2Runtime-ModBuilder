using System.Text.Json;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 support-weapon charge (docs/runtime030-integration-plan.md, Phase 6 and 3.3): the charge fields, the PLAS-45 Epoch's
// charge-level roles and both overcharge explosions are authored with Runtime's ranges and opt-ins; edits that would write one native
// value twice, or edit an overcharge explosion row its swapped reference makes Runtime refuse, are resolved where they are made; the
// generated Lua has the shape of Runtime's own live tests (examples/projects/RailgunChargeTest, EpochExplosionsTest).
public sealed class RuntimeChargeTests
{
    private const string Railgun = "RS-422 Railgun", Epoch = "PLAS-45 Epoch", Arc = "ARC-3 Arc Thrower", Meltagun = "40-K Meltagun";
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string version = Runtime030Tests.Version)
    {
        var sdk = await SdkFixtures.Install(e, version); var w = e.Workspace();
        await w.CreateAsync(new("Charge", "Tests", "mods/tests/charge", "0.1.0"), sdk); return w;
    }
    private static SupportField Field(BuilderWorkspace w, string weapon, string id, string? role = null) => w.Metadata!.SupportAuthoring!.FieldInstances
        .Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == id && f.Target.AttackRole == role);
    // The generated operation (pcall-isolated block) that writes a field constant.
    private static string Operation(string lua, string text) => System.Text.RegularExpressions.Regex.Split(lua, @"(?=add\(function\(\) return )")
        .Single(b => b.Contains(text, StringComparison.Ordinal));
    private static async Task<string> MinVersion(BuilderWorkspace w)
    {
        await w.ExportAsync();
        using var zip = System.IO.Compression.ZipFile.OpenRead(w.LastExport!);
        using var r = new StreamReader(zip.GetEntry("hd2runtime.json")!.Open());
        return (string)System.Text.Json.Nodes.JsonNode.Parse(r.ReadToEnd())!["requires"]!["hd2runtime"]!["min_version"]!;
    }

    [Fact] public async Task Every_charge_field_and_charge_role_is_authored_and_the_audit_says_so()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var catalog = w.Metadata!.SupportAuthoring!;
        var charge = catalog.FieldInstances.Where(ChargeFields.Is).ToArray();
        Assert.Equal(53, charge.Length); Assert.All(charge, f => Assert.True(f.Writable && !f.ReadOnly, f.InstanceKey));
        Assert.Equal(60, catalog.FieldInstances.Count(f => f.Target.AttackRole is { } r && AuthoredTypes.SupportRoles030.Contains(r) && f.Writable));
        // Every field of the new roles needs allow_shared and allow_unverified_effect.
        Assert.All(catalog.FieldInstances.Where(f => f.Target.AttackRole is { } r && AuthoredTypes.SupportRoles030.Contains(r)),
            f => Assert.True(f.Operation.AllowSharedRequired && f.Operation.Acknowledgement == CompositionOptIns.Effect, f.InstanceKey));
        // The stages follow the published ids; the four charge weapons (and only they) have a charge section.
        Assert.Equal([Meltagun, Arc, Epoch, Railgun], charge.Select(f => f.SupportWeapon).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(charge, f => ChargeFields.StageOf(f.SemanticFieldId) == ChargeFields.Stage.Other);
        Assert.Equal([ChargeFields.MinimumTime, "charge.speed_multiplier_min"], ChargeFields.Of(catalog, Railgun).Take(2).Select(f => f.SemanticFieldId));
        var audit = CapabilityAudit.Run(w.Metadata!);
        var reference = Assert.Single(audit.Entries, x => x.Area == "support_weapon" && x.Capability == ChargeFields.OverchargeExplosion);
        Assert.Equal(CapabilityClass.SUPPORTED_UI, reference.Class);
        Assert.Null(CapabilityExemptions.Find("support_weapon", ChargeFields.OverchargeExplosion, "*"));
    }

    [Fact] public async Task Charge_scalars_generate_what_Runtime_accepts_and_raise_the_required_runtime()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        // Charge times are older ids: no acknowledgement, and they share one transaction (Runtime checks their order per operation).
        await w.SetSupportAsync(Field(w, Railgun, ChargeFields.FullTime).InstanceKey, "2");
        await w.SetSupportAsync(Field(w, Railgun, ChargeFields.OverchargeTime).InstanceKey, "8");
        Assert.Null(w.BuildError);
        var times = Operation(w.LuaPreview, "hd2.fields.charge.level_2");
        Assert.Contains("target=hd2.support_weapon('RS-422 Railgun'),", times);
        Assert.Contains("{field=hd2.fields.charge.level_2,expect=0.5,value=2},", times); Assert.Contains("{field=hd2.fields.charge.level_3,expect=3,value=8},", times);
        Assert.DoesNotContain("allow_", times);
        // A new charge field joins the same transaction, which then carries its opt-in.
        await w.SetSupportAsync(Field(w, Railgun, ChargeFields.ExplodeAtOvercharge).InstanceKey, "false");
        await w.SetSupportAsync(Field(w, Arc, "charge.burst_shots").InstanceKey, "3");
        await w.SetSupportAsync(Field(w, Arc, "charge.burst_interval_seconds").InstanceKey, "0.25");
        Assert.Null(w.BuildError);
        var rail = Operation(w.LuaPreview, "hd2.fields.charge.explode_at_overcharge");
        Assert.Contains("allow_unverified_effect=true,", rail); Assert.Contains("{field=hd2.fields.charge.explode_at_overcharge,expect=true,value=false},", rail);
        var arc = Operation(w.LuaPreview, "hd2.fields.charge.burst_shots");
        Assert.Contains("target=hd2.support_weapon('ARC-3 Arc Thrower'),", arc); Assert.Contains("allow_unverified_effect=true,", arc);
        Assert.Contains("{field=hd2.fields.charge.burst_shots,expect=0,value=3},", arc); Assert.Contains("{field=hd2.fields.charge.burst_interval_seconds,expect=0,value=0.25},", arc);
        Assert.Equal(Runtime030Tests.Version, RuntimeRequirement.Minimum(w.Project!, w.Metadata!));
        Assert.Contains(RuntimeRequirement.Floors(w.Project!), f => f.Reason == "support_weapon charge.explode_at_overcharge");
        // The published range is enforced where the value is typed (Runtime refuses it otherwise).
        var shots = Field(w, Arc, "charge.burst_shots");
        Assert.Contains(shots.Charge!.RangeReason!, (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(shots.InstanceKey, "11"))).Message);
        Assert.Equal("3", w.Project!.SupportChanges.Single(c => c.InstanceKey == shots.InstanceKey).DesiredValue.GetRawText());
    }

    [Fact] public async Task The_overcharge_explosion_reference_is_a_choice_and_another_weapons_needs_the_reference_opt_in()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var reference = Field(w, Railgun, ChargeFields.OverchargeExplosion);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(reference.InstanceKey, "\"SG-225 Breaker\""));
        await w.SetSupportAsync(reference.InstanceKey, "\"PLAS-45 Epoch\"");
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "hd2.fields.charge.overcharge_explosion");
        Assert.Contains("allow_unverified_effect=true,", op); Assert.Contains("allow_unverified_reference=true,", op);
        Assert.Contains("expect='RS-422 Railgun',", op); Assert.Contains("value='PLAS-45 Epoch',", op);
        Assert.True(CompositionOptIns.ReferenceFor(reference, JsonSerializer.SerializeToElement(Epoch)));
        Assert.False(CompositionOptIns.ReferenceFor(reference, JsonSerializer.SerializeToElement(Railgun)));
        // Back to the weapon's own explosion is a reset.
        await w.SetSupportAsync(reference.InstanceKey, "\"RS-422 Railgun\""); Assert.Empty(w.Project!.SupportChanges);
    }

    [Fact] public async Task The_new_roles_write_their_rows_with_both_opt_ins()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetSupportAsync(Field(w, Epoch, "projectile.velocity", "full_charge").InstanceKey, "60");
        await w.SetSupportAsync(Field(w, Railgun, "explosion.damage.standard_damage", ChargeFields.OverchargeRole).InstanceKey, "1");
        Assert.Null(w.BuildError);
        var full = Operation(w.LuaPreview, "hd2.fields.projectile.velocity");
        Assert.Contains("target=hd2.support_weapon('PLAS-45 Epoch'):attack('full_charge'):projectile(),", full);
        Assert.Contains("allow_shared=true,", full); Assert.Contains("allow_unverified_effect=true,", full);
        Assert.Contains("field=hd2.fields.projectile.velocity,", full); Assert.Contains("expect=250,", full); Assert.Contains("value=60,", full);
        var rail = Operation(w.LuaPreview, "attack('overcharge_explosion')");
        Assert.Contains("target=hd2.support_weapon('RS-422 Railgun'):attack('overcharge_explosion'):explosion(),", rail);
        Assert.Contains("allow_shared=true,", rail); Assert.Contains("allow_unverified_effect=true,", rail);
        Assert.Contains("field=hd2.fields.explosion.damage_standard_damage,", rail);
        Assert.Contains(RuntimeRequirement.Floors(w.Project!), f => f.Reason == "support attack role full_charge");
        Assert.Equal(Runtime030Tests.Version, await MinVersion(w));
    }

    // The Epoch's full-charge explosion and its overcharge explosion name one damage row: the second edit asks, and keeping both in sync
    // keeps one write (two would overlap in one plan, or conflict as two operations).
    [Fact] public async Task The_shared_Epoch_damage_row_is_resolved_where_the_second_edit_is_made()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var overcharge = Field(w, Epoch, "explosion.damage.standard_damage", ChargeFields.OverchargeRole);
        var impact = Field(w, Epoch, "explosion.damage.standard_damage", "full_charge_impact");
        Assert.Equal([impact.InstanceKey], ChargeFields.SameBytes(w.Metadata!.SupportAuthoring!, overcharge).Select(f => f.InstanceKey));
        Assert.Empty(ChargeFields.SameBytes(w.Metadata!.SupportAuthoring!, Field(w, Epoch, "explosion.inner_radius", ChargeFields.OverchargeRole)));
        Assert.Empty(w.SupportReplacedBy(overcharge.InstanceKey, "1"));
        await w.SetSupportAsync(overcharge.InstanceKey, "1");
        // The other row is written through this edit; editing it asks first.
        Assert.Equal(overcharge.InstanceKey, w.SupportWrittenThrough(impact)!.Value.Field.InstanceKey);
        var replaced = Assert.Single(w.SupportReplacedBy(impact.InstanceKey, "500"));
        Assert.Equal((overcharge.InstanceKey, SupportReplacement.SameBytes), (replaced.Field.InstanceKey, replaced.Reason));
        Assert.Empty(w.SupportReplacedBy(impact.InstanceKey, "800")); // vanilla is a reset, it replaces nothing
        await w.SetSupportReplacingAsync(impact.InstanceKey, "500");
        var kept = Assert.Single(w.Project!.SupportChanges);
        Assert.Equal((impact.InstanceKey, "500"), (kept.InstanceKey, kept.DesiredValue.GetRawText()));
        Assert.Null(w.BuildError); Assert.Single(System.Text.RegularExpressions.Regex.Matches(w.LuaPreview, "damage_standard_damage"));
        // Two saved edits of the row (an older file, a scripted edit) are reported on both rows and refused at build, never written twice.
        w.Project.SupportChanges.Add(new SupportChangeService().Create(w.Metadata!, overcharge.InstanceKey, "500"));
        Assert.Contains(Epoch, w.SupportIssue(w.Project.SupportChanges[0])); Assert.NotNull(w.SupportIssue(w.Project.SupportChanges[1]));
        Assert.Contains("one write", Assert.Throws<InvalidDataException>(() => new SupportLua(new SupportChangeService()).Operations(w.Project, w.Metadata!)).Message);
        await w.SetSupportReplacingAsync(overcharge.InstanceKey, "500");
        Assert.Equal(overcharge.InstanceKey, Assert.Single(w.Project.SupportChanges).InstanceKey); Assert.Null(w.BuildError);
    }

    [Fact] public async Task A_swapped_overcharge_explosion_and_edits_of_the_weapons_own_explosion_are_resolved_in_either_order()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var reference = Field(w, Railgun, ChargeFields.OverchargeExplosion);
        var damage = Field(w, Railgun, "explosion.damage.standard_damage", ChargeFields.OverchargeRole);
        var radius = Field(w, Railgun, "explosion.inner_radius", ChargeFields.OverchargeRole);
        await w.SetSupportAsync(damage.InstanceKey, "1"); await w.SetSupportAsync(radius.InstanceKey, "1");
        // Switching to the Epoch's explosion would make Runtime refuse both edits: the reference asks to discard them.
        var replaced = w.SupportReplacedBy(reference.InstanceKey, "\"PLAS-45 Epoch\"");
        Assert.Equal(2, replaced.Count); Assert.All(replaced, r => Assert.Equal(SupportReplacement.OverchargeRowEdit, r.Reason));
        Assert.Empty(w.SupportReplacedBy(reference.InstanceKey, "\"RS-422 Railgun\""));
        await w.SetSupportReplacingAsync(reference.InstanceKey, "\"PLAS-45 Epoch\"");
        Assert.Equal(reference.InstanceKey, Assert.Single(w.Project!.SupportChanges).InstanceKey); Assert.Null(w.BuildError);
        // An edit of the row while the reference is swapped asks to reset the reference. The Epoch's own rows are not affected.
        Assert.Empty(w.SupportReplacedBy(Field(w, Epoch, "explosion.inner_radius", ChargeFields.OverchargeRole).InstanceKey, "6"));
        var back = Assert.Single(w.SupportReplacedBy(damage.InstanceKey, "5"));
        Assert.Equal((reference.InstanceKey, SupportReplacement.OverchargeReference), (back.Field.InstanceKey, back.Reason));
        await w.SetSupportReplacingAsync(damage.InstanceKey, "5");
        Assert.Equal(damage.InstanceKey, Assert.Single(w.Project.SupportChanges).InstanceKey);
        // Both saved (outside the editor) is an issue on both rows and a build error.
        w.Project.SupportChanges.Add(new SupportChangeService().Create(w.Metadata!, reference.InstanceKey, "\"PLAS-45 Epoch\""));
        Assert.All(w.Project.SupportChanges, c => Assert.NotNull(w.SupportIssue(c)));
        Assert.Throws<InvalidDataException>(() => new SupportLua(new SupportChangeService()).Operations(w.Project, w.Metadata!));
    }

    // Phase 3.3: charge.minimum_seconds / maximum_seconds are deprecated aliases of the speed multipliers on projectile weapons (same bytes),
    // dormant on arc and beam weapons. A saved edit is offered a move to the field that names what it writes, or to a charge time.
    [Fact] public async Task Deprecated_charge_ids_are_flagged_and_their_edits_move_to_the_field_they_write()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var legacy = Field(w, Railgun, ChargeFields.LegacyMinimum); var canonical = Field(w, Railgun, "charge.speed_multiplier_min");
        Assert.True(ChargeFields.Deprecated(legacy)); Assert.False(ChargeFields.Dormant(legacy)); Assert.Equal(canonical.SemanticFieldId, legacy.Charge!.AliasOf);
        Assert.True(ChargeFields.Dormant(Field(w, Arc, ChargeFields.LegacyMinimum))); Assert.True(ChargeFields.Dormant(Field(w, Meltagun, ChargeFields.LegacyMaximum)));
        Assert.Equal([canonical.InstanceKey], ChargeFields.SameBytes(w.Metadata!.SupportAuthoring!, legacy).Select(f => f.InstanceKey));
        // The deprecated id keeps its old contract (no acknowledgement); the canonical one asks before replacing it.
        await w.SetSupportAsync(legacy.InstanceKey, "0.2"); Assert.Null(w.BuildError);
        Assert.DoesNotContain("allow_", Operation(w.LuaPreview, "hd2.fields.charge.minimum_seconds"));
        Assert.Equal(SupportReplacement.SameBytes, Assert.Single(w.SupportReplacedBy(canonical.InstanceKey, "0.3")).Reason);
        await w.MoveSupportChangeAsync(legacy.InstanceKey, canonical.InstanceKey);
        var moved = Assert.Single(w.Project!.SupportChanges);
        Assert.Equal((canonical.InstanceKey, "0.2"), (moved.InstanceKey, moved.DesiredValue.GetRawText()));
        Assert.Contains("allow_unverified_effect=true,", Operation(w.LuaPreview, "hd2.fields.charge.speed_multiplier_min"));
        // On the ARC-3 the old id does nothing: the value can become the charge time it was meant as.
        await w.SetSupportAsync(Field(w, Arc, ChargeFields.LegacyMinimum).InstanceKey, "0.5");
        await w.MoveSupportChangeAsync(Field(w, Arc, ChargeFields.LegacyMinimum).InstanceKey, Field(w, Arc, ChargeFields.MinimumTime).InstanceKey);
        Assert.Equal("0.5", w.Project.SupportChanges.Single(c => c.SemanticFieldId == ChargeFields.MinimumTime).DesiredValue.GetRawText());
        Assert.DoesNotContain(w.Project.SupportChanges, c => c.SemanticFieldId == ChargeFields.LegacyMinimum);
    }

    // Charge times out of order are accepted (Runtime applies them with a one-time CHARGE ORDER notice); the editor notes it inline.
    [Fact] public async Task Charge_times_out_of_order_are_noted_never_blocked()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        Assert.True(w.SupportChargeTimes(Railgun)!.Increasing);
        await w.SetSupportAsync(Field(w, Railgun, ChargeFields.FullTime).InstanceKey, "0.3");
        var times = w.SupportChargeTimes(Railgun)!;
        Assert.True(times.FullNotAboveMinimum); Assert.False(times.OverchargeNotAboveFull);
        Assert.Equal((0.45f, 0.3f), ((float)times.Minimum, (float)times.Full));
        Assert.Null(w.BuildError); Assert.Null(w.SupportIssue(w.Project!.SupportChanges.Single()));
        await w.SetSupportAsync(Field(w, Railgun, ChargeFields.OverchargeTime).InstanceKey, "0.2");
        Assert.True(w.SupportChargeTimes(Railgun)!.OverchargeNotAboveFull); Assert.Null(w.BuildError);
        // A disabled edit no longer counts.
        await w.ToggleSupportAsync(Field(w, Railgun, ChargeFields.OverchargeTime).InstanceKey);
        Assert.False(w.SupportChargeTimes(Railgun)!.OverchargeNotAboveFull);
    }

    [Fact] public async Task Charge_edits_survive_save_and_reopen()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetSupportAsync(Field(w, Railgun, ChargeFields.OverchargeExplosion).InstanceKey, "\"PLAS-45 Epoch\"");
        await w.SetSupportAsync(Field(w, Epoch, "explosion.damage.push_force", "full_charge_impact").InstanceKey, "0");
        await w.SetSupportAsync(Field(w, Epoch, ChargeFields.OverchargeLimit).InstanceKey, "5");
        var id = w.Project!.Id; var lua = w.LuaPreview; Assert.Null(w.BuildError);
        var reopened = e.Workspace(); await reopened.InitializeAsync(); await reopened.OpenAsync(id);
        Assert.Equal(3, reopened.Project!.SupportChanges.Count); Assert.Null(reopened.BuildError); Assert.Equal(lua, reopened.LuaPreview);
        // A saved overcharge explosion reference (a weapon name) needs project format 14, which older ModBuilders refuse instead of misreading.
        Assert.Equal(14, reopened.Project.FormatVersion);
        Assert.Equal(Runtime030Tests.Version, await MinVersion(reopened));
    }

    // An SDK 0.28.1 project with charge time edits (published then as "Charge level" multipliers) keeps them, unchanged, on rebind; the editor
    // shows them as the charge times they always were and notes where the label changed. Its Epoch edit carries 0.30's opt-in (which the
    // editor shows as the field's unproven-effect warning).
    [Fact] public async Task An_SDK_0_28_1_charge_project_rebinds_with_its_edits_and_their_corrected_meaning()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, "0.28.1");
        var level = Field(w, Railgun, ChargeFields.MinimumTime); var legacy = Field(w, Railgun, ChargeFields.LegacyMinimum);
        Assert.Equal(("Charge level 1", "multiplier"), (level.Display.Name, level.Value.Unit)); Assert.Null(level.Charge);
        Assert.True(ChargeFields.Deprecated(legacy) && ChargeFields.Is(legacy));
        await w.SetSupportAsync(level.InstanceKey, "0.3"); await w.SetSupportAsync(legacy.InstanceKey, "0.9");
        await w.SetSupportAsync(Field(w, Epoch, "projectile.velocity", "primary").InstanceKey, "300");
        var lua0281 = w.LuaPreview; Assert.Null(w.BuildError); Assert.Equal("0.28.1", RuntimeRequirement.Minimum(w.Project!, w.Metadata!));
        Assert.DoesNotContain("allow_unverified_effect", lua0281);
        await SdkFixtures.Install(e, Runtime030Tests.Version);
        await w.RebindToInstalledSdkAsync();
        Assert.Equal(Runtime030Tests.Version, w.Project!.SdkVersion); Assert.Null(w.BuildError);
        Assert.Equal(3, w.Project.SupportChanges.Count);
        var time = w.Project.SupportChanges.Single(c => c.SemanticFieldId == ChargeFields.MinimumTime);
        Assert.Equal("0.3", time.DesiredValue.GetRawText()); Assert.True(ChargeFields.SavedBefore030(time));
        Assert.Equal(("seconds", 0d), (Field(w, Railgun, ChargeFields.MinimumTime).Value.Unit, Field(w, Railgun, ChargeFields.MinimumTime).Charge!.Min!.Value));
        Assert.Contains("{field=hd2.fields.charge.level_1,expect=0.45,value=0.3},", w.LuaPreview);
        // The legacy id is now a deprecated alias: still written as before, and offered a move to the speed multiplier it writes.
        Assert.Equal("charge.speed_multiplier_min", Field(w, Railgun, ChargeFields.LegacyMinimum).Charge!.AliasOf);
        var epoch = Field(w, Epoch, "projectile.velocity", "primary");
        Assert.True(CompositionOptIns.EffectFor(epoch, JsonSerializer.SerializeToElement(300)));
        Assert.Equal("partial", epoch.ChargeLevel!.Level);
        Assert.Contains("allow_unverified_effect=true", Operation(w.LuaPreview, "'PLAS-45 Epoch'"));
    }
}
