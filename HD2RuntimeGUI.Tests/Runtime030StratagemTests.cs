using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 stratagem authoring (docs/runtime030-integration-plan.md, Phase 4): the call-in code, presentation (with the mod's own
// icon), call-in time, the orbital pattern, the Eagle attack and the sentry fields are authored with Runtime's own rules
// (domains/stratagem_writes.lua validate_calldown / validate_presentation / validate_change), written in the Lua Runtime accepts, saved
// and reopened, and leave older projects and SDKs unaffected.
public sealed class Runtime030StratagemTests
{
    private const string Barrage = "Orbital 120mm HE Barrage", Precision = "Orbital Precision Strike", Gas = "Eagle Gas Airstrike", Gatling = "A/G-16 Gatling Sentry";
    private static StratagemField Field(SdkMetadata sdk, string stratagem, string field) =>
        sdk.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == stratagem && f.SemanticFieldId == field);
    private static async Task<(BuilderWorkspace W, SdkMetadata Sdk)> Fresh(TestEnvironment e)
    {
        var (w, sdk) = await ExportFixtureTests.Fresh(e, "Stratagem Looks");
        w.Project!.ExportDirectory = e.Paths.Exports;
        return (w, sdk);
    }
    // The generated Lua without layout (operations are indented by their wrapper).
    private static string Squash(string lua) => System.Text.RegularExpressions.Regex.Replace(lua, @"\s+", "");
    private static Dictionary<string, byte[]> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.ToDictionary(z => z.FullName, z => { using var s = z.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }

    [Fact] public async Task A_call_in_code_is_a_list_of_directions_with_the_opt_in_only_for_another_stratagems_native_code()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var code = Field(sdk, Barrage, "stratagem.calldown_code");
        Assert.True(code.Editable); Assert.Equal(94, sdk.Stratagems!.FieldInstances.Count(f => f.Type == StratagemField.CalldownCode && f.Editable));
        await w.SetStratagemAsync(code.InstanceKey, """["up","up","down","down"]""");
        Assert.Null(w.BuildError);
        Assert.Contains("target=hd2.stratagem('Orbital 120mm HE Barrage'),", w.LuaPreview);
        Assert.Contains("field=hd2.fields.stratagem.calldown_code,expect={'right','right','down','left','right','down'},value={'up','up','down','down'},", Squash(w.LuaPreview));
        // Up Up Down Down starts the Cargo Container's code (a mission stratagem outside the catalogue): no opt-in
        // (docs/stratagem-calldown-code.md), but the overlap is reported.
        Assert.DoesNotContain("allow_unverified_effect", w.LuaPreview);
        Assert.NotEmpty(StratagemCode.Overlapping(sdk.Stratagems, code, w.Project!.StratagemChanges[0].DesiredValue));
        // Another catalogued stratagem's native code, and an uncatalogued native (Reinforce: Up Down Right Left Up), need allow_unverified_effect.
        await w.SetStratagemAsync(code.InstanceKey, """["right","right","up"]""");
        Assert.Equal(Precision, Assert.Single(StratagemCode.EqualNative(sdk.Stratagems, code, w.Project.StratagemChanges[0].DesiredValue)).Stratagem);
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
        await w.SetStratagemAsync(code.InstanceKey, """["up","down","right","left","up"]""");
        Assert.Null(Assert.Single(StratagemCode.EqualNative(sdk.Stratagems, code, w.Project.StratagemChanges[0].DesiredValue)).Stratagem);
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
        // Refused where it is made, as Runtime refuses it at registration.
        foreach (var bad in new[] { "[]", """["up","up","up","up","up","up","up","up","up","up"]""", """["north"]""", "\"up\"", "3" })
            await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(code.InstanceKey, bad));
        // Back to its own code: the edit goes away.
        await w.SetStratagemAsync(code.InstanceKey, code.CurrentDefault.GetRawText());
        Assert.Empty(w.Project.StratagemChanges);
    }

    [Fact] public async Task Two_edits_giving_two_stratagems_one_code_are_reported_as_a_clash()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var barrage = Field(sdk, Barrage, "stratagem.calldown_code"); var precision = Field(sdk, Precision, "stratagem.calldown_code");
        await w.SetStratagemAsync(barrage.InstanceKey, """["up","up","down","down"]""");
        await w.SetStratagemAsync(precision.InstanceKey, """["up","up","down","down"]""");
        var value = w.Project!.StratagemChanges.Single(c => c.Stratagem == Precision).DesiredValue;
        var clash = Assert.Single(StratagemCode.ProjectClashes(w.Project, sdk.Stratagems!, precision, value));
        Assert.Equal((Barrage, false), (clash.Other, clash.IsCustom));
        // The Precision Strike's native code is free in this project once it has its own: the barrage may take it (the opt-in stays: Runtime
        // compares with native codes).
        await w.SetStratagemAsync(barrage.InstanceKey, """["right","right","up"]""");
        Assert.Empty(StratagemCode.ProjectClashes(w.Project, sdk.Stratagems!, barrage, w.Project.StratagemChanges.Single(c => c.Stratagem == Barrage).DesiredValue));
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
    }

    [Fact] public async Task Presentation_takes_a_catalogued_source_and_the_icon_the_mods_own_image()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var name = Field(sdk, Barrage, "stratagem.presentation.name"); var icon = Field(sdk, Barrage, "stratagem.presentation.icon");
        var hover = Field(sdk, "LIFT-860 Hover Pack", "stratagem.presentation.name_cased");
        await w.SetStratagemAsync(name.InstanceKey, "\"Orbital Gas Strike\"");
        Assert.Null(w.BuildError);
        Assert.Contains("field=hd2.fields.stratagem.presentation_name,expect='Orbital120mmHEBarrage',value='OrbitalGasStrike',", Squash(w.LuaPreview));
        Assert.DoesNotContain("allow_unverified_effect", w.LuaPreview);
        // Not a source: an unknown name, raw ids, and the LIFT-860's cased name (excluded by Runtime) for another stratagem's field.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(name.InstanceKey, "\"Orbital Banana Strike\""));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(name.InstanceKey, "12345"));
        var cased = Field(sdk, Barrage, "stratagem.presentation.name_cased");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(cased.InstanceKey, "\"LIFT-860 Hover Pack\""));
        Assert.DoesNotContain("LIFT-860 Hover Pack", StratagemPresentation.Sources(sdk.Stratagems!, cased));
        Assert.Contains("LIFT-860 Hover Pack", StratagemPresentation.Sources(sdk.Stratagems!, hover));
        // A custom image only for the icon.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(name.InstanceKey, """{"image":"x"}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemImageAsync(name.InstanceKey, CustomIconCompiler.Placeholder("x"), null));

        var png = CustomIconCompiler.Placeholder("gas"); await w.SetStratagemImageAsync(icon.InstanceKey, png, "gas.png");
        var image = Assert.Single(w.Project!.CustomContent!.Images); var id = image.Id; Assert.EndsWith("_icon", id);
        Assert.False(CustomContentBuild.Has(w.Project)); Assert.Equal(14, w.Project.FormatVersion);
        Assert.Null(w.BuildError);
        Assert.Contains("field=hd2.fields.stratagem.presentation_icon,expect='Orbital120mmHEBarrage',value=hd2.resources.image('" + id + "')", Squash(w.LuaPreview));
        Assert.Same(image, w.StratagemImage(icon.InstanceKey));
        // A second import replaces the same image in place.
        await w.SetStratagemImageAsync(icon.InstanceKey, CustomIconCompiler.Placeholder("other"), "other.png");
        Assert.Equal("other.png", Assert.Single(w.Project.CustomContent!.Images).SourceName);

        // Saved and reopened exactly; the export packs the icon family and its source PNG as custom content does.
        var lua = w.LuaPreview;
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        await w.ExportAsync(); var entries = Entries(w.LastExport!);
        Assert.Equal(Convert.FromBase64String(w.Project!.CustomContent!.Images[0].Png), entries["images/" + id + ".png"]);
        Assert.NotEmpty(entries["mod/" + GameplayArchive.ArchiveName + ".gpu_resources"]);
        var report = JsonNode.Parse(entries["build-report.json"])!;
        Assert.Equal(id, report["image_sources"]![0]!["image"]!.GetValue<string>()); Assert.Null(report["custom_content"]);
        Assert.Equal(RuntimeRequirement.Release030, JsonNode.Parse(entries["hd2runtime.json"])!["requires"]!["hd2runtime"]!["min_version"]!.GetValue<string>());

        // Back to a stratagem's icon: the image is no longer kept, and the empty store goes away.
        await w.SetStratagemAsync(icon.InstanceKey, "\"Orbital Gas Strike\"");
        Assert.Null(w.Project.CustomContent);
        await w.ResetStratagemAsync(Barrage); Assert.Empty(w.Project.StratagemChanges);
    }

    [Fact] public async Task A_custom_icon_image_is_kept_while_custom_content_is_edited_and_a_missing_one_is_reported()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var icon = Field(sdk, Barrage, "stratagem.presentation.icon");
        await w.SetStratagemImageAsync(icon.InstanceKey, CustomIconCompiler.Placeholder("a"), null);
        // A custom content edit keeps the image a stratagem edit shows.
        await w.EditCustomContentAsync(c => { });
        Assert.Single(w.Project!.CustomContent!.Images);
        // A project whose image went missing is refused at the build with the image named, not dropped.
        w.Project.CustomContent.Images.Clear();
        Assert.Contains("_icon'", w.StratagemIssue(w.Project.StratagemChanges[0]));
    }

    [Fact] public async Task Call_in_time_orbital_pattern_eagle_and_sentry_fields_follow_runtimes_ranges_sentinels_and_opt_ins()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var callIn = Field(sdk, Barrage, "stratagem.call_in_time");
        await w.SetStratagemAsync(callIn.InstanceKey, "10");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(callIn.InstanceKey, "61"));
        var salvos = Field(sdk, Barrage, "orbital.salvos");
        await w.SetStratagemAsync(salvos.InstanceKey, "8");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(salvos.InstanceKey, "17"));
        Assert.Null(w.BuildError);
        // Call-in time and the pattern are different records: a plan of two operations, each with Runtime's opt-in.
        Assert.Contains("field=hd2.fields.stratagem.call_in_time", w.LuaPreview); Assert.Contains("field=hd2.fields.orbital.salvos", w.LuaPreview);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(w.LuaPreview, "allow_unverified_effect=true").Count);

        var pattern = Field(sdk, Gas, "eagle.airstrike_pattern");
        Assert.Equal(8, pattern.AllowedValues!.Length); Assert.True(pattern.AllowSharedRequired);
        await w.SetStratagemAsync(pattern.InstanceKey, "3");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(pattern.InstanceKey, "8"));
        var gas = w.LuaPreview[w.LuaPreview.IndexOf("hd2.stratagem('Eagle Gas Airstrike')", StringComparison.Ordinal)..];
        Assert.Contains("allow_shared=true", gas); Assert.Contains("allow_unverified_effect=true", gas);
        Assert.False(Field(sdk, Gas, "eagle.bombs_per_strike").Editable); Assert.False(Field(sdk, Gas, "eagle.payload").Editable);

        // targeting.side_range: -1 (use targeting.range) is accepted besides 0-500, and is the vanilla value.
        var side = Field(sdk, Gatling, "targeting.side_range");
        StratagemScalar.CheckRange(side, JsonSerializer.SerializeToElement(-1));
        Assert.NotNull(StratagemScalar.Sentinel(side, -1));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(side.InstanceKey, "-2"));
        await w.SetStratagemAsync(side.InstanceKey, "40");
        Assert.Contains("target=hd2.stratagem('A/G-16 Gatling Sentry'):deployed_entity():targeting(),", w.LuaPreview);
        Assert.Contains("field=hd2.fields.targeting.side_range,expect=-1,value=40,", Squash(w.LuaPreview));
        await w.SetStratagemAsync(side.InstanceKey, "-1");
        Assert.DoesNotContain(w.Project!.StratagemChanges, c => c.InstanceKey == side.InstanceKey);
        foreach (var id in new[] { "windup.wind_up_seconds", "turret.pitch_yaw_coupling" })
            await w.SetStratagemAsync(Field(sdk, Gatling, id).InstanceKey, "0.25");
        var beam = Field(sdk, "A/LAS-98 Laser Sentry", "beam.fire_rate");
        await w.SetStratagemAsync(beam.InstanceKey, "120");
        Assert.Null(w.BuildError);
        Assert.Contains("hd2.fields.windup.wind_up_seconds", w.LuaPreview); Assert.Contains("hd2.fields.beam.fire_rate", w.LuaPreview);
        var lua = w.LuaPreview;
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        Assert.Equal(RuntimeRequirement.Release030, RuntimeRequirement.Minimum(w.Project!, sdk));
    }

    // An export with every Phase 4 surface, copied with the F1-F9 fixtures (build-rc.ps1 validates them with Runtime's own validator).
    [Fact] public async Task S1_stratagem_authoring_export_fixture()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await ExportFixtureTests.Fresh(e, "S1 Stratagem Authoring");
        await w.SetStratagemAsync(Field(sdk, Barrage, "stratagem.calldown_code").InstanceKey, """["up","up","down","down"]""");
        await w.SetStratagemAsync(Field(sdk, Precision, "stratagem.calldown_code").InstanceKey, """["right","right","down","left","right","down"]""");
        await w.SetStratagemPresentationAsync(Barrage, "Orbital Gas Strike");
        await w.SetStratagemImageAsync(Field(sdk, Precision, "stratagem.presentation.icon").InstanceKey, CustomIconCompiler.Placeholder("s1"), null);
        await w.SetStratagemAsync(Field(sdk, Barrage, "stratagem.call_in_time").InstanceKey, "3");
        await w.SetStratagemAsync(Field(sdk, Barrage, "orbital.salvos").InstanceKey, "6");
        await w.SetStratagemAsync(Field(sdk, Barrage, "orbital.scatter").InstanceKey, "20");
        await w.SetStratagemAsync(Field(sdk, Gas, "eagle.airstrike_pattern").InstanceKey, "3");
        await w.SetStratagemAsync(Field(sdk, Gas, "eagle.drop_interval").InstanceKey, "0.1");
        await w.SetStratagemAsync(Field(sdk, Gatling, "targeting.side_range").InstanceKey, "50");
        await w.SetStratagemAsync(Field(sdk, Gatling, "turret.pitch_yaw_coupling").InstanceKey, "2");
        await w.SetStratagemAsync(Field(sdk, Gatling, "windup.wind_up_seconds").InstanceKey, "0.25");
        await w.SetStratagemAsync(Field(sdk, Gatling, "weapon.recoil_drift_horizontal").InstanceKey, "10");
        await w.SetStratagemAsync(Field(sdk, "A/LAS-98 Laser Sentry", "beam.fire_rate").InstanceKey, "90");
        var (manifest, lua) = await ExportFixtureTests.Export(w, "S1-stratagem-authoring");
        Assert.Equal(RuntimeRequirement.Release030, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Contains("value=hd2.resources.image('", lua); Assert.Contains("value='Orbital Gas Strike'", lua);
        // The Precision Strike takes the barrage's native code: the opt-in Runtime requires for it.
        var precision = lua[lua.IndexOf("hd2.stratagem('Orbital Precision Strike')", StringComparison.Ordinal)..];
        Assert.Contains("allow_unverified_effect=true", precision[..precision.IndexOf("}}", StringComparison.Ordinal)]);
    }

    [Fact] public async Task New_value_types_need_format_14_and_older_sdks_and_projects_are_unaffected()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetStratagemAsync(Field(sdk, Barrage, "stratagem.cooldown").InstanceKey, "100");
        Assert.True(w.Project!.FormatVersion < 14);
        await w.SetStratagemAsync(Field(sdk, Barrage, "stratagem.calldown_code").InstanceKey, """["up","up","down","down"]""");
        Assert.Equal(14, w.Project.FormatVersion); Assert.Equal(14, ProjectIdentity.RequiredFormat(w.Project));
        ProjectIdentity.Validate(w.Project);
        // A malformed saved value is refused when the project is read.
        var broken = JsonSerializer.Deserialize<ModProject>(JsonSerializer.Serialize(w.Project, Core.Storage.JsonStorage.Options), Core.Storage.JsonStorage.Options)!;
        broken.StratagemChanges[1] = broken.StratagemChanges[1] with { DesiredValue = JsonSerializer.SerializeToElement(new { image = "x" }) };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(broken));
        // The 0.28.1 SDK publishes none of these fields: nothing to author there, and its projects build as before.
        using var old = new TestEnvironment(); var published = await SdkFixtures.Install(old, "0.28.1");
        Assert.DoesNotContain(published.Stratagems!.FieldInstances, f => f.Type is StratagemField.CalldownCode or StratagemField.Presentation);
        Assert.DoesNotContain(published.Stratagems.FieldInstances, f => f.SemanticFieldId is "orbital.salvos" or "stratagem.call_in_time" && f.Editable);
        var ow = old.Workspace(); await ow.CreateAsync(new("Old", "Tests", "mods/tests/old", "0.1.0"), published);
        await ow.SetStratagemAsync(published.Stratagems.FieldInstances.First(f => f.SemanticFieldId == "stratagem.cooldown" && f.Editable).InstanceKey, "100");
        Assert.Null(ow.BuildError); Assert.True(ow.Project!.FormatVersion < 14);
        Assert.Equal("0.28.1", RuntimeRequirement.Minimum(ow.Project, published));
    }
}
