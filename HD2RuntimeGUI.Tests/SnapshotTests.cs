using System.Text;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class SnapshotTests
{
    private static string Fixture(TestEnvironment env)
    {
        var path = Path.Combine(env.Paths.Root, "test.hd2snap"); Directory.CreateDirectory(env.Paths.Root);
        using var file = File.Create(path); using var w = new BinaryWriter(file, Encoding.UTF8);
        void Text(string s) { var b = Encoding.UTF8.GetBytes(s); w.Write((uint)b.Length); w.Write(b); }
        w.Write("HD2SNAP\0"u8); w.Write(1u); w.Write(0u); w.Write(SnapshotReader.HeaderReserve); w.Write(1u); w.Write(2u); w.Write(0x8664u); w.Write(4096u); w.Write(0u);
        w.Write(0x10000UL); w.Write(256UL); w.Write(256UL); w.Write(1UL); w.Write(Encoding.ASCII.GetBytes(new string('A', 64))); w.Write(Encoding.ASCII.GetBytes(new string('B', 64)));
        Text("0.13.0"); Text("test"); Text("2026-09-27T00:00:00Z"); for (int i = 0; i < 8; i++) w.Write(0UL);
        Text("helldivers2.exe"); w.Write(0x1000UL); w.Write(256UL); w.Write(Encoding.ASCII.GetBytes(new string('A', 64)));
        Text("game.dll"); w.Write(0x2000UL); w.Write(256UL); w.Write(Encoding.ASCII.GetBytes(new string('B', 64)));
        w.Write(0x1000UL); w.Write(0x1000UL); w.Write(256UL); w.Write(0x1000u); w.Write(0x20000u); w.Write(4u); w.Write(1u); w.Write(256UL); w.Write((ulong)SnapshotReader.HeaderReserve); w.Write(0u); w.Write(0u);
        var headerSize = (uint)file.Position; file.Position = 12; w.Write(headerSize); file.Position = SnapshotReader.HeaderReserve;
        w.Write(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()); return path;
    }
    [Fact] public void Readonly_snapshot_header_and_bounded_raw_reads()
    {
        using var env = new TestEnvironment(); var path = Fixture(env); var original = File.ReadAllBytes(path);
        var reader = new SnapshotReader(); var snapshot = reader.Open(path);
        Assert.Equal("0.13.0", snapshot.RuntimeVersion); Assert.Equal(new string('A', 64), snapshot.Fingerprints.Exe); Assert.Single(snapshot.Regions);
        Assert.Equal(new byte[] { 16, 17, 18, 19 }, reader.ReadBytes(snapshot, 0x1010, 4));
        Assert.Throws<InvalidDataException>(() => reader.ReadBytes(snapshot, 0x10FF, 2));
        Assert.Throws<InvalidDataException>(() => reader.ReadBytes(snapshot, 0x1000, 4097));
        Assert.Equal(original, File.ReadAllBytes(path));
    }
    [Theory] [InlineData(8)] [InlineData(12)] [InlineData(24)] [InlineData(72)]
    public void Malformed_snapshot_headers_are_rejected(int offset)
    {
        using var env = new TestEnvironment(); var path = Fixture(env);
        using (var file = File.OpenWrite(path)) { file.Position = offset; file.WriteByte(255); }
        Assert.Throws<InvalidDataException>(() => new SnapshotReader().Open(path));
    }
    [Fact] public async Task Snapshot_link_survives_restart_and_missing_file_can_be_relinked()
    {
        using var env = new TestEnvironment(); var path = Fixture(env);
        var workspace = new SnapshotWorkspace(new SnapshotReader(), new Picker(), env.Paths);
        await workspace.LoadAsync(path);
        var restored = new SnapshotWorkspace(new SnapshotReader(), new Picker(), env.Paths); await restored.RestoreAsync(); Assert.NotNull(restored.Snapshot);
        File.Move(path, path + ".moved");
        var missing = new SnapshotWorkspace(new SnapshotReader(), new Picker(), env.Paths); await missing.RestoreAsync(); Assert.Contains("Relink", missing.Status);
        await missing.LoadAsync(path + ".moved"); Assert.NotNull(missing.Snapshot);
    }
    [Fact] public async Task Research_report_requires_matching_fingerprints_and_readonly_snapshot_mode()
    {
        using var env = new TestEnvironment(); var path = Fixture(env);
        var workspace = new SnapshotWorkspace(new SnapshotReader(), new Picker(), env.Paths); await workspace.LoadAsync(path);
        var reportPath = Path.Combine(env.Paths.Root, "test.weapon-map.json");
        var report = new { schemaVersion = 2, gameFingerprints = new { exe = new string('A', 64), dll = new string('B', 64) }, hd2RuntimeVersion = "0.13.0", writes = 0, protectionChanges = 0, fixtureFallback = "disabled", mode = "snapshot", runtimeCandidates = new[] { new { resourceHash = "0x0123456789ABCDEF", entityRow = 1, ownership = new { }, resolvedFields = new { }, attacks = Array.Empty<object>(), credibleWikiIdentities = new[] { "Test weapon" } } } };
        var json = System.Text.Json.JsonSerializer.Serialize(report); await File.WriteAllTextAsync(reportPath, json);
        await workspace.LoadReportAsync(reportPath); Assert.Single(workspace.Map!.RuntimeCandidates);
        await File.WriteAllTextAsync(reportPath, json.Replace(new string('A', 64), new string('C', 64)));
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.LoadReportAsync(reportPath));
        await File.WriteAllTextAsync(reportPath, json.Replace("\"writes\":0", "\"writes\":1"));
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.LoadReportAsync(reportPath));
    }
    private sealed class Picker : IResearchFilePicker { public Task<string?> PickAsync(bool report) => Task.FromResult<string?>(null); }
}
