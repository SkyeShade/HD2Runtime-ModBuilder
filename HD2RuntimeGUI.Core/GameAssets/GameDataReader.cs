using System.Buffers.Binary;
using System.Text;

namespace HD2RuntimeGUI.Core.GameAssets;

// Read-only access to single resources in an installed Helldivers 2 "data" folder (Stingray archives).
// Supports the fat edition (one file per archive) and the slim edition (archives packed into DSAR .nxa bundles,
// uncompressed or LZ4 chunks). Ported from the file layout documented by Filediver (github.com/xypwn/filediver).
// Nothing is written to the game folder, and no game process is touched.
public sealed class GameDataReader
{
    public readonly record struct ResourceId(ulong Name, ulong Type);
    private sealed record Locus(ulong ArchiveId, ulong Offset, uint Size);
    private sealed record Chunk(ulong UncompressedOffset, ulong CompressedOffset, uint UncompressedSize, uint CompressedSize, byte Compression);
    private sealed record Bundle(string File, Chunk[] Chunks);
    private sealed record Entry(uint ArchiveOffset, uint BundleOffset, byte BundleIndex);
    private sealed record ArchiveItem(ulong Size, string Name, Entry[] Entries);

    private readonly string dataDir;
    private readonly bool slim;
    private readonly Dictionary<ulong, ArchiveItem> slimArchives = [];
    private readonly Dictionary<ulong, Bundle> singleBundles = [];
    private Bundle[] bundles = [];
    private GameDataReader(string dataDir, bool slim) { this.dataDir = dataDir; this.slim = slim; }

    public const int MaxResourceBytes = 16 * 1024 * 1024;
    public static ulong Hash(string name) => Murmur64(Encoding.UTF8.GetBytes(name));

    public static GameDataReader Open(string dataDir)
    {
        if (!Directory.Exists(dataDir)) throw new DirectoryNotFoundException("Game data folder not found: " + dataDir);
        // The fat edition ships the boot archive as a plain file; the slim edition packs it into bundles.nxa.
        var reader = new GameDataReader(dataDir, !File.Exists(Path.Combine(dataDir, "9ba626afa44a3aa3")));
        if (reader.slim) reader.LoadSlimIndex();
        return reader;
    }

    // Finds and reads the main part of each requested resource. Archive tables are scanned until all are found.
    public IReadOnlyDictionary<ResourceId, byte[]> ReadMain(IReadOnlyCollection<ResourceId> wanted, CancellationToken ct = default)
    {
        var found = new Dictionary<ResourceId, Locus>();
        foreach (var (archiveId, table) in ArchiveTables())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (id, locus) in Toc(archiveId, table))
                if (wanted.Contains(id) && !found.ContainsKey(id)) found[id] = locus;
            if (found.Count == wanted.Count) break;
        }
        return found.ToDictionary(p => p.Key, p => Read(p.Value));
    }

    private IEnumerable<(ulong Id, byte[] Table)> ArchiveTables()
    {
        if (!slim)
        {
            foreach (var file in Directory.EnumerateFiles(dataDir).Where(f => Path.GetExtension(f) == "" && ParseId(Path.GetFileName(f)) is not null).Order(StringComparer.Ordinal))
            {
                using var stream = File.OpenRead(file);
                var header = new byte[72]; if (stream.Read(header) != 72 || BinaryPrimitives.ReadUInt32LittleEndian(header) != 0xF0000011) continue;
                var table = new byte[TableSize(header)]; stream.Position = 0; stream.ReadExactly(table);
                yield return (ParseId(Path.GetFileName(file))!.Value, table);
            }
            yield break;
        }
        foreach (var (id, item) in slimArchives)
        {
            var data = new MemoryStream();
            for (var i = 0; i < item.Entries.Length; i++)
            {
                WriteEntry(item, i, data);
                if (data.Length >= 72 && data.Length >= TableSize(data.GetBuffer())) break;
            }
            yield return (id, data.ToArray());
        }
        foreach (var (id, bundle) in singleBundles)
            yield return (id, ReadChunk(bundle.File, bundle.Chunks[0]));
    }
    private static long TableSize(byte[] header) =>
        72L + 32L * BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) + 80L * BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
    private static IEnumerable<(ResourceId, Locus)> Toc(ulong archiveId, byte[] table)
    {
        if (table.Length < 72 || BinaryPrimitives.ReadUInt32LittleEndian(table) != 0xF0000011) yield break;
        var types = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(4)); var files = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8));
        var start = 72 + 32 * (long)types;
        for (long i = 0; i < files && start + 80 * (i + 1) <= table.Length; i++)
        {
            var e = table.AsSpan((int)(start + 80 * i), 80);
            // FileData: name, type, offsets[main, stream, gpu], main/gpu buffer offsets, sizes[main, stream, gpu], alignments, index.
            yield return (new(BinaryPrimitives.ReadUInt64LittleEndian(e), BinaryPrimitives.ReadUInt64LittleEndian(e[8..])),
                new(archiveId, BinaryPrimitives.ReadUInt64LittleEndian(e[16..]), BinaryPrimitives.ReadUInt32LittleEndian(e[56..])));
        }
    }
    private byte[] Read(Locus l)
    {
        if (l.Size > MaxResourceBytes) throw new InvalidDataException("Game resource is too large.");
        if (!slim)
        {
            using var stream = File.OpenRead(Path.Combine(dataDir, l.ArchiveId.ToString("x16")));
            stream.Position = (long)l.Offset; var buffer = new byte[l.Size]; stream.ReadExactly(buffer); return buffer;
        }
        var output = new MemoryStream();
        if (singleBundles.TryGetValue(l.ArchiveId, out var single))
        {
            var index = Array.FindIndex(single.Chunks, c => c.UncompressedOffset == l.Offset);
            if (index < 0) throw new InvalidDataException("Resource is not aligned with a bundle chunk.");
            while (output.Length < l.Size) output.Write(ReadChunk(single.File, single.Chunks[index++]));
        }
        else
        {
            var item = slimArchives[l.ArchiveId];
            var e = Array.FindLastIndex(item.Entries, x => x.ArchiveOffset <= l.Offset);
            if (e < 0) throw new InvalidDataException("Resource is outside its archive.");
            var entry = item.Entries[e]; var bundle = bundles[entry.BundleIndex];
            var chunk = Array.FindIndex(bundle.Chunks, c => c.UncompressedOffset == entry.BundleOffset);
            if (chunk < 0) throw new InvalidDataException("Bundle chunk not found.");
            var skip = (long)l.Offset - entry.ArchiveOffset;
            while (skip > 0) skip -= bundle.Chunks[chunk++].UncompressedSize;
            if (skip != 0) throw new InvalidDataException("Resource is not aligned with a bundle chunk.");
            while (output.Length < l.Size) output.Write(ReadChunk(bundle.File, bundle.Chunks[chunk++]));
        }
        return output.GetBuffer().AsSpan(0, (int)l.Size).ToArray();
    }

    private void LoadSlimIndex()
    {
        var nxa = Path.Combine(dataDir, "bundles.nxa");
        var index = new MemoryStream();
        foreach (var c in LoadDsar(nxa)) index.Write(ReadChunk(nxa, c));
        var d = index.ToArray();
        if (Encoding.ASCII.GetString(d, 0, 4) != "DSAA") throw new InvalidDataException("Unsupported bundles.nxa index.");
        var nxaCount = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12)); var itemCount = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(16));
        var items = new List<(ulong Size, uint NameOffset, uint Count, ulong EntriesOffset)>();
        for (var i = 0; i < itemCount; i++)
        {
            var h = d.AsSpan(24 + 24 * i, 24);
            items.Add((BinaryPrimitives.ReadUInt64LittleEndian(h), BinaryPrimitives.ReadUInt32LittleEndian(h[8..]), BinaryPrimitives.ReadUInt32LittleEndian(h[12..]), BinaryPrimitives.ReadUInt64LittleEndian(h[16..])));
        }
        var nameOffsets = Enumerable.Range(0, (int)nxaCount).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(24 + 24 * (int)itemCount + 4 * i))).ToArray();
        bundles = nameOffsets.Select(o => { var file = Path.Combine(dataDir, CString(d, o)); return new Bundle(file, LoadDsar(file)); }).ToArray();
        foreach (var (size, nameOffset, count, entriesOffset) in items)
        {
            var name = CString(d, nameOffset);
            // Only main archives carry tables; .stream / .gpu_resources parts are not needed for UI resources.
            if (Path.GetExtension(name) != "" || ParseId(name) is not { } id || count == 0) continue;
            var entries = Enumerable.Range(0, (int)count).Select(i =>
            {
                var e = d.AsSpan((int)entriesOffset + 16 * i, 16);
                return new Entry(BinaryPrimitives.ReadUInt32LittleEndian(e), BinaryPrimitives.ReadUInt32LittleEndian(e[8..]), e[15]);
            }).ToArray();
            slimArchives[id] = new(size, name, entries);
        }
        foreach (var file in Directory.EnumerateFiles(dataDir).Where(f => Path.GetExtension(f) == "").Order(StringComparer.Ordinal))
            if (ParseId(Path.GetFileName(file)) is { } id && !slimArchives.ContainsKey(id)) singleBundles[id] = new(file, LoadDsar(file));
    }
    private void WriteEntry(ArchiveItem item, int index, MemoryStream data)
    {
        var entry = item.Entries[index];
        var size = (index + 1 < item.Entries.Length ? item.Entries[index + 1].ArchiveOffset : item.Size) - entry.ArchiveOffset;
        var bundle = bundles[entry.BundleIndex];
        var chunk = Array.FindIndex(bundle.Chunks, c => c.UncompressedOffset == entry.BundleOffset);
        if (chunk < 0) throw new InvalidDataException("Bundle chunk not found.");
        for (ulong written = 0; written < size; chunk++) { var bytes = ReadChunk(bundle.File, bundle.Chunks[chunk]); data.Write(bytes); written += (ulong)bytes.Length; }
    }
    private static Chunk[] LoadDsar(string file)
    {
        using var stream = File.OpenRead(file);
        var header = new byte[32]; stream.ReadExactly(header);
        if (Encoding.ASCII.GetString(header, 0, 4) != "DSAR") throw new InvalidDataException("Unsupported bundle: " + Path.GetFileName(file));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var raw = new byte[32 * (long)count]; stream.ReadExactly(raw);
        return Enumerable.Range(0, (int)count).Select(i =>
        {
            var c = raw.AsSpan(32 * i, 32);
            return new Chunk(BinaryPrimitives.ReadUInt64LittleEndian(c), BinaryPrimitives.ReadUInt64LittleEndian(c[8..]), BinaryPrimitives.ReadUInt32LittleEndian(c[16..]),
                BinaryPrimitives.ReadUInt32LittleEndian(c[20..]), c[24]);
        }).ToArray();
    }
    private static byte[] ReadChunk(string file, Chunk c)
    {
        if (c.UncompressedSize > MaxResourceBytes || c.CompressedSize > MaxResourceBytes) throw new InvalidDataException("Bundle chunk is too large.");
        using var stream = File.OpenRead(file);
        stream.Position = (long)c.CompressedOffset; var compressed = new byte[c.CompressedSize]; stream.ReadExactly(compressed);
        return c.Compression switch
        {
            0 => compressed,
            3 => Lz4.DecompressBlock(compressed, (int)c.UncompressedSize),
            _ => throw new InvalidDataException("Unsupported bundle compression " + c.Compression + "."),
        };
    }
    private static string CString(byte[] d, uint offset) { var end = Array.IndexOf(d, (byte)0, (int)offset); return Encoding.UTF8.GetString(d, (int)offset, end - (int)offset); }
    private static ulong? ParseId(string name) => name.Length == 16 && ulong.TryParse(name, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;

    // Stingray resource name hash (MurmurHash64A, seed 0), as used by HD2Runtime's tooling.
    public static ulong Murmur64(ReadOnlySpan<byte> data)
    {
        const ulong m = 0xC6A4A7935BD1E995; ulong h = (ulong)data.Length * m; var full = data.Length / 8 * 8;
        for (var i = 0; i < full; i += 8) { var k = BinaryPrimitives.ReadUInt64LittleEndian(data[i..]) * m; k ^= k >> 47; h = (h ^ (k * m)) * m; }
        if (full != data.Length) { ulong t = 0; for (var i = data.Length - 1; i >= full; i--) t = (t << 8) | data[i]; h = (h ^ t) * m; }
        h ^= h >> 47; h *= m; return h ^ (h >> 47);
    }
}

// LZ4 block format decoder (no frame), sufficient for DSAR chunks.
public static class Lz4
{
    public static byte[] DecompressBlock(ReadOnlySpan<byte> src, int size)
    {
        var dst = new byte[size]; int s = 0, d = 0;
        while (s < src.Length)
        {
            var token = src[s++]; var literals = token >> 4;
            if (literals == 15) { byte b; do { b = src[s++]; literals += b; } while (b == 255); }
            if (d + literals > size || s + literals > src.Length) throw new InvalidDataException("Corrupt LZ4 block.");
            src.Slice(s, literals).CopyTo(dst.AsSpan(d)); s += literals; d += literals;
            if (s >= src.Length) break;
            var offset = src[s] | src[s + 1] << 8; s += 2;
            var match = (token & 15) + 4;
            if ((token & 15) == 15) { byte b; do { b = src[s++]; match += b; } while (b == 255); }
            if (offset == 0 || offset > d || d + match > size) throw new InvalidDataException("Corrupt LZ4 block.");
            for (var i = 0; i < match; i++, d++) dst[d] = dst[d - offset];
        }
        if (d != size) throw new InvalidDataException("LZ4 block size mismatch.");
        return dst;
    }
}
