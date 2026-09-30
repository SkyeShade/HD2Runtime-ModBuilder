using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Services;

public sealed record SnapshotRegion(ulong Base, ulong AllocationBase, ulong Size, uint State, uint Type, uint Protect,
    uint Status, ulong CapturedLength, ulong DataOffset, uint ErrorCode);
public sealed record SnapshotModule(string Name, ulong Base, ulong Size, string Sha256);
public sealed record SnapshotInfo(string Path, long FileLength, DateTime LastWriteUtc, string RuntimeVersion, string GameVersion,
    string CapturedAt, ulong CaptureUnixTime, uint Architecture, uint PageSize, BuildFingerprints Fingerprints,
    ulong VirtualBytes, ulong CapturedBytes, IReadOnlyList<SnapshotModule> Modules, IReadOnlyList<SnapshotRegion> Regions);
public interface ISnapshotReader
{
    SnapshotInfo Open(string path);
    byte[] ReadBytes(SnapshotInfo snapshot, ulong address, int count);
}
public sealed class SnapshotReader : ISnapshotReader
{
    public const uint HeaderReserve = 16 * 1024 * 1024;
    public SnapshotInfo Open(string path)
    {
        try
        {
            using var file = File.OpenRead(path); using var input = new BinaryReader(file, new UTF8Encoding(false, true));
            void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
            Require(input.ReadBytes(8).AsSpan().SequenceEqual("HD2SNAP\0"u8), CoreText.Get("Messages.Snapshot.NotSnapshot"));
            Require(input.ReadUInt32() == 1, CoreText.Get("Messages.Snapshot.UnsupportedSchema"));
            var headerLength = input.ReadUInt32(); var reserve = input.ReadUInt32();
            Require(reserve == HeaderReserve && headerLength is >= 200 and <= HeaderReserve && file.Length >= reserve, "Invalid snapshot header bounds.");
            var regionCount = input.ReadUInt32(); var moduleCount = input.ReadUInt32();
            Require(regionCount <= 1_000_000 && moduleCount == 2 && regionCount * 64UL <= headerLength, "Invalid snapshot index bounds.");
            var architecture = input.ReadUInt32(); var page = input.ReadUInt32(); Require(input.ReadUInt32() == 0, "Unsupported snapshot flags.");
            var maximum = input.ReadUInt64(); var virtualBytes = input.ReadUInt64(); var capturedBytes = input.ReadUInt64(); var unix = input.ReadUInt64();
            string Hash() { var hash = Encoding.ASCII.GetString(input.ReadBytes(64)); Require(Regex.IsMatch(hash, "\\A[A-F0-9]{64}\\z"), "Invalid snapshot fingerprint."); return hash; }
            var fingerprints = new BuildFingerprints(Hash(), Hash());
            string Text()
            {
                var length = input.ReadUInt32(); Require(length <= 1_048_576 && file.Position + length <= headerLength, "Snapshot string exceeds bounds.");
                var bytes = input.ReadBytes((int)length); Require(bytes.Length == length, "Truncated snapshot string."); return new UTF8Encoding(false, true).GetString(bytes);
            }
            var runtime = Text(); var game = Text(); var captured = Text();
            for (int i = 0; i < 8; i++) input.ReadUInt64();
            var modules = new List<SnapshotModule>();
            for (int i = 0; i < 2; i++) modules.Add(new(Text(), input.ReadUInt64(), input.ReadUInt64(), Hash()));
            Require(modules.Select(m => m.Name.ToLowerInvariant()).Distinct().Count() == 2 && modules.Any(m => m.Name.Equals("helldivers2.exe", StringComparison.OrdinalIgnoreCase) && m.Sha256 == fingerprints.Exe) && modules.Any(m => m.Name.Equals("game.dll", StringComparison.OrdinalIgnoreCase) && m.Sha256 == fingerprints.Dll), "Snapshot module fingerprints differ.");
            var regions = new List<SnapshotRegion>(); ulong previous = 0, cursor = reserve, totalVirtual = 0, totalCaptured = 0;
            for (int i = 0; i < regionCount; i++)
            {
                var r = new SnapshotRegion(input.ReadUInt64(), input.ReadUInt64(), input.ReadUInt64(), input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt64(), input.ReadUInt64(), input.ReadUInt32());
                Require(input.ReadUInt32() == 0, "Unsupported snapshot region flags.");
                Require(r.Size > 0 && r.Base >= previous && checked(r.Base + r.Size) <= maximum && r.AllocationBase <= r.Base && r.Status <= 5 && r.CapturedLength <= r.Size, "Invalid or overlapping snapshot region.");
                if (r.CapturedLength > 0) { Require(r.DataOffset == cursor && checked(cursor + r.CapturedLength) <= (ulong)file.Length, "Invalid snapshot payload index."); cursor += r.CapturedLength; }
                else Require(r.DataOffset == 0, "Empty snapshot region has payload.");
                Require(r.Status switch { 1 => r.CapturedLength == r.Size && r.ErrorCode == 0, 4 => r.CapturedLength < r.Size, _ => r.CapturedLength == 0 }, "Invalid captured region status.");
                previous = checked(r.Base + r.Size); totalVirtual = checked(totalVirtual + r.Size); totalCaptured = checked(totalCaptured + r.CapturedLength); regions.Add(r);
            }
            Require(file.Position == headerLength && totalVirtual == virtualBytes && totalCaptured == capturedBytes && cursor == (ulong)file.Length, "Snapshot totals or file size differ.");
            return new(System.IO.Path.GetFullPath(path), file.Length, File.GetLastWriteTimeUtc(path), runtime, game, captured, unix, architecture, page, fingerprints, virtualBytes, capturedBytes, modules, regions);
        }
        catch (Exception e) when (e is EndOfStreamException or OverflowException or DecoderFallbackException) { throw new InvalidDataException(CoreText.Get("Messages.Snapshot.Malformed"), e); }
    }
    public byte[] ReadBytes(SnapshotInfo snapshot, ulong address, int count)
    {
        if (count is < 1 or > 4096) throw new InvalidDataException(CoreText.Get("Messages.Snapshot.ReadLimit"));
        var fileInfo = new FileInfo(snapshot.Path);
        if (fileInfo.Length != snapshot.FileLength || fileInfo.LastWriteTimeUtc != snapshot.LastWriteUtc) throw new InvalidDataException(CoreText.Get("Messages.Snapshot.Changed"));
        var region = snapshot.Regions.FirstOrDefault(r => r.Status == 1 && address >= r.Base && address - r.Base < r.Size && (ulong)count <= r.Size - (address - r.Base)) ?? throw new InvalidDataException(CoreText.Get("Messages.Snapshot.NotCaptured"));
        using var file = File.OpenRead(snapshot.Path); file.Position = checked((long)(region.DataOffset + address - region.Base));
        var bytes = new byte[count]; file.ReadExactly(bytes); return bytes;
    }
}

// Read-only projection of Runtime's schema-2 weapon-map report. Mapper-specific research
// details are retained as JSON; the authoring catalog has its own strictly typed model.
public sealed record SnapshotMap(int SchemaVersion, BuildFingerprints GameFingerprints, string Hd2RuntimeVersion,
    int Writes, int ProtectionChanges, string FixtureFallback, string Mode, IReadOnlyList<SnapshotResource> RuntimeCandidates);
public sealed record SnapshotResource(string ResourceHash, int EntityRow, Dictionary<string, JsonElement> Ownership,
    Dictionary<string, JsonElement> ResolvedFields, IReadOnlyList<JsonElement> Attacks, IReadOnlyList<string> CredibleWikiIdentities);
public interface IResearchFilePicker { Task<string?> PickAsync(bool report); }
public sealed class SnapshotWorkspace(ISnapshotReader reader, IResearchFilePicker picker, AppPaths paths)
{
    private sealed record Link(string SnapshotPath);
    public SnapshotInfo? Snapshot { get; private set; }
    public SnapshotMap? Map { get; private set; }
    public string? Status { get; private set; }
    public string RawText { get; private set; } = "";
    public async Task RestoreAsync()
    {
        var file = System.IO.Path.Combine(paths.Root, "research.json");
        if (!File.Exists(file)) return;
        try { await LoadAsync((await JsonStorage.ReadAsync<Link>(file)).SnapshotPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Status = CoreText.Format("Messages.Snapshot.Relink", e.Message); }
    }
    public async Task PickSnapshotAsync() { var file = await picker.PickAsync(false); if (file != null) await LoadAsync(file); }
    public async Task LoadAsync(string file)
    {
        var next = await Task.Run(() => reader.Open(file));
        await JsonStorage.WriteAtomicAsync(System.IO.Path.Combine(paths.Root, "research.json"), new Link(next.Path));
        Snapshot = next; Map = null; RawText = ""; Status = CoreText.Get("Messages.Snapshot.Loaded");
    }
    public async Task PickReportAsync()
    {
        var file = await picker.PickAsync(true); if (file != null) await LoadReportAsync(file);
    }
    public async Task LoadReportAsync(string file)
    {
        if (Snapshot == null) throw new InvalidOperationException(CoreText.Get("Messages.Snapshot.LoadFirst"));
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException(CoreText.Get("Messages.Snapshot.ReportTooLarge"));
        await using var stream = File.OpenRead(file);
        var map = await JsonSerializer.DeserializeAsync<SnapshotMap>(stream, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 48 });
        if (map == null || map.SchemaVersion != 2 || map.GameFingerprints != Snapshot.Fingerprints || map.Writes != 0 || map.ProtectionChanges != 0 || map.FixtureFallback != "disabled" || map.Mode != "snapshot" || map.RuntimeCandidates == null || map.RuntimeCandidates.Count > 10000 || map.RuntimeCandidates.Any(r => r.Ownership == null || r.ResolvedFields == null || r.Attacks == null || r.CredibleWikiIdentities == null || !Regex.IsMatch(r.ResourceHash, "\\A0x[A-Fa-f0-9]{16}\\z"))) throw new InvalidDataException(CoreText.Get("Messages.Snapshot.ReportInvalid"));
        Map = map; Status = CoreText.Get("Messages.Snapshot.ReportLinked");
    }
    public async Task InspectAsync(string hex)
    {
        if (Snapshot == null) throw new InvalidOperationException(CoreText.Get("Messages.Snapshot.LoadFirst"));
        if (!ulong.TryParse(hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address)) throw new InvalidDataException(CoreText.Get("Messages.Snapshot.EnterAddress"));
        var bytes = await Task.Run(() => reader.ReadBytes(Snapshot, address, 256));
        RawText = string.Join('\n', bytes.Chunk(16).Select((row, i) => $"{address + (ulong)(i * 16):X16}  {string.Join(' ', row.Select(b => b.ToString("X2")))}"));
    }
}
