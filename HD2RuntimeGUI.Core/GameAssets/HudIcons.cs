using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.GameAssets;

// The game's own HUD icons of stratagems and boosters, read locally from the installed game's texture atlases (read-only). A C# port of the
// HD2Runtime SDK's tools/hd2_hud_icons.py: HudIconSprites.json names each item's sprite (published by display name); a sprite is a record in a
// texture_atlas resource that points into a page texture. Stratagem sprites are icon masks (R category colour, G white), recoloured here as the
// HD2R Editor's tools/game_icons.py does; booster sprites are colour images. No artwork is shipped: only sprite names are bundled.

// HudIconSprites.json (contract hd2runtime.hud_icon_sprites.v1): display name -> sprite name, and why an item has none.
public sealed record HudIconSpriteList(string Build, string GameDllSha256, IReadOnlyDictionary<string, ulong> Stratagems,
    IReadOnlyDictionary<string, ulong> Boosters, IReadOnlyDictionary<string, string> Missing, string Sha256)
{
    public IReadOnlyDictionary<string, ulong> Of(string kind) => kind switch
    {
        GameIconStore.Stratagem => Stratagems, GameIconStore.Booster => Boosters, _ => new Dictionary<string, ulong>(),
    };
}

public static class HudIconSprites
{
    public const string Contract = "hd2runtime.hud_icon_sprites.v1", Resource = "HD2RuntimeGUI.Core.GameAssets.Bundled.HudIconSprites.json";
    // IconSource resources of the sprite icons in the icon manifest (one per kind).
    public const string StratagemSource = "hud_icon_sprites/stratagem", BoosterSource = "hud_icon_sprites/booster";
    private static readonly string[] Keys = ["schemaVersion", "contract", "build", "gameDllSha256", "note", "sources", "stratagems", "boosters", "missing"];
    private static readonly Regex Sprite = new(@"\A0x[0-9A-F]{16}\z", RegexOptions.CultureInvariant), Build = new(@"\A[0-9A-F]{12}\z", RegexOptions.CultureInvariant),
        Sha = new(@"\A[0-9A-F]{64}\z", RegexOptions.CultureInvariant), Native = new(@"\A[A-Za-z0-9_]{1,64}\z", RegexOptions.CultureInvariant);
    public static readonly Lazy<HudIconSpriteList> Bundled = new(() =>
    {
        using var stream = typeof(HudIconSprites).Assembly.GetManifestResourceStream(Resource) ?? throw new InvalidDataException("HUD icon sprite list missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return Read(buffer.ToArray());
    });

    // Strict: the exact v1 layout, every sprite a non-zero 64-bit name used once, every name plain text; anything else is refused.
    public static HudIconSpriteList Read(byte[] bytes)
    {
        static void Check(bool ok) { if (!ok) throw new InvalidDataException("Invalid HUD icon sprite list."); }
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement; Check(root.ValueKind == JsonValueKind.Object);
        var top = Properties(root, Check); Check(top.Keys.Order(StringComparer.Ordinal).SequenceEqual(Keys.Order(StringComparer.Ordinal)));
        Check(top["schemaVersion"].ValueKind == JsonValueKind.Number && top["schemaVersion"].TryGetInt32(out var schema) && schema == 1);
        string? build = Text(top["build"]), dll = Text(top["gameDllSha256"]);
        Check(Text(top["contract"]) == Contract && build != null && Build.IsMatch(build) && dll != null && Sha.IsMatch(dll));
        Check(top["note"].ValueKind == JsonValueKind.String && top["sources"].ValueKind == JsonValueKind.Object);
        var used = new HashSet<ulong>();
        Dictionary<string, ulong> Items(JsonElement items, string extra, Func<JsonElement, bool> valid)
        {
            Check(items.ValueKind == JsonValueKind.Object);
            var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var (name, entry) in Properties(items, Check))
            {
                Check(ValidName(name) && entry.ValueKind == JsonValueKind.Object);
                var fields = Properties(entry, Check);
                Check(fields.Count == 2 && fields.ContainsKey(extra) && valid(fields[extra]) && Text(fields.GetValueOrDefault("sprite")) is { } s && Sprite.IsMatch(s));
                var sprite = Convert.ToUInt64(Text(fields["sprite"])![2..], 16); Check(sprite != 0 && used.Add(sprite));
                result[name] = sprite;
            }
            return result;
        }
        var stratagems = Items(top["stratagems"], "root", r => r.ValueKind == JsonValueKind.Number && r.TryGetUInt32(out _));
        var boosters = Items(top["boosters"], "nativeName", n => Text(n) is { } v && Native.IsMatch(v));
        Check(top["missing"].ValueKind == JsonValueKind.Object);
        var missing = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, reason) in Properties(top["missing"], Check))
        {
            Check(ValidName(name) && !stratagems.ContainsKey(name) && !boosters.ContainsKey(name) && Text(reason) is { Length: > 0 and <= 512 } why && !why.Any(char.IsControl));
            missing[name] = Text(reason)!;
        }
        return new(build!, dll!, stratagems, boosters, missing, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static bool ValidName(string name) => name.Length is > 0 and <= 128 && name.Trim() == name && !name.Any(char.IsControl);
    private static string? Text(JsonElement? e) => e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    // Object members by name; a repeated name is refused (JsonDocument would otherwise keep both).
    private static Dictionary<string, JsonElement> Properties(JsonElement e, Action<bool> check)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject()) check(result.TryAdd(p.Name, p.Value));
        return result;
    }
}

// One sprite record of a texture_atlas resource: sprite name, page texture name, size in pixels, and its rectangle in UV units.
public sealed record AtlasSprite(ulong Name, ulong Page, uint Width, uint Height, float U0, float V0, float Du, float Dv);
// A texture page's mip 0 layout from its DDS header (DX10 extension): size and DXGI format.
public sealed record TexturePage(int Width, int Height, int Format);

public static class TextureAtlas
{
    public const ulong TextureType = 0xCD4238C6A0C69E32UL, AtlasType = 0x9199BB50B6896F02UL; // murmur64("texture"), murmur64("texture_atlas")
    public const int RecordSize = 40, TextureHeader = 0xC0, MaxSprite = 2048, MaxPage = 16384;
    // DXGI format -> kind and bytes per 4x4 block (BC) or per pixel (RGBA / BGRA): every format a stratagem or booster sprite uses.
    private static readonly Dictionary<int, (char Kind, int Size)> Formats = new()
    {
        [71] = ('1', 8), [72] = ('1', 8), [77] = ('3', 16), [78] = ('3', 16), [80] = ('4', 8), [28] = ('r', 4), [29] = ('r', 4), [87] = ('b', 4), [91] = ('b', 4),
    };
    public static bool Supported(int format) => Formats.ContainsKey(format);

    // Every record of a texture_atlas main part: u32 count, then 40-byte records '<QQII4f'.
    public static AtlasSprite[] ParseAtlas(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 4) throw new InvalidDataException("Texture atlas too short.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        if (4 + RecordSize * (long)count > raw.Length) throw new InvalidDataException("Texture atlas holds fewer records than it declares.");
        var result = new AtlasSprite[count];
        for (var i = 0; i < count; i++)
        {
            var r = raw.Slice(4 + RecordSize * i, RecordSize);
            result[i] = new(BinaryPrimitives.ReadUInt64LittleEndian(r), BinaryPrimitives.ReadUInt64LittleEndian(r[8..]), BinaryPrimitives.ReadUInt32LittleEndian(r[16..]),
                BinaryPrimitives.ReadUInt32LittleEndian(r[20..]), BinaryPrimitives.ReadSingleLittleEndian(r[24..]), BinaryPrimitives.ReadSingleLittleEndian(r[28..]),
                BinaryPrimitives.ReadSingleLittleEndian(r[32..]), BinaryPrimitives.ReadSingleLittleEndian(r[36..]));
        }
        return result;
    }

    // A texture main part: a 0xC0-byte header, then "DDS ", the 124-byte header and the DX10 extension (DXGI format at +128).
    public static TexturePage ParseTexture(ReadOnlySpan<byte> main)
    {
        if (main.Length < TextureHeader + 148 || !main.Slice(TextureHeader, 4).SequenceEqual("DDS "u8)) throw new InvalidDataException("Not a DDS texture.");
        var dds = main[TextureHeader..];
        int height = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(dds[12..]), int.MaxValue), width = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(dds[16..]), int.MaxValue);
        if (!dds.Slice(84, 4).SequenceEqual("DX10"u8)) throw new InvalidDataException("Only DX10 DDS headers are read.");
        var format = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(dds[128..]), int.MaxValue);
        if (!Formats.ContainsKey(format)) throw new InvalidDataException($"Unsupported texture format DXGI {format}.");
        if (width is < 1 or > MaxPage || height is < 1 or > MaxPage) throw new InvalidDataException("Texture size out of range.");
        return new(width, height, format);
    }

    // The sprite's whole-pixel rectangle on its page; refused when it is not one or leaves the page.
    public static (int X, int Y, int W, int H) Rect(AtlasSprite s, TexturePage page)
    {
        double u = (double)s.U0 * page.Width, v = (double)s.V0 * page.Height, du = (double)s.Du * page.Width, dv = (double)s.Dv * page.Height;
        if (!double.IsFinite(u) || !double.IsFinite(v) || !double.IsFinite(du) || !double.IsFinite(dv) || s.Width is < 1 or > MaxSprite || s.Height is < 1 or > MaxSprite)
            throw new InvalidDataException("Sprite size out of range.");
        double x = Math.Round(u), y = Math.Round(v);
        if (Math.Abs(u - x) > 0.01 || Math.Abs(v - y) > 0.01 || Math.Abs(du - s.Width) > 0.5 || Math.Abs(dv - s.Height) > 0.5)
            throw new InvalidDataException($"Sprite 0x{s.Name:X16} is not a whole-pixel rectangle of its page.");
        if (x < 0 || y < 0 || x + s.Width > page.Width || y + s.Height > page.Height) throw new InvalidDataException("Sprite outside its page.");
        return ((int)x, (int)y, (int)s.Width, (int)s.Height);
    }

    // The byte range of mip 0 that holds rows y .. y + h (whole block rows for BC formats), so only those bytes need reading.
    public static (long Offset, int Length) Rows(TexturePage page, int y, int h)
    {
        var (kind, size) = Formats[page.Format];
        long first, end;
        if (kind is 'r' or 'b') { first = (long)y * page.Width * size; end = (long)(y + h) * page.Width * size; }
        else { long row = (page.Width + 3) / 4 * (long)size; first = y / 4 * row; end = (y + h + 3) / 4 * row; }
        return end - first > int.MaxValue ? throw new InvalidDataException("Sprite too large.") : (first, (int)(end - first));
    }

    // RGBA bytes of the w x h rectangle at (x, y) of a page's mip 0; 'pixels' starts at byte 'origin' of mip 0 (Rows). Only the
    // rectangle's own blocks are decoded; BC1 (with its 1-bit alpha), BC3, BC4 (grey) and 8-bit RGBA / BGRA, as hd2_hud_icons.decode.
    public static byte[] Decode(ReadOnlySpan<byte> pixels, TexturePage page, int x, int y, int w, int h, long origin = 0)
    {
        if (!Formats.TryGetValue(page.Format, out var format)) throw new InvalidDataException($"Unsupported texture format DXGI {page.Format}.");
        if (x < 0 || y < 0 || w < 1 || h < 1 || w > MaxSprite || h > MaxSprite || x + w > page.Width || y + h > page.Height) throw new InvalidDataException("Sprite outside its page.");
        var (kind, size) = format; var output = new byte[w * h * 4];
        if (kind is 'r' or 'b')
        {
            for (var row = 0; row < h; row++)
            {
                var line = At(pixels, origin, ((long)(y + row) * page.Width + x) * 4, w * 4); var o = output.AsSpan(row * w * 4, w * 4);
                line.CopyTo(o);
                if (kind == 'b') for (var i = 0; i < o.Length; i += 4) (o[i], o[i + 2]) = (o[i + 2], o[i]);
            }
            return output;
        }
        var blocksWide = (page.Width + 3) / 4; Span<byte> texels = stackalloc byte[64];
        for (var by = y / 4; by < (y + h + 3) / 4; by++)
            for (var bx = x / 4; bx < (x + w + 3) / 4; bx++)
            {
                var block = At(pixels, origin, ((long)by * blocksWide + bx) * size, size);
                switch (kind)
                {
                    case '1': ColourBlock(block, false, texels); break;
                    case '3': ColourBlock(block[8..], true, texels); AlphaBlock(block, texels, 3); break;
                    default: AlphaBlock(block, texels, 0); for (var i = 0; i < 16; i++) { texels[i * 4 + 1] = texels[i * 4 + 2] = texels[i * 4]; texels[i * 4 + 3] = 255; } break;
                }
                for (var i = 0; i < 16; i++)
                {
                    int px = bx * 4 + i % 4 - x, py = by * 4 + i / 4 - y;
                    if (px >= 0 && px < w && py >= 0 && py < h) texels.Slice(i * 4, 4).CopyTo(output.AsSpan((py * w + px) * 4));
                }
            }
        return output;
    }
    private static ReadOnlySpan<byte> At(ReadOnlySpan<byte> pixels, long origin, long offset, int length) =>
        offset - origin < 0 || offset - origin + length > pixels.Length ? throw new InvalidDataException("Texture pixels end early.") : pixels.Slice((int)(offset - origin), length);
    // A BC1 colour block (also the colour half of BC3, always four colours there): RGBA texels in block order.
    public static void ColourBlock(ReadOnlySpan<byte> block, bool four, Span<byte> texels)
    {
        int c0 = BinaryPrimitives.ReadUInt16LittleEndian(block), c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        Span<int> a = [Expand(c0 >> 11 & 31, 5), Expand(c0 >> 5 & 63, 6), Expand(c0 & 31, 5)], b = [Expand(c1 >> 11 & 31, 5), Expand(c1 >> 5 & 63, 6), Expand(c1 & 31, 5)];
        Span<byte> palette = stackalloc byte[16];
        for (var c = 0; c < 3; c++)
        {
            palette[c] = (byte)a[c]; palette[4 + c] = (byte)b[c];
            palette[8 + c] = (byte)(four || c0 > c1 ? (2 * a[c] + b[c]) / 3 : (a[c] + b[c]) / 2);
            palette[12 + c] = (byte)(four || c0 > c1 ? (a[c] + 2 * b[c]) / 3 : 0);
        }
        palette[3] = palette[7] = palette[11] = 255; palette[15] = (byte)(four || c0 > c1 ? 255 : 0);
        for (var i = 0; i < 16; i++) palette.Slice((int)(bits >> (2 * i) & 3) * 4, 4).CopyTo(texels[(i * 4)..]);
    }
    // A BC3 / BC4 alpha block: eight interpolated values (six when a0 <= a1, plus 0 and 255), written to channel 'channel' of each texel.
    public static void AlphaBlock(ReadOnlySpan<byte> block, Span<byte> texels, int channel)
    {
        int a0 = block[0], a1 = block[1]; ulong bits = 0;
        for (var i = 7; i >= 2; i--) bits = bits << 8 | block[i];
        Span<int> palette = stackalloc int[8]; palette[0] = a0; palette[1] = a1;
        if (a0 > a1) for (var i = 1; i <= 6; i++) palette[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        else { for (var i = 1; i <= 4; i++) palette[i + 1] = ((5 - i) * a0 + i * a1) / 5; palette[6] = 0; palette[7] = 255; }
        for (var i = 0; i < 16; i++) texels[i * 4 + channel] = (byte)palette[(int)(bits >> (3 * i) & 7)];
    }
    private static int Expand(int v, int bits) => bits == 5 ? v << 3 | v >> 2 : v << 2 | v >> 4;

    // A stratagem sprite (R the category-colour mask, G the white mask, alpha the sprite's coverage) as the HUD draws it: R in the accent
    // colour, G in white, transparent where neither (HD2R Editor tools/game_icons.py stratagem_mask, drawn like CustomIconCompiler.Preview).
    public static byte[] Recolour(ReadOnlySpan<byte> rgba, (byte R, byte G, byte B) accent)
    {
        var output = new byte[rgba.Length];
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            double cover = rgba[i + 3] / 255.0, red = rgba[i] / 255.0 * cover, white = rgba[i + 1] / 255.0 * cover, a = Math.Min(1.0, red + white);
            if (a <= 0) continue;
            output[i] = (byte)Math.Round(Math.Min(255, (accent.R * red + 255 * white) / a));
            output[i + 1] = (byte)Math.Round(Math.Min(255, (accent.G * red + 255 * white) / a));
            output[i + 2] = (byte)Math.Round(Math.Min(255, (accent.B * red + 255 * white) / a));
            output[i + 3] = (byte)Math.Round(a * 255);
        }
        return output;
    }
}

// The installed game's HUD sprites: every texture atlas record and page texture, indexed once (one pass over every archive table).
public sealed class HudIconReader
{
    private readonly GameDataReader data;
    private readonly Dictionary<ulong, AtlasSprite> sprites = [];
    private readonly Dictionary<ulong, (GameDataReader.Locus Main, GameDataReader.Locus Gpu)> textures = [];
    private readonly Dictionary<ulong, TexturePage> pages = [];
    public int Atlases { get; }
    public HudIconReader(GameDataReader data, CancellationToken ct = default)
    {
        this.data = data;
        foreach (var (id, parts) in data.FindByType(new HashSet<ulong> { TextureAtlas.TextureType, TextureAtlas.AtlasType }, ct))
        {
            if (id.Type == TextureAtlas.TextureType) { textures[id.Name] = parts; continue; }
            ct.ThrowIfCancellationRequested();
            // A damaged atlas is skipped; its sprites fall back to the vector icons. The first atlas naming a sprite wins, as in the SDK.
            try { foreach (var s in TextureAtlas.ParseAtlas(data.Read(parts.Main))) sprites.TryAdd(s.Name, s); Atlases++; }
            catch (InvalidDataException) { }
        }
    }
    public bool Has(ulong sprite) => sprites.ContainsKey(sprite);
    // (width, height, RGBA) of a sprite, or null when no atlas has it. Throws InvalidDataException when it cannot be read or decoded.
    public (int Width, int Height, byte[] Rgba)? Sprite(ulong name)
    {
        if (!sprites.TryGetValue(name, out var s)) return null;
        if (!textures.TryGetValue(s.Page, out var parts)) throw new InvalidDataException($"Atlas page texture 0x{s.Page:X16} is not in the game data.");
        if (!pages.TryGetValue(s.Page, out var page)) pages[s.Page] = page = TextureAtlas.ParseTexture(data.Read(parts.Main));
        var (x, y, w, h) = TextureAtlas.Rect(s, page); var (offset, length) = TextureAtlas.Rows(page, y, h);
        return (w, h, TextureAtlas.Decode(data.ReadGpu(parts.Gpu, offset, length), page, x, y, w, h, offset));
    }
}
