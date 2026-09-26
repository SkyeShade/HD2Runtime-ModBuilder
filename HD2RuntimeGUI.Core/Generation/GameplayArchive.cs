using System.Buffers.Binary;
using System.Text;

namespace HD2RuntimeGUI.Core.Generation;

// Binary packaging only. Ported from HD2Runtime's standalone starter/build.ps1.
// No game process access, resource resolution, or memory operations.
public static class GameplayArchive
{
    public const string ArchiveName = "9ba626afa44a3aa3.patch_0";
    public static ulong ResourceHash(string name)
    {
        const ulong mix = 0xC6A4A7935BD1E995UL;
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        unchecked
        {
            ulong result = (ulong)bytes.Length * mix;
            int complete = bytes.Length / 8 * 8;
            for (int at = 0; at < complete; at += 8)
            { ulong word = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at, 8)); word *= mix; word ^= word >> 47; result = (result ^ (word * mix)) * mix; }
            if (complete != bytes.Length)
            { ulong tail = 0; for (int i = complete; i < bytes.Length; i++) tail |= (ulong)bytes[i] << ((i - complete) * 8); result = (result ^ tail) * mix; }
            result ^= result >> 47; result *= mix; return result ^ (result >> 47);
        }
    }
    public static byte[] Build(string resource, byte[] body)
    {
        const ulong luaType = 0xA14E8DFA2CD117E2UL;
        uint length = checked((uint)body.Length + 8U), padding = (16 - length % 16) % 16;
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        void Zero(int count) => w.Write(new byte[count]);
        w.Write(0xF0000011U); w.Write(1U); w.Write(1U); Zero(20); w.Write(192UL + length + padding); w.Write(0UL); Zero(24);
        w.Write(0U); w.Write(0U); w.Write(luaType); w.Write(1U); w.Write(0U); w.Write(16U); w.Write(16U);
        w.Write(ResourceHash(resource)); w.Write(luaType); w.Write(192UL);
        w.Write(0UL); w.Write(0UL); w.Write(0UL); w.Write(0UL);
        w.Write(length); w.Write(0U); w.Write(0U); w.Write(16U); w.Write(16U); w.Write(0U);
        Zero(192 - (int)stream.Position); w.Write((uint)body.Length); w.Write(2U); w.Write(body); Zero((int)padding);
        return stream.ToArray();
    }
}
