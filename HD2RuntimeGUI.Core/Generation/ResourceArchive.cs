namespace HD2RuntimeGUI.Core.Generation;

/// <summary>An archive of several resource types in the layout of the game's own archives (HD2Runtime's sdk/tools/hd2_archive.py
/// make_resource_archive, byte for byte): {(type hash, name hash): (main part, GPU part)} -> (archive, .gpu_resources). Types and entries are
/// sorted by hash; main parts are 16-byte and GPU parts 64-byte aligned; every entry carries the running sums of the 256-byte-rounded part
/// sizes and the header their totals. Used only for mods with compiled images (custom stratagem icons); Lua-only mods keep
/// GameplayArchive's layout exactly as before.</summary>
public static class ResourceArchive
{
    public const ulong LuaType = 0xA14E8DFA2CD117E2UL;
    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
    /// <summary>A Lua resource's main part: its length, the format word 2, then the source.</summary>
    public static byte[] LuaResource(byte[] body)
    {
        var data = new byte[8 + body.Length];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), (uint)body.Length); BitConverter.TryWriteBytes(data.AsSpan(4, 4), 2U);
        body.CopyTo(data, 8); return data;
    }
    public static (byte[] Archive, byte[] Gpu) Build(IReadOnlyDictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)> resources)
    {
        if (resources.Count == 0) throw new InvalidDataException("At least one resource is required.");
        var keys = resources.Keys.OrderBy(k => k.Type).ThenBy(k => k.Name).ToArray();
        var types = keys.Select(k => k.Type).Distinct().Order().ToArray();
        var tableEnd = 72 + 32 * types.Length + 80 * keys.Length;
        using var archive = new MemoryStream(); using var gpu = new MemoryStream(); using var entries = new MemoryStream();
        archive.Write(new byte[Align(tableEnd, 256)]);
        using var e = new BinaryWriter(entries);
        long mainSum = 0, gpuSum = 0;
        for (var index = 0; index < keys.Length; index++)
        {
            var (main, data) = resources[keys[index]];
            archive.Write(new byte[Align((int)archive.Length, 16) - (int)archive.Length]);
            gpu.Write(new byte[Align((int)gpu.Length, 64) - (int)gpu.Length]);
            e.Write(keys[index].Name); e.Write(keys[index].Type); e.Write((ulong)archive.Length); e.Write(0UL); e.Write((ulong)gpu.Length);
            e.Write((ulong)mainSum); e.Write((ulong)gpuSum);
            e.Write((uint)main.Length); e.Write(0U); e.Write((uint)data.Length); e.Write(16U); e.Write(64U); e.Write((uint)index);
            archive.Write(main); gpu.Write(data);
            mainSum += Align(main.Length, 256); gpuSum += Align(data.Length, 256);
        }
        using var head = new MemoryStream(); using var h = new BinaryWriter(head);
        h.Write(0xF0000011U); h.Write((uint)types.Length); h.Write((uint)keys.Length); h.Write(new byte[20]); h.Write((ulong)mainSum); h.Write((ulong)gpuSum); h.Write(new byte[24]);
        foreach (var type in types) { h.Write(0U); h.Write(0U); h.Write(type); h.Write((uint)keys.Count(k => k.Type == type)); h.Write(0U); h.Write(16U); h.Write(64U); }
        h.Write(entries.ToArray());
        var bytes = archive.ToArray();
        head.ToArray().CopyTo(bytes, 0);
        return (bytes, gpu.ToArray());
    }
}
