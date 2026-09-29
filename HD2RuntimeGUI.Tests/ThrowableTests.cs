using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.27.0 throwable authoring (ThrowableAuthoringCapabilities.json, hd2.throwable): every published throwable and field, bound
// through the shared entity pipeline (changes, reset, Lua, export, Mod Options).
public sealed class ThrowableTests
{
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string version = "0.27.0")
    {
        var sdk = await SdkFixtures.Install(e, version); var w = e.Workspace();
        await w.CreateAsync(new("Throwables", "Tests", "mods/tests/throwables", "0.1.0"), sdk); return w;
    }
    private static ThrowableCatalog Catalog(BuilderWorkspace w) => w.Metadata!.Entities!.Throwables!;
    private static EntityField Field(BuilderWorkspace w, string throwable, string path, string field, string? effect = null) =>
        Catalog(w).FieldInstances.Single(f => f.Target.Throwable == throwable && f.Target.Path == path && f.Target.Effect == effect && f.SemanticFieldId == field);
    private static JsonNode Json() => JsonNode.Parse(SdkFixtures.Entry("0.27.0", ThrowableAuthoringReader.FileName))!;
    private static ThrowableCatalog Read(JsonNode j) => ThrowableAuthoringReader.Read(Encoding.UTF8.GetBytes(j.ToJsonString()));

    [Fact] public async Task All_23_throwables_and_their_published_fields_are_bound()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var c = Catalog(w);
        Assert.Equal(23, c.Throwables.Length); Assert.All(c.Throwables, t => Assert.Equal("RESOLVED", t.Identity.Status));
        Assert.Equal(411, c.FieldInstances.Length); Assert.Equal(388, c.FieldInstances.Count(f => f.Editable)); Assert.Equal(316, c.FieldInstances.Count(f => f.Shared));
        // Categories and families exactly as published.
        Assert.Equal(["Special", "Standard"], c.Throwables.Select(t => t.Category).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains("ThrowingKnife", c.Throwables.Select(t => t.Family)); Assert.Contains("Shield", c.Throwables.Select(t => t.Family));
        // Every editable field carries Runtime's unverified-effect opt-in and a published range; shared rows also need allow_shared.
        Assert.All(c.FieldInstances.Where(f => f.Editable), f => { Assert.Equal("allow_unverified_effect", f.Acknowledgement); Assert.NotNull(f.Range); });
        Assert.All(c.FieldInstances, f => Assert.Equal(f.Shared, f.AllowSharedRequired));
        // Read-only fields publish their reason.
        Assert.All(c.FieldInstances.Where(f => !f.Editable), f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
        // The sidebar gains Throwables right after Player Weapons; older SDKs do not show it.
        Assert.Equal("throwables", Navigation.Build(w.Metadata, null)[2].Page);
        Assert.DoesNotContain(Navigation.Build(await SdkFixtures.Install(e, "0.26.0"), null), i => i.Page == "throwables");
    }

    [Fact] public async Task Inventory_counts_are_local_and_write_through_the_throwable_itself()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var f = Field(w, "G-12 High Explosive", "throwable", "throwable.starting_count");
        Assert.False(f.Shared); Assert.Equal(4, f.CurrentDefault.GetInt32());
        await w.SetEntityAsync(f.InstanceKey, "6");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(f.InstanceKey, "100")); // published range 0-99
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("target=hd2.throwable('G-12 High Explosive'),", lua); Assert.Contains("field=hd2.fields.throwable.starting_count,", lua);
        Assert.Contains("value=6,", lua); Assert.Contains("allow_unverified_effect=true", lua); Assert.DoesNotContain("allow_shared", lua);
    }

    [Fact] public async Task Frag_explosion_and_shrapnel_are_shared_rows_with_their_own_targets()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var radius = Field(w, "G-6 Frag", "explosion", "explosion.outer_radius"); var count = Field(w, "G-6 Frag", "explosion", "explosion.shrapnel_count");
        var shrapnel = Field(w, "G-6 Frag", "shrapnel", "damage.standard_damage");
        Assert.True(radius.Shared && shrapnel.Shared);
        await w.SetEntityAsync(radius.InstanceKey, "12"); await w.SetEntityAsync(count.InstanceKey, "40"); await w.SetEntityAsync(shrapnel.InstanceKey, "150");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.throwable('G-6 Frag'):explosion()", lua); Assert.Contains("hd2.throwable('G-6 Frag'):shrapnel()", lua);
        Assert.Contains("hd2.fields.explosion.outer_radius", lua); Assert.Contains("hd2.fields.explosion.shrapnel_count", lua);
        Assert.Contains("hd2.fields.damage.player_standard_damage", lua); // the published typed constant, not one derived from the ID
        Assert.Contains("allow_shared=true", lua); Assert.Contains("allow_unverified_effect=true", lua);
        // The frag shrapnel row is also the Lure Mine's (identical published consumers): one value, edited through one throwable only.
        var mine = Field(w, "TM-1 Lure Mine", "shrapnel", "damage.standard_damage");
        Assert.Equal(shrapnel.BackingObjectId, mine.BackingObjectId);
        await w.SetEntityAsync(mine.InstanceKey, "175");
        Assert.Contains("one shared value", w.BuildError);
    }

    [Fact] public async Task Incendiary_status_effect_writes_strength_and_the_shared_fire_definition()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var strength = Field(w, "G-10 Incendiary", "status_effect", "status.strength", "fire");
        var duration = Field(w, "G-10 Incendiary", "status_effect", "status.duration", "fire");
        var raw = Catalog(w).Raw(duration.InstanceKey)!.Value;
        Assert.Contains("G-8 Immolation", ThrowableCatalog.Consumers(raw.Target, raw.Field)!.Throwables); // the fire definition is reached from many sources
        await w.SetEntityAsync(strength.InstanceKey, "60"); await w.SetEntityAsync(duration.InstanceKey, "4");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.throwable('G-10 Incendiary'):explosion():status_effect('fire')", lua);
        Assert.Contains("hd2.fields.status.strength", lua); Assert.Contains("hd2.fields.status.duration", lua); Assert.Contains("allow_shared=true", lua);
        // The same fire definition reached through another incendiary is the same value.
        Assert.Equal(duration.BackingObjectId, Field(w, "G-13 Incendiary Impact", "status_effect", "status.duration", "fire").BackingObjectId);
        Assert.NotEqual(strength.BackingObjectId, duration.BackingObjectId);
    }

    [Fact] public async Task Gas_and_stun_status_chains_are_keyed_by_their_published_effects()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var gas = Catalog(w).Find("G-4 Gas")!;
        Assert.Equal(["gas", "gas-confusion"], gas.Targets.Where(t => t.Path == "status_effect").Select(t => t.Key!).Order(StringComparer.Ordinal));
        Assert.All(gas.Targets.Where(t => t.Path == "status_effect"), t => Assert.NotEqual("exact", t.LabelConfidence)); // labels rest on slot order
        var confusion = Field(w, "G-4 Gas", "status_effect", "status.strength", "gas-confusion");
        var stun = Field(w, "G-23 Stun", "status_effect", "status.duration", "stun-large");
        await w.SetEntityAsync(confusion.InstanceKey, Bump(confusion)); await w.SetEntityAsync(stun.InstanceKey, Bump(stun));
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.throwable('G-4 Gas'):explosion():status_effect('gas-confusion')", lua);
        Assert.Contains("hd2.throwable('G-23 Stun'):explosion():status_effect('stun-large')", lua);
    }

    [Fact] public async Task Throwing_knife_exposes_its_direct_hit_damage()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var knife = Catalog(w).Find("K-2 Throwing Knife")!;
        Assert.Equal(["damage", "throwable"], knife.Targets.Select(t => t.Path).Order(StringComparer.Ordinal)); // no explosion
        var damage = Field(w, "K-2 Throwing Knife", "damage", "damage.standard_damage");
        Assert.Equal(300, damage.CurrentDefault.GetInt32());
        await w.SetEntityAsync(damage.InstanceKey, "450"); Assert.Null(w.BuildError);
        var lua = w.LuaPreview; Assert.Contains("hd2.throwable('K-2 Throwing Knife'):damage()", lua); Assert.Contains("value=450", lua);
    }

    [Fact] public async Task Shield_grenade_exposes_its_deployed_shield_and_keeps_its_expiry_explosion_read_only()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var radius = Field(w, "G/SH-39 Shield", "shield", "shield.radius"); var health = Field(w, "G/SH-39 Shield", "shield", "shield.durability");
        Assert.False(radius.Shared);
        await w.SetEntityAsync(radius.InstanceKey, "2.5"); await w.SetEntityAsync(health.InstanceKey, "1500");
        var lua = w.LuaPreview; Assert.Null(w.BuildError);
        Assert.Contains("hd2.throwable('G/SH-39 Shield'):shield()", lua); Assert.Contains("hd2.fields.shield.entity_radius", lua); Assert.Contains("hd2.fields.shield.entity_durability", lua);
        Assert.DoesNotContain("allow_shared", lua);
        var expiry = Field(w, "G/SH-39 Shield", "explosion", "explosion.outer_radius");
        Assert.False(expiry.Editable); await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(expiry.InstanceKey, "3"));
        // Mines expose their deployed entity's health.
        var mine = Field(w, "TM-1 Lure Mine", "entity", "entity.health"); await w.SetEntityAsync(mine.InstanceKey, "400");
        Assert.Contains("hd2.throwable('TM-1 Lure Mine'):entity()", w.LuaPreview);
    }

    [Fact] public async Task Pineapple_bomblets_and_their_explosion_are_separate_submunition_targets()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var pineapple = Catalog(w).Find("G-7 Pineapple")!;
        var bomblets = pineapple.Targets.Single(t => t.Path == "bomblets");
        Assert.True(bomblets.SharedDamageWithParentExplosion); Assert.All(bomblets.Fields, f => Assert.StartsWith("projectile.", f.SemanticFieldId));
        var velocity = Field(w, "G-7 Pineapple", "bomblets", "projectile.velocity"); var blast = Field(w, "G-7 Pineapple", "bomblet_explosion", "explosion.inner_radius");
        await w.SetEntityAsync(velocity.InstanceKey, "30"); await w.SetEntityAsync(blast.InstanceKey, "3");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.throwable('G-7 Pineapple'):bomblets()", lua); Assert.Contains("hd2.throwable('G-7 Pineapple'):bomblets():explosion()", lua);
    }

    [Fact] public async Task Read_only_fuses_are_refused_and_changes_reset_per_field_and_throwable()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var dynamite = Field(w, "TED-63 Dynamite", "detonation", "throwable.explosion_delay"); Assert.False(dynamite.Editable);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(dynamite.InstanceKey, "8"));
        var fuse = Field(w, "G-12 High Explosive", "detonation", "throwable.explosion_delay");
        await w.SetEntityAsync(fuse.InstanceKey, "2"); await w.SetEntityAsync(Field(w, "G-12 High Explosive", "throwable", "throwable.max_count").InstanceKey, "6");
        Assert.Contains("hd2.throwable('G-12 High Explosive'):detonation()", w.LuaPreview);
        await w.ResetEntityAsync(instance: fuse.InstanceKey); Assert.Single(w.Project!.EntityChanges);
        await w.ResetEntityAsync(ThrowableAuthoringReader.Resource, "G-12 High Explosive"); Assert.Empty(w.Project.EntityChanges);
        Assert.DoesNotContain("hd2.throwable", w.LuaPreview);
    }

    [Fact] public async Task Throwable_edits_save_as_format_9_and_reload_unchanged()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var strength = Field(w, "G-10 Incendiary", "status_effect", "status.strength", "fire");
        await w.SetEntityAsync(strength.InstanceKey, "70"); var lua = w.LuaPreview;
        Assert.Equal(9, w.Project!.FormatVersion); ProjectIdentity.Validate(w.Project);
        var saved = w.Project.EntityChanges.Single(); Assert.Equal("fire", saved.Effect); Assert.Equal("G-10 Incendiary", saved.Entity);
        await w.OpenAsync(w.Project.Id);
        Assert.Equal("fire", w.Project!.EntityChanges.Single().Effect); Assert.Equal(lua, w.LuaPreview); Assert.Null(w.BuildError);
        // Throwable edits participate in Mod Options like other numeric fields.
        Assert.True(ModOptionsService.Targets(w.Project, w.Metadata!).Single(t => t.Key == ModOptionsService.EntityKey(strength.InstanceKey)).Eligible);
        // A project file with a malformed throwable target is rejected.
        var original = w.Project.EntityChanges;
        w.Project.EntityChanges = [original[0] with { Effect = "Fire Loud" }]; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(w.Project));
        w.Project.EntityChanges = [original[0] with { Effect = null }]; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(w.Project));
        w.Project.EntityChanges = original;
    }

    [Fact] public async Task A_0260_project_upgrades_to_0270_and_gains_throwables_without_changing_existing_edits()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.26.0"); var w = e.Workspace();
        await w.CreateAsync(new("Upgrade", "Tests", "mods/tests/upgrade027", "0.1.0"), old);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "700", false);
        Assert.Null(old.Entities!.Throwables); var lua = w.LuaPreview; var format = w.Project!.FormatVersion;
        await SdkFixtures.Install(e, "0.27.0"); await w.OpenAsync(w.Project.Id);
        Assert.Equal(format, w.Project!.FormatVersion); Assert.Equal(lua, w.LuaPreview); Assert.Null(w.Metadata!.Entities!.Throwables); // still pinned to 0.26.0
        await w.RebindToInstalledSdkAsync(); Assert.Equal(format, w.Project.FormatVersion); Assert.Null(w.BuildError);
        var maxCount = Field(w, "G-6 Frag", "throwable", "throwable.max_count"); await w.SetEntityAsync(maxCount.InstanceKey, (maxCount.CurrentDefault.GetInt32() + 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(9, w.Project.FormatVersion); var both = w.LuaPreview;
        Assert.Contains("hd2.throwable('G-6 Frag')", both); Assert.Contains("hd2.fields.weapon.fire_rate", both);
    }

    [Theory]
    [InlineData("accessor")] [InlineData("no-unverified-opt-in")] [InlineData("shared-without-consumers")] [InlineData("api-constant")]
    [InlineData("baseline-out-of-range")] [InlineData("summary")] [InlineData("read-only-without-reason")]
    public void Inconsistent_throwable_metadata_fails_closed(string fault)
    {
        var j = Json(); var frag = j["throwables"]!.AsArray().First(t => (string)t!["name"]! == "G-6 Frag")!;
        JsonNode Target(string path) => frag["targets"]!.AsArray().First(t => (string)t!["path"]! == path)!;
        JsonNode First(string path) => Target(path)["fields"]!.AsArray()[0]!;
        switch (fault)
        {
            case "accessor": Target("shrapnel")["accessor"] = new JsonArray("explosion", "shrapnel", "extra"); break;
            case "no-unverified-opt-in": First("throwable")["acknowledgements"] = new JsonArray(); break;
            case "shared-without-consumers": Target("shrapnel")["scope"]!.AsObject().Remove("projectile"); break;
            case "api-constant": First("explosion")["apiFieldConstant"] = "hd2.fields.explosion.radius"; break;
            case "baseline-out-of-range": First("throwable")["baseline"] = 500; break;
            case "summary": j["summary"]!["writableFieldInstances"] = 389; break;
            case "read-only-without-reason": First("throwable")["editable"] = false; First("throwable")["reason"] = null; break;
        }
        Assert.Throws<InvalidDataException>(() => Read(j));
    }
    [Fact] public void Unknown_throwable_contract_or_target_is_unsupported()
    {
        var j = Json(); j["contract"] = "hd2runtime.throwable.guarded_authoring.v2"; Assert.Throws<UnsupportedSdkException>(() => Read(j));
        j = Json(); j["throwables"]!.AsArray()[0]!["targets"]!.AsArray()[0]!["path"] = "trajectory"; Assert.Throws<UnsupportedSdkException>(() => Read(j));
    }

    private static string Bump(EntityField f) => f.Type == "integer"
        ? Math.Min(f.CurrentDefault.GetInt32() + 1, (int)f.Range!.Max).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : Math.Min(f.CurrentDefault.GetDouble() + 0.5, f.Range!.Max).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
