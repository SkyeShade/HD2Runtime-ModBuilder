using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Custom content in a project (format 14): the workspace's create / duplicate / rename / id change / delete, icons, families, custom projectiles
// and the weapons that fire them, the dependency graph, save and reopen, the build (Lua, archive, ZIP) and that projects without custom
// content are unchanged.
public sealed class CustomContentProjectTests
{
    private const string Reprimand = "SMG-32 Reprimand";
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "custom-content", name);
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string name = "Custom Content")
    {
        var (w, _) = await ExportFixtureTests.Fresh(e, name);
        w.Project!.ExportDirectory = e.Paths.Exports;
        return w;
    }
    private static Dictionary<string, byte[]> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.ToDictionary(z => z.FullName, z => { using var s = z.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }
    private static string Json(ModProject p) => JsonSerializer.Serialize(p, JsonStorage.Options);
    private static async Task<ModProject> Reopen(TestEnvironment e, ModProject p) => await e.Store.LoadAsync(p.Id);

    [Fact] public async Task A_custom_stratagem_is_created_saved_and_reopened_exactly_in_its_semantic_model()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        Assert.Null(w.Project!.CustomContent);
        var id = await w.CreateCustomStratagemAsync("Orbital Gas Barrage", DeliveryFamily.Orbital);
        Assert.Equal("orbital_gas_barrage", id);
        await w.EditCustomStratagemAsync(id, s =>
        {
            s.Description = "Calls down a barrage of gas shells."; s.Code = ["up", "up", "down", "down"]; s.Cooldown = 60;
            s.Delivery.Orbital = new() { Shell = "Orbital Gas Strike", Salvos = 3, ShellInterval = 0.75, Scatter = 27, ImpactPayload = new(ContentRef.VanillaExplosion, "Orbital Gas Strike") };
        });
        await w.UsePlaceholderIconAsync(id);
        Assert.Equal(14, w.Project.FormatVersion);
        var saved = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id));
        var json = JsonNode.Parse(saved)!["customContent"]!;
        Assert.Equal(1, (int)json["version"]!);
        var s0 = json["stratagems"]![0]!;
        Assert.Equal("orbital_gas_barrage", (string)s0["id"]!); Assert.Equal("ORBITAL GAS BARRAGE", (string)s0["name"]!); Assert.Equal("Orbital Gas Barrage", (string)s0["nameCased"]!);
        Assert.Equal(["up", "up", "down", "down"], s0["code"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("orbital", (string)s0["delivery"]!["family"]!);
        Assert.Equal("vanilla_explosion", (string)s0["delivery"]!["orbital"]!["impactPayload"]!["kind"]!);
        // The project stores intent, never Runtime field names or generated Lua.
        foreach (var runtime in new[] { "shells_per_salvo", "impact_explosion", "name_cased", "hd2.custom_stratagem", "register", "prefer_families" }) Assert.DoesNotContain(runtime, saved);
        // Only the chosen family's settings are saved; the icon's source PNG is in the project itself.
        Assert.Null(s0["delivery"]!["support"]); Assert.Null(s0["delivery"]!["eagle"]);
        Assert.Equal(id, (string)json["images"]![0]!["id"]!);
        var reopened = await Reopen(e, w.Project);
        Assert.Equal(Json(w.Project), Json(reopened));
        var other = e.Workspace(); await other.OpenAsync(w.Project.Id);
        Assert.Equal(Json(w.Project), Json(other.Project!));
        Assert.Contains(other.CustomDiagnosticsFor(CustomDiagnostic.StratagemOwner(id)), d => d.Severity == CustomSeverity.Pass && d.Field == "id");
        Assert.Empty(other.CustomDiagnostics().Where(d => d.Severity == CustomSeverity.Error));
        Assert.Null(other.BuildError);
        Assert.Contains("register_custom_stratagem('orbital_gas_barrage', function()", other.LuaPreview);
        Assert.Contains("orbital={shell='Orbital Gas Strike',salvos=3,", other.LuaPreview);
    }

    [Fact] public async Task Ids_are_unique_renames_keep_them_and_id_changes_are_explicit()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var a = await w.CreateCustomStratagemAsync("Gas Barrage");
        var b = await w.CreateCustomStratagemAsync("Gas Barrage");
        Assert.Equal(("gas_barrage", "gas_barrage_2"), (a, b));
        await w.RenameCustomStratagemAsync(a, "Nerve Gas Barrage");
        Assert.Equal(("NERVE GAS BARRAGE", "Nerve Gas Barrage", "gas_barrage"), (w.FindCustomStratagem(a)!.Name, w.FindCustomStratagem(a)!.NameCased, w.FindCustomStratagem(a)!.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.ChangeCustomStratagemIdAsync(b, a));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.ChangeCustomStratagemIdAsync(b, "Bad Id"));
        Assert.Equal("gas_barrage_2", w.CustomStratagems[1].Id);
        await w.ChangeCustomStratagemIdAsync(b, "nerve_barrage");
        Assert.Equal("nerve_barrage", w.CustomStratagems[1].Id);
        // A hand-edited project with two equal ids is refused when it is read.
        var json = JsonNode.Parse(await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id)))!;
        json["customContent"]!["stratagems"]![1]!["id"] = "gas_barrage";
        await File.WriteAllTextAsync(e.Paths.ProjectFile(w.Project.Id), json.ToJsonString());
        Assert.Contains("custom content", (await Assert.ThrowsAsync<InvalidDataException>(() => e.Store.LoadAsync(w.Project.Id))).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task Duplicate_rename_delete_and_family_switch_behave_like_editing_one_object()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var gas = await w.CreateCustomStratagemFromTemplateAsync("SimpleSupportDrop");
        var copy = await w.DuplicateCustomStratagemAsync(gas);
        Assert.Equal("machine_gun_drop_copy", copy);
        var (source, duplicate) = (w.FindCustomStratagem(gas)!, w.FindCustomStratagem(copy)!);
        Assert.NotEqual(source.Code, duplicate.Code);
        Assert.Equal(copy, duplicate.Icon); Assert.Equal(2, w.CustomContent!.Images.Count);
        Assert.Equal(source.Delivery.Support!.Item, duplicate.Delivery.Support!.Item);
        Assert.Empty(w.CustomDiagnostics().Where(d => d.Severity == CustomSeverity.Error));
        // Switching family starts the new family from its defaults and moves the beacon to one it can throw.
        await w.SetCustomDeliveryFamilyAsync(copy, DeliveryFamily.Eagle);
        duplicate = w.FindCustomStratagem(copy)!;
        Assert.Equal((DeliveryFamily.Eagle, (SupportDelivery?)null, "eagle", (string?)null), (duplicate.Delivery.Family, duplicate.Delivery.Support, duplicate.Carrier.Group, duplicate.Carrier.Beacon));
        Assert.Equal(["eagle"], duplicate.Carrier.Allow); Assert.Null(duplicate.Cooldown);
        Assert.Contains(w.CustomDiagnosticsFor(CustomDiagnostic.StratagemOwner(copy)), d => d.Severity == CustomSeverity.Error && d.Field == "payload.donor" && d.Runtime);
        await w.DeleteCustomStratagemAsync(copy);
        Assert.Single(w.CustomStratagems); Assert.Single(w.CustomContent!.Images);
        await w.DeleteCustomStratagemAsync(gas);
        Assert.Null(w.Project!.CustomContent); Assert.Equal(14, w.Project.FormatVersion);
    }

    [Fact] public async Task Icons_are_imported_as_source_PNGs_validated_and_prepared()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomStratagemAsync("Art Test");
        // A new custom stratagem starts with a generated placeholder icon; a refused import leaves it as it was.
        var placeholder = CustomIconCompiler.Placeholder(id);
        Assert.Equal(placeholder, Convert.FromBase64String(w.CustomImageOf(w.FindCustomStratagem(id)!.Icon)!.Png));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.ImportCustomIconAsync(id, Fixture("grey2.png")));
        Assert.Equal(placeholder, Convert.FromBase64String(w.CustomImageOf(w.FindCustomStratagem(id)!.Icon)!.Png));
        await w.ImportCustomIconAsync(id, Fixture("art.png"));
        var image = w.CustomImageOf(w.FindCustomStratagem(id)!.Icon)!;
        Assert.Equal(("art.png", CustomImage.Auto), (image.SourceName, image.Preparation));
        Assert.Equal(File.ReadAllBytes(Fixture("art.png")), Convert.FromBase64String(image.Png));
        Assert.Contains(w.CustomDiagnosticsFor(CustomDiagnostic.StratagemOwner(id)), d => d.Severity == CustomSeverity.Pass && d.Field == "icon");
        Assert.NotNull(w.CustomIconPreviewUri(w.FindCustomStratagem(id)!));
        await w.SetCustomIconPreparationAsync(id, CustomImage.Raw);
        Assert.Equal(CustomImage.Raw, w.CustomImageOf(w.FindCustomStratagem(id)!.Icon)!.Preparation);
        await w.RemoveCustomIconAsync(id);
        Assert.Empty(w.CustomContent!.Images);
        Assert.Contains(w.CustomDiagnosticsFor(CustomDiagnostic.StratagemOwner(id)), d => d.Severity == CustomSeverity.Error && d.Field == "icon");
    }

    [Fact] public async Task Errors_refuse_the_build_and_name_the_custom_object_to_open()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomStratagemAsync("Unfinished");
        Assert.NotNull(w.BuildError); Assert.Contains("unfinished", w.BuildError);
        Assert.Equal(CustomDiagnostic.StratagemOwner(id), w.CustomBuildIssue!.Owner);
        await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        // A problem is saved with the edit (it is shown where the object is edited), never lost.
        Assert.Equal("Unfinished", (await Reopen(e, w.Project!)).CustomContent!.Stratagems[0].NameCased);
    }

    [Fact] public async Task Every_template_builds_into_one_readable_registration_per_stratagem()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        foreach (var template in CustomTemplates.Stratagems) await w.CreateCustomStratagemFromTemplateAsync(template.Key);
        Assert.Null(w.BuildError);
        Assert.Equal(7, CustomTemplates.Stratagems.Count);
        Assert.Equal(2, CustomTemplates.Stratagems.Count(t => t.RuntimeExample));
        Assert.Equal(CustomTemplates.Stratagems.Count, System.Text.RegularExpressions.Regex.Matches(w.LuaPreview, "register_custom_stratagem\\('").Count);
        foreach (var expected in new[] { "delivery={family='support',", "delivery={family='expendable',", "sentry={", "eagle={", "orbital={native=true,", "pelican={hover=", "silo={donor=" })
            Assert.Contains(expected, w.LuaPreview);
        Assert.Contains("allow_families={'sentry'}", w.LuaPreview);
        Assert.Equal(w.LuaPreview, e.Generator.Generate(w.Project!, w.Metadata!));
    }

    [Fact] public async Task A_custom_projectile_is_created_saved_reopened_and_its_references_stay_typed()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        Assert.Equal("talon_hybrid_bolt", id);
        Assert.Empty(w.CustomDiagnosticsFor(CustomDiagnostic.ProjectileOwner(id)).Where(d => d.Severity == CustomSeverity.Error));
        Assert.Contains(w.CustomDiagnosticsFor(CustomDiagnostic.ProjectileOwner(id)), d => d.Severity == CustomSeverity.Experimental);
        var made = await w.CreateCustomProjectileAsync("Gas EAT Rocket", "output/v1/projectile/eat-17-expendable-anti-tank");
        Assert.Equal("gas_eat_rocket", made);
        await w.EditCustomProjectileAsync(made, p => p.ImpactExplosion = "output/v1/projectile/r-36-eruptor");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id);
        var reopened = await Reopen(e, w.Project!);
        Assert.Equal(Json(w.Project!), Json(reopened));
        var assignment = reopened.CustomContent!.Assignments.Single();
        Assert.Equal((ContentRef.CustomProjectile, id, Reprimand), (assignment.Projectile.Kind, assignment.Projectile.Id, assignment.Weapon));
        // A custom projectile is never a vanilla projectile change: no projectile swap or attack output names it.
        Assert.Empty(reopened.ProjectileChanges); Assert.Null(reopened.AttackOutputChanges);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id)))!["customContent"]!["assignments"]![0]!["projectile"]!;
        Assert.Equal(("custom_projectile", id), ((string)saved["kind"]!, (string)saved["id"]!));
        Assert.NotEqual(ContentRef.Custom(id), new ContentRef(ContentRef.VanillaRound, id));
    }

    [Fact] public async Task Every_mapped_vanilla_projectile_is_offered_as_an_exact_copy_to_start_from()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var sources = w.CustomProjectileCloneSources();
        // Every projectile the SDK maps but the borrowed spare twin and 0.30's projectile donors; those Runtime cannot use as a base (unknown
        // package) say why.
        Assert.Equal(w.Metadata!.AttackOutputs!.Outputs.Count(o => o.Family == "projectile" && o.SpareTwin == null && o.Owner.Kind != "spare_row" && !o.IsDonor), sources.Count);
        Assert.Equal((107, 88), (sources.Count, sources.Count(s => s.Issue == null)));
        // A donor (an Eagle payload, an orbital shell…) has no dependencyKey in Runtime's own table, so it is never a base, and a component
        // that loads its source's package refuses it; ballistics (no package) may still come from one.
        var donor = w.Metadata.AttackOutputs.Outputs.First(o => o.IsDonor);
        Assert.Equal(31, w.Metadata.AttackOutputs.Outputs.Count(o => o.IsDonor));
        Assert.DoesNotContain(sources, s => s.Output.IsDonor);
        Assert.NotNull(w.CustomTarget.ProjectileOutputIssue(donor.SemanticId, true, w.Metadata));
        Assert.Null(w.CustomTarget.ProjectileOutputIssue(donor.SemanticId, false, w.Metadata));
        Assert.All(sources.Where(s => s.Issue != null), s => Assert.False(s.Output.Package.Known));
        Assert.Equal(AttackOutputChange.PlayerHost, sources[0].Output.Owner.Kind);
        // A clone: the vanilla projectile is its base and nothing is taken from another one. Valid and ready; noted, not warned, as a copy.
        var id = await w.CloneVanillaProjectileAsync("output/v1/projectile/las-58-talon", "LAS-58 Talon clone");
        var p = w.FindCustomProjectile(id)!;
        Assert.Equal(("output/v1/projectile/las-58-talon", (string?)null, (string?)null, (string?)null, (string?)null), (p.Base, p.Visual, p.Damage, p.ImpactExplosion, p.Ballistics));
        Assert.Equal(CustomSeverity.Pass, w.CustomState(CustomDiagnostic.ProjectileOwner(id)));
        Assert.Contains(w.CustomDiagnosticsFor(CustomDiagnostic.ProjectileOwner(id)), d => d.Severity == CustomSeverity.Info && d.Message.Contains("LAS-58 Talon"));
        // It compiles to Runtime's definition of a clone of the base row: a base and no components.
        Assert.Equal("custom_projectiles.define {\n    id = \"" + CustomIds.RuntimeProjectileId(w.Project!.ResourceId, id) + "\",\n    base = \"output/v1/projectile/las-58-talon\",\n}",
            w.CustomProjectilePreview(id));
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id);
        Assert.Null(w.BuildError);
        Assert.Matches(id + " = \\{\\s+id = \"[^\"]+\",\\s+base = \"output/v1/projectile/las-58-talon\",\\s+\\}", w.LuaPreview);
        // A projectile Runtime cannot use as a base is refused with the reason, and nothing is added.
        var refused = sources.First(s => s.Issue != null);
        Assert.Equal(refused.Issue, (await Assert.ThrowsAsync<InvalidDataException>(() => w.CloneVanillaProjectileAsync(refused.Output.SemanticId, "Refused"))).Message);
        Assert.Single(w.CustomProjectiles);
    }

    [Fact] public async Task A_weapon_fires_a_custom_projectile_through_a_guarded_carrier_swap_and_a_replacement_binding()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id);
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        // The carrier swap is an ordinary guarded patch in the generated operations, isolated like every other one.
        Assert.Contains("value=hd2.attack_output('output/v1/projectile/td-110-maelstrom-slot-2')", lua);
        Assert.Contains("add(function() return hd2.ensure({", lua);
        Assert.Contains("id='custom-carrier-", lua);
        Assert.Contains("target=hd2.weapon('SMG-32 Reprimand'):attack('primary'),", lua);
        // The definition and the binding: Runtime's own model, qualified by the mod's resource.
        Assert.Contains("pcall(require, 'hd2runtime/runtime/custom_projectiles')", lua);
        Assert.Contains("id = \"modbuilder_fixtures/custom_content/talon_hybrid_bolt\",", lua);
        Assert.Contains("base = \"output/v1/projectile/las-58-talon\",", lua);
        Assert.Contains("visual = \"output/v1/projectile/plas-1-scorcher\",", lua);
        Assert.Matches("projectile = \"talon_hybrid_bolt\",\\s+carrier = \"output/v1/projectile/td-110-maelstrom-slot-2\",\\s+weapon = \"SMG-32 Reprimand\",", lua);
        Assert.Contains("hd2.events.on('mission_started'", lua);
        Assert.Empty(Core.Scripting.LuaScriptAnalyzer.Analyze(lua, null).Where(d => d.Severity == Core.Scripting.LuaDiagnostic.Error));
        // Runtime binds one weapon per carrier: a second assignment asks first, then moves the custom projectile there.
        var limit = await Assert.ThrowsAsync<CustomAssignmentConflictException>(() => w.AssignCustomProjectileAsync("AR-23 Liberator", "primary", id));
        Assert.Equal(Reprimand, Assert.Single(limit.Displaced).Weapon);
        Assert.NotNull(w.CustomAssignment(Reprimand, "primary")); Assert.Null(w.CustomAssignment("AR-23 Liberator", "primary"));
        await w.AssignCustomProjectileAsync("AR-23 Liberator", "primary", id, discardConflicts: true);
        Assert.Null(w.CustomAssignment(Reprimand, "primary")); Assert.NotNull(w.CustomAssignment("AR-23 Liberator", "primary"));
        Assert.Null(w.BuildError);
        // Two attacks that both fire one (an assignment switched back on elsewhere) refuse the build.
        await w.ToggleCustomAssignmentAsync(w.CustomAssignment("AR-23 Liberator", "primary")!.Id);
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id);
        await w.ToggleCustomAssignmentAsync(w.CustomAssignment("AR-23 Liberator", "primary")!.Id);
        Assert.Contains(w.CustomDiagnostics(), d => d.Severity == CustomSeverity.Error && d.Area == CustomDiagnostic.Assignment);
        Assert.NotNull(w.BuildError);
        await w.AssignCustomProjectileAsync("AR-23 Liberator", "primary", null);
        Assert.Null(w.BuildError);
    }

    [Fact] public async Task Every_projectile_selector_asks_the_target_whether_its_host_can_fire_a_custom_projectile()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        var catalog = w.Metadata!.AttackOutputs!;
        Assert.True(w.CustomProjectileHostSupport(CustomProjectileHost.Player(Reprimand, "primary")).Supported);
        var support = catalog.ProjectileSources.First(s => s.HostKind == AttackOutputChange.SupportHost);
        var mounted = catalog.ProjectileSources.First(s => s.HostKind == AttackOutputChange.VehicleHost);
        foreach (var host in new[] { new CustomProjectileHost(support.HostKind, support.Weapon, support.Attack), new CustomProjectileHost(mounted.HostKind, mounted.Weapon, mounted.Attack),
            new CustomProjectileHost(CustomProjectileHost.AmmoMode, Reprimand, "alternate") })
        {
            var info = w.CustomProjectileHostSupport(host);
            Assert.False(info.Supported); Assert.Contains(w.CustomTarget.RuntimeLabel, info.Reason);
            // Nothing is assigned where the target cannot bind it, and no draft starts there.
            var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.AssignCustomProjectileAsync(host, id));
            Assert.Equal(info.Reason, refused.Message);
            await Assert.ThrowsAsync<InvalidDataException>(() => w.BeginCustomProjectileDraftAsync(host, "Refused"));
        }
        Assert.Empty(w.CustomContent!.Assignments); Assert.Single(w.CustomProjectiles);
        // A player attack whose projectile Runtime cannot write says why.
        var readOnly = catalog.ProjectileSources.First(s => s.HostKind == AttackOutputChange.PlayerHost && catalog.Host(w.Metadata.PlayerWeapons!, s.Weapon, s.Attack, out _) == null);
        Assert.False(w.CustomProjectileHostSupport(CustomProjectileHost.Player(readOnly.Weapon, readOnly.Attack)).Supported);
    }

    [Fact] public async Task A_custom_projectile_created_from_a_weapon_is_assigned_there_only_when_the_flow_completes()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var host = CustomProjectileHost.Player(Reprimand, "primary");
        var fired = w.FiredRow(host.Kind, host.Weapon, host.Role)!.SemanticId;
        var id = await w.BeginCustomProjectileDraftAsync(host, "SMG-32 Reprimand custom projectile");
        Assert.Equal("smg32_reprimand_custom", id);
        // A draft in the project from the start, starting from what the attack fires now; the attack keeps firing it.
        Assert.Equal(new CustomProjectileDraft(id, host), w.CustomDraft);
        Assert.Equal(fired, w.FindCustomProjectile(id)!.Base);
        Assert.Null(w.CustomAssignment(host));
        Assert.Equal(id, (await Reopen(e, w.Project!)).CustomContent!.Projectiles.Single().Id);
        // While nothing refers to it, its id follows its name.
        id = await w.RenameCustomProjectileAsync(id, "Gas Rocket");
        Assert.Equal(("gas_rocket", "Gas Rocket"), (id, w.FindCustomProjectile(id)!.Name));
        Assert.Equal(id, w.CustomDraft!.ProjectileId);
        // It is assigned only once it has no error.
        await w.EditCustomProjectileAsync(id, p => p.Base = "");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.CompleteCustomProjectileDraftAsync());
        Assert.Null(w.CustomAssignment(host)); Assert.NotNull(w.CustomDraft);
        await w.EditCustomProjectileAsync(id, p => { p.Base = fired; p.ImpactExplosion = "output/v1/projectile/r-36-eruptor"; });
        Assert.Equal(host, await w.CompleteCustomProjectileDraftAsync());
        Assert.Equal(ContentRef.Custom("gas_rocket"), w.CustomAssignment(host)!.Projectile);
        Assert.Null(w.CustomDraft);
        // Assigned, its id no longer follows its name.
        Assert.Equal("gas_rocket", await w.RenameCustomProjectileAsync("gas_rocket", "Gas Rocket Mk II"));
        Assert.Equal("Gas Rocket Mk II", w.FindCustomProjectile("gas_rocket")!.Name);
    }

    [Fact] public async Task Cancelling_or_deleting_a_draft_leaves_the_weapon_firing_what_it_fired()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var host = CustomProjectileHost.Player(Reprimand, "primary");
        var donor = w.Metadata!.AttackOutputs!.Donors(w.OutputHost(Reprimand, "primary", out _)!).First(d => d.Allowed && !d.CrossClass && d.Output.SemanticId != Runtime030Target.Carrier);
        await w.SetAttackOutputAsync(Reprimand, "primary", donor.Output.SemanticId);
        string Changes() => JsonSerializer.Serialize((w.Project!.AttackOutputChanges, w.Project.ProjectileChanges, w.Project.CompositionChanges), JsonStorage.Options);
        var before = Changes();
        var id = await w.BeginCustomProjectileDraftAsync(host, "Throwaway");
        Assert.Equal(host, await w.CancelCustomProjectileDraftAsync());
        Assert.Null(w.CustomDraft); Assert.Null(w.FindCustomProjectile(id));
        Assert.Equal(donor.Output.SemanticId, w.AttackOutput(Reprimand, "primary")!.Output);
        Assert.Null(w.Project!.CustomContent);
        Assert.Equal(before, Changes());
        // Deleting the draft from its editor ends the flow the same way.
        id = await w.BeginCustomProjectileDraftAsync(host, "Throwaway");
        await w.DeleteCustomProjectileAsync(id);
        Assert.Null(w.CustomDraft); Assert.Null(await w.CancelCustomProjectileDraftAsync());
        Assert.Equal(donor.Output.SemanticId, w.AttackOutput(Reprimand, "primary")!.Output);
        // A draft assigned elsewhere meanwhile is kept by Cancel (only the flow ends); its explicit id stops following its name.
        id = await w.BeginCustomProjectileDraftAsync(host, "Shared Draft");
        await w.ChangeCustomProjectileIdAsync(id, "shared_bolt");
        Assert.Equal(("shared_bolt", false), (w.CustomDraft!.ProjectileId, w.CustomDraft.IdFollowsName));
        Assert.Equal("shared_bolt", await w.RenameCustomProjectileAsync("shared_bolt", "Renamed"));
        await w.EditCustomProjectileAsync("shared_bolt", p => p.Base = "output/v1/projectile/las-58-talon");
        await w.AssignCustomProjectileAsync("AR-23 Liberator", "primary", "shared_bolt");
        Assert.Equal(host, await w.CancelCustomProjectileDraftAsync());
        Assert.NotNull(w.FindCustomProjectile("shared_bolt")); Assert.Null(w.CustomDraft);
        Assert.Equal(donor.Output.SemanticId, w.AttackOutput(Reprimand, "primary")!.Output);
    }

    [Fact] public async Task A_copy_made_from_a_weapon_replaces_its_custom_projectile_only_when_completed_and_conflicts_are_asked_there()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var host = CustomProjectileHost.Player(Reprimand, "primary");
        var bolt = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        await w.AssignCustomProjectileAsync(host, bolt);
        var copy = await w.BeginCustomProjectileDraftAsync(host, "Talon Hybrid Bolt copy", bolt);
        Assert.Equal(("talon_hybrid_bolt_copy", bolt), (copy, w.CustomDraft!.CopyOf));
        var (source, made) = (w.FindCustomProjectile(bolt)!, w.FindCustomProjectile(copy)!);
        Assert.Equal((source.Base, source.Visual, source.Damage, source.Ballistics), (made.Base, made.Visual, made.Damage, made.Ballistics));
        Assert.Equal(ContentRef.Custom(bolt), w.CustomAssignment(host)!.Projectile);
        await w.CompleteCustomProjectileDraftAsync();
        Assert.Equal(ContentRef.Custom(copy), w.CustomAssignment(host)!.Projectile);
        Assert.Equal(2, w.CustomProjectiles.Count);
        // Created from another attack while this one fires a custom projectile: completing asks first (one attack at a time).
        var other = CustomProjectileHost.Player("AR-23 Liberator", "primary");
        var third = await w.BeginCustomProjectileDraftAsync(other, "Liberator Bolt", bolt);
        var conflict = await Assert.ThrowsAsync<CustomAssignmentConflictException>(() => w.CompleteCustomProjectileDraftAsync());
        Assert.Equal(Reprimand, Assert.Single(conflict.Displaced).Weapon);
        Assert.Equal(third, w.CustomDraft!.ProjectileId);
        Assert.Equal(other, await w.CompleteCustomProjectileDraftAsync(discardConflicts: true));
        Assert.Null(w.CustomAssignment(host)); Assert.Equal(ContentRef.Custom(third), w.CustomAssignment(other)!.Projectile);
    }

    [Fact] public async Task A_vanilla_swap_and_a_custom_projectile_on_one_attack_are_resolved_where_the_choice_is_made()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        var donor = w.Metadata!.AttackOutputs!.Donors(w.OutputHost(Reprimand, "primary", out _)!).First(d => d.Allowed && !d.CrossClass && d.Output.SemanticId != Runtime030Target.Carrier);
        await w.SetAttackOutputAsync(Reprimand, "primary", donor.Output.SemanticId);
        var conflict = await Assert.ThrowsAsync<CustomAssignmentConflictException>(() => w.AssignCustomProjectileAsync(Reprimand, "primary", id));
        Assert.Equal((1, 0), (conflict.Swaps, conflict.Edits));
        Assert.Null(w.CustomAssignment(Reprimand, "primary"));
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id, discardConflicts: true);
        Assert.NotNull(w.CustomAssignment(Reprimand, "primary"));
        Assert.True(w.Project.AttackOutputChanges is null or { Count: 0 }); Assert.Empty(w.Project.ProjectileChanges);
        // Choosing a vanilla projectile again replaces the custom one.
        await w.SetAttackOutputAsync(Reprimand, "primary", donor.Output.SemanticId);
        Assert.Null(w.CustomAssignment(Reprimand, "primary"));
        // Resetting the whole weapon resets its custom projectile too (the custom projectile itself stays).
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id, discardConflicts: true);
        await w.ResetWeaponsAsync(Reprimand);
        Assert.Null(w.CustomAssignment(Reprimand, "primary")); Assert.Single(w.CustomProjectiles);
    }

    [Fact] public async Task A_referenced_custom_projectile_is_never_deleted_or_renamed_behind_its_references()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var id = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        var other = await w.CreateCustomProjectileAsync("Plain Bolt", "output/v1/projectile/las-58-talon");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", id);
        var refused = await Assert.ThrowsAsync<CustomReferenceException>(() => w.DeleteCustomProjectileAsync(id));
        Assert.Equal(CustomNodeKind.Weapon, Assert.Single(refused.Uses).From.Kind);
        Assert.Equal(2, w.CustomProjectiles.Count);
        // Renaming the id moves every reference with it.
        await w.ChangeCustomProjectileIdAsync(id, "hybrid_bolt");
        Assert.Equal("hybrid_bolt", w.CustomAssignment(Reprimand, "primary")!.Projectile.Id);
        // Deleting with a replacement moves the uses; deleting with removal drops them.
        await w.DeleteCustomProjectileAsync("hybrid_bolt", ContentRef.Custom(other));
        Assert.Equal(other, w.CustomAssignment(Reprimand, "primary")!.Projectile.Id);
        await w.DeleteCustomProjectileAsync(other, removeReferences: true);
        Assert.Null(w.CustomAssignment(Reprimand, "primary")); Assert.Empty(w.CustomProjectiles);
    }

    [Fact] public async Task The_dependency_graph_knows_what_uses_what()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var sentry = await w.CreateCustomStratagemFromTemplateAsync("SimpleSentry");
        var gas = await w.CreateCustomStratagemFromTemplateAsync("EAT17GExample");
        var bolt = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", bolt);
        // A delivered weapon's round may reference a custom projectile in the model (refused by this Runtime target, but tracked).
        await w.EditCustomStratagemAsync(sentry, s => (s.Delivery.Sentry!.Weapon ??= new()).Projectile = ContentRef.Custom(bolt));
        var graph = w.CustomGraph();
        Assert.Contains(graph.Uses(CustomNodeKind.Stratagem, gas), u => u.Role == "icon" && u.To == new CustomNode(CustomNodeKind.Image, w.FindCustomStratagem(gas)!.Icon));
        Assert.Contains(graph.Uses(CustomNodeKind.Stratagem, gas), u => u.Role == "donor" && u.To.Id == "EAT-17 Expendable Anti-Tank");
        Assert.Contains(graph.Uses(CustomNodeKind.Stratagem, gas), u => u.Role == "payload" && u.To == new CustomNode(CustomNodeKind.VanillaExplosion, "Orbital Gas Strike"));
        Assert.Contains(graph.Uses(CustomNodeKind.Projectile, bolt), u => u.Role == "base" && u.To.Kind == CustomNodeKind.VanillaProjectile);
        var users = graph.UsedBy(CustomNodeKind.Projectile, bolt);
        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.From == new CustomNode(CustomNodeKind.Weapon, Reprimand + "#primary") && u.Role == "fires");
        Assert.Contains(users, u => u.From == new CustomNode(CustomNodeKind.Stratagem, sentry) && u.Role == "round");
        Assert.Contains(w.CustomDiagnosticsFor(CustomDiagnostic.StratagemOwner(sentry)), d => d.Severity == CustomSeverity.Error && d.Field == "payload.weapon.projectile");
        // Removing the projectile with its uses clears the sentry's round too.
        await w.DeleteCustomProjectileAsync(bolt, removeReferences: true);
        Assert.Null(w.FindCustomStratagem(sentry)!.Delivery.Sentry!.Weapon?.Projectile);
    }

    [Fact] public async Task The_export_holds_the_registration_compiled_icons_their_source_PNGs_and_the_newer_Runtime_requirement()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var gas = await w.CreateCustomStratagemFromTemplateAsync("GasBarrageExample");
        await w.ImportCustomIconAsync(gas, Fixture("art.png"));
        var bolt = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", bolt);
        await w.ExportAsync();
        var entries = Entries(w.LastExport!);
        Assert.Equal(PackagedFiles.Generated.Append("images/" + gas + ".png").Order(StringComparer.Ordinal), entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(File.ReadAllBytes(Fixture("art.png")), entries["images/" + gas + ".png"]);
        var manifest = JsonNode.Parse(entries["hd2runtime.json"])!;
        // Bound to the pinned 0.30.3, the export requires it (custom content alone requires the custom stratagem API's 0.30.0: Runtime0302Tests).
        Assert.Equal(SdkPin.Version, (string)manifest["requires"]!["hd2runtime"]!["min_version"]!);
        Assert.Contains("HD2Runtime " + SdkPin.Version + "+", (string)JsonNode.Parse(entries["manifest.json"])!["Description"]!);
        var report = JsonNode.Parse(entries["build-report.json"])!["custom_content"]!;
        Assert.Equal(("hd2runtime-0.30", true), ((string)report["target"]!, (bool)report["experimental"]!));
        Assert.Equal([gas], report["stratagems"]!.AsArray().Select(x => (string)x!)); Assert.Equal([bolt], report["projectiles"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("converted to masks", (string)report["image_sources"]![0]!["prepared"]!);
        // The archive: the addon, the image record and the icon's texture and GUI material, under the mod's own names.
        var archive = entries["mod/" + GameplayArchive.ArchiveName]; var gpu = entries["mod/" + GameplayArchive.ArchiveName + ".gpu_resources"];
        var resources = ReadArchive(archive, gpu);
        var resource = w.Project!.ResourceId;
        var name = GameplayArchive.ResourceHash(resource + "/images/" + gas);
        var (main, data, _) = CustomIconCompiler.Texture(File.ReadAllBytes(Fixture("art.png")), CustomImage.Auto);
        Assert.Equal(main, resources[(CustomIconCompiler.TextureType, name)].Main); Assert.Equal(data, resources[(CustomIconCompiler.TextureType, name)].Gpu);
        Assert.Equal(CustomIconCompiler.Material(name), resources[(CustomIconCompiler.MaterialType, name)].Main);
        var record = System.Text.Encoding.UTF8.GetString(resources[(ResourceArchive.LuaType, GameplayArchive.ResourceHash(resource + "/hd2runtime_images"))].Main[8..]);
        Assert.Contains("[\"" + gas + "\"]={source=\"images/" + gas + ".png\"", record);
        var addon = System.Text.Encoding.UTF8.GetString(resources[(ResourceArchive.LuaType, GameplayArchive.ResourceHash(resource))].Main[8..]);
        Assert.StartsWith("-- HD2-Addon: " + resource, addon);
        Assert.Contains("local required_runtime='" + SdkPin.Version + "'", addon); Assert.Contains("local function at_least(have,need)", addon);
        // The bound SDK stays the mod's declared SDK (Runtime's legacy acknowledgements read x,y,z): never the newer Runtime it requires.
        Assert.Contains("local x,y,z=version('" + w.Project!.SdkVersion + "')", addon); Assert.DoesNotContain("local minimum=", addon);
        Assert.Contains("register_custom_stratagem('" + gas + "', function()", addon);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(entries["src/addon.lua"]), w.LuaPreview);
        // Deterministic: the same project exports the same bytes.
        var first = await File.ReadAllBytesAsync(w.LastExport!); await w.ExportAsync();
        Assert.Equal(first, await File.ReadAllBytesAsync(w.LastExport!));
    }
    // {(type, name): (main, gpu)} of an archive in the resource archive layout (ResourceArchive / hd2_archive.make_resource_archive).
    private static Dictionary<(ulong, ulong), (byte[] Main, byte[] Gpu)> ReadArchive(byte[] archive, byte[] gpu)
    {
        Assert.Equal(0xF0000011U, BitConverter.ToUInt32(archive, 0));
        int types = BitConverter.ToInt32(archive, 4), files = BitConverter.ToInt32(archive, 8), start = 72 + 32 * types;
        var found = new Dictionary<(ulong, ulong), (byte[], byte[])>();
        for (var i = 0; i < files; i++)
        {
            var row = start + 80 * i;
            ulong name = BitConverter.ToUInt64(archive, row), type = BitConverter.ToUInt64(archive, row + 8);
            int offset = (int)BitConverter.ToUInt64(archive, row + 16), gpuOffset = (int)BitConverter.ToUInt64(archive, row + 32);
            int length = BitConverter.ToInt32(archive, row + 56), gpuLength = BitConverter.ToInt32(archive, row + 64);
            found[(type, name)] = (archive[offset..(offset + length)], gpu[gpuOffset..(gpuOffset + gpuLength)]);
        }
        return found;
    }

    [Fact] public async Task A_project_without_custom_content_exports_exactly_as_before_even_after_custom_content_came_and_went()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "720", false);
        var format = w.Project!.FormatVersion; var lua = w.LuaPreview;
        await w.ExportAsync(); var before = await File.ReadAllBytesAsync(w.LastExport!);
        Assert.True(format < 14); Assert.True(ProjectIdentity.RequiredFormat(w.Project) < 14);
        Assert.DoesNotContain("customContent", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)));
        var entries = Entries(w.LastExport!);
        Assert.Equal(PackagedFiles.Generated.Order(StringComparer.Ordinal), entries.Keys.Order(StringComparer.Ordinal));
        Assert.Null(JsonNode.Parse(entries["build-report.json"])!["custom_content"]);
        Assert.Equal(w.Project.SdkVersion, (string)JsonNode.Parse(entries["hd2runtime.json"])!["requires"]!["hd2runtime"]!["min_version"]!);
        var id = await w.CreateCustomStratagemFromTemplateAsync("SimpleSupportDrop");
        Assert.NotEqual(lua, w.LuaPreview);
        await w.DeleteCustomStratagemAsync(id);
        Assert.Null(w.Project.CustomContent); Assert.Equal(lua, w.LuaPreview);
        await w.ExportAsync();
        Assert.Equal(before, await File.ReadAllBytesAsync(w.LastExport!));
    }

    // The custom content export fixture: every template, a custom projectile and the weapon that fires it. Written to custom-content-exports/
    // next to the test binaries (and to HD2_CUSTOM_EXPORT_OUT when set), never with the release export fixtures, which HD2Runtime 0.28.1's
    // validator checks: tools/custom-content/validate-runtime.py loads these in the HD2Runtime 0.30.0 tree's own Lua harness.
    [Fact] public async Task The_custom_content_export_fixture_is_written_for_Runtime_validation()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e, "Custom Templates");
        foreach (var template in CustomTemplates.Stratagems) await w.CreateCustomStratagemFromTemplateAsync(template.Key);
        var bolt = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", bolt);
        Assert.Null(w.BuildError);
        await w.ExportAsync();
        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "custom-content-exports"), Environment.GetEnvironmentVariable("HD2_CUSTOM_EXPORT_OUT") }.OfType<string>())
        { Directory.CreateDirectory(dir); File.Copy(w.LastExport!, Path.Combine(dir, "C1-custom-templates.zip"), true); }
        var entries = Entries(w.LastExport!);
        Assert.Equal(CustomTemplates.Stratagems.Count, entries.Keys.Count(k => k.StartsWith("images/", StringComparison.Ordinal)));
    }

    [Fact] public async Task Duplicating_a_project_copies_its_custom_content_and_a_newer_section_is_refused()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.CreateCustomStratagemFromTemplateAsync("SimpleSentry");
        var bolt = await w.CreateCustomProjectileFromTemplateAsync("talon_hybrid");
        await w.AssignCustomProjectileAsync(Reprimand, "primary", bolt);
        await w.DuplicateAsync(w.Project!.Id, new("Custom Copy", "Tests", "mods/tests/custom_copy", "0.1.0"));
        var copy = w.Project!;
        Assert.Equal("custom_copy", copy.ResourceId.Split('/')[^1]);
        Assert.Equal(["quick_mg_sentry"], copy.CustomContent!.Stratagems.Select(s => s.Id)); Assert.Single(copy.CustomContent.Images);
        Assert.Equal(14, copy.FormatVersion);
        Assert.Contains("tests/custom_copy/talon_hybrid_bolt", w.LuaPreview);
        // A custom content section saved by a newer ModBuilder is refused with the reason, not dropped.
        var json = JsonNode.Parse(await File.ReadAllTextAsync(e.Paths.ProjectFile(copy.Id)))!;
        json["customContent"]!["version"] = CustomContentSection.CurrentVersion + 1;
        await File.WriteAllTextAsync(e.Paths.ProjectFile(copy.Id), json.ToJsonString());
        Assert.Contains("newer", (await Assert.ThrowsAsync<InvalidDataException>(() => e.Store.LoadAsync(copy.Id))).Message);
        // A project that has custom content must be format 14.
        json["customContent"]!["version"] = CustomContentSection.CurrentVersion; json["formatVersion"] = 13;
        await File.WriteAllTextAsync(e.Paths.ProjectFile(copy.Id), json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => e.Store.LoadAsync(copy.Id));
    }
}
