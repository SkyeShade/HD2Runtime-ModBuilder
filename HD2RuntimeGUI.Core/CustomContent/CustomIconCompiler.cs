using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using HD2RuntimeGUI.Core.Localization;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>A custom stratagem icon as the game's own resources: a C# port of HD2Runtime's SDK image build (sdk/tools/hd2_image.py,
/// docs/custom-images.md), byte for byte (the tests compare with the SDK's own output). A 256 x 256 PNG becomes a BC1 texture with its full
/// mip chain (the layout of the game's stratagem icon textures) and the GUI material that names it. "auto" preparation keeps a PNG drawn in
/// the game's mask convention (R = category-coloured art, G = white art, B = 0, opaque) and converts any other picture (red-and-white art)
/// to the masks; "raw" uses the pixels as given. Like GameplayArchive, packaging only: nothing here touches the game.</summary>
public static class CustomIconCompiler
{
    public const int IconSize = 256, IconMips = 9, Bc1Unorm = 71;
    public const ulong TextureType = 0xCD4238C6A0C69E32UL, MaterialType = 0xEAC0B497876ADEDFUL;
    public const string MasksConverter = "icon-masks-2";
    private const uint IconClass = 0xFCE7DA44U, IconShader = 0x3461FF0DU, ImageSlot = 0x3AA8B87EU;
    private const int MaskBlueMax = 128;
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    // The build's cache (the SDK keeps one per project in build/.image-cache): keyed by the conversion rule, the preparation and the PNG digest.
    private static readonly ConcurrentDictionary<string, (byte[] Main, byte[] Gpu, string Label)> cache = new();

    public sealed record Png(int Width, int Height, byte[] Rgba);

    /// <summary>The RGBA pixels of a non-interlaced PNG (8 and 16 bit, every colour type); refuses anything else with the reason.</summary>
    public static Png Read(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature)) throw Invalid("CustomContent.Icon.NotPng");
        int at = 8, width = 0, height = 0, depth = 0, colour = 0;
        bool header = false;
        byte[]? palette = null, alpha = null;
        using var idat = new MemoryStream();
        while (at + 8 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            if (length < 0 || at + 12 + (long)length > data.Length) throw Invalid("CustomContent.Icon.Truncated");
            var kind = data.AsSpan(at + 4, 4);
            var body = data.AsSpan(at + 8, length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8 + length));
            if (Crc32(data.AsSpan(at + 4, 4 + length)) != crc) throw Invalid("CustomContent.Icon.Checksum");
            at += 12 + length;
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (length != 13) throw Invalid("CustomContent.Icon.Truncated");
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(body); height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                depth = body[8]; colour = body[9];
                if (body[10] != 0 || body[11] != 0 || colour is not (0 or 2 or 3 or 4 or 6)) throw Invalid("CustomContent.Icon.ColourType");
                if (body[12] != 0) throw Invalid("CustomContent.Icon.Interlaced");
                header = true;
            }
            else if (kind.SequenceEqual("PLTE"u8)) palette = body.ToArray();
            else if (kind.SequenceEqual("tRNS"u8)) alpha = body.ToArray();
            else if (kind.SequenceEqual("IDAT"u8)) idat.Write(body);
            else if (kind.SequenceEqual("IEND"u8)) break;
        }
        if (!header || idat.Length == 0) throw Invalid("CustomContent.Icon.NoImage");
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw Invalid("CustomContent.Icon.Dimensions");
        var channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        if (colour is 0 or 3 ? depth is not (1 or 2 or 4 or 8 or 16) : depth is not (8 or 16)) throw Invalid("CustomContent.Icon.Depth");
        if (colour == 3 && depth == 16) throw Invalid("CustomContent.Icon.Depth");
        if (colour == 3 && palette == null) throw Invalid("CustomContent.Icon.Palette");
        byte[] raw;
        try
        {
            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            using var output = new MemoryStream(); z.CopyTo(output); raw = output.ToArray();
        }
        catch (InvalidDataException) { throw Invalid("CustomContent.Icon.Truncated"); }
        var bits = channels * depth;
        int stride = (width * bits + 7) / 8, step = Math.Max(1, bits / 8);
        if (raw.Length != height * (long)(stride + 1)) throw Invalid("CustomContent.Icon.Truncated");
        var rgba = new byte[width * height * 4];
        var previous = new byte[stride];
        var line = new byte[stride];
        var o = 0;
        for (var y = 0; y < height; y++)
        {
            var kind = raw[y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, line, 0, stride);
            switch (kind)
            {
                case 0: break;
                case 1: for (var i = step; i < stride; i++) line[i] = (byte)(line[i] + line[i - step]); break;
                case 2: for (var i = 0; i < stride; i++) line[i] = (byte)(line[i] + previous[i]); break;
                case 3: for (var i = 0; i < stride; i++) line[i] = (byte)(line[i] + (((i >= step ? line[i - step] : 0) + previous[i]) >> 1)); break;
                case 4:
                    for (var i = 0; i < stride; i++)
                    {
                        int a = i >= step ? line[i - step] : 0, b = previous[i], c = i >= step ? previous[i - step] : 0;
                        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        line[i] = (byte)(line[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                    }
                    break;
                default: throw Invalid("CustomContent.Icon.Filter");
            }
            for (var x = 0; x < width; x++)
            {
                int Sample(int index)
                {
                    if (depth < 8)
                    {
                        var bit = index * depth;
                        return (line[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
                    }
                    return depth == 16 ? line[index * 2] : line[index];
                }
                switch (colour)
                {
                    case 3:
                        var index = Sample(x);
                        if (3 * index + 3 > palette!.Length) throw Invalid("CustomContent.Icon.Palette");
                        rgba[o++] = palette[3 * index]; rgba[o++] = palette[3 * index + 1]; rgba[o++] = palette[3 * index + 2];
                        rgba[o++] = alpha != null && index < alpha.Length ? alpha[index] : (byte)255;
                        break;
                    case 0:
                        var g = depth < 8 ? Sample(x) * (255 / ((1 << depth) - 1)) : Sample(x);
                        rgba[o++] = (byte)g; rgba[o++] = (byte)g; rgba[o++] = (byte)g; rgba[o++] = 255;
                        break;
                    case 4:
                        var grey = Sample(2 * x);
                        rgba[o++] = (byte)grey; rgba[o++] = (byte)grey; rgba[o++] = (byte)grey; rgba[o++] = (byte)Sample(2 * x + 1);
                        break;
                    case 2:
                        rgba[o++] = (byte)Sample(3 * x); rgba[o++] = (byte)Sample(3 * x + 1); rgba[o++] = (byte)Sample(3 * x + 2); rgba[o++] = 255;
                        break;
                    default:
                        rgba[o++] = (byte)Sample(4 * x); rgba[o++] = (byte)Sample(4 * x + 1); rgba[o++] = (byte)Sample(4 * x + 2); rgba[o++] = (byte)Sample(4 * x + 3);
                        break;
                }
            }
            (previous, line) = (line, previous);
        }
        return new(width, height, rgba);
    }

    /// <summary>Why a PNG cannot be an icon (null when it can): a non-interlaced PNG of exactly 256 x 256 pixels.</summary>
    public static string? IconIssue(byte[] png)
    {
        try
        {
            var image = Read(png);
            return image.Width == IconSize && image.Height == IconSize ? null : CoreText.Format("CustomContent.Icon.Size", IconSize, image.Width, image.Height);
        }
        catch (InvalidDataException e) { return e.Message; }
    }
    public static byte[] IconPixels(byte[] png)
    {
        var image = Read(png);
        if (image.Width != IconSize || image.Height != IconSize) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.Size", IconSize, image.Width, image.Height));
        return image.Rgba;
    }

    /// <summary>Whether pixels are drawn in the game's mask convention: opaque everywhere and no blue above 128.</summary>
    public static bool IsMasks(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4) if (rgba[i + 2] > MaskBlueMax || rgba[i + 3] != 255) return false;
        return true;
    }
    /// <summary>The pixels the texture is built from and what was done ("masks (as given)", "converted to masks", "raw (as given)").</summary>
    public static (byte[] Rgba, string Label) Prepare(byte[] png, string mode)
    {
        if (mode is not (Models.CustomImage.Auto or Models.CustomImage.Raw)) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.Preparation", mode));
        var rgba = IconPixels(png);
        if (mode == Models.CustomImage.Raw) return (rgba, "raw (as given)");
        return IsMasks(rgba) ? (rgba, "masks (as given)") : (Masks(rgba), "converted to masks");
    }
    /// <summary>The texture's main part and GPU part (cached by the PNG's digest and the preparation), and the preparation label.</summary>
    public static (byte[] Main, byte[] Gpu, string Label) Texture(byte[] png, string mode)
    {
        var key = MasksConverter + "-" + mode + "-" + Convert.ToHexString(SHA256.HashData(png));
        var (main, gpu, label) = cache.GetOrAdd(key, _ =>
        {
            var (rgba, prepared) = Prepare(png, mode);
            return (TextureMain(IconSize, IconSize, IconMips), Bc1Mips(IconSize, IconSize, rgba), prepared);
        });
        return (main.ToArray(), gpu.ToArray(), label);
    }

    // ---- BC1 ------------------------------------------------------------------------------------------------------------------------------

    private static int OverBlack(byte c, byte a) => (c * a + 127) / 255;
    private static ushort To565(int r, int g, int b) => (ushort)(((r * 31 + 127) / 255) << 11 | ((g * 63 + 127) / 255) << 5 | (b * 31 + 127) / 255);
    private static (int R, int G, int B) Expand(int v)
    {
        int r = v >> 11 & 31, g = v >> 5 & 63, b = v & 31;
        return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2);
    }
    // One BC1 block (16 RGB pixels): the two most distant colours as endpoints, four-colour mode (hd2_image._block, ties resolved alike).
    private static void Block(Span<int> pixels, Span<byte> output)
    {
        Span<int> distinct = stackalloc int[16];
        var count = 0;
        foreach (var p in pixels) if (distinct[..count].IndexOf(p) < 0) distinct[count++] = p;
        if (count == 1)
        {
            var c = To565(pixels[0] >> 16, pixels[0] >> 8 & 255, pixels[0] & 255);
            BinaryPrimitives.WriteUInt16LittleEndian(output, c); BinaryPrimitives.WriteUInt16LittleEndian(output[2..], c); BinaryPrimitives.WriteUInt32LittleEndian(output[4..], 0);
            return;
        }
        int best = -1, e0 = pixels[0], e1 = pixels[0];
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
            {
                int a = distinct[i], b = distinct[j];
                int dr = (a >> 16) - (b >> 16), dg = (a >> 8 & 255) - (b >> 8 & 255), db = (a & 255) - (b & 255);
                var d = dr * dr + dg * dg + db * db;
                if (d > best) { best = d; e0 = a; e1 = b; }
            }
        int c0 = To565(e0 >> 16, e0 >> 8 & 255, e0 & 255), c1 = To565(e1 >> 16, e1 >> 8 & 255, e1 & 255);
        if (c0 == c1)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output, (ushort)c0); BinaryPrimitives.WriteUInt16LittleEndian(output[2..], (ushort)c1); BinaryPrimitives.WriteUInt32LittleEndian(output[4..], 0);
            return;
        }
        if (c0 < c1) (c0, c1) = (c1, c0);
        var p0 = Expand(c0); var p1 = Expand(c1);
        Span<int> pr = [p0.R, p1.R, (2 * p0.R + p1.R + 1) / 3, (p0.R + 2 * p1.R + 1) / 3];
        Span<int> pg = [p0.G, p1.G, (2 * p0.G + p1.G + 1) / 3, (p0.G + 2 * p1.G + 1) / 3];
        Span<int> pb = [p0.B, p1.B, (2 * p0.B + p1.B + 1) / 3, (p0.B + 2 * p1.B + 1) / 3];
        uint indices = 0;
        for (var k = 0; k < 16; k++)
        {
            int r = pixels[k] >> 16, g = pixels[k] >> 8 & 255, b = pixels[k] & 255, nearest = 0, distance = int.MaxValue;
            for (var n = 0; n < 4; n++)
            {
                int dr = r - pr[n], dg = g - pg[n], db = b - pb[n], d = dr * dr + dg * dg + db * db;
                if (d < distance) { distance = d; nearest = n; }
            }
            indices |= (uint)nearest << (2 * k);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(output, (ushort)c0); BinaryPrimitives.WriteUInt16LittleEndian(output[2..], (ushort)c1); BinaryPrimitives.WriteUInt32LittleEndian(output[4..], indices);
    }
    private static void Bc1(int width, int height, int[] pixels, MemoryStream output)
    {
        Span<int> block = stackalloc int[16];
        Span<byte> bytes = stackalloc byte[8];
        for (var by = 0; by < Math.Max(height, 4); by += 4)
            for (var bx = 0; bx < Math.Max(width, 4); bx += 4)
            {
                for (var y = 0; y < 4; y++)
                    for (var x = 0; x < 4; x++)
                        block[y * 4 + x] = pixels[Math.Min(by + y, height - 1) * width + Math.Min(bx + x, width - 1)];
                Block(block, bytes);
                output.Write(bytes);
            }
    }
    /// <summary>The full BC1 mip chain (box-filtered), largest level first; transparent pixels count as black (the game draws them empty).</summary>
    public static byte[] Bc1Mips(int width, int height, byte[] rgba)
    {
        var pixels = new int[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var a = rgba[4 * i + 3];
            pixels[i] = OverBlack(rgba[4 * i], a) << 16 | OverBlack(rgba[4 * i + 1], a) << 8 | OverBlack(rgba[4 * i + 2], a);
        }
        using var data = new MemoryStream();
        while (true)
        {
            Bc1(width, height, pixels, data);
            if (width == 1 && height == 1) return data.ToArray();
            int w = Math.Max(1, width / 2), h = Math.Max(1, height / 2);
            var next = new int[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (var dy = 0; dy < 2; dy++)
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var p = pixels[Math.Min(2 * y + dy, height - 1) * width + Math.Min(2 * x + dx, width - 1)];
                            r += p >> 16; g += p >> 8 & 255; b += p & 255;
                        }
                    next[y * w + x] = (r + 2) / 4 << 16 | (g + 2) / 4 << 8 | (b + 2) / 4;
                }
            (pixels, width, height) = (next, w, h);
        }
    }
    /// <summary>The texture's main part: the icon texture header, then the DDS header with the DX10 extension.</summary>
    public static byte[] TextureMain(int width, int height, int mips, int dxgi = Bc1Unorm)
    {
        var data = new byte[0xC0 + 4 + 124 + 20];
        BinaryPrimitives.WriteUInt32LittleEndian(data, IconClass);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0xFFFFFFFFU);
        var at = 0xC0;
        "DDS "u8.CopyTo(data.AsSpan(at)); at += 4;
        void U(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), v); at += 4; }
        U(124); U(0x21007); U((uint)height); U((uint)width); U(0); U(0); U((uint)mips);
        at += 44;
        U(32); U(4); U(0x30315844); U(0); U(0); U(0); U(0); U(0);
        U(0x401008); U(0); U(0); U(0); U(0);
        U((uint)dxgi); U(3); U(0); U(1); U(0);
        return data;
    }
    /// <summary>The GUI icon material of an icon named nameHash: the vanilla icon material whose one slot names the icon's own texture.</summary>
    public static byte[] Material(ulong nameHash)
    {
        var data = new byte[160];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x120); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x18); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0x7C);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18 + 0x28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18 + 0x68), IconShader);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x88), ImageSlot);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x8C), nameHash);
        return data;
    }

    // ---- mask conversion (hd2_image.icon_masks) -----------------------------------------------------------------------------------------

    private static readonly int[] Dark = [25, 3, 1], Red = [220, 100, 85], White = [255, 255, 238];
    /// <summary>(red, white) shares of an RGB pixel over the dark colour: least squares against the red and white reference colours,
    /// each clamped to [0, 1] and scaled down together when they add up to more than 1.</summary>
    public static (double Red, double White) Shares(int r, int g, int b)
    {
        Span<long> p = [Math.Max(0, r - Dark[0]), Math.Max(0, g - Dark[1]), Math.Max(0, b - Dark[2])];
        Span<long> s = [Red[0] - Dark[0], Red[1] - Dark[1], Red[2] - Dark[2]];
        Span<long> k = [White[0] - Dark[0], White[1] - Dark[1], White[2] - Dark[2]];
        long ss = 0, kk = 0, sk = 0, ps = 0, pk = 0;
        for (var c = 0; c < 3; c++) { ss += s[c] * s[c]; kk += k[c] * k[c]; sk += s[c] * k[c]; ps += p[c] * s[c]; pk += p[c] * k[c]; }
        var det = ss * kk - sk * sk;
        double a = Math.Min(1.0, Math.Max(0.0, (double)(ps * kk - pk * sk) / det)), w = Math.Min(1.0, Math.Max(0.0, (double)(pk * ss - ps * sk) / det));
        if (a + w > 1) (a, w) = (a / (a + w), w / (a + w));
        return (a, w);
    }
    /// <summary>A red-and-white picture (transparent pixels count as dark) as the game's icon masks: R = red share, G = white share, B = 0, A = 255.</summary>
    public static byte[] Masks(byte[] rgba)
    {
        var output = new byte[rgba.Length];
        var known = new Dictionary<int, (byte, byte)>();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var alpha = rgba[i + 3];
            int r = OverBlack(rgba[i], alpha), g = OverBlack(rgba[i + 1], alpha), b = OverBlack(rgba[i + 2], alpha);
            var key = r << 16 | g << 8 | b;
            if (!known.TryGetValue(key, out var value))
            {
                var (a, w) = Shares(r, g, b);
                known[key] = value = ((byte)(int)(a * 255 + 0.5), (byte)(int)(w * 255 + 0.5));
            }
            output[i] = value.Item1; output[i + 1] = value.Item2; output[i + 2] = 0; output[i + 3] = 255;
        }
        return output;
    }

    // ---- PNG writing: placeholder icons and the in-game preview ------------------------------------------------------------------------

    /// <summary>An RGBA PNG (filter 0).</summary>
    public static byte[] WritePng(int width, int height, byte[] rgba)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++) { raw.WriteByte(0); raw.Write(rgba, y * width * 4, width * 4); }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(z); }
        using var png = new MemoryStream();
        png.Write(Signature);
        void Chunk(ReadOnlySpan<byte> kind, ReadOnlySpan<byte> body)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)body.Length); png.Write(number);
            var both = new byte[4 + body.Length]; kind.CopyTo(both); body.CopyTo(both.AsSpan(4));
            png.Write(both);
            BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(both)); png.Write(number);
        }
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR"u8, header);
        Chunk("IDAT"u8, compressed.ToArray());
        Chunk("IEND"u8, []);
        return png.ToArray();
    }
    /// <summary>A placeholder icon in the mask convention (a ring and a chevron), so a new custom stratagem can be built before its art
    /// exists. Deterministic for a seed (the stratagem id): its proportions vary a little so placeholders are told apart.</summary>
    public static byte[] Placeholder(string seed)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        double inner = 84 + digest[0] % 12, outer = inner + 16, lift = 60 + digest[1] % 20;
        var rgba = new byte[IconSize * IconSize * 4];
        for (var y = 0; y < IconSize; y++)
            for (var x = 0; x < IconSize; x++)
            {
                double dx = x - 127.5, dy = y - 127.5, r = Math.Sqrt(dx * dx + dy * dy);
                var red = r >= inner && r <= outer;
                var white = Math.Abs(dx) < 62 && Math.Abs(dx) * 0.9 + 34 <= dy + lift && dy + lift <= Math.Abs(dx) * 0.9 + 66;
                var i = (y * IconSize + x) * 4;
                rgba[i] = red ? (byte)255 : (byte)0; rgba[i + 1] = white ? (byte)255 : (byte)0; rgba[i + 2] = 0; rgba[i + 3] = 255;
            }
        return WritePng(IconSize, IconSize, rgba);
    }
    /// <summary>How the game draws the icon: the prepared masks with R in the category colour and G in white, on transparency.</summary>
    public static byte[] Preview(byte[] png, string mode, (byte R, byte G, byte B) category)
    {
        var (masks, _) = Prepare(png, mode);
        var output = new byte[masks.Length];
        for (var i = 0; i < masks.Length; i += 4)
        {
            double red = masks[i] / 255.0, white = masks[i + 1] / 255.0, a = Math.Min(1.0, red + white);
            if (a <= 0) continue;
            output[i] = (byte)Math.Round((category.R * red + 255 * white) / a);
            output[i + 1] = (byte)Math.Round((category.G * red + 255 * white) / a);
            output[i + 2] = (byte)Math.Round((category.B * red + 255 * white) / a);
            output[i + 3] = (byte)Math.Round(a * 255);
        }
        return WritePng(IconSize, IconSize, output);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320U ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFU;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFU;
    }
    private static InvalidDataException Invalid(string key) => new(CoreText.Get(key));
}
