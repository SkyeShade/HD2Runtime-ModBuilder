using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.GameAssets;
using HD2RuntimeGUI.Core.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace HD2RuntimeGUI.Tests;

// The game's HUD atlas sprites as stratagem and booster icons (a C# port of the HD2Runtime SDK's tools/hd2_hud_icons.py): the bundled
// sprite list, the atlas / texture parsers and block decoders on small synthetic inputs, and an import from a synthetic game. No game art.
public sealed class HudIconTests(ITestOutputHelper output)
{
    // ---- HudIconSprites.json ----
    private static byte[] BundledList()
    {
        using var stream = typeof(HudIconSprites).Assembly.GetManifestResourceStream(HudIconSprites.Resource)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    [Fact] public void The_bundled_sprite_list_is_the_sdk_file_and_names_94_stratagems_and_20_boosters()
    {
        var list = HudIconSprites.Bundled.Value;
        Assert.Equal("F5FEE03DCFDB", list.Build); Assert.Equal(94, list.Stratagems.Count); Assert.Equal(20, list.Boosters.Count);
        Assert.Equal(["CQC-72 Entrenchment Tool", "SG-88 Break-Action Shotgun"], list.Missing.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0x98D919DDDC809B33UL, list.Stratagems["40-K Meltagun"]); Assert.Equal(0x94A2EEACED1E8163UL, list.Boosters["Armed Resupply Pods"]);
        // Byte for byte as HD2Runtime publishes it (sdk/HudIconSprites.json at b3f42ec): LF line endings, no BOM.
        Assert.Equal("D0FE0790114FAE20D911BF44889F40CE5BEBBD0B46BC55C3CB71EA6980C7C267", list.Sha256);
        Assert.DoesNotContain((byte)'\r', BundledList());
    }
    [Theory]
    [InlineData("contract")] [InlineData("schema")] [InlineData("build")] [InlineData("sprite-hex")] [InlineData("sprite-zero")] [InlineData("sprite-shared")]
    [InlineData("extra-top")] [InlineData("extra-entry")] [InlineData("no-boosters")] [InlineData("root")] [InlineData("native")] [InlineData("missing-listed")]
    [InlineData("missing-empty")] [InlineData("name")] [InlineData("duplicate-name")]
    public void Malformed_sprite_lists_are_refused(string fault)
    {
        var j = JsonNode.Parse(BundledList())!.AsObject(); var s = j["stratagems"]!.AsObject(); var b = j["boosters"]!.AsObject();
        switch (fault)
        {
            case "contract": j["contract"] = "hd2runtime.hud_icon_sprites.v2"; break;
            case "schema": j["schemaVersion"] = 2; break;
            case "build": j["build"] = "f5fee03dcfdb"; break;
            case "sprite-hex": s["40-K Meltagun"]!["sprite"] = "0x98D919DDDC809B3"; break;
            case "sprite-zero": s["40-K Meltagun"]!["sprite"] = "0x0000000000000000"; break;
            case "sprite-shared": b["Armed Resupply Pods"]!["sprite"] = "0x98D919DDDC809B33"; break;
            case "extra-top": j["artwork"] = "x"; break;
            case "extra-entry": s["40-K Meltagun"]!["file"] = "a.png"; break;
            case "no-boosters": j.Remove("boosters"); break;
            case "root": s["40-K Meltagun"]!["root"] = -1; break;
            case "native": b["Armed Resupply Pods"]!["nativeName"] = "../x"; break;
            case "missing-listed": j["missing"]!["40-K Meltagun"] = "listed twice"; break;
            case "missing-empty": j["missing"]!["SG-88 Break-Action Shotgun"] = ""; break;
            case "name": s[" padded"] = JsonNode.Parse("{\"sprite\":\"0x1111111111111111\",\"root\":1}"); break;
        }
        var bytes = Encoding.UTF8.GetBytes(j.ToJsonString());
        if (fault == "duplicate-name") bytes = Encoding.UTF8.GetBytes(j.ToJsonString().Replace("\"boosters\":{", "\"boosters\":{\"Dead Sprint\":{\"sprite\":\"0x2222222222222222\",\"nativeName\":\"X\"},"));
        Assert.Throws<InvalidDataException>(() => HudIconSprites.Read(bytes));
    }
    [Fact] public void The_untouched_list_reads_back_from_its_json() => Assert.Equal(94, HudIconSprites.Read(Encoding.UTF8.GetBytes(JsonNode.Parse(BundledList())!.ToJsonString())).Stratagems.Count);

    // ---- atlas records and texture headers ----
    internal static byte[] Atlas(params (ulong Sprite, ulong Page, uint W, uint H, float U0, float V0, float Du, float Dv)[] records)
    {
        var raw = new byte[4 + 40 * records.Length]; BinaryPrimitives.WriteUInt32LittleEndian(raw, (uint)records.Length);
        for (var i = 0; i < records.Length; i++)
        {
            var r = raw.AsSpan(4 + 40 * i); var x = records[i];
            BinaryPrimitives.WriteUInt64LittleEndian(r, x.Sprite); BinaryPrimitives.WriteUInt64LittleEndian(r[8..], x.Page);
            BinaryPrimitives.WriteUInt32LittleEndian(r[16..], x.W); BinaryPrimitives.WriteUInt32LittleEndian(r[20..], x.H);
            BinaryPrimitives.WriteSingleLittleEndian(r[24..], x.U0); BinaryPrimitives.WriteSingleLittleEndian(r[28..], x.V0);
            BinaryPrimitives.WriteSingleLittleEndian(r[32..], x.Du); BinaryPrimitives.WriteSingleLittleEndian(r[36..], x.Dv);
        }
        return raw;
    }
    [Fact] public void Atlas_records_parse_and_short_atlases_are_refused()
    {
        var raw = Atlas((0xAAUL, 0xBBUL, 8, 4, 0.5f, 0.25f, 0.5f, 0.25f), (0xCCUL, 0xBBUL, 4, 4, 0, 0, 0.25f, 0.25f));
        var sprites = TextureAtlas.ParseAtlas(raw);
        Assert.Equal(new AtlasSprite(0xAA, 0xBB, 8, 4, 0.5f, 0.25f, 0.5f, 0.25f), sprites[0]); Assert.Equal(0xCCUL, sprites[1].Name);
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseAtlas(raw[..^1]));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseAtlas([1, 0]));
        var huge = (byte[])raw.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(huge, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseAtlas(huge));
        // Whole-pixel rectangles inside the page only.
        var page = new TexturePage(16, 16, 71);
        Assert.Equal((8, 4, 8, 4), TextureAtlas.Rect(sprites[0], page));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Rect(sprites[0] with { U0 = 0.53f }, page));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Rect(sprites[0] with { U0 = 0.75f, Du = 0.5f }, page));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Rect(sprites[0] with { Du = float.NaN }, page));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Rect(sprites[0] with { Width = 0, Du = 0 }, page));
    }
    [Fact] public void Texture_headers_give_the_page_layout_and_unsupported_ones_are_refused()
    {
        // The game's texture main part: 0xC0-byte header, DDS header, DX10 extension (as CustomIconCompiler writes an icon texture).
        Assert.Equal(new TexturePage(64, 32, 71), TextureAtlas.ParseTexture(CustomIconCompiler.TextureMain(64, 32, 1)));
        foreach (var format in new[] { 72, 77, 78, 80, 28, 29, 87, 91 }) Assert.Equal(format, TextureAtlas.ParseTexture(CustomIconCompiler.TextureMain(8, 8, 1, format)).Format);
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseTexture(CustomIconCompiler.TextureMain(8, 8, 1, 98))); // BC7
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseTexture(CustomIconCompiler.TextureMain(8, 8, 1)[..0x100]));
        var notDds = CustomIconCompiler.TextureMain(8, 8, 1); notDds[0xC0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseTexture(notDds));
        var noDx10 = CustomIconCompiler.TextureMain(8, 8, 1); noDx10[0xC0 + 84] = (byte)'D'; noDx10[0xC0 + 87] = (byte)'1';
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseTexture(noDx10));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseTexture(CustomIconCompiler.TextureMain(0, 8, 1)));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.ParseTexture(CustomIconCompiler.TextureMain(1 << 20, 8, 1)));
    }

    // ---- block decoders ----
    private static byte[] Px(byte[] rgba, int w, int x, int y) => rgba.AsSpan((y * w + x) * 4, 4).ToArray();
    // A 4x4 block whose texel i uses palette index indices[i % indices.Length].
    private static uint Bits2(params int[] indices) { uint bits = 0; for (var i = 0; i < 16; i++) bits |= (uint)indices[i % indices.Length] << (2 * i); return bits; }
    private static byte[] Bc1Block(ushort c0, ushort c1, uint bits)
    {
        var b = new byte[8]; BinaryPrimitives.WriteUInt16LittleEndian(b, c0); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), c1); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), bits); return b;
    }
    private static byte[] AlphaBlock(byte a0, byte a1, params int[] indices)
    {
        var b = new byte[8]; b[0] = a0; b[1] = a1; ulong bits = 0;
        for (var i = 0; i < 16; i++) bits |= (ulong)indices[i % indices.Length] << (3 * i);
        for (var i = 0; i < 6; i++) b[2 + i] = (byte)(bits >> (8 * i));
        return b;
    }
    [Fact] public void Bc1_decodes_four_colour_and_three_colour_blocks()
    {
        var page = new TexturePage(4, 4, 71);
        var four = TextureAtlas.Decode(Bc1Block(0xF800, 0x001F, Bits2(0, 1, 2, 3)), page, 0, 0, 4, 4);
        Assert.Equal([255, 0, 0, 255], Px(four, 4, 0, 0)); Assert.Equal([0, 0, 255, 255], Px(four, 4, 1, 0));
        Assert.Equal([170, 0, 85, 255], Px(four, 4, 2, 0)); Assert.Equal([85, 0, 170, 255], Px(four, 4, 3, 0));
        // c0 <= c1: three colours and transparent black.
        var three = TextureAtlas.Decode(Bc1Block(0x001F, 0xF800, Bits2(0, 1, 2, 3)), page, 0, 0, 4, 4);
        Assert.Equal([127, 0, 127, 255], Px(three, 4, 2, 0)); Assert.Equal([0, 0, 0, 0], Px(three, 4, 3, 0));
        // 6-bit green expands as 5-bit red / blue do (0x07E0 -> 255).
        Assert.Equal([0, 255, 0, 255], Px(TextureAtlas.Decode(Bc1Block(0x07E0, 0, 0), page, 0, 0, 4, 4), 4, 3, 3));
        // ModBuilder's own BC1 encoder (custom stratagem icons) decodes back to its solid colour.
        var solid = Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 64).SelectMany(p => p).ToArray();
        var encoded = CustomIconCompiler.Bc1Mips(8, 8, solid);
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal([255, 0, 0, 255], Px(TextureAtlas.Decode(encoded, new(8, 8, 71), 0, 0, 8, 8), 8, i % 8, i / 8)));
    }
    [Fact] public void Bc3_decodes_interpolated_alpha_over_a_four_colour_block()
    {
        // Eight-value mode (a0 > a1) and the four-colour block even though c0 < c1 (BC3 colour is always four colours).
        var block = AlphaBlock(255, 0, 0, 1, 2, 7).Concat(Bc1Block(0x001F, 0xF800, Bits2(3))).ToArray();
        var rgba = TextureAtlas.Decode(block, new(4, 4, 77), 0, 0, 4, 4);
        Assert.Equal([170, 0, 85, 255], Px(rgba, 4, 0, 0)); Assert.Equal(0, Px(rgba, 4, 1, 0)[3]);
        Assert.Equal(218, Px(rgba, 4, 2, 0)[3]); Assert.Equal(36, Px(rgba, 4, 3, 0)[3]);
        // Six-value mode (a0 <= a1): indices 6 and 7 are 0 and 255.
        var six = TextureAtlas.Decode(AlphaBlock(10, 60, 2, 6, 7, 5).Concat(Bc1Block(0xFFFF, 0, 0)).ToArray(), new(4, 4, 78), 0, 0, 4, 4);
        Assert.Equal([20, 0, 255, 50], six.Where((_, i) => i % 4 == 3).Take(4).ToArray());
    }
    [Fact] public void Bc4_decodes_grey()
    {
        var rgba = TextureAtlas.Decode(AlphaBlock(0, 255, 0, 1, 2, 6, 7), new(4, 4, 80), 0, 0, 4, 4);
        Assert.Equal([0, 0, 0, 255], Px(rgba, 4, 0, 0)); Assert.Equal([255, 255, 255, 255], Px(rgba, 4, 1, 0)); Assert.Equal([51, 51, 51, 255], Px(rgba, 4, 2, 0));
        Assert.Equal([0, 0, 0, 255], Px(rgba, 4, 3, 0)); Assert.Equal([255, 255, 255, 255], Px(rgba, 4, 0, 1));
    }
    [Fact] public void Uncompressed_pages_decode_rgba_and_swap_bgra()
    {
        var pixels = Enumerable.Range(0, 16).SelectMany(i => new byte[] { (byte)i, 100, 200, 255 }).ToArray(); // 4 x 4, pixel i = (i, 100, 200)
        var rgba = TextureAtlas.Decode(pixels, new(4, 4, 28), 1, 2, 2, 1);
        Assert.Equal([9, 100, 200, 255, 10, 100, 200, 255], rgba);
        Assert.Equal([200, 100, 9, 255, 200, 100, 10, 255], TextureAtlas.Decode(pixels, new(4, 4, 87), 1, 2, 2, 1));
    }
    [Fact] public void Only_the_sprite_rows_are_needed_and_short_or_outside_reads_are_refused()
    {
        // An 8 x 8 BC1 page of four blocks; the sprite is the bottom-right block, read from the bytes Rows names (the second block row only).
        var page = new TexturePage(8, 8, 71);
        var blocks = new[] { Bc1Block(0, 0, 0), Bc1Block(0, 0, 0), Bc1Block(0, 0, 0), Bc1Block(0xF800, 0, 0) }.SelectMany(b => b).ToArray();
        var (offset, length) = TextureAtlas.Rows(page, 4, 4); Assert.Equal((16L, 16), (offset, length));
        var rgba = TextureAtlas.Decode(blocks.AsSpan((int)offset, length), page, 4, 4, 4, 4, offset);
        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal([255, 0, 0, 255], Px(rgba, 4, i % 4, i / 4)));
        // A rectangle that is not block-aligned takes only its own texels.
        Assert.Equal([255, 0, 0, 255], Px(TextureAtlas.Decode(blocks, page, 3, 3, 2, 2), 2, 1, 1)); Assert.Equal([0, 0, 0, 255], Px(TextureAtlas.Decode(blocks, page, 3, 3, 2, 2), 2, 0, 0));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Decode(blocks.AsSpan(0, 24), page, 4, 4, 4, 4));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Decode(blocks.AsSpan(16), page, 0, 0, 4, 4, 16));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Decode(blocks, page, 6, 0, 4, 4));
        Assert.Throws<InvalidDataException>(() => TextureAtlas.Decode(blocks, page with { Format = 98 }, 0, 0, 4, 4));
    }
    [Fact] public void Stratagem_masks_are_recoloured_red_to_accent_green_to_white()
    {
        byte[] masks = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 255, 255, 0, 0, 128, 128, 0, 0, 255];
        var rgba = TextureAtlas.Recolour(masks, (121, 174, 252));
        Assert.Equal([121, 174, 252, 255], Px(rgba, 5, 0, 0)); Assert.Equal([255, 255, 255, 255], Px(rgba, 5, 1, 0)); Assert.Equal([0, 0, 0, 0], Px(rgba, 5, 2, 0));
        Assert.Equal([121, 174, 252, 128], Px(rgba, 5, 3, 0)); Assert.Equal([121, 174, 252, 128], Px(rgba, 5, 4, 0));
    }
    [Fact] public void Accents_follow_the_library_icon_then_the_family_then_the_category()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <DataTemplate x:Key="StratagemA"><Canvas><Path Fill="#FF2B2B2B" /><Path Fill="#FFDE7B6C" /><Path Stroke="#FFDE7B6C" /><Path Fill="#FF49ADC9" /></Canvas></DataTemplate>
              <DataTemplate x:Key="StratagemB"><Canvas><Path Fill="#FF49ADC9" /><Path><Path.Fill><SolidColorBrush Color="#FF679552" /></Path.Fill></Path></Canvas></DataTemplate>
              <DataTemplate x:Key="StratagemGrey"><Canvas><Path Fill="#FFFFFFFF" /></Canvas></DataTemplate>
            </ResourceDictionary>
            """;
        var library = XamlIcons.Accents(xaml);
        Assert.Equal((0xDE, 0x7B, 0x6C), library["StratagemA"]); Assert.Equal((0x49, 0xAD, 0xC9), library["StratagemB"]); Assert.False(library.ContainsKey("StratagemGrey"));
        var accents = GameIconStore.StratagemAccents(library, [("A", "orbital", "StratagemA"), ("B", "support", "StratagemB"), ("C", "orbital", null), ("D", "vehicle", null),
            ("E", "sentry", "StratagemGrey")]);
        Assert.Equal((0xDE, 0x7B, 0x6C), accents["A"]); Assert.Equal((0xDE, 0x7B, 0x6C), accents["C"]); // family
        Assert.Equal((0x49, 0xAD, 0xC9), accents["D"]); Assert.Equal((0x49, 0xAD, 0xC9), accents["E"]);  // the support family's
        Assert.Equal((98, 213, 150), GameIconStore.StratagemAccents(new Dictionary<string, (byte, byte, byte)>(), [("E", "sentry", null)])["E"]); // category
    }

    // ---- import from a synthetic game ----
    // A fat-edition archive with typed resources and GPU parts (in its .gpu_resources companion).
    private static void WriteArchive(string folder, string id, IReadOnlyList<(ulong Name, ulong Type, byte[] Main, byte[] Gpu)> resources)
    {
        var tableSize = 72 + 32 + 80 * resources.Count; var data = new MemoryStream(); var gpu = new MemoryStream();
        var table = new byte[tableSize]; BinaryPrimitives.WriteUInt32LittleEndian(table, 0xF0000011);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), 1); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(8), (uint)resources.Count);
        for (var i = 0; i < resources.Count; i++)
        {
            var e = table.AsSpan(104 + 80 * i, 80); var r = resources[i];
            BinaryPrimitives.WriteUInt64LittleEndian(e, r.Name); BinaryPrimitives.WriteUInt64LittleEndian(e[8..], r.Type);
            BinaryPrimitives.WriteUInt64LittleEndian(e[16..], (ulong)(tableSize + data.Length)); BinaryPrimitives.WriteUInt32LittleEndian(e[56..], (uint)r.Main.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(e[32..], (ulong)gpu.Length); BinaryPrimitives.WriteUInt32LittleEndian(e[64..], (uint)r.Gpu.Length);
            data.Write(r.Main); gpu.Write(r.Gpu);
        }
        File.WriteAllBytes(Path.Combine(folder, id), [.. table, .. data.ToArray()]);
        File.WriteAllBytes(Path.Combine(folder, id + ".gpu_resources"), gpu.ToArray());
    }
    internal static string SyntheticGame(string root, bool atlas = true)
    {
        var data = Path.Combine(root, "game-hud"); Directory.CreateDirectory(data);
        File.WriteAllBytes(Path.Combine(data, "9ba626afa44a3aa3"), ReadabilityPassTests.Archive([]));
        var xaml = GameDataReader.Hash("xaml"); var list = HudIconSprites.Bundled.Value;
        var resources = ReadabilityPassTests.Libraries().Select(l => (l.Item1, xaml, l.Item2, Array.Empty<byte>())).ToList();
        if (atlas)
        {
            // One 8 x 4 BC1 page: the Meltagun's mask (left block: R mask, i.e. pure red) and Integrated Extinguishers' colour sprite (right
            // block: green). Armed Resupply Pods' record points at a page the game lacks, so it keeps its vector icon.
            const ulong page = 0x1234UL, lost = 0x5678UL;
            var pixels = Bc1Block(0xF800, 0, 0).Concat(Bc1Block(0x07E0, 0, 0)).ToArray();
            resources.Add((0x77UL, TextureAtlas.AtlasType, Atlas((list.Stratagems["40-K Meltagun"], page, 4, 4, 0, 0, 0.5f, 1), (list.Boosters["Integrated Extinguishers"], page, 4, 4, 0.5f, 0, 0.5f, 1),
                (list.Boosters["Armed Resupply Pods"], lost, 4, 4, 0, 0, 1, 1)), []));
            resources.Add((page, TextureAtlas.TextureType, CustomIconCompiler.TextureMain(8, 4, 1), pixels));
        }
        WriteArchive(data, "0123456789abcdef", resources);
        return data;
    }
    [Fact] public async Task Import_composes_hud_sprites_and_keeps_vector_icons_as_the_fallback()
    {
        using var e = new TestEnvironment(); var data = SyntheticGame(e.Paths.Root);
        var store = new GameIconStore(e.Paths); var manifest = await store.ImportAsync(data);
        Assert.Equal(3, manifest.FormatVersion); Assert.Equal("F5FEE03DCFDB", manifest.SpriteBuild); Assert.Null(manifest.GameBuild); // no bin/helldivers2.exe here
        Assert.Equal(["40-K Meltagun"], manifest.StratagemSprites!.Keys); Assert.Equal(["Integrated Extinguishers"], manifest.BoosterSprites!.Keys);
        Assert.Equal([1, 1, 1, 1], manifest.Sources.Select(s => s.Icons));
        Assert.Equal(2, store.Count(GameIconStore.Stratagem)); Assert.Equal(2, store.Count(GameIconStore.Booster));
        // The stratagem mask is painted in its accent (no library icon of its family in this game: the support category blue); the
        // booster sprite is used as it is.
        var meltagun = store.IconUri(GameIconStore.Stratagem, "40-K Meltagun", null)!;
        Assert.StartsWith("data:image/png;base64,", meltagun);
        var png = CustomIconCompiler.Read(Convert.FromBase64String(meltagun["data:image/png;base64,".Length..]));
        Assert.Equal((4, 4), (png.Width, png.Height)); Assert.Equal([121, 174, 252, 255], Px(png.Rgba, 4, 0, 0));
        var extinguishers = CustomIconCompiler.Read(Convert.FromBase64String(store.SpriteUri(GameIconStore.Booster, "Integrated Extinguishers")!["data:image/png;base64,".Length..]));
        Assert.Equal([0, 255, 0, 255], Px(extinguishers.Rgba, 4, 2, 2));
        // Published boosters: the HUD sprite first, then the vector icon its uiIcon names; none for an item the list leaves without one.
        var boosters = (await SdkFixtures.Install(e, "0.24.0")).Entities!.Boosters!;
        Assert.StartsWith("data:image/png;base64,", store.BoosterIconUri(boosters.Find("Integrated Extinguishers")!));
        Assert.Null(store.BoosterIcon(boosters.Find("Integrated Extinguishers")!)); // still no vector identity
        Assert.StartsWith("data:image/svg+xml;base64,", store.BoosterIconUri(boosters.Find("Armed Resupply Pods")!));
        Assert.Null(store.BoosterIconUri(boosters.Find("Dead Sprint")!));
        Assert.Null(store.SpriteUri(GameIconStore.Stratagem, "SG-88 Break-Action Shotgun")); Assert.NotNull(GameIconStore.NoIconReason("SG-88 Break-Action Shotgun"));
        // Unsafe or unknown names never reach the file system; reloading from disk gives the same icons.
        Assert.Null(store.SpriteUri(GameIconStore.Stratagem, "../manifest")); Assert.Null(store.SpriteUri("throwable", "40-K Meltagun"));
        Assert.Equal(meltagun, new GameIconStore(e.Paths).StratagemIcon(new StratagemCatalogReader().Read(Bundled("StratagemAuthoringCapabilities.json")).Stratagems.Single(s => s.Name == "40-K Meltagun")));
        Assert.Null(store.RefreshReason(data));
    }
    [Fact] public async Task A_game_without_atlases_imports_vector_icons_only()
    {
        using var e = new TestEnvironment(); var data = SyntheticGame(e.Paths.Root, atlas: false);
        var store = new GameIconStore(e.Paths); var manifest = await store.ImportAsync(data);
        Assert.Empty(manifest.StratagemSprites!); Assert.Empty(manifest.BoosterSprites!); Assert.Equal([1, 1, 0, 0], manifest.Sources.Select(s => s.Icons));
        Assert.StartsWith("data:image/svg+xml;base64,", store.IconUri(GameIconStore.Booster, "Armed Resupply Pods", "BoosterArmedpods"));
        Assert.Null(store.RefreshReason(data));
    }
    [Fact] public async Task Caches_of_an_older_format_or_another_sprite_list_are_imported_again()
    {
        using var e = new TestEnvironment(); var data = SyntheticGame(e.Paths.Root);
        var store = new GameIconStore(e.Paths); await store.ImportAsync(data); Assert.Null(store.RefreshReason(data));
        var file = Path.Combine(store.Folder, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!;
        json["formatVersion"] = 2; File.WriteAllText(file, json.ToJsonString());
        Assert.Equal("ModBuilder now reads more icons from the game.", new GameIconStore(e.Paths).RefreshReason(data));
        json["formatVersion"] = 3; json["sources"]![2]!["sha256"] = new string('0', 64); File.WriteAllText(file, json.ToJsonString());
        Assert.Equal("ModBuilder now reads more icons from the game.", new GameIconStore(e.Paths).RefreshReason(data));
        await store.ImportAsync(data); File.Delete(Directory.GetFiles(Path.Combine(store.Folder, GameIconStore.Stratagem, "hud"))[0]);
        Assert.Equal("Imported icon files are missing.", new GameIconStore(e.Paths).RefreshReason(data));
    }
    [Fact] public async Task A_sprite_list_build_mismatch_is_a_warning_against_the_game_or_the_sdk()
    {
        using var e = new TestEnvironment(); var data = SyntheticGame(e.Paths.Root);
        var store = new GameIconStore(e.Paths); await store.ImportAsync(data);
        Assert.Null(store.SpriteBuildMismatch("F5FEE03DCFDB")); Assert.Null(store.SpriteBuildMismatch(null));
        Assert.Equal(("F5FEE03DCFDB", "0123456789AB", false), store.SpriteBuildMismatch("0123456789AB"));
        // The installed game's own build (bin/helldivers2.exe) wins over the SDK's.
        var bin = Path.Combine(data, "..", "bin"); Directory.CreateDirectory(bin); File.WriteAllBytes(Path.Combine(bin, "helldivers2.exe"), [1, 2, 3]);
        await store.ImportAsync(data);
        Assert.Equal(GameIconStore.GameBuild(data), store.Manifest!.GameBuild); Assert.Equal(12, store.Manifest.GameBuild!.Length);
        Assert.Equal(("F5FEE03DCFDB", store.Manifest.GameBuild, true), store.SpriteBuildMismatch("F5FEE03DCFDB"));
    }
    private static byte[] Bundled(string name)
    {
        using var stream = typeof(HudIconSprites).Assembly.GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled." + name)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }

    // ---- the installed game (local only) ----
    // Imports from the real install into a temporary folder and writes a contact sheet of every produced icon to
    // artifacts/survey/icons-contact-sheet.png. Runs only when HD2_GAME_DATA names a Helldivers 2 data folder.
    [Fact, Trait("Category", "Local")] public async Task Installed_game_icons_contact_sheet()
    {
        if (Environment.GetEnvironmentVariable("HD2_GAME_DATA") is not { Length: > 0 } data || !Directory.Exists(data)) { output.WriteLine("SKIPPED: set HD2_GAME_DATA."); return; }
        using var e = new TestEnvironment(); var store = new GameIconStore(e.Paths); var started = DateTime.UtcNow;
        var manifest = await store.ImportAsync(data);
        output.WriteLine($"imported in {(DateTime.UtcNow - started).TotalSeconds:F1} s; sprite build {manifest.SpriteBuild}, game build {manifest.GameBuild}");
        var catalog = new StratagemCatalogReader().Read(Bundled("StratagemAuthoringCapabilities.json"));
        var boosters = BoosterAuthoringReader.Read(Bundled("BoosterAuthoringCapabilities.json"), SdkPin.Version);
        var tiles = new List<(string Name, string? Uri, string Source)>();
        foreach (var s in catalog.Stratagems) tiles.Add((s.Name, store.StratagemIcon(s), store.SpriteUri(GameIconStore.Stratagem, s.Name) != null ? "hud" : store.StratagemIcon(s) != null ? "vector" : "none"));
        foreach (var b in boosters.Boosters) tiles.Add((b.Name, store.BoosterIconUri(b), store.SpriteUri(GameIconStore.Booster, b.Name) != null ? "hud" : store.BoosterIconUri(b) != null ? "vector" : "none"));
        var stratagems = tiles.Take(catalog.Stratagems.Length).ToArray(); var boosterTiles = tiles.Skip(catalog.Stratagems.Length).ToArray();
        output.WriteLine($"stratagems: {stratagems.Count(t => t.Uri != null)} of {stratagems.Length} ({stratagems.Count(t => t.Source == "hud")} HUD sprites, {stratagems.Count(t => t.Source == "vector")} vector)");
        output.WriteLine($"boosters: {boosterTiles.Count(t => t.Uri != null)} of {boosterTiles.Length} ({boosterTiles.Count(t => t.Source == "hud")} HUD sprites, {boosterTiles.Count(t => t.Source == "vector")} vector)");
        foreach (var t in tiles.Where(t => t.Uri == null)) output.WriteLine($"no icon: {t.Name} ({GameIconStore.NoIconReason(t.Name) ?? "?"})");
        // Contact sheet: 12 columns of 96 px tiles on the app's dark panel colour; HUD sprites only (vector icons are SVG).
        const int size = 96, columns = 12, pad = 4; var hud = tiles.Where(t => t.Source == "hud").ToArray();
        int rows = (hud.Length + columns - 1) / columns, width = columns * (size + pad) + pad, height = rows * (size + pad) + pad;
        var sheet = new byte[width * height * 4];
        for (var i = 0; i < sheet.Length; i += 4) { sheet[i] = 0x1B; sheet[i + 1] = 0x1D; sheet[i + 2] = 0x22; sheet[i + 3] = 255; }
        for (var n = 0; n < hud.Length; n++)
        {
            var png = CustomIconCompiler.Read(Convert.FromBase64String(hud[n].Uri!["data:image/png;base64,".Length..]));
            int ox = pad + n % columns * (size + pad), oy = pad + n / columns * (size + pad);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    int sx = x * png.Width / size, sy = y * png.Height / size; var s = (sy * png.Width + sx) * 4; var d = ((oy + y) * width + ox + x) * 4; var a = png.Rgba[s + 3] / 255.0;
                    for (var c = 0; c < 3; c++) sheet[d + c] = (byte)Math.Round(png.Rgba[s + c] * a + sheet[d + c] * (1 - a));
                }
        }
        var root = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(root, "HD2RuntimeGUI.sln"))) root = Path.GetDirectoryName(root)!;
        var outFile = Path.Combine(root, "artifacts", "survey", "icons-contact-sheet.png"); Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        File.WriteAllBytes(outFile, CustomIconCompiler.WritePng(width, height, sheet));
        File.WriteAllLines(Path.ChangeExtension(outFile, ".txt"), hud.Select((t, n) => $"{n / columns},{n % columns}: {t.Name}"));
        output.WriteLine("contact sheet: " + outFile);
        Assert.True(hud.Length > 0);
        // Optional parity with the SDK's own extraction (py sdk/tools/hd2_hud_icons.py OUT; HD2_HUD_REFERENCE=OUT): identical raw sprite pixels.
        if (Environment.GetEnvironmentVariable("HD2_HUD_REFERENCE") is not { Length: > 0 } reference) return;
        var reader = new HudIconReader(GameDataReader.Open(data)); var compared = 0;
        foreach (var kind in new[] { GameIconStore.Stratagem, GameIconStore.Booster })
            foreach (var (name, sprite) in HudIconSprites.Bundled.Value.Of(kind))
            {
                var file = Path.Combine(reference, kind, System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9._-]+", "_").Trim('_') + ".png");
                var want = CustomIconCompiler.Read(File.ReadAllBytes(file)); var got = reader.Sprite(sprite)!.Value;
                Assert.Equal((want.Width, want.Height), (got.Width, got.Height)); Assert.True(want.Rgba.AsSpan().SequenceEqual(got.Rgba), name); compared++;
            }
        output.WriteLine($"PARITY: {compared} raw sprites identical to the SDK's hd2_hud_icons.py output.");
    }
}
