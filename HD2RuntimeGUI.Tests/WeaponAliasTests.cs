using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class WeaponAliasTests
{
    private static readonly PlayerWeaponCatalog Catalog = new PlayerWeaponCatalogReader().Read(SdkCache.BundledCapabilities(), "0.14.1");
    private static readonly SdkMetadata Sdk = new MetadataReader().Read(SdkCache.BundledMetadata()) with { PlayerWeapons = Catalog };
    private static readonly WeaponChangeService Changes = new();
    public static TheoryData<string, string, string> Mappings => new()
    {
        { "P-113 Verdict", "weapon.capacity", "magazine.capacity" },
        { "SG-8 Punisher", "weapon.feed_capacity_1", "rounds.feed_capacity_1" },
        { "SG-8 Punisher", "weapon.feed_capacity_2", "rounds.feed_capacity_2" }
    };
    private static async Task<SdkMetadata> Install(TestEnvironment env, bool old = false)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in new[] { ("metadata.json", SdkCache.BundledMetadata()),
                (PlayerWeaponCatalogReader.FileName, SdkCache.BundledCapabilities()), (PlayerWeaponAmmoCatalogReader.FileName, SdkCache.BundledAmmoCapabilities()) })
            { using var stream = zip.CreateEntry(name).Open(); stream.Write(old ? AmmoAuthoringTests.Release014(name) : bytes); }
        }
        env.GitHub.Archive = output.ToArray();
        return await env.Cache.InstallAsync(FakeGitHub.MakeRelease(old ? "0.14.0" : "0.14.1", env.GitHub.Archive));
    }
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment env, bool old = false)
    {
        var sdk = await Install(env, old); var w = env.Workspace();
        await w.CreateAsync(new("Aliases", "Tests", "mods/tests/aliases", "0.1.0"), sdk); return w;
    }
    private static WeaponChange SavedAlias(string weapon, string alias, string value)
    {
        var c = Changes.Create(Sdk, weapon, alias, value, false);
        c.SemanticFieldId = alias; c.BaselineSdkVersion = "0.14.0"; return c;
    }
    [Fact] public void Published_schema_v2_loads_alias_rules_and_distinct_semantic_evidence()
    {
        Assert.Equal(2, Catalog.SchemaVersion); Assert.Equal(3, Catalog.SemanticAliases!.Count);
        Assert.Equal(62, Catalog.Summary.SemanticAliasInstances); Assert.Equal(1907, Catalog.Summary.WritableFieldInstances);
        Assert.Equal(2965, Catalog.Weapons.Sum(w => w.Fields.Count(f => f.IsPreferred)));
        Assert.All(Catalog.Weapons.SelectMany(w => w.Fields).Where(f => f.AliasOf != null), f => { Assert.False(f.IsPreferred); Assert.True(f.Deprecated); Assert.False(f.Editable); });
        var fields = Catalog.Weapon("P-113 Verdict").Fields;
        var baseCapacity = fields.Single(f => f.SemanticFieldId == "weapon.base_capacity"); var capacity = fields.Single(f => f.SemanticFieldId == "magazine.capacity");
        Assert.Equal(baseCapacity.Backing!.Offset, capacity.Backing!.Offset);
        Assert.NotEqual(baseCapacity.SemanticTarget, capacity.SemanticTarget);
        Assert.True(baseCapacity.IsPreferred); Assert.True(capacity.IsPreferred);
        Assert.Equal("weapon.base_capacity", Catalog.FindCanonicalField("P-113 Verdict", "weapon.base_capacity")!.SemanticFieldId);
        Assert.Null(Catalog.Field("AR-23C Liberator Concussive", "weapon.capacity").AliasOf);
    }
    [Theory] [MemberData(nameof(Mappings))]
    public async Task Old_project_pins_rebinds_displays_and_edits_canonical_without_losing_baseline(string weapon, string alias, string canonical)
    {
        using var env = new TestEnvironment(); var w = await Workspace(env, old: true);
        await w.SetWeaponChangeAsync(weapon, alias, "15", false);
        var source = Assert.Single(w.Project!.WeaponChanges); var expected = source.ExpectedValue.GetRawText(); var id = source.Id;
        await Install(env); await w.OpenAsync(w.Project.Id);
        Assert.Equal("0.14.0", w.Metadata!.Version); Assert.Null(w.Metadata.PlayerWeapons!.Field(weapon, alias).AliasOf);
        await w.RebindToInstalledSdkAsync();
        var group = Assert.Single(w.WeaponGroups); Assert.Equal(canonical, group.FieldId); Assert.Equal(15, group.Representative.DesiredValue.GetDouble());
        Assert.Equal(expected, group.Representative.ExpectedValue.GetRawText()); Assert.Equal("0.14.0", group.Representative.BaselineSdkVersion);
        Assert.Contains("field=hd2.fields." + canonical, w.LuaPreview); Assert.DoesNotContain("field=hd2.fields." + alias, w.LuaPreview);
        await w.SetWeaponChangeAsync(weapon, canonical, "16", false);
        var edited = Assert.Single(w.Project.WeaponChanges); Assert.Equal(id, edited.Id); Assert.Equal(canonical, edited.SemanticFieldId);
        Assert.Equal(expected, edited.ExpectedValue.GetRawText()); Assert.Equal("0.14.0", edited.BaselineSdkVersion);
        await w.OpenAsync(w.Project.Id); Assert.Equal(16, Assert.Single(w.WeaponGroups).Representative.DesiredValue.GetDouble());
        await w.SetWeaponChangeAsync(weapon, canonical, expected, false); Assert.Empty(w.Project.WeaponChanges);
    }
    [Theory] [MemberData(nameof(Mappings))]
    public async Task Equal_alias_values_coalesce_for_display_Lua_and_export_without_rewriting_sources(string weapon, string alias, string canonical)
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        w.Project!.WeaponChanges = [SavedAlias(weapon, alias, "15.0"), Changes.Create(Sdk, weapon, canonical, "15", false)];
        await env.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Equal(2, w.Project.WeaponChanges.Count); var g = Assert.Single(w.WeaponGroups); Assert.Null(g.Conflict); Assert.Equal(canonical, g.FieldId);
        Assert.Null(w.BuildError); Assert.Contains("patch=", w.LuaPreview); Assert.DoesNotContain("transaction=", w.LuaPreview);
        var lua = w.LuaPreview; w.Project.WeaponChanges.Reverse(); Assert.Equal(lua, env.Generator.Generate(w.Project, w.Metadata!));
        await w.ExportAsync(); var first = await File.ReadAllBytesAsync(w.LastExport!); await w.ExportAsync(); Assert.Equal(first, await File.ReadAllBytesAsync(w.LastExport!));
        await w.SetWeaponChangeAsync(weapon, canonical, "18", false); Assert.Single(w.Project.WeaponChanges);
        Assert.Equal(18, Assert.Single(w.WeaponGroups).Representative.DesiredValue.GetDouble());
    }
    [Theory] [MemberData(nameof(Mappings))]
    public async Task Different_values_block_build_until_user_keeps_a_source(string weapon, string alias, string canonical)
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        var old = SavedAlias(weapon, alias, "15"); var current = Changes.Create(Sdk, weapon, canonical, "20", false);
        w.Project!.WeaponChanges = [old, current]; await env.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Contains("Migration conflict", Assert.Single(w.WeaponGroups).Conflict); Assert.NotNull(w.BuildError); Assert.NotEmpty(w.WeaponIssues);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.ExportAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync(weapon, canonical, "25", false));
        await w.ResolveWeaponAliasAsync(old.Id); Assert.Null(w.BuildError); Assert.Equal(15, Assert.Single(w.Project.WeaponChanges).DesiredValue.GetDouble());
        Assert.Equal(old.ExpectedValue.GetRawText(), w.Project.WeaponChanges[0].ExpectedValue.GetRawText());
        Assert.Contains("field=hd2.fields." + canonical, w.LuaPreview);
        await w.OpenAsync(w.Project.Id); Assert.Null(Assert.Single(w.WeaponGroups).Conflict);
    }
    [Fact] public async Task Baseline_noop_and_disabled_sources_do_not_conceal_migration_conflicts()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        var alias = SavedAlias("P-113 Verdict", "weapon.capacity", "10"); alias.Enabled = false;
        w.Project!.WeaponChanges = [alias, Changes.Create(Sdk, alias.Weapon, "magazine.capacity", "20", false)];
        await env.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Equal(2, w.Project.WeaponChanges.Count); Assert.NotNull(w.BuildError);
        await w.ResetWeaponsAsync(alias.Weapon, "magazine.capacity"); Assert.Empty(w.Project.WeaponChanges); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Saved_baselines_are_still_reviewed_after_alias_resolution()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        var alias = SavedAlias("P-113 Verdict", "weapon.capacity", "15"); alias.ExpectedValue = JsonSerializer.SerializeToElement(9);
        w.Project!.WeaponChanges = [alias]; await env.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Contains("baseline changed", w.BuildError); Assert.Equal(9, w.Project.WeaponChanges[0].ExpectedValue.GetInt32());
        await w.AcceptWeaponBaselineAsync(alias.Id); Assert.Null(w.BuildError); Assert.Equal(15, w.Project.WeaponChanges[0].DesiredValue.GetDouble());
        Assert.Equal(10, w.Project.WeaponChanges[0].ExpectedValue.GetInt32());
    }
    [Fact] public void Rejected_aliases_cannot_bypass_duplicate_identity_safety()
    {
        var original = Catalog.Field("GP-31 Grenade Pistol", "weapon.feed_capacity_1");
        Assert.False(original.WriteAccepted); Assert.False(Catalog.FindCanonicalField("GP-31 Grenade Pistol", original.SemanticFieldId)!.WriteAccepted);
        Assert.Throws<InvalidDataException>(() => Changes.Create(Sdk, "GP-31 Grenade Pistol", original.SemanticFieldId, "2", true));
    }
    [Fact] public async Task Equal_desired_values_never_silently_discard_a_different_saved_baseline()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        var alias = SavedAlias("P-113 Verdict", "weapon.capacity", "15"); alias.ExpectedValue = JsonSerializer.SerializeToElement(9);
        var canonical = Changes.Create(Sdk, alias.Weapon, "magazine.capacity", "15", false);
        w.Project!.WeaponChanges = [alias, canonical]; await env.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Single(w.WeaponGroups); Assert.Contains("baselines differ", w.BuildError); Assert.Equal(2, w.Project.WeaponChanges.Count);
        await w.ResolveWeaponAliasAsync(alias.Id); Assert.Contains("baseline changed", w.BuildError);
        Assert.Equal(9, Assert.Single(w.Project.WeaponChanges).ExpectedValue.GetInt32());
    }
    [Fact] public void Alias_properties_cannot_bypass_schema_v2_validation_by_claiming_v1()
    {
        var root = JsonNode.Parse(SdkCache.BundledCapabilities())!; root["schemaVersion"] = 1;
        Assert.Throws<InvalidDataException>(() => new PlayerWeaponCatalogReader().Read(Encoding.UTF8.GetBytes(root.ToJsonString()), "0.14.1"));
    }
    [Theory] [InlineData("missing")] [InlineData("cycle")] [InlineData("target")] [InlineData("preferred")] [InlineData("accepted")] [InlineData("summary")] [InlineData("schema")]
    public void Malformed_alias_metadata_fails_closed(string defect)
    {
        var root = JsonNode.Parse(SdkCache.BundledCapabilities())!;
        var fields = root["weapons"]!.AsArray().Single(w => w!["name"]!.GetValue<string>() == "P-113 Verdict")!["fields"]!.AsArray();
        var alias = fields.Single(f => f!["semanticFieldId"]!.GetValue<string>() == "weapon.capacity")!;
        if (defect == "missing") alias["aliasOf"] = "missing.field";
        if (defect == "cycle") alias["aliasOf"] = "weapon.capacity";
        if (defect == "target") alias["semanticTarget"] = "unrelated_semantics";
        if (defect == "preferred") alias["preferred"] = true;
        if (defect == "accepted") alias["acceptedForWrites"] = false;
        if (defect == "summary") root["summary"]!["semanticAliasInstances"] = 999;
        if (defect == "schema") root["schemaVersion"] = 3;
        Assert.ThrowsAny<Exception>(() => new PlayerWeaponCatalogReader().Read(Encoding.UTF8.GetBytes(root.ToJsonString()), "0.14.1"));
    }
}
