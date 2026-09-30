using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class LargeProjectData : IAsyncLifetime
{
    public TestEnvironment Environment { get; } = new();
    public LargeProjectFixture.Build Build { get; private set; } = null!;
    public async Task InitializeAsync() => Build = await LargeProjectFixture.CreateAsync(Environment, LargeProjectFixture.Mix.User610);
    public Task DisposeAsync() { Environment.Dispose(); return Task.CompletedTask; }
}

// A ~610-change project (LargeProjectFixture: player weapon and object fields, support weapons, stratagems, vehicles, enemy zones) built
// through the editors' workspace calls. Pins what large projects must keep: every change, one write per native value, no request merged
// across objects, true shared values still refused, and the generated Lua byte for byte (so a performance change cannot alter output).
public sealed class LargeProjectTests(LargeProjectData data) : IClassFixture<LargeProjectData>
{
    // SHA-256 of the fixture's generated Lua. Update only for an intended output change, never for a performance change.
    private const string LuaSha256 = "62bcebf513d81e2161a8dd4da4a963996bd1e87aec04fc307eb233e898138190";
    private static int Count(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    private static string[] Requests(string lua) => lua.Split("add(function() return ")[1..];

    [Fact] public async Task All_changes_are_retained_and_survive_save_and_reopen()
    {
        var b = data.Build; var p = b.Workspace.Project!; var mix = LargeProjectFixture.Mix.User610;
        Assert.Equal(new Dictionary<string, int> { ["weapon"] = mix.Weapon, ["object"] = mix.Object, ["support"] = mix.Support, ["stratagem"] = mix.Stratagem,
            ["vehicle"] = mix.Vehicle, ["enemy"] = mix.Enemy }, b.Applied);
        Assert.Equal(610, mix.Total);
        Assert.Equal((mix.Weapon, mix.Object, mix.Support, mix.Stratagem, mix.Vehicle + mix.Enemy),
            (p.WeaponChanges.Count, p.CompositionChanges.Count, p.SupportChanges.Count, p.StratagemChanges.Count, p.EntityChanges.Count));
        Assert.Null(b.Workspace.BuildError);
        var reopened = data.Environment.Workspace(); await reopened.OpenAsync(p.Id);
        var q = reopened.Project!;
        Assert.Equal((p.WeaponChanges.Count, p.CompositionChanges.Count, p.SupportChanges.Count, p.StratagemChanges.Count, p.EntityChanges.Count),
            (q.WeaponChanges.Count, q.CompositionChanges.Count, q.SupportChanges.Count, q.StratagemChanges.Count, q.EntityChanges.Count));
        Assert.Equal(b.Workspace.LuaPreview, reopened.LuaPreview);
    }

    [Fact] public void Every_change_is_its_own_write_except_two_edits_of_one_shared_row_with_the_same_value()
    {
        var sdk = data.Build.Sdk; var p = data.Build.Workspace.Project!; var lua = data.Build.Workspace.LuaPreview;
        // Weapon and object edits write one value per native owner and field; support, stratagem and entity edits one value each.
        var identities = WeaponAliasResolver.Group(sdk, p.WeaponChanges).Where(g => g.Enabled).Select(g => g.Representative)
            .Select(c => (Edit: "weapon " + c.Weapon + " " + c.SemanticFieldId, Field: sdk.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)!, c.Weapon, Desired: c.DesiredValue.GetRawText()))
            .Concat(p.CompositionChanges.Where(c => c.Scalar != null).Select(c => (Edit: "object " + c.Weapon + " " + c.Scalar!.SemanticFieldId,
                Field: sdk.PlayerWeapons!.Field(c.Scalar.Weapon, c.Scalar.SemanticFieldId), Weapon: c.Scalar.Weapon, Desired: c.Scalar.DesiredValue.GetRawText())))
            .Select(x => (Key: (SemanticBackingObject.For(sdk, x.Weapon, x.Field), x.Field.SemanticTarget ?? x.Field.SemanticFieldId), x.Edit, x.Desired)).ToArray();
        var shared = identities.GroupBy(x => x.Key).Where(g => g.Count() > 1).ToArray();
        Assert.Equal(identities.Length - shared.Sum(g => g.Count() - 1) + p.SupportChanges.Count + p.StratagemChanges.Count + p.EntityChanges.Count, Count(lua, "expect="));
        // The only folded edits are two weapons firing one projectile row, both edited to the same value (one native value, written once).
        Assert.Equal(["object AR-23 Liberator projectile.velocity|object AR-23A Liberator Carbine projectile.velocity", "object LAS-16 Sickle projectile.velocity|object LAS-17 Double-Edge Sickle projectile.velocity"],
            shared.Select(g => string.Join("|", g.Select(x => x.Edit).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal));
        Assert.All(shared, g => Assert.Single(g.Select(x => x.Desired).Distinct()));
    }

    [Fact] public void Requests_are_not_merged_across_objects()
    {
        var p = data.Build.Workspace.Project!; var requests = Requests(data.Build.Workspace.LuaPreview);
        Assert.Equal(222, requests.Length);
        var ids = requests.Select(r => Regex.Match(r, @"id='([^']+)'").Groups[1].Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        // Every edited enemy zone is its own request, holding exactly that zone's edits (the 1.4.1 zone identity at scale).
        var zones = p.EntityChanges.Where(c => EntityChangeService.IsEnemy(c.Resource) && c.Zone != null).GroupBy(c => (c.Entity, c.Zone)).ToArray();
        Assert.True(zones.Length > 20);
        var zoneRequests = requests.Where(r => r.Contains(":zone('", StringComparison.Ordinal)).ToArray();
        Assert.Equal(zones.Length, zoneRequests.Length);
        Assert.All(zoneRequests, r => Assert.Equal(1, Count(r, ":zone('")));
        foreach (var zone in zones)
        {
            var cls = data.Build.Sdk.Entities!.Enemies!.Find(zone.Key.Entity)!;
            var request = Assert.Single(zoneRequests, r => r.Contains("('" + cls.ClassName + "'):zone('" + zone.Key.Zone + "')", StringComparison.Ordinal));
            Assert.Equal(zone.Count(), Count(request, "expect="));
        }
    }

    [Fact] public async Task True_shared_values_are_still_refused_in_a_large_project()
    {
        var w = data.Build.Workspace; var sdk = data.Build.Sdk; var before = w.Project!.EntityChanges.Count;
        // One attack row reached through two Gunship attacks: the second edit is refused where it is made.
        var left = sdk.Entities!.Enemies!.Attack("enemy/v1/automatons/gunship", "slot_0").Single(f => f.SemanticFieldId == "damage.standard_damage");
        var right = sdk.Entities.Enemies.Attack("enemy/v1/automatons/gunship", "slot_1").Single(f => f.SemanticFieldId == "damage.standard_damage");
        await w.SetEntityAsync(left.InstanceKey, "10");
        Assert.Contains("already edited through", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(right.InstanceKey, "12"))).Message);
        Assert.Null(w.BuildError);
        // A project carrying both anyway (edited outside ModBuilder) does not build.
        var p = w.Project!; p.EntityChanges.Add(new EntityChangeService().Create(sdk, right.InstanceKey, "12"));
        Assert.Contains("is one shared value reached through", Assert.Throws<InvalidDataException>(() => new EntityLua(new EntityChangeService()).Operations(p, sdk)).Message);
        p.EntityChanges.RemoveAt(p.EntityChanges.Count - 1);
        await w.ResetEntityAsync(instance: left.InstanceKey);
        Assert.Equal(before, w.Project!.EntityChanges.Count); Assert.Null(w.BuildError);
    }

    // The Changes page and field editors read one weapon's groups; grouping never mixes weapons, so it is exactly the full grouping filtered.
    [Fact] public void One_weapons_groups_are_the_full_grouping_filtered_to_that_weapon()
    {
        var w = data.Build.Workspace; var all = w.WeaponGroups;
        Assert.True(all.Select(g => g.Weapon).Distinct().Count() > 50);
        foreach (var weapon in all.Select(g => g.Weapon).Distinct())
        {
            static string Shape(WeaponChangeGroup g) => g.Weapon + "|" + g.FieldId + "|" + g.Field?.SemanticFieldId + "|" + string.Join(",", g.Sources.Select(c => c.Id)) + "|" + g.Conflict + "|" + g.Enabled;
            Assert.Equal(all.Where(g => g.Weapon == weapon).Select(Shape), w.WeaponGroupsFor(weapon).Select(Shape));
        }
        Assert.Empty(w.WeaponGroupsFor("Not A Weapon"));
    }

    // Generation validates composition once and plans without validating it again: the plan is the same one.
    [Fact] public void Planning_after_validation_produces_the_same_plan()
    {
        var p = data.Build.Workspace.Project!; var sdk = data.Build.Sdk; var planner = new SemanticOperationPlanner();
        Assert.Equal(CompositionPlanLua.Operations(p.ResourceId, sdk, planner.Plan(p, sdk)), CompositionPlanLua.Operations(p.ResourceId, sdk, planner.PlanValidated(p, sdk)));
    }

    [Fact] public void Generated_Lua_is_byte_identical_to_the_pinned_baseline()
    {
        var lua = data.Build.Workspace.LuaPreview;
        Assert.Equal(LuaSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lua))).ToLowerInvariant());
    }
}
