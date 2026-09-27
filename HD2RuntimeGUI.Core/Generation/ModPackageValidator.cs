using System.IO.Compression;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Generation;

public static class ModPackageValidator
{
    public static void Validate(byte[] bytes, IReadOnlyDictionary<string, byte[]> expected)
    {
        using var input = new MemoryStream(bytes); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count != expected.Count) throw new InvalidDataException("Generated ZIP inventory mismatch.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            SdkCache.ValidateEntryPath(entry.FullName);
            if (!names.Add(entry.FullName) || !expected.TryGetValue(entry.FullName, out var content) || entry.Length != content.Length) throw new InvalidDataException("Unexpected generated ZIP entry.");
            using var stream = entry.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            if (!buffer.ToArray().AsSpan().SequenceEqual(content)) throw new InvalidDataException("Generated ZIP content did not verify.");
        }
    }
}
