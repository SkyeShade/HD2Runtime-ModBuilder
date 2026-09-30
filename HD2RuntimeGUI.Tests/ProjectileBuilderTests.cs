using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using Xunit;
using static HD2RuntimeGUI.Tests.ProjectileHostTests;

namespace HD2RuntimeGUI.Tests;

// 0.28.0 projectile builder on the frozen SDK fixture: row slot writes (direct hit, impact and expiry explosion) and weapon-function mode
// presentation on hd2.attack_output(row). A row write edits the row for every entity that fires it; it never follows a host swap.
public sealed class ProjectileBuilderTests
{
    private const string Impact = OutputRowChangeService.ImpactExplosion, Expiry = OutputRowChangeService.ExpiryExplosion, Direct = OutputRowChangeService.DirectDamage;
    private const string Label = OutputRowChangeService.ModeLabel, Icon = OutputRowChangeService.ModeIcon;
    private static readonly string Gl21Impact = OutputRowChangeService.Handle(Gl21, AttackOutputSlots.ImpactExplosionKey);
    private static readonly string Hmg = Out("mg-206-heavy-machine-gun"), Coyote = Out("ar-2-coyote"), Eruptor = Out("r-36-eruptor"), Ma5c = Out("ma5c-assault-rifle");
    private static string Flat(string s) => Regex.Replace(s, @"\s+", "");

    [Fact] public async Task The_patriot_bullet_row_takes_the_live_proven_grenade_blast_with_only_allow_shared()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var slot = OutputRowChangeService.Row(sdk, PatriotRow).Slots!.ImpactExplosion;
        Assert.True(slot.Shared); Assert.Equal(10, slot.SharedConsumerCount); Assert.Contains("gatling_turret", slot.NamedSharedConsumers);
        var donors = OutputRowChangeService.SlotDonors(sdk, PatriotRow, Impact);
        var proven = donors.Single(d => d.Value == Gl21Impact);
        Assert.True(proven.LiveProven && proven.Allowed); Assert.Equal(["allow_shared"], proven.Acknowledgements);
        // An explosion slot takes either explosion of a donor: the Speargun's gas cloud is its expiry explosion (live-proven here too).
        Assert.True(donors.Single(d => d.Value == OutputRowChangeService.Handle(Out("s-11-speargun"), AttackOutputSlots.ExpiryExplosionKey)).LiveProven);
        Assert.DoesNotContain(donors, d => d.Slot == AttackOutputSlots.DirectDamageKey);
        await w.SetRowAsync(PatriotRow, Impact, Gl21Impact);
        var change = Assert.Single(w.Project!.OutputRowChanges!);
        Assert.Equal(OutputRowChangeService.None, change.Expect); Assert.Equal(["allow_shared"], change.Acknowledgements); Assert.Equal("GL-21 Grenade Launcher", change.ValueName);
        Assert.Equal(11, w.Project.FormatVersion); Assert.Null(w.BuildError);
        Assert.Contains(Flat("transaction={id='row-"), Flat(w.LuaPreview));
        Assert.Contains(Flat("target=hd2.attack_output('" + PatriotRow + "'),allow_shared=true,changes={{field=hd2.fields.projectile.impact_explosion,expect='none',value=hd2.attack_output('" + Gl21 + "'):impact_explosion()},},"), Flat(w.LuaPreview));
        Assert.DoesNotContain("allow_unverified", w.LuaPreview);
        // An unproven donor on the same shared row carries both opt-ins; allow_shared never drops.
        await w.SetRowAsync(PatriotRow, Impact, OutputRowChangeService.Handle(Eruptor, AttackOutputSlots.ImpactExplosionKey));
        Assert.Equal(["allow_shared", "allow_unverified_effect"], Assert.Single(w.Project.OutputRowChanges!).Acknowledgements);
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
        // Choosing the row's reviewed value removes the edit.
        await w.SetRowAsync(PatriotRow, Impact, OutputRowChangeService.None);
        Assert.Null(w.Project.OutputRowChanges);
    }

    [Fact] public async Task None_removes_an_explosion_but_never_the_direct_hit_and_slot_types_must_match()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        // The GL-21 row has an expiry explosion: 'none' removes it (a shared row, not live-proven).
        var none = OutputRowChangeService.SlotDonors(sdk, Gl21, Expiry).First();
        Assert.Null(none.Output); Assert.Equal(OutputRowChangeService.None, none.Value); Assert.Equal(["allow_shared", "allow_unverified_effect"], none.Acknowledgements);
        await w.SetRowAsync(Gl21, Expiry, OutputRowChangeService.None);
        Assert.Contains(Flat("{field=hd2.fields.projectile.expiry_explosion,expect=hd2.attack_output('" + Gl21 + "'):expiry_explosion(),value='none'}"), Flat(w.LuaPreview));
        // A projectile always has a direct hit.
        Assert.DoesNotContain(OutputRowChangeService.SlotDonors(sdk, Gl21, Direct), d => d.Value == OutputRowChangeService.None);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetRowAsync(Gl21, Direct, OutputRowChangeService.None));
        Assert.Contains("direct", refused.Message, StringComparison.OrdinalIgnoreCase);
        // A damage slot takes only a direct_damage() handle, an explosion slot only an explosion handle, and only one the donor has.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetRowAsync(Coyote, Direct, Gl21Impact));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetRowAsync(Coyote, Impact, OutputRowChangeService.Handle(Out("ar-32-pacifier"), AttackOutputSlots.DirectDamageKey)));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetRowAsync(Coyote, Impact, OutputRowChangeService.Handle(Talon, AttackOutputSlots.ImpactExplosionKey)));
        // The Coyote direct hit swapped for the Pacifier stun is live-proven (a row only the Coyote fires: no opt-in at all).
        await w.SetRowAsync(Coyote, Direct, OutputRowChangeService.Handle(Out("ar-32-pacifier"), AttackOutputSlots.DirectDamageKey));
        Assert.Empty(w.RowChange(Coyote, Direct)!.Acknowledgements);
        Assert.Contains(Flat("{field=hd2.fields.projectile.direct_damage,expect=hd2.attack_output('" + Coyote + "'):direct_damage(),value=hd2.attack_output('" + Out("ar-32-pacifier") + "'):direct_damage()}"), Flat(w.LuaPreview));
        // A donor without a catalogued package is refused (the Guard Dog gun).
        Assert.StartsWith("ASSET_UNAVAILABLE", OutputRowChangeService.SlotDonors(sdk, Coyote, Direct).Single(d => d.Output?.SemanticId == GuardDogRow).Refusal);
        Assert.Null(w.BuildError); Assert.Equal(2, w.Project!.OutputRowChanges!.Count);
    }

    [Fact] public async Task A_swapped_host_gets_its_effect_on_the_donor_row_never_through_the_swap()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetHostOutputAsync(MountKind, Patriot, "primary", Talon);
        // The donor-row composition a live test proved: the Talon row with the GL-21 blast (a row only the Talon fires natively).
        var talonImpact = OutputRowChangeService.Row(sdk, Talon).Slots!.ImpactExplosion;
        Assert.False(talonImpact.Shared); Assert.Contains(Gl21Impact, talonImpact.LiveProvenValues);
        await w.SetRowAsync(Talon, Impact, Gl21Impact);
        Assert.Empty(w.RowChange(Talon, Impact)!.Acknowledgements);
        Assert.Equal(Talon, w.FiredRow(MountKind, Patriot, "primary")!.SemanticId);
        // The Patriot fires the Talon row: it is listed with the row's consumers even though Runtime's count covers native consumers only.
        Assert.Equal([Patriot], w.HostsSwappedOnto(Talon));
        var lua = Flat(w.LuaPreview);
        Assert.Contains(Flat("target=hd2.attack_output('" + Talon + "'),changes={{field=hd2.fields.projectile.impact_explosion,expect='none',value=hd2.attack_output('" + Gl21 + "'):impact_explosion()},},"), lua);
        Assert.Contains(Flat("value=hd2.attack_output('" + Talon + "'),"), lua);
        // A write on the Patriot's own row stays on that row (it reaches the minigun only while it fires its own bullet).
        await w.SetRowAsync(PatriotRow, Impact, Gl21Impact);
        Assert.Contains("hd2.attack_output('" + PatriotRow + "')", w.LuaPreview);
        Assert.Null(w.BuildError);
    }

    [Fact] public async Task Mode_labels_and_icons_come_from_the_published_native_sets_and_drop_the_effect_opt_in_only_where_proven()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var modes = OutputRowChangeService.Modes(sdk);
        Assert.Contains(OutputRowChangeService.Icons(sdk), i => i.Value == ModePresentation.Auto);
        Assert.DoesNotContain(OutputRowChangeService.Icons(sdk), i => i.Value == ModePresentation.Default);
        // The spare twin: STUN with the auto icon (the exact native stun icon) is live-proven, and the row is fired by nothing else.
        var twin = OutputRowChangeService.Row(sdk, SpareTwin);
        Assert.Equal(("ammo_stun", "exact_native"), OutputRowChangeService.AutoIcon(modes, twin, "stun"));
        await w.SetRowAsync(SpareTwin, Label, "stun"); await w.SetRowAsync(SpareTwin, Icon, ModePresentation.Auto);
        Assert.All(w.RowChanges(SpareTwin), c => Assert.Empty(c.Acknowledgements));
        Assert.Contains(Flat("target=hd2.attack_output('" + SpareTwin + "'),changes={{field=hd2.fields.presentation.mode_label,expect='none',value='stun'},{field=hd2.fields.presentation.mode_icon,expect='default',value='auto'},},"), Flat(w.LuaPreview));
        // The HMG's STANDARD label with its auto icon (the generic fallback) is live-proven, but the HMG row is shared: allow_shared stays.
        await w.SetRowAsync(Hmg, Label, "standard"); await w.SetRowAsync(Hmg, Icon, ModePresentation.Auto);
        Assert.All(w.RowChanges(Hmg), c => Assert.Equal(["allow_shared"], c.Acknowledgements));
        // Another label: the auto icon now resolves to the stun icon, which no test showed on this row.
        await w.SetRowAsync(Hmg, Label, "stun");
        Assert.All(w.RowChanges(Hmg), c => Assert.Equal(["allow_shared", "allow_unverified_effect"], c.Acknowledgements));
        Assert.Null(w.RowIssue(w.RowChange(Hmg, Icon)!));
        // Native values only: custom text is refused. 'default' (the empty placeholder) is never written: on a row whose vanilla icon it is,
        // choosing it removes the edit; on a row with a native icon it is refused.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetRowAsync(Hmg, Label, "rainbow"));
        await w.SetRowAsync(Hmg, Icon, ModePresentation.Default); Assert.Null(w.RowChange(Hmg, Icon));
        var iconned = sdk.AttackOutputs!.Outputs.First(o => o.Presentation is { Icon: not ModePresentation.Default });
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetRowAsync(iconned.SemanticId, Icon, ModePresentation.Default));
        await w.SetRowAsync(Hmg, Icon, "ammo_he");
        Assert.Equal(["allow_shared", "allow_unverified_effect"], w.RowChange(Hmg, Icon)!.Acknowledgements);
        // Slots and presentation of a row are separate operations (like builder:operations()).
        await w.SetRowAsync(Hmg, Impact, Gl21Impact);
        Assert.Equal(2, Regex.Matches(w.LuaPreview, Regex.Escape("target=hd2.attack_output('" + Hmg + "')")).Count);
        Assert.Null(w.BuildError);
    }

    [Fact] public async Task Row_writes_round_trip_in_format_11_with_a_semantic_json_shape()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetRowAsync(PatriotRow, Impact, Gl21Impact); await w.SetRowAsync(SpareTwin, Label, "stun");
        var lua = w.LuaPreview; var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id));
        Assert.Contains("\"outputRowChanges\": [", json); Assert.Contains("\"field\": \"projectile.impact_explosion\"", json);
        Assert.Contains("\"value\": \"output/v1/projectile/gl-21-grenade-launcher#impactExplosion\"", json); Assert.Contains("\"expect\": \"none\"", json);
        Assert.Contains("\"field\": \"presentation.mode_label\"", json); Assert.Contains("\"formatVersion\": 11", json);
        await w.OpenAsync(w.Project.Id);
        Assert.Equal(lua, w.LuaPreview); Assert.Null(w.BuildError);
        Assert.All(w.Project!.OutputRowChanges!, c => Assert.Null(w.RowIssue(c)));
        // Toggle and reset in place.
        var id = w.RowChange(PatriotRow, Impact)!.Id;
        await w.ToggleRowAsync(id); Assert.DoesNotContain("hd2.attack_output('" + PatriotRow + "')", w.LuaPreview);
        await w.ToggleRowAsync(id); await w.ResetRowAsync(SpareTwin); Assert.Single(w.Project.OutputRowChanges!);
        // Saved rows are semantic identities only.
        var p = w.Project; var good = p.OutputRowChanges![0];
        foreach (var bad in new[] { good with { Field = "projectile.velocity" }, good with { Value = "row 148" }, good with { Output = "projectile/148" }, good with { Acknowledgements = ["allow_unverified_reference"] }, good with { Value = good.Expect } })
        {
            p.OutputRowChanges = [bad]; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        }
        p.OutputRowChanges = [good]; ProjectIdentity.Validate(p);
        // A project without row writes keeps its format and JSON.
        await w.ResetRowAsync(PatriotRow); Assert.Null(w.Project.OutputRowChanges);
        Assert.DoesNotContain("outputRowChanges", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)));
    }

    [Fact] public async Task A_new_snapshot_that_withdraws_a_live_proven_tuple_flags_the_row_write_for_review()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetRowAsync(PatriotRow, Impact, Gl21Impact); await w.SetRowAsync(Talon, Impact, Gl21Impact);
        var n = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(EnemyAuthoringTests.DevSdk(e), AttackOutputReader.FileName)))!;
        var row = n["outputs"]!.AsArray().Single(o => (string)o!["semanticId"]! == PatriotRow)!;
        var proven = row["slots"]!["impactExplosion"]!["liveProvenValues"]!.AsArray(); proven.Remove(proven.Single(v => (string)v! == Gl21Impact));
        var reopened = await Reopen(e, w.Project!.Id, (AttackOutputReader.FileName, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(n)));
        var patriot = reopened.RowChange(PatriotRow, Impact)!;
        Assert.NotNull(reopened.RowIssue(patriot)); Assert.NotNull(reopened.BuildError);
        // The unchanged row stays valid; accepting the current SDK in place keeps the value and takes the published opt-ins.
        Assert.Null(reopened.RowIssue(reopened.RowChange(Talon, Impact)!));
        await reopened.AcceptRowAsync(patriot.Id);
        Assert.Equal(["allow_shared", "allow_unverified_effect"], reopened.RowChange(PatriotRow, Impact)!.Acknowledgements);
        Assert.Equal(Gl21Impact, reopened.RowChange(PatriotRow, Impact)!.Value);
        Assert.Null(reopened.BuildError); Assert.Contains("allow_unverified_effect=true", reopened.LuaPreview);
    }

    [Fact] public async Task The_row_view_reads_flight_values_from_the_owner_catalog_that_authors_them()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        // A player row: the owner's projectile object fields (the MA5C's penetration slowdown included), editable where the owner fires its own row.
        var ma5c = w.RowScalars(Ma5c);
        Assert.Equal(["velocity", "drag", "penetration_slowdown"], ma5c.Select(s => s.Key));
        var slowdown = ma5c.Single(s => s.Key == "penetration_slowdown");
        Assert.Equal(0.25, slowdown.Baseline.GetDouble()); Assert.Equal("projectile.penetration_slowdown", slowdown.Player!.SemanticFieldId); Assert.Equal("primary", slowdown.PlayerRole);
        // Support, mounted and stratagem rows use their own catalogs' fields.
        Assert.All(w.RowScalars(Eat17Row), s => Assert.NotNull(s.Support));
        var patriot = w.RowScalars(PatriotRow);
        Assert.All(patriot, s => Assert.Equal("projectile_reference", s.Mount!.Target.Path)); Assert.Equal(820, patriot.Single(s => s.Key == "velocity").Baseline.GetDouble());
        Assert.Contains(w.RowScalars(Mortar), s => s.Stratagem != null);
        // A player owner that fires another row in this project cannot edit its own row's flight through its attack: read-only with a note.
        await w.SetAttackOutputAsync("MA5C Assault Rifle", "primary", Napalm);
        Assert.All(w.RowScalars(Ma5c), s => { Assert.Null(s.Player); Assert.NotNull(s.Note); });
    }

    [Fact] public async Task A_terminal_explosion_edit_and_a_slot_write_on_the_same_row_are_one_conflict()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        const string Concussive = "AR-23C Liberator Concussive";
        var row = sdk.AttackOutputs!.OwnedBy(Concussive)!.SemanticId;
        await w.SetTerminalAsync(Concussive, "primary", "impact", w.ExplosionSources.First(s => s.Projectile?.Weapon == "R-36 Eruptor"), true);
        Assert.Null(w.BuildError);
        await w.SetRowAsync(row, Impact, Gl21Impact);
        Assert.Contains(Concussive, w.RowIssue(w.RowChange(row, Impact)!)); Assert.NotNull(w.BuildError);
        await w.ResetRowAsync(row); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Every_operation_id_is_unique_in_one_addon()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetAttackOutputAsync("AR-23 Liberator", "primary", Napalm);
        await w.SetHostOutputAsync(SupportKind, Eat17, "primary", Scorcher);
        await w.SetHostOutputAsync(MountKind, Patriot, "primary", Talon);
        await w.SetHostOutputAsync(MountKind, GuardDog, "primary", Penetrator);
        await w.SetRowAsync(Talon, Impact, Gl21Impact); await w.SetRowAsync(Talon, Label, "he"); await w.SetRowAsync(Talon, Icon, ModePresentation.Auto);
        await w.SetRowAsync(PatriotRow, Impact, Gl21Impact); await w.SetRowAsync(Hmg, Label, "standard");
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        await w.AddCustomLuaAsync();
        Assert.Null(w.BuildError);
        var ids = Ids(w.LuaPreview);
        Assert.Equal(ids.Distinct().Count(), ids.Length); Assert.True(ids.Length >= 9);
        Assert.All(ids, id => Assert.Matches(@"\A[A-Za-z0-9_-]{1,64}\z", id));
        Assert.Contains("hd2.attack_output('" + Talon + "')", await ExportedLua(w));
    }
    private static async Task<string> ExportedLua(HD2RuntimeGUI.Core.Services.BuilderWorkspace w)
    {
        await w.ExportAsync(); using var zip = System.IO.Compression.ZipFile.OpenRead(w.LastExport!);
        using var reader = new StreamReader(zip.GetEntry("src/addon.lua")!.Open()); return await reader.ReadToEndAsync();
    }
}
