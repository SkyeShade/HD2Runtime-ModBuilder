using System.Buffers.Binary;
using System.Text;
using HD2RuntimeGUI.Core.GameAssets;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Information-architecture pass: support equipment presented through Support stratagems, readable field groups,
// and locally imported game icons attached only through published identities. Synthetic assets only; no game art in the repo.
public sealed class ReadabilityPassTests
{
    private static async Task<SdkMetadata> Bundled(TestEnvironment e) { File.Delete(e.Paths.CachePath("current.json")); return await e.Cache.GetCurrentAsync(); }

    [Fact] public async Task Support_equipment_is_merged_into_support_stratagems_or_listed_once_as_unlinked()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.24.0"); // 0.24.0: C4 unlinked; 0.25.x links it (Runtime025Tests)
        Assert.Equal(["B/MD C4 Pack", "CQC-72 Entrenchment Tool", "SG-88 Break-Action Shotgun"], SupportEquipment.Unlinked(sdk));
        Assert.DoesNotContain(Navigation.Build(sdk, null), i => i.Page == "support");
        // Every support item has exactly one home: its linked Support stratagem, or the unlinked list.
        foreach (var weapon in sdk.Advanced!.Support.Weapons.Keys)
        {
            var link = sdk.SupportLinks!.ForWeapon(weapon);
            Assert.True(link != null ^ SupportEquipment.Unlinked(sdk).Contains(weapon), weapon);
            if (link != null) Assert.Contains(link.Stratagem.Family, StratagemCategories.Support.Families);
        }
        // The C4 call-in exists, but Runtime publishes no delivery link; it is never paired by name.
        var c4 = sdk.Stratagems!.Stratagems.Single(s => s.Name == "B/MD C4 Pack");
        Assert.Null(sdk.SupportLinks!.ForStratagem(c4.Name)); Assert.False(c4.Delivers!.Known); Assert.False(string.IsNullOrWhiteSpace(c4.Delivers.Blocker));
    }
    [Fact] public async Task Older_sdks_keep_their_support_equipment_reachable()
    {
        using var e = new TestEnvironment();
        var noLinks = await SdkFixtures.Install(e, "0.22.0");
        Assert.Null(noLinks.SupportLinks); Assert.Equal(noLinks.Advanced!.Support.Weapons.Count, SupportEquipment.Unlinked(noLinks).Count);
        Assert.DoesNotContain(Navigation.Build(noLinks, null), i => i.Page == "support");
        // Before the stratagem catalog existed there is no Support stratagem to hold equipment, so the destination stays.
        var noStratagems = await SdkFixtures.Install(e, "0.20.1");
        Assert.Null(noStratagems.Stratagems);
        var nav = Navigation.Build(noStratagems, null).Single(i => i.Page == "support");
        Assert.Equal("Support equipment", nav.Label); Assert.Equal(noStratagems.Advanced!.Support.Weapons.Count, nav.Count);
    }
    [Fact] public async Task Stratagem_search_covers_semantic_id_and_type()
    {
        using var e = new TestEnvironment(); var c = (await Bundled(e)).Stratagems!; var bastion = c.Stratagems.Single(s => s.Name == "TD-220 Bastion MK XVI");
        Assert.True(StratagemBrowser.Matches(c, bastion, search: "80b5f97195d26140"));
        Assert.True(StratagemBrowser.Matches(c, bastion, search: "vehicle"));
        Assert.False(StratagemBrowser.Matches(c, bastion, search: "orbital"));
    }

    [Theory]
    [InlineData("weapon.capacity", "Ammo")] [InlineData("magazine.spare_magazines", "Ammo")] [InlineData("reload.duration", "Ammo")] [InlineData("rounds.max_rounds", "Ammo")]
    [InlineData("weapon.ergonomics", "Handling")] [InlineData("weapon.sway", "Handling")] [InlineData("weapon.horizontal_spread", "Handling")] [InlineData("weapon.recoil_climb", "Handling")]
    [InlineData("weapon.fire_rate", "Firing")] [InlineData("windup.wind_up_seconds", "Firing")] [InlineData("charge.level_1", "Firing")]
    [InlineData("projectile.lifetime", "Projectile")] [InlineData("projectile.penetration_slowdown", "Projectile")]
    [InlineData("damage.durable_damage", "Damage")] [InlineData("explosion.outer_radius", "Explosion")] [InlineData("explosion.damage.standard_damage", "Explosion")]
    public void Fields_group_into_readable_sections(string field, string group) => Assert.Equal(group, FieldGroups.Of(field));
    [Fact] public async Task Every_published_support_field_lands_in_a_named_group_in_a_stable_order()
    {
        using var e = new TestEnvironment(); var sdk = await Bundled(e);
        var groups = FieldGroups.Group(sdk.SupportAuthoring!.FieldInstances, f => f.SemanticFieldId, f => f.Display.Domain).Select(g => g.Key).ToArray();
        Assert.All(groups, g => Assert.Contains(g, FieldGroups.Order));
        Assert.Equal(groups.OrderBy(FieldGroups.Rank), groups);
    }

    private const string Xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <DataTemplate x:Key="StratagemTest"><Viewbox><Canvas Width="256" Height="256">
            <Path Fill="#FF2B2B2B" Data="F1M0,0h256v256h-256z" Stretch="Fill" Height="256" Width="256" Canvas.Left="0" Canvas.Top="0" />
            <Path Data="M10 10L20 20Z" Fill="#80FFDD1F" Canvas.Left="4" Canvas.Top="6" />
          </Canvas></Viewbox></DataTemplate>
          <DataTemplate x:Key="StratagemEmpty"><Viewbox><Canvas Width="256" Height="256"></Canvas></Viewbox></DataTemplate>
          <DataTemplate x:Key="StratagemEvil"><Canvas Width="10" Height="10"><Path Data="M0 0&quot;/&gt;&lt;script&gt;alert(1)&lt;/script&gt;" Fill="#FFFFFFFF" /></Canvas></DataTemplate>
          <DataTemplate x:Key="StratagemTypeDataTemplate"><ContentControl x:Name="C"/><DataTemplate.Triggers>
            <DataTrigger Binding="{Binding}" Value="DropoffTank"><Setter TargetName="C" Property="ContentTemplate" Value="{DynamicResource StratagemTest}"/></DataTrigger>
          </DataTemplate.Triggers></DataTemplate>
        </ResourceDictionary>
        """;
    private static byte[] Resource(string text)
    {
        var body = Encoding.UTF8.GetBytes(text); var r = new byte[16 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(r, (uint)body.Length); body.CopyTo(r, 16); return r;
    }
    [Fact] public void Vector_xaml_templates_convert_to_safe_svg()
    {
        var text = XamlIcons.Text(Resource(Xaml)); var svgs = XamlIcons.ToSvgs(text);
        Assert.Equal(["StratagemTest"], svgs.Keys);
        var svg = svgs["StratagemTest"];
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\">", svg);
        Assert.Contains("<path d=\"M0,0h256v256h-256z\" fill-rule=\"nonzero\" fill=\"#2B2B2B\"/>", svg);
        Assert.Contains("fill=\"#FFDD1F\" fill-opacity=\"0.502\" transform=\"translate(4 6)\"", svg);
        Assert.DoesNotContain("script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new Dictionary<string, string> { ["DropoffTank"] = "StratagemTest" }, XamlIcons.TypeBindings(text, "StratagemTypeDataTemplate"));
        Assert.Throws<InvalidDataException>(() => XamlIcons.Text(new byte[8]));
    }
    [Fact] public void Lz4_blocks_decode_literals_and_matches()
    {
        Assert.Equal("abcabcabc"u8.ToArray(), Lz4.DecompressBlock([0x32, (byte)'a', (byte)'b', (byte)'c', 3, 0], 9));
        Assert.Throws<InvalidDataException>(() => Lz4.DecompressBlock([0x32, (byte)'a', (byte)'b', (byte)'c', 9, 0], 9));
    }

    // Synthetic Stingray archive: header, one type entry, one 80-byte entry per resource, then the resource data.
    internal static byte[] Archive(IReadOnlyList<(ulong Name, byte[] Data)> resources)
    {
        var type = GameDataReader.Hash("xaml"); var tableSize = 72 + 32 + 80 * resources.Count; var data = new MemoryStream();
        var table = new byte[tableSize]; BinaryPrimitives.WriteUInt32LittleEndian(table, 0xF0000011);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), 1); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(8), (uint)resources.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(72 + 8), type);
        for (var i = 0; i < resources.Count; i++)
        {
            var e = table.AsSpan(104 + 80 * i, 80);
            BinaryPrimitives.WriteUInt64LittleEndian(e, resources[i].Name); BinaryPrimitives.WriteUInt64LittleEndian(e[8..], type);
            BinaryPrimitives.WriteUInt64LittleEndian(e[16..], (ulong)(tableSize + data.Length)); BinaryPrimitives.WriteUInt32LittleEndian(e[56..], (uint)resources[i].Data.Length);
            data.Write(resources[i].Data);
        }
        return [.. table, .. data.ToArray()];
    }
    private static readonly string BoosterXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <DataTemplate x:Key="BoosterArmedpods"><Viewbox><Canvas Width="256" Height="256"><Path Data="M70.5 228L13 128Z" Fill="#FFFFDD1F" /></Canvas></Viewbox></DataTemplate>
          <DataTemplate x:Key="BoosterDataTemplate"><ContentControl x:Name="B"/><DataTemplate.Triggers>
            <DataTrigger Binding="{Binding}" Value="DefensiveAmmoPod"><Setter TargetName="B" Property="ContentTemplate" Value="{DynamicResource BoosterArmedpods}"/></DataTrigger>
          </DataTemplate.Triggers></DataTemplate>
        </ResourceDictionary>
        """;
    internal static (ulong, byte[])[] Libraries() =>
        [(GameDataReader.Hash(XamlIcons.StratagemLibrary), Resource(Xaml)), (GameDataReader.Hash(XamlIcons.BoosterLibrary), Resource(BoosterXaml))];

    [Fact] public async Task Icons_import_from_a_fat_edition_data_folder_and_attach_only_through_published_booster_identity()
    {
        using var e = new TestEnvironment(); var data = Path.Combine(e.Paths.Root, "game-fat"); Directory.CreateDirectory(data);
        File.WriteAllBytes(Path.Combine(data, "9ba626afa44a3aa3"), Archive([]));
        File.WriteAllBytes(Path.Combine(data, "0123456789abcdef"), Archive(Libraries()));
        var store = new GameIconStore(e.Paths); var manifest = await store.ImportAsync(data);
        Assert.Equal([1, 1], manifest.Sources.Select(s => s.Icons)); Assert.Equal(1, store.Count(GameIconStore.Booster)); Assert.Equal(1, store.Count(GameIconStore.Stratagem));
        Assert.Equal("BoosterArmedpods", manifest.BoosterTypeIcons["DefensiveAmmoPod"]);
        var sdk = await SdkFixtures.Install(e, "0.24.0"); var boosters = sdk.Entities!.Boosters!; // stratagems publish no icon identity in 0.24.0
        var pods = boosters.Find("Armed Resupply Pods")!;
        Assert.Equal("BoosterArmedpods", store.BoosterIcon(pods)); Assert.StartsWith("data:image/svg+xml;base64,", store.DataUri(GameIconStore.Booster, store.BoosterIcon(pods)));
        // No published icon identity (Integrated Extinguishers) or no matching game binding: no icon, category glyph instead.
        Assert.Null(store.BoosterIcon(boosters.Find("Integrated Extinguishers")!));
        Assert.Null(store.BoosterIcon(boosters.Find("Dead Sprint")!));
        Assert.Null(store.DataUri(GameIconStore.Booster, "BoosterMissing")); Assert.Null(store.Svg(GameIconStore.Booster, "../manifest"));
        // Stratagem roots publish no icon identity, so none is attached even when icons are imported.
        Assert.All(sdk.Stratagems!.Stratagems, s => Assert.Null(GameIconStore.StratagemIconKey(s)));
        // Reloading from disk gives the same answers; removing icons falls back cleanly.
        Assert.Equal("BoosterArmedpods", new GameIconStore(e.Paths).BoosterIcon(pods));
        store.Clear(); Assert.Null(store.Manifest); Assert.Null(store.BoosterIcon(pods)); Assert.Equal(0, store.Count(GameIconStore.Booster));
    }
    [Fact] public async Task Published_booster_icon_identities_match_the_game_template_for_18_of_20()
    {
        using var e = new TestEnvironment(); var boosters = (await Bundled(e)).Entities!.Boosters!.Boosters;
        var bindings = boosters.Where(b => b.Identity.NativeName != null && b.Identity.UiIcon != null).ToDictionary(b => b.Identity.NativeName!, b => b.Identity.UiIcon!);
        var manifest = new IconManifest(1, "", DateTimeOffset.UnixEpoch, [], [], bindings);
        Assert.Equal(18, boosters.Count(b => GameIconStore.BoosterIconKey(b, manifest) != null));
        Assert.All(boosters.Where(b => b.Identity.Status is "EFFECT_CATEGORY" or "ELIMINATION"), b => Assert.Null(GameIconStore.BoosterIconKey(b, manifest)));
        Assert.All(boosters, b => Assert.Null(GameIconStore.BoosterIconKey(b, null)));
    }
    [Fact] public async Task Icons_import_from_a_slim_edition_bundle_with_lz4_chunks()
    {
        using var e = new TestEnvironment(); var data = Path.Combine(e.Paths.Root, "game-slim"); Directory.CreateDirectory(data);
        var libraries = Libraries(); var archive = Archive(libraries); var tableSize = 72 + 32 + 80 * 2;
        // Bundle chunks start at resource boundaries, as in the game: the archive table, the first resource as LZ4 (literals only),
        // then the second resource uncompressed.
        var table = archive[..tableSize]; var first = libraries[0].Item2; var second = libraries[1].Item2;
        var lz4 = new MemoryStream(); lz4.WriteByte(0xF0); var extra = first.Length - 15; while (extra >= 255) { lz4.WriteByte(255); extra -= 255; } lz4.WriteByte((byte)extra); lz4.Write(first);
        File.WriteAllBytes(Path.Combine(data, "bundles.00.nxa"), Dsar([(table, 0, (uint)table.Length), (lz4.ToArray(), 3, (uint)first.Length), (second, 0, (uint)second.Length)]));
        var dsaa = Dsaa("0123456789abcdef", (ulong)archive.Length, "bundles.00.nxa");
        File.WriteAllBytes(Path.Combine(data, "bundles.nxa"), Dsar([(dsaa, 0, (uint)dsaa.Length)]));
        var manifest = await new GameIconStore(e.Paths).ImportAsync(data);
        Assert.Equal(2, manifest.Sources.Sum(s => s.Icons)); Assert.Equal("StratagemTest", manifest.StratagemTypeIcons["DropoffTank"]);
    }
    private static byte[] Dsar(IReadOnlyList<(byte[] Data, byte Compression, uint Size)> chunks)
    {
        var header = 32 + 32 * chunks.Count; var output = new byte[header + chunks.Sum(c => c.Data.Length)];
        Encoding.ASCII.GetBytes("DSAR").CopyTo(output, 0); BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), (uint)chunks.Count);
        ulong uncompressed = 0; var offset = header;
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = output.AsSpan(32 + 32 * i, 32);
            BinaryPrimitives.WriteUInt64LittleEndian(c, uncompressed); BinaryPrimitives.WriteUInt64LittleEndian(c[8..], (ulong)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(c[16..], chunks[i].Size); BinaryPrimitives.WriteUInt32LittleEndian(c[20..], (uint)chunks[i].Data.Length); c[24] = chunks[i].Compression;
            chunks[i].Data.CopyTo(output, offset); offset += chunks[i].Data.Length; uncompressed += chunks[i].Size;
        }
        return output;
    }
    // DSAA index with one archive (one entry at archive offset 0 -> bundle 0, uncompressed offset 0) and one bundle file name.
    private static byte[] Dsaa(string archive, ulong size, string bundle)
    {
        var d = new byte[24 + 24 + 4 + 16 + 64];
        Encoding.ASCII.GetBytes("DSAA").CopyTo(d, 0); BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(12), 1); BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(16), 1);
        const int entries = 52, names = 68;
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(24), size); BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(32), names);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(36), 1); BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(40), entries);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(48), names + 17);
        Encoding.ASCII.GetBytes(archive).CopyTo(d, names); Encoding.ASCII.GetBytes(bundle).CopyTo(d, names + 17);
        return d;
    }

    // Stratagem icon identity published by Runtime after 0.24.0 (StratagemAuthoringCapabilities uiIcon).
    private static byte[] WithUiIcons(Action<System.Text.Json.Nodes.JsonArray> edit)
    {
        // Synthetic identities on the icon-free 0.24.0 catalog.
        var j = System.Text.Json.Nodes.JsonNode.Parse(SdkFixtures.Entry("0.24.0", StratagemCatalogReader.FileName))!;
        edit(j["stratagems"]!.AsArray()); return Encoding.UTF8.GetBytes(j.ToJsonString());
    }
    private static System.Text.Json.Nodes.JsonObject Icon(string state, string? key) => new()
    {
        ["state"] = state, ["nativeType"] = key == null ? null : "C4", ["nativeTypeValue"] = key == null ? null : 14, ["iconKey"] = key,
        ["library"] = key == null ? null : StratagemUiIcon.Library0,
    };
    [Fact] public void Published_stratagem_icon_identities_drive_the_icon_key_only_when_resolved()
    {
        var catalog = new StratagemCatalogReader().Read(WithUiIcons(roots =>
        {
            roots.First(r => (string)r!["name"]! == "B/MD C4 Pack")!["uiIcon"] = Icon("resolved", "StratagemC4");
            roots.First(r => (string)r!["name"]! == "M-1000 Maxigun")!["uiIcon"] = Icon("empty_template", "StratagemMaxigun");
            roots.First(r => (string)r!["name"]! == "AC-8 Autocannon")!["uiIcon"] = Icon("unbound", null);
        }));
        string? Key(string name) => GameIconStore.StratagemIconKey(catalog.Stratagems.Single(s => s.Name == name));
        Assert.Equal("StratagemC4", Key("B/MD C4 Pack"));
        Assert.Null(Key("M-1000 Maxigun")); Assert.Null(Key("AC-8 Autocannon")); Assert.Null(Key("Orbital Precision Strike"));
    }
    [Theory] [InlineData("resolved", "../manifest")] [InlineData("resolved", null)] [InlineData("guessed", "StratagemC4")]
    public void Unsafe_stratagem_icon_identities_are_rejected(string state, string? key) =>
        Assert.Throws<InvalidDataException>(() => new StratagemCatalogReader().Read(WithUiIcons(roots => roots[0]!["uiIcon"] = Icon(state, key))));

    [Fact] public async Task Icons_import_automatically_once_until_removed_and_notify_open_pages()
    {
        using var e = new TestEnvironment(); var data = Path.Combine(e.Paths.Root, "game-fat"); Directory.CreateDirectory(data);
        Assert.False(GameIconStore.IsGameData(data));
        File.WriteAllBytes(Path.Combine(data, "9ba626afa44a3aa3"), Archive([]));
        File.WriteAllBytes(Path.Combine(data, "0123456789abcdef"), Archive(Libraries()));
        Assert.True(GameIconStore.IsGameData(data));
        var store = new GameIconStore(e.Paths); var changes = 0; store.Changed += () => changes++;
        Assert.True(store.AutoImportEnabled);
        await store.ImportAsync(data); Assert.Equal(1, changes); Assert.Null(store.AutoImportPlan()); // a valid cache is not re-extracted
        // Removing icons turns automatic import off; a manual import turns it back on.
        store.Clear(); store.DisableAutoImport(); Assert.Equal(3, changes);
        Assert.False(store.AutoImportEnabled); Assert.Null(store.AutoImportPlan());
        Assert.False(new GameIconStore(e.Paths).AutoImportEnabled);
        await store.ImportAsync(data); Assert.True(store.AutoImportEnabled); Assert.Equal(4, changes);
    }
}
