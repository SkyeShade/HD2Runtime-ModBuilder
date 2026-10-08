using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.GameAssets;
using Xunit;
using Xunit.Abstractions;

namespace HD2RuntimeGUI.Tests;

// Parity of the C# custom model pipeline with HD2Runtime's SDK (sdk/tools/hd2_model.py). The oracle is written by
// tools/custom-content/make-model-oracle.py from the SDK on the installed game: SHA-256s and record Lua only, never game bytes.
public sealed class CustomModelCompilerTests(ITestOutputHelper output)
{
    private const string Maxigun = "M-1000 Maxigun";
    private static readonly Lazy<JsonElement> OracleDoc = new(() => JsonDocument.Parse(File.ReadAllBytes(
        Path.Combine(ModBuilderIaTests.Root(), "HD2RuntimeGUI.Tests", "Fixtures", "custom-content", "model-oracle.json"))).RootElement);
    private static JsonElement Oracle => OracleDoc.Value;
    private static ModelBase Facts() => ModelBases.Read(CustomModelCompiler.BundledBases())[Maxigun];
    private static ModelPalette Palette(JsonElement p) => p.ValueKind == JsonValueKind.String ? ModelPalette.DebugPalette
        : new(false, p.TryGetProperty("all", out var all) ? all.GetString() : null,
            p.TryGetProperty("rows", out var rows) ? rows.EnumerateObject().ToDictionary(r => r.Name, r => r.Value.GetString()!) : new Dictionary<string, string>());
    private static ModelRecord Record(JsonElement r) => new(S(r, "base"), S(r, "archive"), S(r, "build"), S(r, "baseUnit"), S(r, "baseUnitSha256"), S(r, "unit"),
        S(r, "material"), S(r, "lut"), S(r, "palette"), r.GetProperty("rows").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray(),
        new(S(r.GetProperty("sha256"), "unit"), S(r.GetProperty("sha256"), "material"), S(r.GetProperty("sha256"), "lut")));
    private static string S(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    [Fact] public void Bundled_facts_are_the_oracles_and_parse_to_the_reviewed_maxigun_base()
    {
        Assert.Equal(S(Oracle, "factsSha256"), CustomModelCompiler.Sha256(CustomModelCompiler.BundledBases()));
        var f = Facts();
        Assert.Equal(("b2c627b3ba7e0c0a", "F5FEE03DCFDB", 0x43A58CB89CFA197CUL), (f.Archive, f.Build, f.Unit.ResourceHash));
        Assert.Equal((1, "m_weapon", 0x24C8, 0xB8F8D0F13850E657UL), (f.Body.SlotIndex, f.Slots[1].Name, f.Slots[1].Offset, f.Body.MaterialHash));
        Assert.Equal((0x114, 23, 8, 5, 10), (f.Body.LutOffset, f.Lut.Width, f.Lut.Height, f.Lut.Mips, f.Lut.Dxgi));
        Assert.Equal("b2c627b3ba7e0c0a.patch_0", CustomModelCompiler.PatchArchiveName(f));
        Assert.Equal(Oracle.GetProperty("baseParts").EnumerateArray().Select(p => (Convert.ToUInt64(S(p, "type"), 16), Convert.ToUInt64(S(p, "name"), 16))),
            CustomModelCompiler.BaseKeys(f));
        Assert.Throws<InvalidDataException>(() => CustomModelCompiler.PatchArchiveName(f with { Archive = "9ba626afa44a3aa3" }));
    }
    [Theory]
    [InlineData("\"format\": 1", "\"format\": 2")]
    [InlineData("\"archive\": \"b2c627b3ba7e0c0a\"", "\"archive\": \"../escape\"")]
    [InlineData("\"mainSha256\": \"f1fc", "\"mainSha256\": \"F1FC")]
    [InlineData("\"slotIndex\": 1", "\"slotIndex\": 3")]
    [InlineData("\"lutAt\": \"0x114\"", "\"lutAt\": \"0x100000000\"")]
    [InlineData("\"resource\": \"0x43A58CB89CFA197C\"", "\"resource\": \"43A58CB89CFA197C\"")]
    [InlineData("\"dxgi\": 10", "\"dxgi\": \"10\"")]
    [InlineData("\"bases\": {", "\"bases\": [{")]
    [InlineData("\"width\": 23", "\"width\": 23,")]
    public void Malformed_facts_fail_closed(string from, string to)
    {
        var json = Encoding.UTF8.GetString(CustomModelCompiler.BundledBases());
        Assert.Contains(from, json);
        Assert.Throws<InvalidDataException>(() => ModelBases.Read(Encoding.UTF8.GetBytes(json.Replace(from, to))));
    }

    [Fact] public void Palettes_map_rows_like_the_sdk()
    {
        var debug = CustomModelCompiler.PaletteRows(ModelPalette.DebugPalette, 10);
        Assert.Equal((1.0, 32 / 255.0, 32 / 255.0), debug[0]); Assert.Equal(debug[0], debug[8]);
        var custom = CustomModelCompiler.PaletteRows(new(false, "#ff0000", new Dictionary<string, string> { ["2"] = "#00FF00", ["07"] = "#0000ff" }), 8);
        Assert.Equal((1.0, 0.0, 0.0), custom[0]); Assert.Equal((0.0, 1.0, 0.0), custom[2]); Assert.Equal((0.0, 0.0, 1.0), custom[7]);
        var rowsOnly = CustomModelCompiler.PaletteRows(new(false, null, new Dictionary<string, string> { ["0"] = "#000000" }), 8);
        Assert.Equal((0.0, 0.0, 0.0), rowsOnly[0]); Assert.All(rowsOnly[1..], r => Assert.Null(r));
    }
    [Theory]
    [InlineData(false, "#GG0000", null, null, "a palette colour is \"#RRGGBB\": '#GG0000'")]
    [InlineData(false, "red", null, null, "a palette colour is \"#RRGGBB\": 'red'")]
    [InlineData(false, null, "8", "#FFFFFF", "palette rows are \"0\"..\"7\": '8'")]
    [InlineData(false, null, "-1", "#FFFFFF", "palette rows are \"0\"..\"7\": '-1'")]
    [InlineData(false, null, " 1", "#FFFFFF", "palette rows are \"0\"..\"7\": ' 1'")]
    [InlineData(false, null, "1", "#FFF", "a palette colour is \"#RRGGBB\": '#FFF'")]
    [InlineData(false, null, null, null, "palette must be \"debug\" or {\"all\": \"#RRGGBB\", \"rows\": {\"<row>\": \"#RRGGBB\"}}")]
    [InlineData(true, "#FFFFFF", null, null, "palette must be \"debug\" or {\"all\": \"#RRGGBB\", \"rows\": {\"<row>\": \"#RRGGBB\"}}")]
    public void Invalid_palettes_fail_with_the_sdk_wording(bool debug, string? all, string? row, string? colour, string message)
    {
        var rows = row == null ? new Dictionary<string, string>() : new Dictionary<string, string> { [row] = colour! };
        Assert.Equal(message, Assert.Throws<InvalidDataException>(() => CustomModelCompiler.PaletteRows(new(debug, all, rows), 8)).Message);
    }

    // struct.pack('<e', v) over 2000 doubles: ties to even, one double ulp either side of every tie (no double rounding), subnormals.
    [Fact] public void Half_rounding_matches_python_struct_e()
    {
        var table = Oracle.GetProperty("half").EnumerateArray().ToArray(); Assert.True(table.Length >= 2000);
        var bad = table.Select(t => (Value: BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Convert.FromHexString(t[0].GetString()!))),
            Want: BinaryPrimitives.ReadUInt16LittleEndian(Convert.FromHexString(t[1].GetString()!))))
            .Where(t => CustomModelCompiler.HalfBits(t.Value) != t.Want).Select(t => $"{t.Value:R} -> {CustomModelCompiler.HalfBits(t.Value):X4} (python {t.Want:X4})").ToArray();
        Assert.True(bad.Length == 0, string.Join("\n", bad.Take(20)));
    }
    [Fact] public void Lut_mips_average_clamped_blocks_like_the_sdk()
    {
        var lut = Oracle.GetProperty("lutMips"); int w = lut.GetProperty("width").GetInt32(), h = lut.GetProperty("height").GetInt32();
        var raw = Convert.FromHexString(S(lut, "top")); var top = new double[h, w, 4];
        for (var i = 0; i < w * h * 4; i++) top[i / 4 / w, i / 4 % w, i % 4] = (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2 * i)));
        Assert.Equal(S(lut, "out"), Convert.ToHexStringLower(CustomModelCompiler.LutMips(top, w, h, lut.GetProperty("mips").GetInt32())));
    }
    [Fact] public void Record_lua_is_the_sdks_byte_for_byte()
    {
        var cases = Oracle.GetProperty("cases").EnumerateArray().ToArray();
        foreach (var c in cases)
            Assert.Equal(S(c, "recordLua"), CustomModelCompiler.RecordLua(new Dictionary<string, ModelRecord> { [S(c, "modelId")] = Record(c.GetProperty("record")) }));
        Assert.Equal(S(Oracle, "allRecordLua"), CustomModelCompiler.RecordLua(cases.Reverse().ToDictionary(c => S(c, "modelId"), c => Record(c.GetProperty("record")))));
        // json.dumps escaping (ensure_ascii: quotes, controls, DEL, non-ASCII and astral characters) and code point order of ids.
        Assert.Equal(S(Oracle, "oddRecordLua"), CustomModelCompiler.RecordLua(new Dictionary<string, ModelRecord>
        { ["z_last"] = Record(Oracle.GetProperty("oddRecord")), ["a_first"] = Record(cases[0].GetProperty("record")) }));
    }
    [Fact] public void Derive_fails_closed_on_parts_that_are_not_the_reviewed_ones()
    {
        var f = Facts(); var keys = CustomModelCompiler.BaseKeys(f);
        var parts = keys.ToDictionary(k => k, _ => (Main: new byte[] { 1 }, Gpu: Array.Empty<byte>()));
        Assert.Equal("debug_model: the installed M-1000 Maxigun's unit is not the reviewed one (game build F5FEE03DCFDB): another game build or a modded base; the model is not built",
            Assert.Throws<InvalidDataException>(() => CustomModelCompiler.Derive("mods/a/b", "debug_model", Maxigun, ModelPalette.DebugPalette, f, parts)).Message);
        parts.Remove(keys[2]);
        Assert.Equal($"the installed game has no resource 0x59B90501E2825030 of type 0xCD4238C6A0C69E32",
            Assert.Throws<InvalidDataException>(() => CustomModelCompiler.Derive("mods/a/b", "debug_model", Maxigun, ModelPalette.DebugPalette, f, parts)).Message);
        Assert.Throws<InvalidDataException>(() => CustomModelCompiler.Derive("mods/a/b", "Bad-Id", Maxigun, ModelPalette.DebugPalette, f, parts));
    }

    // Fat edition: the GPU part is read at its own offset from <archive>.gpu_resources; a missing companion or zero size is empty.
    [Fact] public void Game_data_reader_reads_gpu_parts_of_a_fat_edition_archive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HD2RuntimeGUI-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "9ba626afa44a3aa3"), ReadabilityPassTests.Archive([]));
            var archive = ReadabilityPassTests.Archive([(11, [1, 2, 3]), (12, [4])]);
            BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(104 + 32), 70); BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(104 + 64), 5);
            File.WriteAllBytes(Path.Combine(dir, "0123456789abcdef"), archive);
            File.WriteAllBytes(Path.Combine(dir, "0123456789abcdef.gpu_resources"), [.. new byte[70], 9, 8, 7, 6, 5, 0]);
            var type = GameDataReader.Hash("xaml");
            var parts = GameDataReader.Open(dir).ReadParts([new(11, type), new(12, type)]);
            Assert.Equal([1, 2, 3], parts[new(11, type)].Main); Assert.Equal([9, 8, 7, 6, 5], parts[new(11, type)].Gpu);
            Assert.Equal([4], parts[new(12, type)].Main); Assert.Empty(parts[new(12, type)].Gpu);
        }
        finally { Directory.Delete(dir, true); }
    }

    // The real game: every oracle case derived in C# must give the SDK's SHA-256 for each part and its exact record Lua. Without the
    // game, or on another game build (base parts not the reviewed ones), there is nothing to compare: the test says so and passes.
    [Fact] public void Derived_models_match_the_sdk_on_the_installed_game()
    {
        var data = Environment.GetEnvironmentVariable("HD2_GAME_DATA") is { Length: > 0 } env ? env : GameIconStore.DefaultGameDataPath();
        if (data == null || !Directory.Exists(data)) { output.WriteLine("SKIPPED: no installed Helldivers 2 data folder found (set HD2_GAME_DATA)."); return; }
        var f = Facts(); var started = DateTime.UtcNow;
        var parts = CustomModelCompiler.GameParts(data, CustomModelCompiler.BaseKeys(f));
        output.WriteLine($"read {parts.Count} base parts from {data} in {(DateTime.UtcNow - started).TotalSeconds:F1} s");
        foreach (var p in Oracle.GetProperty("baseParts").EnumerateArray())
        {
            var (main, gpu) = parts[(Convert.ToUInt64(S(p, "type"), 16), Convert.ToUInt64(S(p, "name"), 16))];
            if (CustomModelCompiler.Sha256(main) != S(p, "mainSha256") || CustomModelCompiler.Sha256(gpu) != S(p, "gpuSha256"))
            { output.WriteLine($"SKIPPED: the installed base part {S(p, "name")} is not the reviewed one (another game build {f.Build} or a modded base)."); return; }
        }
        var records = new Dictionary<string, ModelRecord>();
        foreach (var c in Oracle.GetProperty("cases").EnumerateArray())
        {
            var id = S(c, "modelId");
            var d = CustomModelCompiler.Derive(S(Oracle, "resource"), id, Maxigun, Palette(c.GetProperty("palette")), f, parts);
            var want = c.GetProperty("resources").EnumerateArray().Select(r => $"{S(r, "type")} {S(r, "name")} {S(r, "mainSha256")} {S(r, "gpuSha256")}");
            var got = d.Resources.OrderBy(r => r.Key).Select(r => $"0x{r.Key.Type:X16} 0x{r.Key.Name:X16} {CustomModelCompiler.Sha256(r.Value.Main)} {CustomModelCompiler.Sha256(r.Value.Gpu)}");
            Assert.Equal(want, got);
            Assert.Equal(S(c, "recordLua"), CustomModelCompiler.RecordLua(new Dictionary<string, ModelRecord> { [id] = d.Record }));
            Assert.Equivalent(Record(c.GetProperty("record")), d.Record, strict: true);
            records[id] = d.Record;
            output.WriteLine($"{id}: unit {d.Record.Sha256.Unit[..12]}, material {d.Record.Sha256.Material[..12]}, lut {d.Record.Sha256.Lut[..12]} = SDK");
        }
        Assert.Equal(S(Oracle, "allRecordLua"), CustomModelCompiler.RecordLua(records));
        output.WriteLine($"PARITY: {records.Count} models derived from the installed game match the SDK oracle.");
    }
}
