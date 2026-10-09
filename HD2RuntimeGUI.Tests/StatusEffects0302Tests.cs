using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.2 status effects (sdk/docs/status-effects.md, schemas/status_attachment_policy.json), authored since 1.7.1: status slots on
// player-weapon explosions (13 weapons, slot 1 of primary.impact, PurifierIncendiaryCharge) and the 23 extended statuses (attach tiers enemy_slot
// and other_system). Every write of an extended status carries allow_unverified_effect, live-proven direct-hit rows included, and the export
// requires 0.30.2; the opt-ins are implicit (warnings only). Project format 14 is kept: ModBuilder 1.7.0 cannot read the 0.30.2 SDK these
// edits need (its player weapon summary check refuses 0.30.2's 3365/3361 writable counts), so it refuses such a project instead of misreading it.
public sealed class StatusEffects0302Tests
{
    private const string Purifier = "PLAS-101 Purifier", Scorcher = "PLAS-1 Scorcher", Liberator = "AR-23 Liberator", Maxigun = "M-1000 Maxigun", K9Gun = "AX/ARC-3 K-9 / gun";
    private const string Slot = "explosion.primary.impact.damage.status_1_type", Strength = "explosion.primary.impact.damage.status_1_strength";
    private const string Blast = "hd2.weapon('PLAS-101 Purifier'):attack('primary'):projectile():terminal_action('impact'):explosion()";

    // The generated request that writes this field (one add(function() return ... end) block).
    private static string Operation(string lua, string field) => Regex.Split(lua, @"(?=add\(function\(\) return )").Single(b => b.Contains(field, StringComparison.Ordinal));
    private static SupportField Support(SdkMetadata sdk, string weapon, string field) =>
        sdk.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == field && f.Target.AttackRole == "primary");
    private static EntityField Mounted(SdkMetadata sdk, string weapon, string field) => sdk.Entities!.VehicleWeapons!.FieldInstances.Single(f => f.Target.Weapon == weapon && f.SemanticFieldId == field);
    // The floors a project needs if it were bound to the released 0.30.0 SDK (a 0.30.2 binding already declares 0.30.2).
    private static string MinimumOn030(ModProject p, SdkMetadata sdk) => RuntimeRequirement.Minimum(p, sdk with { Version = RuntimeRequirement.Release030 });

    [Fact] public async Task The_Purifier_explosion_status_is_written_like_the_Runtime_example()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await ExportFixtureTests.Fresh(e, "Purifier Incendiary Charge");
        var slots = sdk.PlayerWeapons!.Weapons.SelectMany(x => x.Fields.Select(f => (Weapon: x.Name, Field: f))).Where(x => AuthoredTypes.IsPlayerExplosionStatus(x.Field.SemanticFieldId)).ToArray();
        Assert.Equal(26, slots.Length); Assert.Equal(13, slots.Select(x => x.Weapon).Distinct().Count()); Assert.Equal(8, slots.Count(x => x.Field.AffectsMultipleWeapons));
        Assert.All(slots, x => Assert.True(x.Field.Editable && x.Field.Acknowledgement == CompositionOptIns.Effect));
        // The explosion row lists both fields next to its other explosion fields (Player Weapons › impact actions).
        Assert.Contains(w.ObjectFields(Purifier, "primary", "explosion", "impact"), f => f.SemanticFieldId == Slot);
        Assert.Contains(w.ObjectFields(Purifier, "primary", "explosion", "impact"), f => f.SemanticFieldId == Strength);

        await w.SetObjectScalarAsync(Purifier, "primary", "explosion", "impact", Slot, "\"fire\"", true);
        await w.SetObjectScalarAsync(Purifier, "primary", "explosion", "impact", Strength, "2", true);
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "hd2.fields.explosion.damage_status_1_type");
        // sdk/docs/status-effects.md and examples/projects/PurifierIncendiaryCharge, field for field.
        Assert.Contains("target=" + Blast + ",", op);
        Assert.Contains("{field=hd2.fields.explosion.damage_status_1_type,expect='none',value='fire'}", op);
        Assert.Contains("{field=hd2.fields.explosion.damage_status_1_strength,expect=0,value=2}", op);
        Assert.Contains("allow_unverified_effect=true", op); Assert.DoesNotContain("allow_shared", op);
        Assert.Contains((RuntimeRequirement.Release0302, "player weapon explosion status " + Slot), RuntimeRequirement.Floors(w.Project!, sdk));
        Assert.Equal(RuntimeRequirement.Release0302, MinimumOn030(w.Project!, sdk));
        Assert.True(w.Project!.FormatVersion <= 14); Assert.True(ProjectIdentity.RequiredFormat(w.Project, sdk) <= 14);

        // The strength takes an in-game option like any number; a status key does not.
        var strength = w.Project.CompositionChanges.Single(c => c.Scalar!.SemanticFieldId == Strength);
        Assert.Null(ModOptionsService.ObjectTarget(sdk, strength)!.Blocker);
        Assert.NotNull(ModOptionsService.ObjectTarget(sdk, w.Project.CompositionChanges.Single(c => c.Scalar!.SemanticFieldId == Slot))!.Blocker);

        // A shared explosion row (the PLAS-1 Scorcher's) adds allow_shared.
        await w.SetObjectScalarAsync(Scorcher, "primary", "explosion", "impact", Slot, "\"stun_small\"", true);
        await w.SetObjectScalarAsync(Scorcher, "primary", "explosion", "impact", Strength, "1", true);
        Assert.Null(w.BuildError);
        var shared = Operation(w.LuaPreview, "'stun_small'");
        Assert.Contains("hd2.weapon('PLAS-1 Scorcher')", shared); Assert.Contains("allow_shared=true", shared); Assert.Contains("allow_unverified_effect=true", shared);

        // Saved and reopened: the same Lua.
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task An_extended_status_adds_allow_unverified_effect_even_on_a_live_proven_row_and_requires_0_30_2()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await ExportFixtureTests.Fresh(e, "Extended Statuses");
        var field = sdk.PlayerWeapons!.Field(Liberator, "damage.status_1_type");
        Assert.NotNull(field.LiveEvidence); Assert.Null(field.Acknowledgement);
        // The picker offers every tier, grouped in the catalog's order, with each tier's published text.
        var groups = w.StatusGroups(w.StatusChoices(field).Allowed);
        Assert.Equal(["player_attack", "enemy_slot", "other_system"], groups.Select(g => g.Tier));
        Assert.Equal([11, 8, 15], groups.Select(g => g.Keys.Count));
        Assert.StartsWith("EXPERIMENTAL", w.StatusTierText("other_system"));
        var bleed = w.ExtendedStatus("bleed")!;
        Assert.Equal(("other_system", 9999d), (bleed.Tier, bleed.Duration)); Assert.Equal(w.StatusTierText("other_system"), bleed.TierText);
        Assert.Null(w.ExtendedStatus("fire")); Assert.Null(w.ExtendedStatus(StatusReference.None));

        // A player_attack status on the live-proven direct-hit row: no effect opt-in, no 0.30.2 floor.
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_type", "\"fire\"", true);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_strength", "2", true);
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "hd2.fields.damage.status_1_type");
        Assert.DoesNotContain("allow_unverified_effect", op);
        Assert.DoesNotContain(RuntimeRequirement.Floors(w.Project!, sdk), f => f.Reason.StartsWith("extended status", StringComparison.Ordinal));
        Assert.Equal(RuntimeRequirement.Release030, MinimumOn030(w.Project!, sdk));

        // The same row with an extended status: allow_unverified_effect, and the export requires 0.30.2.
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_type", "\"bleed\"", true);
        Assert.Null(w.BuildError);
        op = Operation(w.LuaPreview, "hd2.fields.damage.status_1_type");
        Assert.Contains("{field=hd2.fields.damage.status_1_type,expect='none',value='bleed'}", op);
        Assert.Contains("allow_unverified_effect=true", op); Assert.Contains("allow_shared=true", op);
        Assert.Contains((RuntimeRequirement.Release0302, "extended status bleed"), RuntimeRequirement.Floors(w.Project!, sdk));
        Assert.Equal(RuntimeRequirement.Release0302, MinimumOn030(w.Project!, sdk));
        // Bound to the pinned 0.30.3, the export declares the pin (the extended status's own floor is 0.30.2, above).
        Assert.Equal(SdkPin.Version, RuntimeRequirement.Minimum(w.Project!, sdk));
        // A status no tier attaches stays refused where the edit is made.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_type", "\"smoke_covered\"", true));
        Assert.True(w.Project!.FormatVersion <= 14);
    }

    [Fact] public async Task Support_and_mounted_pickers_offer_the_extended_statuses_with_the_opt_in()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await ExportFixtureTests.Fresh(e, "Extended Support");
        var extended = sdk.StatusEffects!.Statuses.Values.Where(s => s.Extended).Select(s => s.SemanticId).ToArray();
        Assert.Equal(23, extended.Length); Assert.Equal(RuntimeRequirement.ExtendedStatuses0302.Order(), extended.Order());
        // Support weapon (MaxigunStun: a live-proven direct-hit row, so a player_attack status needs no effect opt-in).
        var type = Support(sdk, Maxigun, "damage.status_1_type"); var strength = Support(sdk, Maxigun, "damage.status_1_strength");
        Assert.All(extended, k => Assert.Contains(k, w.StatusChoices(type).Allowed));
        await w.SetSupportAsync(type.InstanceKey, "\"confusion\""); await w.SetSupportAsync(strength.InstanceKey, "2");
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "value='confusion'");
        Assert.Contains("target=hd2.support_weapon('M-1000 Maxigun'):attack('primary'):projectile()", op); Assert.Contains("allow_unverified_effect=true", op);
        Assert.Contains((RuntimeRequirement.Release0302, "extended status confusion"), RuntimeRequirement.Floors(w.Project!, sdk));
        Assert.True(CompositionOptIns.EffectFor(sdk, type, JsonSerializer.SerializeToElement("confusion")));
        Assert.False(CompositionOptIns.EffectFor(sdk, type, JsonSerializer.SerializeToElement("stun_medium")));
        // Mounted weapon (the K-9's first empty slot).
        var slot = Mounted(sdk, K9Gun, "damage.primary.status_2_type"); var power = Mounted(sdk, K9Gun, "damage.primary.status_2_strength");
        Assert.All(extended, k => Assert.Contains(k, MountedStatusSlots.For(sdk, slot).Statuses));
        await w.SetEntityAsync(slot.InstanceKey, "acid_splash"); await w.SetEntityAsync(power.InstanceKey, "2");
        Assert.Null(w.BuildError);
        Assert.Contains("allow_unverified_effect=true", Operation(w.LuaPreview, "value='acid_splash'"));
        Assert.Contains((RuntimeRequirement.Release0302, "extended status acid_splash"), RuntimeRequirement.Floors(w.Project!, sdk));
        Assert.Equal(RuntimeRequirement.Release0302, MinimumOn030(w.Project!, sdk));

        // Saved and reopened: the same Lua; the project stays at format 14 or below.
        var lua = w.LuaPreview; await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        Assert.True(w.Project!.FormatVersion <= 14); ProjectIdentity.Validate(w.Project);
    }

    // P2: the explosion status, an extended status on a direct hit and on a support weapon, exported for tools/validate-exports.py.
    [Fact] public async Task P2_explosion_status_export()
    {
        using var e = new TestEnvironment(); var (w, _) = await ExportFixtureTests.Fresh(e, "P2 Explosion Status");
        await w.SetObjectScalarAsync(Purifier, "primary", "explosion", "impact", Slot, "\"fire\"", true);
        await w.SetObjectScalarAsync(Purifier, "primary", "explosion", "impact", Strength, "2", true);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_type", "\"stun_massive\"", true);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_strength", "1", true);
        var sdk = w.Metadata!;
        await w.SetSupportAsync(Support(sdk, Maxigun, "damage.status_1_type").InstanceKey, "\"bleed\"");
        await w.SetSupportAsync(Support(sdk, Maxigun, "damage.status_1_strength").InstanceKey, "1");
        var (manifest, lua) = await ExportFixtureTests.Export(w, "P2-explosion-status");
        // Bound to the pinned 0.30.3: the export declares the pin (these statuses' own floor is 0.30.2).
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Contains("target=" + Blast + ",", lua);
        Assert.Equal(3, Regex.Matches(lua, "allow_unverified_effect=true").Count);
    }
}
