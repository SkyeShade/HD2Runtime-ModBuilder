using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class Runtime017Tests
{
    private const string Concussive = "AR-23C Liberator Concussive", Eruptor = "R-36 Eruptor", Jar = "JAR-5 Dominator", Verdict = "P-113 Verdict";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        e.GitHub.Archive = Archive(); var sdk = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.17.0", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new("Runtime017", "Tests", "mods/tests/runtime017", "0.1.0"), sdk); return w;
    }
    private static Dictionary<string, byte[]> FixtureFiles()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.17.0.zip"));
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var input = e.Open(); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); });
    }
    private static byte[] Archive(string? omit = null)
    {
        var files = FixtureFiles();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in files.Where(x => x.Key != omit)) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
        return output.ToArray();
    }
    [Theory]
    [InlineData("SupportWeaponCapabilities.json")] [InlineData("ExplosionAuthoringCapabilities.json")]
    [InlineData("ProjectileCompositionCapabilities.json")] [InlineData("AttachmentOptionCapabilities.json")]
    public async Task Missing_017_contract_rejects_update_and_preserves_installed_SDK(string name)
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = Archive(name);
        await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.17.0", e.GitHub.Archive)));
        Assert.Equal("0.5.1", (await e.Cache.GetCurrentAsync()).Version); Assert.False(File.Exists(e.Paths.SdkFile("0.17.0")));
    }
    [Theory] [InlineData("schemaVersion", "2")] [InlineData("contract", "\"unpublished.support\"")] [InlineData("weapons", "null")]
    public async Task Invalid_support_contract_is_rejected(string property, string value)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var files = FixtureFiles();
        var node = JsonNode.Parse(files["SupportWeaponCapabilities.json"])!; node[property] = JsonNode.Parse(value);
        files["SupportWeaponCapabilities.json"] = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
        var error = Record.Exception(() => new AdvancedCapabilitiesReader().Read(files, w.Metadata!.PlayerWeapons!, w.Metadata.Composition!));
        Assert.True(error is InvalidDataException or UnsupportedSdkException);
    }
    [Fact] public async Task Old_project_stays_pinned_until_rebind_then_requires_residency_evidence_review()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.15.0.zip"));
        var old = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.15.0", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned", "0.1.0"), old); await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        var evidence = w.Project!.ProjectileChanges[0].ReplacementEvidence;
        e.GitHub.Archive = Archive(); await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.17.0", e.GitHub.Archive));
        await w.OpenAsync(w.Project.Id); Assert.Equal("0.15.0", w.Metadata!.Version); Assert.Null(w.BuildError);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.17.0", w.Metadata.Version); Assert.NotNull(w.BuildError);
        Assert.Equal(new(Jar, "primary"), w.Project.ProjectileChanges[0].ReplacementProjectile); Assert.Equal(evidence, w.Project.ProjectileChanges[0].ReplacementEvidence);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"), true); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Release_017_loads_all_named_contracts_and_preserves_expected_counts()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var sdk = w.Metadata!;
        Assert.Equal("0.17.0", sdk.Version); Assert.Equal(80, sdk.PlayerWeapons!.Weapons.Count); Assert.Equal(3490, sdk.PlayerWeapons.Summary.FieldInstances);
        Assert.Equal(21, sdk.PlayerWeapons.Weapons.Count(w => w.Fields.Any(f => f.SemanticFieldId == "weapon.default_fire_mode" && f.Editable)));
        Assert.Equal(130, sdk.Composition!.TerminalActions.Weapons.SelectMany(w => w.Attacks).SelectMany(a => a.Actions).Count(a => a.Writable));
        Assert.Equal(144, sdk.Advanced!.Explosions.Explosions.Sum(x => x.WritableScalarFields)); Assert.Equal(35, sdk.Advanced.Support.Weapons.Count);
        Assert.Equal(419, sdk.Composition.Magazines.Weapons.Sum(w => w.Categories!.Sum(c => c.Options.Count)));
        foreach (var n in AdvancedCapabilitiesReader.FileNames) Assert.True(File.Exists(e.Paths.CachePath(sdk.Version, n)));
    }
    [Fact] public async Task Fire_mode_saves_semantic_enum_Lua_and_resets_to_baseline()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Concussive, "weapon.default_fire_mode", "2", false);
        Assert.Contains("expect=hd2.enums.fire_mode.full_auto", w.LuaPreview); Assert.Contains("value=hd2.enums.fire_mode.semi_auto", w.LuaPreview);
        Assert.DoesNotContain("expect=1", w.LuaPreview); await w.OpenAsync(w.Project!.Id); Assert.Single(w.Project.WeaponChanges);
        await w.SetWeaponChangeAsync(Concussive, "weapon.default_fire_mode", "1", false); Assert.Empty(w.Project.WeaponChanges);
    }
    [Theory] [InlineData("JAR-5 Dominator", "1")] [InlineData("R-36 Eruptor", "1")] [InlineData("AR-23C Liberator Concussive", "3")] [InlineData("AR-23C Liberator Concussive", "5")]
    public async Task Unproven_or_disallowed_fire_modes_fail_closed(string weapon, string value)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync(weapon, "weapon.default_fire_mode", value, false));
        Assert.Empty(w.Project!.WeaponChanges);
    }
    [Fact] public async Task Mode_3_and_5_have_no_fabricated_global_enum_names()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        foreach (var f in w.Metadata!.PlayerWeapons!.Weapons.SelectMany(w => w.Fields).Where(f => f.SemanticFieldId == "weapon.default_fire_mode" && f.CurrentDefault.GetInt32() is 3 or 5))
        { Assert.False(f.Editable); Assert.DoesNotContain(f.EnumValues!.Values, v => v.GetInt32() is 3 or 5); }
    }
    [Fact] public async Task Residency_filters_Talon_and_preserves_unresolved_warning_metadata()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        Assert.DoesNotContain(w.ProjectileSources(Verdict, "primary"), s => s.Weapon == "LAS-58 Talon");
        Assert.Contains(new(Jar, "primary"), w.ProjectileSources(Verdict, "primary"));
        Assert.Equal("SELF_CONTAINED", w.Metadata!.Composition!.Attack(Jar, "primary").Residency!.Classification);
        Assert.Contains(w.ProjectileSources(Jar, "primary"), s => w.Metadata.Composition.Attack(s.Weapon, s.AttackRole).Residency!.Classification == "DEPENDENCY_UNRESOLVED");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetProjectileAsync(Verdict, "primary", new("LAS-58 Talon", "primary")));
    }
    [Fact] public async Task Swap_then_shared_scalar_uses_source_projectile_in_separate_ordered_operation()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", false);
        // Shared approval is implicit: only the composition dependency blocks the build.
        Assert.Contains("Composition dependency", w.BuildError); Assert.DoesNotContain("Shared", w.BuildError, StringComparison.OrdinalIgnoreCase);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true);
        Assert.Contains("Composition dependency", w.BuildError);
        Assert.Equal(new(Jar, "primary"), Assert.Single(w.Project!.CompositionChanges).Target);
        Assert.Equal(180, w.Project.CompositionChanges[0].Scalar!.ExpectedValue.GetDouble());
        await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        await w.OpenAsync(w.Project.Id); Assert.Single(w.Project.CompositionChanges); Assert.Contains("Composition dependency", w.BuildError);
    }
    [Fact] public async Task Changing_replacement_blocks_stale_object_edits_before_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary")); Assert.Contains("no longer fires", w.BuildError);
        await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        await w.RemoveCompositionAsync(w.Project!.CompositionChanges[0].Id); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Legacy_projectile_scalar_plus_swap_is_blocked()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Verdict, "projectile.velocity", "350", true); await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        Assert.Contains("old weapon-level projectile", w.BuildError); await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
    }
    [Fact] public async Task Legacy_object_override_moves_without_losing_values_and_builds_without_fresh_shared_approval()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Verdict, "projectile.velocity", "350", false); var old = w.Project!.WeaponChanges[0];
        await w.MoveToCompositionAsync(old.Id); Assert.Empty(w.Project.WeaponChanges); var moved = Assert.Single(w.Project.CompositionChanges);
        Assert.Equal(old.ExpectedValue.GetRawText(), moved.Scalar!.ExpectedValue.GetRawText()); Assert.Equal(350, moved.Scalar.DesiredValue.GetDouble()); Assert.Null(w.BuildError);
        Assert.Contains("allow_shared=true", w.LuaPreview);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
    }
    [Theory] [InlineData("impact")] [InlineData("expiry")]
    public async Task Terminal_add_remove_and_typed_None_roundtrip(string phase)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var source = w.ExplosionSources.First(s => s.Projectile?.Weapon == "CB-9 Exploding Crossbow");
        await w.SetTerminalAsync(Jar, "primary", phase, source, false); Assert.Null(w.BuildError);
        Assert.Contains(":no_explosion()", w.LuaPreview); Assert.Contains("field=hd2.fields.terminal.explosion", w.LuaPreview);
        Assert.DoesNotContain("expect=0", w.LuaPreview); Assert.DoesNotContain("value=59", w.LuaPreview);
        await w.SetTerminalAsync(Jar, "primary", phase, ExplosionReference.None, false); Assert.Empty(w.Project!.CompositionChanges);
        await w.SetTerminalAsync(Eruptor, "primary", phase, ExplosionReference.None, false); Assert.Null(w.BuildError);
        Assert.Contains("value=hd2.weapon('R-36 Eruptor'):attack('primary'):projectile():terminal_action('" + phase + "'):no_explosion()", w.LuaPreview);
        await w.OpenAsync(w.Project.Id); Assert.True(Assert.Single(w.Project.CompositionChanges).DesiredExplosion!.IsNone);
    }
    [Fact] public async Task Shared_terminal_builds_without_consumer_approval_and_emits_allow_shared()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var weapon = w.Metadata!.PlayerWeapons!.Weapons.First(w => w.Fields.Any(f => f.Domain == "terminal" && f.Editable && f.AffectsMultipleWeapons));
        var f = weapon.Fields.First(f => f.Domain == "terminal" && f.Editable && f.AffectsMultipleWeapons);
        var desired = f.CurrentDefault.GetProperty("explosionType").GetInt32() == 0 ? w.ExplosionSources.First(s => !s.IsNone) : ExplosionReference.None;
        await w.SetTerminalAsync(weapon.Name, f.ReferenceRole!, f.ReferencePhase!, desired, false); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
        await w.SetTerminalAsync(weapon.Name, f.ReferenceRole!, f.ReferencePhase!, desired, true); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
    }
    [Theory] [InlineData("explosion.primary.impact.outer_radius", "10", "7")] [InlineData("explosion.primary.impact.damage.standard_damage", "500", "225")]
    public async Task Explosion_fields_generate_semantic_handles_and_remove_baseline_override(string field, string value, string baseline)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", field, value, false); Assert.Null(w.BuildError);
        Assert.Contains(":terminal_action('impact'):explosion()", w.LuaPreview); Assert.Contains("value=" + value, w.LuaPreview);
        await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", field, baseline, false); Assert.Empty(w.Project!.CompositionChanges);
    }
    [Fact] public async Task Shared_explosion_builds_without_approval_and_emits_allow_shared()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var weapon = w.Metadata!.PlayerWeapons!.Weapons.First(w => w.Fields.Any(f => f.Domain == "explosion" && f.Editable && f.AffectsMultipleWeapons));
        var f = weapon.Fields.First(f => f.Domain == "explosion" && f.Editable && f.AffectsMultipleWeapons);
        var role = w.Metadata.Composition!.Projectiles.Weapons.Single(w => w.Weapon == weapon.Name).Attacks[0].Role;
        var value = (f.CurrentDefault.GetDouble() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await w.SetObjectScalarAsync(weapon.Name, role, "explosion", "impact", f.SemanticFieldId, value, false); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
        await w.SetObjectScalarAsync(weapon.Name, role, "explosion", "impact", f.SemanticFieldId, value, true); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
    }
    [Theory] [InlineData("GR-8 Recoilless Rifle", "Projectile")] [InlineData("ARC-3 Arc Thrower", "Arc")] [InlineData("B/MD C4 Pack", "Explosion")] [InlineData("MS-11 Solo Silo", "Explosion")]
    public async Task Support_contract_preserves_system_graph_and_readonly_state(string name, string kind)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var s = w.Metadata!.Advanced!.Support.Weapons[name];
        Assert.False(s.GuardedAuthoringReady); Assert.Contains(s.AttackGraph, a => a.Kind == kind);
        if (name == "MS-11 Solo Silo") Assert.Contains(s.OwnershipChain, n => n.Component == "HellpodRackComponentData");
        if (name == "B/MD C4 Pack") Assert.Contains(s.OwnershipChain, n => n.Kind == "placed_or_attack_entity");
    }
    [Fact] public async Task Support_duplicates_unresolved_railgun_and_shared_groups_survive()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var s = w.Metadata!.Advanced!.Support;
        Assert.Equal(8, s.Summary.DuplicateIdentityGroups); Assert.Contains(s.Weapons.Values, w => w.IdentityResolution != "UNIQUE");
        Assert.Contains(s.Weapons["RS-422 Railgun"].AttackGraph, a => a.State != "RESOLVED"); Assert.Equal(5, s.Summary.SharedSettingsGroups);
    }
    [Fact] public async Task All_attachment_categories_and_Concussive_values_are_readonly()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var graph = w.Metadata!.Composition!.Magazines;
        Assert.Equal(new[] { "Magazine", "Muzzle", "Optics", "Underbarrel" }, graph.Weapons.SelectMany(w => w.Categories!).Select(c => c.Category).Distinct().Order());
        var options = graph.Weapons.Single(w => w.Weapon == Concussive).Categories!.Single(c => c.Category == "Magazine").Options;
        Assert.Equal(60, options.Single(o => o.Name == "Drum Magazine").Effects.CapacityRounds); Assert.Equal(30, options.Single(o => o.Name == "Short Magazine").Effects.CapacityRounds); Assert.Equal(45, options.Single(o => o.Name == "Extended Magazine").Effects.CapacityRounds);
        Assert.All(graph.Weapons.SelectMany(w => w.Categories!).SelectMany(c => c.Options), o => Assert.False(o.Writable));
    }
    [Fact] public async Task All_new_changes_reload_and_export_deterministically()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Concussive, "weapon.default_fire_mode", "2", false);
        await w.SetTerminalAsync(Jar, "primary", "impact", w.ExplosionSources.First(s => s.Projectile?.Weapon == "CB-9 Exploding Crossbow"), false);
        await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", "explosion.primary.impact.outer_radius", "10", false);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true);
        var lua = w.LuaPreview; Assert.Null(w.BuildError); await w.OpenAsync(w.Project!.Id); Assert.Equal(lua, w.LuaPreview);
        await w.ExportAsync(); var bytes = await File.ReadAllBytesAsync(w.LastExport!); await w.ExportAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(w.LastExport!));
        using var zip = ZipFile.OpenRead(w.LastExport!); Assert.Equal(8, zip.Entries.Count); Assert.DoesNotContain(zip.Entries, x => x.Name.Contains("Capabilities"));
    }
    [Fact] public async Task Rebind_baseline_evidence_and_shared_scope_changes_block()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", "explosion.primary.impact.outer_radius", "10", false);
        var c = w.Project!.CompositionChanges[0]; c.TargetEvidence = new string('0',64);
        await e.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id); Assert.NotNull(w.BuildError);
        await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", c.Scalar!.SemanticFieldId, "10", false, true); Assert.Null(w.BuildError);
    }
    [Theory] [InlineData("fire mode")] [InlineData("read-only explosion")]
    public async Task Changed_capability_permissions_block_saved_changes(string scenario)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        if (scenario == "fire mode") await w.SetWeaponChangeAsync(Concussive, "weapon.default_fire_mode", "2", false);
        else await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", "explosion.primary.impact.outer_radius", "10", false);
        var sdk = w.Metadata!; var weapon = scenario == "fire mode" ? Concussive : Eruptor;
        var id = scenario == "fire mode" ? "weapon.default_fire_mode" : "explosion.primary.impact.outer_radius";
        var changed = sdk with { PlayerWeapons = sdk.PlayerWeapons! with { Weapons = sdk.PlayerWeapons!.Weapons.Select(x => x.Name != weapon ? x : x with {
            Fields = x.Fields.Select(f => f.SemanticFieldId != id ? f : scenario switch {
                "fire mode" => f with { AllowedValues = [1] },
                _ => f with { Editable = false, AcceptedForWrites = false }
            }).ToArray() }).ToArray() } };
        Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project!, changed));
    }
    [Fact] public async Task Changed_capability_to_shared_explosion_builds_without_approval_and_emits_allow_shared()
    {
        // A capability that becomes shared after a rebind no longer blocks on a missing approval: allow_shared is implicit.
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", "explosion.primary.impact.outer_radius", "10", false);
        Assert.DoesNotContain("allow_shared", w.LuaPreview);
        var sdk = w.Metadata!; const string id = "explosion.primary.impact.outer_radius";
        var changed = sdk with { PlayerWeapons = sdk.PlayerWeapons! with { Weapons = sdk.PlayerWeapons!.Weapons.Select(x => x.Name != Eruptor ? x : x with {
            Fields = x.Fields.Select(f => f.SemanticFieldId != id ? f : f with { AffectsMultipleWeapons = true, WriteScope = "shared_explosion_settings", SharedWithWeapons = [Jar] }).ToArray() }).ToArray() } };
        Assert.Contains("allow_shared=true", e.Generator.Generate(w.Project!, changed));
    }
    [Fact] public async Task Changed_source_residency_blocks_saved_replacement()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        var sdk = w.Metadata!; var graph = sdk.Composition!.Projectiles;
        var changed = sdk with { Composition = sdk.Composition with { Projectiles = graph with { Weapons = graph.Weapons.Select(x => x.Weapon != Jar ? x : x with {
            Attacks = x.Attacks.Select(a => a with { Residency = a.Residency! with { Classification = "SOURCE_WEAPON_REQUIRED" } }).ToArray() }).ToArray() } } };
        Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project!, changed));
    }
    [Fact] public async Task Removed_typed_explosion_source_blocks_saved_terminal_edit()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetTerminalAsync(Jar, "primary", "impact", w.ExplosionSources.First(s => s.Projectile?.Weapon == "CB-9 Exploding Crossbow"), false);
        var sdk = w.Metadata!; var changed = sdk with { Advanced = sdk.Advanced! with { Explosions = sdk.Advanced!.Explosions with { Explosions = [] } } };
        Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project!, changed));
    }
}
