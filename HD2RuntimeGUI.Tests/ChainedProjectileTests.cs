using System.IO.Compression;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class ChainedProjectileTests
{
    private const string One = "AR/GL-21 One-Two", Sta = "StA-52 Assault Rifle", Tender = "AR-61 Tenderizer";
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, bool originalHandles)
    {
        var bytes = SdkFixtures.Archive("0.30.3");
        using var source = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open(); using var dest = zip.CreateEntry(entry.FullName).Open();
                if (entry.FullName == CompositionPlanCapabilitiesReader.FileName)
                {
                    var json = JsonNode.Parse(input)!; json["originalPlayerProjectiles"] = originalHandles;
                    using var writer = new StreamWriter(dest); writer.Write(json.ToJsonString());
                }
                else input.CopyTo(dest);
            }
        e.GitHub.Archive = output.ToArray();
        var sdk = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.30.3", e.GitHub.Archive));
        var w = e.Workspace(); await w.CreateAsync(new("Chain", "Tests", "mods/tests/chain", "0.1.0"), sdk); return w;
    }
    private static async Task Damage(BuilderWorkspace w, string weapon)
    {
        await w.SetObjectScalarAsync(weapon, "primary", "projectile", null, "damage.standard_damage", "80", true);
        await w.SetObjectScalarAsync(weapon, "primary", "projectile", null, "damage.durable_damage", "85", true);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Both_edit_orders_keep_original_damage_on_Sta_and_export_stable_handles(bool damageFirst)
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, true);
        if (damageFirst) await Damage(w, One);
        await w.SwapProjectileAsync(Sta, "primary", new(One, "primary"), false);
        if (!damageFirst) await Damage(w, Sta);
        await w.SwapProjectileAsync(One, "primary", new(Tender, "primary"), false);
        Assert.Null(w.BuildError);
        Assert.Equal(new(One, "primary"), w.EffectiveProjectile(Sta, "primary"));
        Assert.Equal(new(Tender, "primary"), w.EffectiveProjectile(One, "primary"));
        var edits = w.ObjectEditsOnCurrentProjectile(Sta, "primary"); Assert.Equal(2, edits.Count);
        Assert.All(edits, c => Assert.Equal(new(One, "primary"), c.Target));
        Assert.Equal("80", edits.Single(c => c.Scalar!.SemanticFieldId == "damage.standard_damage").Scalar!.DesiredValue.GetRawText());
        Assert.Equal("85", edits.Single(c => c.Scalar!.SemanticFieldId == "damage.durable_damage").Scalar!.DesiredValue.GetRawText());
        Assert.Empty(w.ObjectEditsOnCurrentProjectile(One, "primary"));
        Assert.Contains("target=hd2.weapon('AR/GL-21 One-Two'):attack('primary'):original_projectile()", w.LuaPreview);
        Assert.Contains("value=hd2.weapon('AR/GL-21 One-Two'):attack('primary'):original_projectile()", w.LuaPreview);
        Assert.DoesNotContain("target_from=", w.LuaPreview);
        Assert.DoesNotContain("phases=", w.LuaPreview);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError);
        Assert.Equal(2, w.ObjectEditsOnCurrentProjectile(Sta, "primary").Count);
        await w.ExportAsync(); Assert.NotNull(w.LastExport);
    }

    [Fact] public async Task Existing_sdk_keeps_the_guard_and_ordinary_swaps_work()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, false);
        await w.SwapProjectileAsync(Sta, "primary", new(One, "primary"), false); Assert.Null(w.BuildError);
        Assert.DoesNotContain("original_projectile", w.LuaPreview);
        await w.SwapProjectileAsync(One, "primary", new(Tender, "primary"), false);
        Assert.Contains("source is itself replaced", w.BuildError);
    }

    [Fact] public async Task Conflicting_damage_values_remain_blocked()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, true);
        await Damage(w, One);
        await w.SwapProjectileAsync(Sta, "primary", new(One, "primary"), false);
        await w.SetObjectScalarAsync(Sta, "primary", "projectile", null, "damage.standard_damage", "100", true);
        Assert.NotNull(w.BuildError);
    }

    [Fact] public async Task Original_damage_option_binding_moves_to_the_remaining_borrower()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, true); await Damage(w, One);
        var standard = w.Project!.CompositionChanges.Single(c => c.Scalar!.SemanticFieldId == "damage.standard_damage");
        w.Project.ModOptions = new() { Rows = [new() { Key = ModOptionsService.ObjectKey(standard), Id = "damage", Min = 1, Max = 200, Default = 80 }] };
        await w.SwapProjectileAsync(Sta, "primary", new(One, "primary"), false);
        await w.SwapProjectileAsync(One, "primary", new(Tender, "primary"), false);
        Assert.Null(w.BuildError);
        var moved = w.Project.CompositionChanges.Single(c => c.Scalar!.SemanticFieldId == "damage.standard_damage");
        Assert.Equal(Sta, moved.Weapon);
        Assert.Equal(ModOptionsService.ObjectKey(moved), Assert.Single(w.Project.ModOptions.Rows).Key);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task A_swap_without_a_borrower_keeps_the_existing_keep_or_discard_choice(bool keep)
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, true); await Damage(w, One);
        await w.SwapProjectileAsync(One, "primary", new(Tender, "primary"), keep);
        Assert.Null(w.BuildError);
        Assert.Equal(keep ? 2 : 0, w.Project!.CompositionChanges.Count);
        Assert.All(w.Project.CompositionChanges, c => Assert.Equal(new(Tender, "primary"), c.Target));
    }
}
