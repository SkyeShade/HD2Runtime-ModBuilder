using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.24.0: Booster authoring (hd2.booster) and the expanded support-weapon catalog (bundled current SDK).
public sealed class Runtime024Tests
{
    private const string Pods = "Armed Resupply Pods", Infusion = "Experimental Infusion", Extinguishers = "Integrated Extinguishers";
    private static readonly string[] DeliveryResolved = ["MG-43 Machine Gun", "M-105 Stalwart", "MG-206 Heavy Machine Gun", "CQC-20 Breaching Hammer"];
    private static readonly string[] StillBlocked = ["EAT-17 Expendable Anti-Tank", "LAS-98 Laser Cannon", "B/FLAM-80 Cremator", "CQC-72 Entrenchment Tool"];
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string resource = "mods/tests/boosters")
    {
        // Pinned to the published 0.24.0 SDK (the bundled offline SDK moves with each release).
        var sdk = await SdkFixtures.Install(e, "0.24.0");
        var w = e.Workspace(); await w.CreateAsync(new("Boosters", "Tests", resource, "0.1.0"), sdk); return w;
    }
    private static BoosterCatalog Boosters(BuilderWorkspace w) => w.Metadata!.Entities!.Boosters!;
    private static EntityField Booster(BuilderWorkspace w, string booster, string id) => Boosters(w).FieldInstances.Single(f => f.Target.Booster == booster && f.SemanticFieldId == id);
    private static SupportField Support(BuilderWorkspace w, string weapon, string id) => w.Metadata!.SupportAuthoring!.FieldInstances.First(f => f.SupportWeapon == weapon && f.SemanticFieldId == id);
    private static JsonNode BoosterJson() => JsonNode.Parse(SdkFixtures.Entry("0.24.0", BoosterAuthoringReader.FileName))!;

    [Fact] public async Task Booster_catalog_publishes_every_booster_and_only_five_fields()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = Boosters(w);
        Assert.Equal("0.24.0", w.Metadata!.Version); Assert.Equal(20, c.Boosters.Length); Assert.Equal(5, c.FieldInstances.Length);
        Assert.Equal([Pods, Infusion], c.Boosters.Where(b => b.Writable).Select(b => b.Name).Order(StringComparer.Ordinal));
        Assert.Equal(7, c.Boosters.Count(b => b.Identity.Status == "RESOLVED")); Assert.Equal(11, c.Boosters.Count(b => b.Identity.Status == "CANDIDATES"));
        // Every booster write requires allow_unverified_effect; only fields of writable, uniquely resolved boosters exist.
        Assert.All(c.FieldInstances, f => { Assert.Equal("allow_unverified_effect", f.Acknowledgement); Assert.Equal("booster", f.Target.Resource); Assert.True(f.Editable); });
        Assert.Equal(5, w.Metadata.Entities!.AllFields.Count(f => f.Target.Resource == "booster"));
    }
    [Fact] public async Task Booster_category_appears_for_0240_with_yellow_styling()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var item = Navigation.Build(w.Metadata, null).Single(i => i.Page == "boosters");
        Assert.Equal("Boosters", item.Label); Assert.Equal(20, item.Count); Assert.Equal("cat-booster", item.CssClass); Assert.Equal(StratagemCategories.BoosterCssClass, item.CssClass);
        // Support blue, Offensive red, Defensive green, Boosters yellow.
        var css = File.ReadAllText(Path.Combine(Root(), "HD2RuntimeGUI", "wwwroot", "app.css"));
        Assert.Contains("--booster-category-yellow:#f2c94c;", css); Assert.Contains("--cat-booster:var(--booster-category-yellow);", css);
        // Brand and category tokens are separate, so a brand change never alters the Booster category colour.
        Assert.Contains("--brand-yellow:#fdd00e;", css); Assert.Contains("--accent:var(--brand-yellow);", css); Assert.Contains(".cat-booster { --cat:var(--cat-booster) }", css);
        Assert.Equal(["cat-support", "cat-offensive", "cat-defensive"], StratagemCategories.All.Select(c => c.CssClass));
    }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI.slnx")) || Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI", "wwwroot"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
    [Theory] [InlineData("0.23.2")] [InlineData("0.23.0")] [InlineData("0.22.1")]
    public async Task Booster_category_is_absent_for_older_sdks(string version)
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, version);
        Assert.Null(sdk.Entities?.Boosters); Assert.DoesNotContain(Navigation.Build(sdk, null), i => i.Page == "boosters");
        Assert.DoesNotContain(sdk.Entities?.AllFields ?? [], f => f.Target.Resource == "booster");
    }
    [Fact] public async Task Armed_resupply_pods_expose_the_turret_fire_rate_and_magazine()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var pods = Boosters(w).Find(Pods)!;
        Assert.Equal(["deployed_entity"], pods.Targets); Assert.Equal("RESOLVED", pods.Identity.Status);
        var rate = Booster(w, Pods, "weapon.fire_rate"); var capacity = Booster(w, Pods, "magazine.capacity");
        Assert.Equal(640, rate.CurrentDefault.GetDouble()); Assert.Equal("number", rate.Type); Assert.Equal("rpm", rate.Unit);
        Assert.Equal(140, capacity.CurrentDefault.GetInt32()); Assert.Equal("integer", capacity.Type);
        Assert.All([rate, capacity], f => { Assert.Equal("deployed_entity", f.Target.Path); Assert.False(f.AllowSharedRequired); Assert.Equal("structural_chain_exact_fingerprint", f.Evidence.Tier); });
        Assert.NotEqual(rate.OperationGroup, capacity.OperationGroup); Assert.Equal(rate.PlanGroup, capacity.PlanGroup);
        Assert.Contains(pods.Relationships, r => r.Kind == "stratagem_booster_entry" && r.TurretProjectileShared == true && r.StratagemSemanticId != null);
        Assert.Contains(pods.BlockedFields, b => b.Field == "turret projectile / damage");

        await w.SetEntityAsync(rate.InstanceKey, "900"); await w.SetEntityAsync(capacity.InstanceKey, "300");
        // The unverified-effect opt-in is implicit: the booster edit builds without an acknowledgement.
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        // Two Runtime operation groups on one target: one hd2.plan, matching Runtime's ArmedResupplyTurret example.
        Assert.Contains("plan={", lua); Assert.Equal(2, Count(lua, "target=hd2.booster('Armed Resupply Pods'):deployed_entity(),"));
        Assert.Equal(2, Count(lua, "allow_unverified_effect=true,")); Assert.DoesNotContain("allow_shared", lua);
        Assert.Contains("field=hd2.fields.weapon.fire_rate,\n", lua); Assert.Contains("expect=640,", lua); Assert.Contains("value=900,", lua);
        Assert.Contains("field=hd2.fields.magazine.capacity,\n", lua); Assert.Contains("expect=140,", lua); Assert.Contains("value=300,", lua);
    }
    private static int Count(string text, string part) { var n = 0; for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++; return n; }
    [Fact] public async Task Experimental_infusion_requires_allow_shared_and_allow_unverified_effect()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var infusion = Boosters(w).Find(Infusion)!;
        Assert.Equal(["status_effect"], infusion.Targets);
        var fields = Boosters(w).FieldInstances.Where(f => f.Target.Booster == Infusion).ToArray();
        Assert.Equal(["status.duration", "status.incoming_damage_scale", "status.strength"], fields.Select(f => f.SemanticFieldId).Order(StringComparer.Ordinal));
        Assert.All(fields, f => { Assert.True(f.AllowSharedRequired); Assert.True(f.Shared); Assert.False(f.ReviewedScopeComplete); Assert.Equal("allow_unverified_effect", f.Acknowledgement); });
        Assert.Equal(1.1f, Booster(w, Infusion, "status.strength").CurrentDefault.GetSingle()); Assert.Equal(0.9f, Booster(w, Infusion, "status.incoming_damage_scale").CurrentDefault.GetSingle());
        Assert.All(Boosters(w).Raw(fields[0].InstanceKey)!.Acknowledgements, a => Assert.Contains(a, new[] { "allow_shared", "allow_unverified_effect" }));

        await w.SetEntityAsync(Booster(w, Infusion, "status.incoming_damage_scale").InstanceKey, "0.8");
        // Both opt-ins are implicit: the shared booster edit builds before any acknowledgement.
        Assert.Null(w.BuildError); Assert.Contains("allow_shared=true,", w.LuaPreview); Assert.Contains("allow_unverified_effect=true,", w.LuaPreview);
        await w.SetBoosterAcknowledgedAsync(Infusion, true); Assert.Null(w.BuildError);
        Assert.True(BuilderWorkspace.BoosterAcknowledged(w.Project, w.Metadata!.Entities!, Infusion));
        var lua = w.LuaPreview;
        Assert.Contains("target=hd2.booster('Experimental Infusion'):status_effect(),", lua); Assert.Contains("allow_shared=true,", lua); Assert.Contains("allow_unverified_effect=true,", lua);
        Assert.Contains("patch={", lua); Assert.Contains("expect=0.9,", lua); Assert.Contains("value=0.8,", lua);
        // A second field of the booster joins the same Runtime operation group: one transaction.
        await w.SetEntityAsync(Booster(w, Infusion, "status.strength").InstanceKey, "1.2"); Assert.Null(w.BuildError);
        Assert.Contains("transaction={", w.LuaPreview); Assert.Contains("{field=hd2.fields.status.strength,expect=1.1,value=1.2},", w.LuaPreview);
        // Revoking the recorded acknowledgement neither blocks the build nor drops the Runtime flags.
        await w.SetBoosterAcknowledgedAsync(Infusion, false); Assert.Null(w.BuildError);
        Assert.False(BuilderWorkspace.BoosterAcknowledged(w.Project, w.Metadata!.Entities!, Infusion));
        Assert.Contains("allow_shared=true,", w.LuaPreview); Assert.Contains("allow_unverified_effect=true,", w.LuaPreview);
    }
    [Fact] public async Task Blocked_and_unresolved_boosters_are_listed_without_controls()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = Boosters(w);
        var ext = c.Find(Extinguishers)!;
        Assert.False(ext.Writable); Assert.Empty(ext.FieldInstanceKeys); Assert.Empty(ext.Targets); Assert.Equal("EFFECT_CATEGORY", ext.Identity.Status);
        Assert.Contains(ext.Relationships, r => r.Kind == "susceptibility_gate" && r.GatedSusceptibilities == 2);
        Assert.Contains(ext.BlockedFields, b => b.Field == "susceptibility override" && b.Reason.Contains("not proven", StringComparison.Ordinal));
        // Candidate enum values stay candidates; nothing is guessed.
        var vitality = c.Find("Vitality Enhancement")!;
        Assert.Equal("CANDIDATES", vitality.Identity.Status); Assert.Null(vitality.Identity.EnumValue); Assert.Equal([1, 4], vitality.Identity.EnumValueCandidates);
        foreach (var b in c.Boosters.Where(b => !b.Writable))
        {
            Assert.NotEmpty(b.BlockedFields); Assert.DoesNotContain(c.FieldInstances, f => f.Target.Booster == b.Name);
            await Assert.ThrowsAsync<InvalidDataException>(() => w.SetBoosterAcknowledgedAsync(b.Name, true));
        }
        Assert.Throws<InvalidDataException>(() => new EntityChangeService().Create(w.Metadata!, "booster:integrated-extinguishers:status_effect:status.strength", "1"));
    }
    [Fact] public async Task Booster_changes_reset_persist_and_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var rate = Booster(w, Pods, "weapon.fire_rate"); var capacity = Booster(w, Pods, "magazine.capacity"); var strength = Booster(w, Infusion, "status.strength");
        await w.SetEntityAsync(rate.InstanceKey, "900"); await w.SetEntityAsync(capacity.InstanceKey, "300"); await w.SetEntityAsync(strength.InstanceKey, "1.3");
        await w.SetBoosterAcknowledgedAsync(Pods, true); await w.SetBoosterAcknowledgedAsync(Infusion, true); Assert.Null(w.BuildError);
        Assert.All(w.Project!.EntityChanges, c => { Assert.Equal("booster", c.Resource); Assert.StartsWith("booster:", c.InstanceKey); });
        Assert.Equal(6, w.Project.FormatVersion);
        // Persisted and reloaded unchanged; export is deterministic.
        await w.OpenAsync(w.Project.Id); Assert.Equal(3, w.Project!.EntityChanges.Count); Assert.Null(w.BuildError);
        var lua = w.LuaPreview; await w.ExportAsync(); var zip = File.ReadAllBytes(w.LastExport!); await w.ExportAsync();
        Assert.Equal(zip, File.ReadAllBytes(w.LastExport!)); Assert.Equal(lua, w.LuaPreview); Assert.DoesNotContain("0x", lua);
        using (var archive = ZipFile.OpenRead(w.LastExport!))
            Assert.Contains(archive.Entries, x => { using var r = new StreamReader(x.Open()); return r.ReadToEnd().Contains("hd2.booster('Armed Resupply Pods')", StringComparison.Ordinal); });
        // Per-field reset, then per-booster reset.
        await w.ResetEntityAsync(instance: capacity.InstanceKey); Assert.DoesNotContain("magazine.capacity", w.LuaPreview); Assert.Contains("weapon.fire_rate", w.LuaPreview);
        await w.ResetEntityAsync("booster", Pods); Assert.DoesNotContain(Pods, w.LuaPreview); Assert.Contains(Infusion, w.LuaPreview);
        await w.ResetEntityAsync("booster", Infusion); Assert.Empty(w.Project.EntityChanges); Assert.DoesNotContain("hd2.booster", w.LuaPreview);
    }
    [Fact] public async Task Saved_booster_changes_reject_raw_or_unknown_targets()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetEntityAsync(Booster(w, Pods, "weapon.fire_rate").InstanceKey, "900");
        var saved = w.Project!.EntityChanges.Single();
        foreach (var bad in new[] { saved with { Entity = "0xCF5F176E0E322BE1\n" }, saved with { Path = "booster" }, saved with { Zone = "zone_0" } })
        { w.Project.EntityChanges = [bad]; await Assert.ThrowsAsync<InvalidDataException>(() => e.Store.SaveAsync(w.Project)); }
    }

    [Theory] [InlineData("candidate-writable")] [InlineData("no-effect-ack")] [InlineData("shared-mismatch")] [InlineData("unknown-path")]
    [InlineData("summary")] [InlineData("blocked-without-reason")] [InlineData("api")] [InlineData("unknown-tier")] [InlineData("cross-target-group")]
    public void Malformed_booster_catalogs_are_rejected(string mode)
    {
        var j = BoosterJson(); var fields = j["fieldInstances"]!.AsArray(); var boosters = j["boosters"]!.AsArray();
        JsonNode Field(string id) => fields.First(f => (string)f!["instanceKey"]! == id)!;
        JsonNode B(string name) => boosters.First(b => (string)b!["name"]! == name)!;
        switch (mode)
        {
            case "candidate-writable": B(Pods)["identity"]!["status"] = "CANDIDATES"; break;
            case "no-effect-ack": Field("booster:armed-resupply-pods:deployed_entity:weapon.fire_rate")["acknowledgements"] = new JsonArray(); break;
            case "shared-mismatch": Field("booster:experimental-infusion:status_effect:status.strength")["acknowledgements"] = new JsonArray("allow_unverified_effect"); break;
            case "unknown-path": Field("booster:armed-resupply-pods:deployed_entity:weapon.fire_rate")["target"]!["path"] = "booster"; break;
            case "summary": j["summary"]!["fieldInstances"] = 6; break;
            case "blocked-without-reason": B(Extinguishers)["blockedFields"] = new JsonArray(); break;
            case "api": Field("booster:armed-resupply-pods:deployed_entity:weapon.fire_rate")["apiFieldConstant"] = "os.execute('x')"; break;
            case "unknown-tier": Field("booster:armed-resupply-pods:deployed_entity:weapon.fire_rate")["evidence"]!["tier"] = "rumoured"; break;
            case "cross-target-group": Field("booster:armed-resupply-pods:deployed_entity:magazine.capacity")["operation"]!["transactionGroupingKey"] =
                Field("booster:experimental-infusion:status_effect:status.strength")["operation"]!["transactionGroupingKey"]!.GetValue<string>(); break;
        }
        Assert.Throws<InvalidDataException>(() => BoosterAuthoringReader.Read(Encoding.UTF8.GetBytes(j.ToJsonString()), "0.24.0"));
    }
    [Fact] public void Unknown_booster_contract_is_unsupported()
    {
        var j = BoosterJson(); j["contract"] = "hd2runtime.booster.guarded_authoring.v2";
        Assert.Throws<UnsupportedSdkException>(() => BoosterAuthoringReader.Read(Encoding.UTF8.GetBytes(j.ToJsonString()), "0.24.0"));
    }
    [Fact] public async Task A_0240_archive_without_booster_capabilities_is_rejected_and_not_activated()
    {
        using var e = new TestEnvironment(); using var output = new MemoryStream(); output.Write(SdkFixtures.Archive("0.24.0"));
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true)) zip.GetEntry(BoosterAuthoringReader.FileName)!.Delete();
        e.GitHub.Archive = output.ToArray();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.24.0", e.GitHub.Archive)));
        Assert.Equal(BoosterAuthoringReader.FileName, error.Data[SdkCache.MissingFileKey]); Assert.Equal("0.5.1", (await e.Cache.GetCurrentAsync()).Version);
    }
    [Fact] public async Task A_stale_cache_without_boosters_is_completed_only_from_an_identical_bundle()
    {
        // The bundled SDK (0.26.0) completes its own version offline; an older incomplete cache needs the published release.
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, "0.26.0");
        File.Delete(Path.Combine(Path.GetDirectoryName(e.Paths.SdkFile("0.26.0"))!, BoosterAuthoringReader.FileName)); e.GitHub.Offline = true;
        var sdk = await new SdkCache(e.Paths, e.Reader, e.GitHub).GetCurrentAsync();
        Assert.Equal(20, sdk.Entities!.Boosters!.Boosters.Length);
        using var old = new TestEnvironment(); await SdkFixtures.Install(old, "0.24.0");
        File.Delete(Path.Combine(Path.GetDirectoryName(old.Paths.SdkFile("0.24.0"))!, BoosterAuthoringReader.FileName)); old.GitHub.Offline = true;
        await Assert.ThrowsAsync<IncompleteSdkCacheException>(() => new SdkCache(old.Paths, old.Reader, old.GitHub).GetCurrentAsync());
    }

    [Fact] public async Task Expanded_support_catalog_publishes_new_field_families_with_their_guards()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.SupportAuthoring!;
        Assert.Equal(970, c.FieldInstances.Length); Assert.Equal(31, c.Weapons.Count(x => x.Writable));
        var byId = c.FieldInstances.GroupBy(f => f.SemanticFieldId).ToDictionary(g => g.Key, g => g.ToArray());
        Assert.Equal(14, byId["reload.duration"].Length); Assert.All(byId["reload.duration"], f => { Assert.Equal("allow_unverified_effect", f.Operation.Acknowledgement); Assert.False(f.Operation.AllowSharedRequired); });
        Assert.Equal(19, byId["projectile.penetration_slowdown"].Length); Assert.Equal(5, byId["projectile.lifetime"].Length);
        Assert.All(byId["projectile.penetration_slowdown"].Concat(byId["projectile.lifetime"]), f => { Assert.True(f.Operation.AllowSharedRequired); Assert.Null(f.Operation.Acknowledgement); });
        Assert.Null(Assert.Single(byId["windup.wind_up_seconds"]).Operation.Acknowledgement);
        Assert.Equal("allow_unverified_effect", Assert.Single(byId["windup.wind_down_seconds"]).Operation.Acknowledgement);
        Assert.All(c.FieldInstances, f => Assert.True(f.Writable && !f.ReadOnly));
        // Blocked declarations stay blocked: a zero native lifetime is not tunable and is not published as a field.
        var mg43 = c.Weapons.Single(x => x.Name == "MG-43 Machine Gun");
        Assert.Contains(mg43.BlockedFields, b => b.Field == "projectile.lifetime"); Assert.DoesNotContain(byId["projectile.lifetime"], f => f.SupportWeapon == mg43.Name);
    }
    [Fact] public async Task Unverified_support_fields_build_without_acknowledgement_and_emit_allow_unverified_effect()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var reload = Support(w, "MG-43 Machine Gun", "reload.duration");
        await w.SetSupportAsync(reload.InstanceKey, JsonSerializer.Serialize(reload.Value.Baseline.GetDouble() + 1));
        // The unverified-effect opt-in is implicit: the edit builds without an acknowledgement.
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("target=hd2.support_weapon('MG-43 Machine Gun'),", lua); Assert.Contains("allow_unverified_effect=true,", lua);
        Assert.Contains("field=hd2.fields.reload.duration,", lua); Assert.DoesNotContain("allow_shared", lua);
        // A value change, or a reset and re-edit, still builds with the flag.
        await w.SetSupportAsync(reload.InstanceKey, JsonSerializer.Serialize(reload.Value.Baseline.GetDouble() + 2)); Assert.Null(w.BuildError);
        await w.ResetSupportAsync(instance: reload.InstanceKey); await w.SetSupportAsync(reload.InstanceKey, JsonSerializer.Serialize(reload.Value.Baseline.GetDouble() + 1));
        Assert.Null(w.BuildError); Assert.Contains("allow_unverified_effect=true,", w.LuaPreview);
        await w.ResetSupportAsync(); var windUp = Support(w, "M-1000 Maxigun", "windup.wind_up_seconds");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportEffectAcknowledgedAsync(windUp.InstanceKey, true));
    }
    [Fact] public async Task Shared_projectile_fields_still_require_allow_shared()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var f = w.Metadata!.SupportAuthoring!.FieldInstances.First(x => x.SemanticFieldId == "projectile.penetration_slowdown" && x.SupportWeapon == "M-105 Stalwart");
        await w.SetSupportAsync(f.InstanceKey, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 0.5)); Assert.Null(w.BuildError); // approval is implicit
        Assert.Contains("target=hd2.support_weapon('M-105 Stalwart'):attack('primary'):projectile(),", w.LuaPreview); Assert.Contains("allow_shared=true,", w.LuaPreview);
        Assert.DoesNotContain("allow_unverified_effect", w.LuaPreview);
    }
    [Fact] public async Task Delivery_resolved_support_weapons_are_now_writable_through_their_call_in()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.SupportAuthoring!;
        foreach (var name in DeliveryResolved)
        {
            var weapon = c.Weapons.Single(x => x.Name == name);
            Assert.True(weapon.Writable); Assert.Equal("DELIVERY_RESOLVED", weapon.IdentityStatus); Assert.NotEmpty(weapon.FieldInstanceKeys);
            Assert.Equal("call_in_delivery_and_scraped_fingerprint", weapon.IdentityResolution!.Basis); Assert.False(weapon.IdentityResolution.NonDeliveredRootsAffected);
            // Merged support-stratagem presentation: linked through published relationship IDs, not names.
            Assert.True(w.Metadata.SupportLinks!.ByWeapon.ContainsKey(name));
            var f = c.Field(weapon.FieldInstanceKeys[0]);
            await w.SetSupportAsync(f.InstanceKey, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1));
            if (f.SharedScope.RequiresAcknowledgement) await w.SetSupportApprovalAsync(f.InstanceKey, true);
            if (f.Operation.Acknowledgement != null) await w.SetSupportEffectAcknowledgedAsync(f.InstanceKey, true);
            Assert.Null(w.BuildError); Assert.Contains($"hd2.support_weapon('{name}')", w.LuaPreview);
        }
    }
    [Fact] public async Task Unresolved_support_weapons_stay_blocked()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.SupportAuthoring!;
        foreach (var name in StillBlocked)
        {
            var weapon = c.Weapons.Single(x => x.Name == name);
            Assert.False(weapon.Writable); Assert.Equal("DUPLICATE", weapon.IdentityStatus); Assert.Null(weapon.IdentityResolution);
            Assert.Empty(weapon.FieldInstanceKeys); Assert.NotEmpty(weapon.BlockedFields); Assert.DoesNotContain(c.FieldInstances, f => f.SupportWeapon == name);
        }
        Assert.Equal(["B/FLAM-80 Cremator", "EAT-17 Expendable Anti-Tank", "LAS-98 Laser Cannon"], c.SupportCallInLinks!.Audit.AmbiguousWeaponIdentityNames.Order(StringComparer.Ordinal));
    }
    [Fact] public async Task Every_0240_support_instance_creates_validates_and_generates()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var svc = new SupportChangeService();
        foreach (var f in w.Metadata!.SupportAuthoring!.FieldInstances)
        {
            var c = svc.Create(w.Metadata, f.InstanceKey, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1)) with { EffectAcknowledgement = SupportChangeService.EffectEvidence(f) };
            w.Project!.SupportApprovals[f.SharedScope.ScopeKey] = SupportChangeService.ApprovalEvidence(f);
            svc.Validate(w.Project, w.Metadata, c); w.Project.SupportChanges = [c];
            var lua = e.Generator.Generate(w.Project, w.Metadata);
            Assert.Contains(f.ApiFieldConstant, lua); Assert.Equal(f.Operation.Acknowledgement != null, lua.Contains("allow_unverified_effect=true", StringComparison.Ordinal));
        }
    }
    [Theory] [InlineData("identity-without-evidence")] [InlineData("unknown-acknowledgement")] [InlineData("ack-without-reason")]
    public void Malformed_support_metadata_is_rejected(string mode)
    {
        var j = JsonNode.Parse(SdkFixtures.Entry("0.24.0", SupportAuthoringReader.FileName))!;
        var mg = j["weapons"]!.AsArray().First(x => (string)x!["name"]! == "MG-43 Machine Gun")!;
        var reload = j["fieldInstances"]!.AsArray().First(x => (string)x!["semanticFieldId"]! == "reload.duration")!;
        switch (mode)
        {
            case "identity-without-evidence": mg["identityResolution"] = null; break;
            case "unknown-acknowledgement": reload["operation"]!["acknowledgement"] = "allow_everything"; break;
            case "ack-without-reason": reload["operation"]!["acknowledgementReason"] = null; break;
        }
        Assert.Throws<InvalidDataException>(() => new SupportAuthoringReader().Read(Encoding.UTF8.GetBytes(j.ToJsonString()), "0.24.0"));
    }

    [Fact] public async Task Rebinding_a_023x_project_to_0240_keeps_every_saved_key_without_review()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.23.2");
        var w = e.Workspace(); await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_0232", "0.1.0"), old);
        var support = old.SupportAuthoring!.FieldInstances.First(f => f.SupportWeapon == "GR-8 Recoilless Rifle" && f.SemanticFieldId == "projectile.velocity");
        await w.SetSupportAsync(support.InstanceKey, "350"); await w.SetSupportApprovalAsync(support.InstanceKey, true);
        var armor = old.Entities!.Vehicles.FieldInstances.First(f => f.Target.Vehicle == "TD-220 Bastion MK XVI" && f.SemanticFieldId == "entity.armor");
        await w.SetEntityAsync(armor.InstanceKey, "5");
        const string drum = "weapon-attachment/v1/magazine/rifle-5-5x50mm-drum/fa499a29b375c6cf";
        await w.SetEntityAsync(old.Entities.Attachments!.FieldInstances.First(f => f.Target.Attachment == drum).InstanceKey, "90"); await w.SetAttachmentAcknowledgedAsync(drum, true);
        var cooldown = old.Stratagems!.FieldInstances.First(f => f.Editable && f.SemanticFieldId == "stratagem.cooldown" && f.Target.Stratagem == "Orbital Precision Strike");
        await w.SetStratagemAsync(cooldown.InstanceKey, "60");
        Assert.Null(w.BuildError);
        var before = (Support: w.Project!.SupportChanges.ToList(), Entity: w.Project.EntityChanges.ToList(), Stratagem: w.Project.StratagemChanges.ToList(), Lua: w.LuaPreview);

        await SdkFixtures.Install(e, "0.24.0"); await w.OpenAsync(w.Project.Id); Assert.Equal("0.23.2", w.Project!.SdkVersion);
        await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.24.0", w.Project.SdkVersion); Assert.Null(w.BuildError);
        // Saved changes are byte-identical after rebind (keys, baselines, evidence and acknowledgements).
        Assert.Equal(JsonSerializer.Serialize(before.Support), JsonSerializer.Serialize(w.Project.SupportChanges)); Assert.Equal(JsonSerializer.Serialize(before.Entity), JsonSerializer.Serialize(w.Project.EntityChanges));
        Assert.Equal(before.Stratagem.Select(c => c.InstanceKey), w.Project.StratagemChanges.Select(c => c.InstanceKey));
        Assert.Equal(before.Lua.Replace("0.23.2", "0.24.0"), w.LuaPreview);
        Assert.NotNull(w.Metadata!.Entities!.Boosters);
    }
}
