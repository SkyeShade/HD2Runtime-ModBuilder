using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.GameAssets;
using HD2RuntimeGUI.Core.Generation;

namespace HD2RuntimeGUI.Core.CustomContent;

// The reviewed facts of one model base (HD2Runtime sdk/ModelBaseCapabilities.json "bases"). Hash and offset fields keep the
// facts' own text, because the build record copies some of them verbatim (baseUnit); the parsed values are beside them.
public sealed record ModelBase(string Name, string Archive, string Package, string Build, ModelBaseUnit Unit, IReadOnlyList<ModelBaseSlot> Slots,
    ModelBaseBody Body, ModelBaseLut Lut);
public sealed record ModelBaseUnit(string Resource, long Main, long Gpu, string MainSha256, string GpuSha256, string MaterialsAt)
{ public ulong ResourceHash => ModelBases.Hex(Resource); }
// Name is null where the game's unit names no material slot (b3f42ec: the Cremator, C4 Pack, Commando, Airburst and Speargun bases).
public sealed record ModelBaseSlot(string Slot, string? Name, string Material, string At, string Where)
{ public ulong MaterialHash => ModelBases.Hex(Material); public int Offset => (int)ModelBases.Hex(At); }
public sealed record ModelBaseBody(int SlotIndex, string Material, long Main, string MainSha256, string LutAt)
{ public ulong MaterialHash => ModelBases.Hex(Material); public int LutOffset => (int)ModelBases.Hex(LutAt); }
public sealed record ModelBaseLut(string Texture, long Main, long Gpu, int Width, int Height, int Mips, string FourCC, int Dxgi, string MainSha256, string GpuSha256)
{ public ulong TextureHash => ModelBases.Hex(Texture); }

// A model palette: Python's "debug", or {"all": "#RRGGBB", "rows": {"<row>": "#RRGGBB"}} (All null = no "all" key).
public sealed record ModelPalette(bool Debug, string? All, IReadOnlyDictionary<string, string> Rows)
{
    public static ModelPalette DebugPalette { get; } = new(true, null, new Dictionary<string, string>());
}
public sealed record ModelDigests(string Unit, string Material, string Lut);
// hd2_model.derive's record entry, field for field (Rows: one "#RRGGBB" per LUT row, null where the base colour stays).
public sealed record ModelRecord(string Base, string Archive, string Build, string BaseUnit, string BaseUnitSha256, string Unit, string Material,
    string Lut, string Palette, IReadOnlyList<string?> Rows, ModelDigests Sha256);
public sealed record ModelDerivation(IReadOnlyDictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)> Resources, ModelRecord Record);

public static class ModelBases
{
    private static readonly Regex HexText = new("^0x[0-9A-Fa-f]{1,16}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ArchiveId = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);

    internal static ulong Hex(string text) => ulong.Parse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    // Fails closed on anything the derivation would otherwise read as a guess: hashes, offsets and digests are checked here.
    public static IReadOnlyDictionary<string, ModelBase> Read(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = Obj(doc.RootElement, "the model base facts");
            if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var f) || f != 1)
                throw new InvalidDataException("Unsupported model base facts format (expected 1).");
            var bases = new Dictionary<string, ModelBase>(StringComparer.Ordinal);
            foreach (var p in Obj(Prop(root, "bases"), "bases").EnumerateObject()) bases[p.Name] = Base(p.Name, Obj(p.Value, p.Name));
            return bases;
        }
        catch (JsonException e) { throw new InvalidDataException("The model base facts are not valid JSON: " + e.Message, e); }
    }
    private static ModelBase Base(string name, JsonElement b)
    {
        var archive = Str(b, "archive"); if (!ArchiveId.IsMatch(archive)) throw Bad(name, "archive");
        var u = Obj(Prop(b, "unit"), name + " unit");
        var unit = new ModelBaseUnit(HexStr(u, "resource", name), Long(u, "main", name), Long(u, "gpu", name), ShaStr(u, "mainSha256", name), ShaStr(u, "gpuSha256", name),
            OffsetStr(u, "materialsAt", name));
        var slots = Prop(b, "slots"); if (slots.ValueKind != JsonValueKind.Array) throw Bad(name, "slots");
        var slotList = slots.EnumerateArray().Select(s => { var o = Obj(s, name + " slot");
            var slotName = Prop(o, "name") is { ValueKind: JsonValueKind.Null } ? null : Str(o, "name");
            return new ModelBaseSlot(HexStr(o, "slot", name), slotName, HexStr(o, "material", name), OffsetStr(o, "at", name), Str(o, "where")); }).ToArray();
        var bo = Obj(Prop(b, "body"), name + " body");
        var body = new ModelBaseBody((int)Long(bo, "slotIndex", name), HexStr(bo, "material", name), Long(bo, "main", name), ShaStr(bo, "mainSha256", name), OffsetStr(bo, "lutAt", name));
        if (body.SlotIndex >= slotList.Length) throw Bad(name, "body.slotIndex");
        var lo = Obj(Prop(b, "lut"), name + " lut"); var fo = Obj(Prop(lo, "format"), name + " lut format");
        var lut = new ModelBaseLut(HexStr(lo, "texture", name), Long(lo, "main", name), Long(lo, "gpu", name), (int)Long(fo, "width", name), (int)Long(fo, "height", name),
            (int)Long(fo, "mips", name), Str(fo, "fourcc"), (int)Long(fo, "dxgi", name), ShaStr(lo, "mainSha256", name), ShaStr(lo, "gpuSha256", name));
        if (lut.Width is < 1 or > 4096 || lut.Height is < 1 or > 4096 || lut.Mips is < 1 or > 16) throw Bad(name, "lut.format");
        return new(name, archive, Str(b, "package"), Str(b, "build"), unit, slotList, body, lut);
    }
    private static InvalidDataException Bad(string name, string field) => new($"The model base facts of {name} have an invalid \"{field}\".");
    private static JsonElement Obj(JsonElement e, string what) =>
        e.ValueKind == JsonValueKind.Object ? e : throw new InvalidDataException("The model base facts: " + what + " is not an object.");
    private static JsonElement Prop(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) ? v : throw new InvalidDataException("The model base facts lack \"" + name + "\".");
    private static string Str(JsonElement o, string name) =>
        Prop(o, name) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : throw new InvalidDataException("The model base facts: \"" + name + "\" is not a string.");
    private static string HexStr(JsonElement o, string field, string name) => Str(o, field) is var s && HexText.IsMatch(s) ? s : throw Bad(name, field);
    private static string OffsetStr(JsonElement o, string field, string name) => HexStr(o, field, name) is var s && Hex(s) <= int.MaxValue ? s : throw Bad(name, field);
    private static string ShaStr(JsonElement o, string field, string name) => Str(o, field) is var s && Sha.IsMatch(s) ? s : throw Bad(name, field);
    private static long Long(JsonElement o, string field, string name) =>
        Prop(o, field) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) && n >= 0 && n <= int.MaxValue ? n : throw Bad(name, field);
}

// Port of HD2Runtime's SDK custom models (sdk/tools/hd2_model.py, docs/custom-models.md), byte for byte: a model is a mod's own
// unit derived from a reviewed vanilla weapon unit in the installed game, shipped beside the vanilla resources. Three resources:
// <mod>/models/<id> (the base unit, its body material slot renamed), <id>/m_weapon (the body material, its LUT reference
// renamed), <id>/lut (the material LUT with the palette in its base colour column, mips rebuilt). Every installed base part must
// match the reviewed SHA-256 or nothing is built. Parity is pinned by CustomModelCompilerTests against the SDK on the real game.
//
// Packaging (sdk/hd2.py build_project): all models on the same base archive share ONE patch of that package archive, written
// with hd2_archive.make_resource_archive(resources) -> (archive, gpu) as
//   mod/<archive>.patch_0 = archive, mod/<archive>.patch_0.stream = b"", mod/<archive>.patch_0.gpu_resources = gpu
// (PatchArchiveName; a base archive whose patch would be the boot patch 9ba626afa44a3aa3.patch_0 is refused). The build record
// (RecordLua) is the Lua resource <mod resource>/hd2runtime_models in the mod's own 9ba626afa44a3aa3.patch_0 (src/hd2runtime_models.lua
// is reserved); that archive keeps make_archive's Lua-only layout unless the mod also has images (then make_resource_archive).
public static class CustomModelCompiler
{
    public const ulong UnitType = 0xE0A48D0BE9A7453F, MaterialType = 0xEAC0B497876ADEDF, TextureType = 0xCD4238C6A0C69E32;
    public const string Record = "hd2runtime_models";
    // The reviewed facts of the pinned Runtime (sdk/ModelBaseCapabilities.json), embedded; ModelBases.Read parses them.
    public const string BundledResource = "HD2RuntimeGUI.Core.CustomContent.Targets.ModelBaseCapabilities-0.30.0.json";
    public static byte[] BundledBases()
    {
        using var stream = typeof(CustomModelCompiler).Assembly.GetManifestResourceStream(BundledResource)
            ?? throw new InvalidDataException("The bundled model base facts are missing: " + BundledResource);
        using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
    }
    // One unmistakable colour per material zone (LUT row), so a live look also tells which zone is which.
    public static readonly IReadOnlyList<string> DebugColours = ["#FF2020", "#20FF40", "#2060FF", "#FFE020", "#FF20E0", "#20F0FF", "#FFFFFF", "#FF8010"];
    private static readonly Regex Id = new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Colour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public static bool ValidModelId(string? value) => value != null && Id.IsMatch(value);
    public static string ModelName(string resource, string modelId) => resource + "/models/" + modelId;
    public static (string Unit, string Material, string Lut) Names(string resource, string modelId)
    { var b = ModelName(resource, modelId); return (b, b + "/m_weapon", b + "/lut"); }
    public static string PatchArchiveName(ModelBase facts) => facts.Archive + ".patch_0" is var name && name != GameplayArchive.ArchiveName ? name
        : throw new InvalidDataException("a model never patches the boot archive");
    public static IReadOnlyList<(ulong Type, ulong Name)> BaseKeys(ModelBase facts) =>
        [(UnitType, facts.Unit.ResourceHash), (MaterialType, facts.Body.MaterialHash), (TextureType, facts.Lut.TextureHash)];

    // {(type, name hash): (main, gpu)} of the wanted resources from the installed game's data folder (read-only).
    public static IReadOnlyDictionary<(ulong, ulong), (byte[], byte[])> GameParts(string dataFolder, IReadOnlyCollection<(ulong Type, ulong Name)> keys)
    {
        var found = GameDataReader.Open(dataFolder).ReadParts(keys.Select(k => new GameDataReader.ResourceId(k.Name, k.Type)).ToHashSet());
        var parts = new Dictionary<(ulong, ulong), (byte[], byte[])>();
        foreach (var (type, name) in keys)
            parts[(type, name)] = found.TryGetValue(new(name, type), out var p) ? p : throw Missing(type, name);
        return parts;
    }
    private static InvalidDataException Missing(ulong type, ulong name) => new($"the installed game has no resource 0x{name:X16} of type 0x{type:X16}");

    private static (double R, double G, double B) ParseColour(string? text)
    {
        if (text == null || !Colour.IsMatch(text)) throw new InvalidDataException("a palette colour is \"#RRGGBB\": " + PyRepr(text));
        double C(int k) => int.Parse(text.AsSpan(k, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255.0;
        return (C(1), C(3), C(5));
    }
    // The colour of each LUT row, or null to keep the base's (hd2_model.palette_rows). A palette naming no colour at all is
    // refused: Python accepts {"rows": {}}, which this record cannot tell from {}, and a no-op palette is a mistake anyway.
    public static (double R, double G, double B)?[] PaletteRows(ModelPalette palette, int rows)
    {
        if (palette.Debug)
        {
            if (palette.All != null || palette.Rows.Count > 0) throw BadPalette();
            return Enumerable.Range(0, rows).Select(k => ((double, double, double)?)ParseColour(DebugColours[k % DebugColours.Count])).ToArray();
        }
        if (palette.All == null && palette.Rows.Count == 0) throw BadPalette();
        var all = palette.All != null ? ParseColour(palette.All) : ((double, double, double)?)null;
        var output = Enumerable.Repeat(all, rows).ToArray();
        foreach (var (key, value) in palette.Rows)
        {
            // str.isdigit / int(): ASCII digits here; leading zeros name the same row, as in Python.
            if (key.Length == 0 || !key.All(char.IsAsciiDigit) || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row >= rows)
                throw new InvalidDataException($"palette rows are \"0\"..\"{rows - 1}\": {PyRepr(key)}");
            output[row] = ParseColour(value);
        }
        return output;
    }
    private static InvalidDataException BadPalette() => new("palette must be \"debug\" or {\"all\": \"#RRGGBB\", \"rows\": {\"<row>\": \"#RRGGBB\"}}");
    private static string PyRepr(string? s) => s == null ? "None" : s.Contains('\'') && !s.Contains('"') ? "\"" + s.Replace("\\", "\\\\") + "\""
        : "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    // ------------------------------------------------------------------------------------------------ the LUT
    // struct.pack('<e', v): binary16, round half to even straight from the double (System.Half does the same; pinned by a sweep).
    public static ushort HalfBits(double value) => BitConverter.HalfToUInt16Bits((Half)value);
    private static double ReadHalf(ReadOnlySpan<byte> b) => (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(b));

    // The mip chain of an RGBA16F image (top: height x width x 4 doubles): each lower mip the mean of its clamped 2 x 2 block, summed
    // in Python's order (0 + p0 + p1 + p2 + p3, so -0.0 sums to +0.0) and divided by 4.0 in double.
    public static byte[] LutMips(double[,,] top, int width, int height, int mips)
    {
        var levels = new List<double[,,]> { top }; int w = width, h = height;
        for (var m = 1; m < mips; m++)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2); var prev = levels[^1]; var next = new double[nh, nw, 4];
            for (var y = 0; y < nh; y++)
                for (var x = 0; x < nw; x++)
                    for (var c = 0; c < 4; c++)
                    {
                        var sum = 0.0;
                        for (var dy = 0; dy < 2; dy++)
                            for (var dx = 0; dx < 2; dx++) sum += prev[Math.Min(2 * y + dy, h - 1), Math.Min(2 * x + dx, w - 1), c];
                        next[y, x, c] = sum / 4.0;
                    }
            levels.Add(next); w = nw; h = nh;
        }
        var output = new MemoryStream();
        Span<byte> px = stackalloc byte[2];
        foreach (var level in levels)
            for (var y = 0; y < level.GetLength(0); y++)
                for (var x = 0; x < level.GetLength(1); x++)
                    for (var c = 0; c < 4; c++) { BinaryPrimitives.WriteUInt16LittleEndian(px, HalfBits(level[y, x, c])); output.Write(px); }
        return output.ToArray();
    }
    public static byte[] RecolourLut(byte[] gpu, ModelBaseLut format, (double R, double G, double B)?[] rows)
    {
        int width = format.Width, height = format.Height;
        if (format.Dxgi != 10 || rows.Length != height) throw new InvalidDataException("the material LUT is not the reviewed R16G16B16A16_FLOAT image");
        if (gpu.Length < width * height * 8) throw new InvalidDataException("the material LUT is not the reviewed R16G16B16A16_FLOAT image");
        var top = new double[height, width, 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < 4; c++) top[y, x, c] = ReadHalf(gpu.AsSpan((y * width + x) * 8 + 2 * c));
                if (x == 0 && rows[y] is { } colour) { top[y, x, 0] = colour.R; top[y, x, 1] = colour.G; top[y, x, 2] = colour.B; }
            }
        var output = LutMips(top, width, height, format.Mips);
        if (output.Length != gpu.Length)
            throw new InvalidDataException($"the rebuilt LUT mip chain differs in size from the base ({output.Length}, {gpu.Length})");
        return output;
    }

    // ------------------------------------------------------------------------------------------------ derive
    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // hd2_model.derive: the model's three resources and its build record. parts: {(type, name hash): (main, gpu)} of BaseKeys(facts)
    // from the installed game (GameParts); every base part must be byte-identical to the reviewed one.
    public static ModelDerivation Derive(string resource, string modelId, string baseName, ModelPalette palette, ModelBase facts,
        IReadOnlyDictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)> parts)
    {
        if (!ValidModelId(modelId)) throw new InvalidDataException("a model id is 1 to 64 lowercase letters, digits or underscores: " + modelId);
        var keys = BaseKeys(facts); ulong matId = keys[1].Name, lutId = keys[2].Name;
        (byte[] Main, byte[] Gpu) Part((ulong Type, ulong Name) k) => parts.TryGetValue(k, out var p) ? p : throw Missing(k.Type, k.Name);
        var (unitMain, unitGpu) = Part(keys[0]); var (matMain, _) = Part(keys[1]); var (lutMain, lutGpu) = Part(keys[2]);
        foreach (var (data, want, what) in new[] { (unitMain, facts.Unit.MainSha256, "unit"), (unitGpu, facts.Unit.GpuSha256, "unit GPU part"),
            (matMain, facts.Body.MainSha256, "body material"), (lutMain, facts.Lut.MainSha256, "material LUT"), (lutGpu, facts.Lut.GpuSha256, "material LUT GPU part") })
            if (Sha256(data) != want)
                throw new InvalidDataException($"{modelId}: the installed {baseName}'s {what} is not the reviewed one (game build {facts.Build}): another game build or a modded base; the model is not built");
        var n = Names(resource, modelId);
        ulong hUnit = GameplayArchive.ResourceHash(n.Unit), hMaterial = GameplayArchive.ResourceHash(n.Material), hLut = GameplayArchive.ResourceHash(n.Lut);
        var at = facts.Slots[facts.Body.SlotIndex].Offset;
        if (at < 0 || at + 8 > unitMain.Length || BinaryPrimitives.ReadUInt64LittleEndian(unitMain.AsSpan(at)) != matId)
            throw new InvalidDataException(modelId + ": the body material slot is not where the review found it");
        var unit = (byte[])unitMain.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(unit.AsSpan(at), hMaterial);
        var lutAt = facts.Body.LutOffset;
        if (lutAt < 0 || lutAt + 8 > matMain.Length || BinaryPrimitives.ReadUInt64LittleEndian(matMain.AsSpan(lutAt)) != lutId)
            throw new InvalidDataException(modelId + ": the body material's LUT reference is not where the review found it");
        var material = (byte[])matMain.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(material.AsSpan(lutAt), hLut);
        var rows = PaletteRows(palette, facts.Lut.Height);
        var lut = RecolourLut(lutGpu, facts.Lut, rows);
        var resources = new Dictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)>
        { [(UnitType, hUnit)] = (unit, unitGpu), [(MaterialType, hMaterial)] = (material, []), [(TextureType, hLut)] = (lutMain, lut) };
        // round(c * 255) in Python is round-half-even, as Math.Round; c is n / 255.0, so it gives n back.
        static string Hex((double R, double G, double B) c) => "#" + string.Concat(new[] { c.R, c.G, c.B }.Select(v => ((int)Math.Round(v * 255)).ToString("X2", CultureInfo.InvariantCulture)));
        var record = new ModelRecord(baseName, facts.Archive, facts.Build, facts.Unit.Resource, facts.Unit.MainSha256, n.Unit, n.Material, n.Lut,
            palette.Debug ? "debug" : "custom", rows.Select(r => r is { } c ? Hex(c) : null).ToArray(), new(Sha256(unit), Sha256(material), Sha256(lut)));
        return new(resources, record);
    }

    // hd2_model.record_lua: the Lua resource <mod resource>/hd2runtime_models (runtime/model_resources.lua checks it before the
    // model's unit is named anywhere). Strings are json.dumps'd (ensure_ascii), models in code point order of their ids.
    public static string RecordLua(IReadOnlyDictionary<string, ModelRecord> records)
    {
        var s = new StringBuilder("-- Generated by the HD2Runtime SDK build (docs/custom-models.md): the models this mod derived.\nreturn {format=1,models={\n");
        foreach (var (id, r) in records.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var rows = string.Join(",", r.Rows.Select(x => string.IsNullOrEmpty(x) ? "false" : Json(x)));
            s.Append($"[{Json(id)}]={{base={Json(r.Base)},archive={Json(r.Archive)},build={Json(r.Build)},baseUnit={Json(r.BaseUnit)},baseUnitSha256={Json(r.BaseUnitSha256)},")
             .Append($"unit={Json(r.Unit)},material={Json(r.Material)},lut={Json(r.Lut)},palette={Json(r.Palette)},rows={{{rows}}},")
             .Append($"sha256={{unit={Json(r.Sha256.Unit)},material={Json(r.Sha256.Material)},lut={Json(r.Sha256.Lut)}}}}},\n");
        }
        return s.Append("}}\n").ToString();
    }
    // json.dumps(str) with ensure_ascii: \" \\ \n \r \t \b \f, every other char outside ' '..'~' as \uXXXX (lowercase hex, UTF-16 units).
    public static string Json(string value)
    {
        var s = new StringBuilder("\"");
        foreach (var ch in value)
            s.Append(ch switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                >= ' ' and <= '~' => ch.ToString(),
                _ => "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture),
            });
        return s.Append('"').ToString();
    }
}
