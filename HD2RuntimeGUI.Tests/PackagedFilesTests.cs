using System.IO.Compression;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Additional packaged files (format 12): files from the user's computer that every export adds to the mod ZIP (a thumbnail), read at
// export. The generated files are unchanged; an entry that cannot be packaged as asked refuses the export, naming the file.
public sealed class PackagedFilesTests
{
    private static readonly string[] GeneratedEntries = [.. PackagedFiles.Generated.Order(StringComparer.Ordinal)];
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string name = "Packaged Files")
    {
        var (w, _) = await ExportFixtureTests.Fresh(e, name);
        w.Project!.ExportDirectory = e.Paths.Exports;
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "720", false);
        return w;
    }
    // A source file under the test root; folder and file names may contain spaces.
    private static string Source(TestEnvironment e, string relative, byte[] bytes)
    {
        var path = Path.GetFullPath(Path.Combine(e.Paths.Root, "sources", relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); return path;
    }
    // Bytes that survive no text conversion: a PNG signature, NULs, CR/LF and every byte value.
    private static byte[] Binary(int seed) => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, (byte)seed, .. Enumerable.Range(0, 256).Select(i => (byte)(i ^ seed))];
    private static Dictionary<string, byte[]> Entries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.ToDictionary(z => z.FullName, z => { using var s = z.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }
    private static void Set(ModProject p, params (string Source, string Destination)[] files) =>
        p.PackagedFiles = [.. files.Select(f => new PackagedFile { Source = f.Source, Destination = f.Destination })];
    private static async Task<string> Refused(BuilderWorkspace w)
    {
        var path = Path.Combine(w.Project!.ExportDirectory, "Packaged-Files-" + w.Project.Version + ".zip");
        if (File.Exists(path)) File.Delete(path);
        var message = (await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync)).Message;
        Assert.False(File.Exists(path)); Assert.Null(w.LastExport);
        return message;
    }

    // 1. No additional files: exactly the generated files, the project JSON has no packagedFiles and keeps its format.
    [Fact] public async Task Without_additional_files_the_export_holds_exactly_the_generated_files()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.ExportAsync(); var first = await File.ReadAllBytesAsync(w.LastExport!);
        Assert.Equal(GeneratedEntries, Entries(w.LastExport!).Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("packagedFiles", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id)), StringComparison.OrdinalIgnoreCase);
        Assert.True(w.Project.FormatVersion < 12); Assert.True(ProjectIdentity.RequiredFormat(w.Project) < 12);
        // An empty list is the same as none.
        w.Project.PackagedFiles = []; await w.ExportAsync(); Assert.Equal(first, await File.ReadAllBytesAsync(w.LastExport!));
    }

    // 2 and 14. One file, its exact bytes, next to the unchanged generated files.
    [Fact] public async Task One_file_is_added_with_its_exact_bytes_and_the_generated_files_are_unchanged()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.ExportAsync(); var plain = Entries(w.LastExport!);
        var bytes = Binary(7); await w.AddPackagedFileAsync(Source(e, "thumbnail.png", bytes));
        Assert.Equal("thumbnail.png", w.Project!.PackagedFiles!.Single().Destination);
        await w.ExportAsync(); var packaged = Entries(w.LastExport!);
        Assert.Equal(bytes, packaged["thumbnail.png"]);
        Assert.Equal(plain.Keys.Append("thumbnail.png").Order(StringComparer.Ordinal), packaged.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, content) in plain) Assert.Equal(content, packaged[name]);
    }

    // 3, 4, 5 and 6. Several files, nested destinations, spaces in source paths and in ZIP paths.
    [Fact] public async Task Several_files_go_to_nested_paths_with_spaces()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var a = Source(e, "My Thumbnails/thumb nail.png", Binary(1)); var b = Source(e, "preview.jpg", Binary(2)); var c = Source(e, "docs folder/read me first.txt", "hello\r\nmod"u8.ToArray());
        await w.AddPackagedFileAsync(a); await w.AddPackagedFileAsync(b); await w.AddPackagedFileAsync(c);
        var listed = w.PackagedFileStates();
        Assert.Equal(new[] { "thumb nail.png", "preview.jpg", "read me first.txt" }, listed.Select(f => f.Destination));
        Assert.Equal(a, listed[0].Source); Assert.Equal("thumb nail.png", listed[0].Name); Assert.All(listed, f => Assert.Null(f.Issue));
        await w.SetPackagedFileDestinationAsync(listed[1], @" images\previews\preview 1.jpg ");
        await w.SetPackagedFileDestinationAsync(w.PackagedFileStates()[2], "extras/notes and docs/read me first.txt");
        Assert.Equal("images/previews/preview 1.jpg", w.Project!.PackagedFiles![1].Destination);
        await w.ExportAsync(); var zip = Entries(w.LastExport!);
        Assert.Equal(Binary(1), zip["thumb nail.png"]); Assert.Equal(Binary(2), zip["images/previews/preview 1.jpg"]);
        Assert.Equal("hello\r\nmod"u8.ToArray(), zip["extras/notes and docs/read me first.txt"]);
        Assert.Equal(GeneratedEntries.Length + 3, zip.Count);
    }

    // 7. A missing source refuses the export and names the file and the path.
    [Fact] public async Task A_missing_source_refuses_the_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var path = Source(e, "thumbnail.png", Binary(3)); await w.AddPackagedFileAsync(path); File.Delete(path);
        Assert.Contains("Source file does not exist", w.PackagedFileStates().Single().Issue); Assert.False(w.PackagedFileStates().Single().DestinationIssue);
        var message = await Refused(w);
        Assert.StartsWith("Cannot package additional file: thumbnail.png\n", message);
        Assert.Contains("Source file does not exist: " + path, message);
        // Adding a file that does not exist is refused where it is picked.
        Assert.Contains("does not exist", (await Assert.ThrowsAsync<InvalidDataException>(() => w.AddPackagedFileAsync(path + ".missing"))).Message);
        // A relative source (hand-edited project) is not resolved against any folder.
        Set(w.Project!, ("thumbnail.png", "thumbnail.png"));
        Assert.Contains("Source path is not a full path: thumbnail.png", await Refused(w));
    }

    // 8. A source another program holds exclusively cannot be read: refused, never skipped.
    [Fact] public async Task An_unreadable_source_refuses_the_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var path = Source(e, "locked thumbnail.png", Binary(4)); await w.AddPackagedFileAsync(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var message = await Refused(w);
            Assert.StartsWith("Cannot package additional file: locked thumbnail.png\n", message);
            Assert.Contains("Source file cannot be read: " + path, message);
        }
        await w.ExportAsync(); Assert.Equal(Binary(4), Entries(w.LastExport!)["locked thumbnail.png"]);
    }

    // 9 and 10. Absolute paths, drives, UNC paths and anything that leaves the ZIP root are refused.
    [Theory]
    [InlineData("C:/thumbnail.png")] [InlineData("C:thumbnail.png")] [InlineData("/thumbnail.png")] [InlineData(@"\thumbnail.png")] [InlineData(@"\\server\share\thumbnail.png")]
    [InlineData("../thumbnail.png")] [InlineData("images/../../thumbnail.png")] [InlineData("images/../thumbnail.png")] [InlineData("./thumbnail.png")] [InlineData("..")]
    public async Task Absolute_and_escaping_destinations_refuse_the_export(string destination)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        Set(w.Project!, (Source(e, "thumbnail.png", Binary(5)), destination));
        var message = await Refused(w);
        Assert.Equal("Cannot package additional file: thumbnail.png", message.Split('\n')[0]);
        Assert.Contains("Invalid ZIP destination: " + destination, message);
        Assert.NotNull(PackagedFiles.DestinationIssue(destination));
    }

    // Other unsafe ZIP paths: empty, a folder, Windows-invalid names, reserved device names, too long, the mod/ folder.
    [Theory]
    [InlineData("")] [InlineData("images/")] [InlineData("images//thumbnail.png")] [InlineData("thumbnail.png.")] [InlineData("thumb?.png")] [InlineData("a|b.png")]
    [InlineData("CON")] [InlineData("images/nul.png")] [InlineData("mod/thumbnail.png")] [InlineData("MOD/preview/thumbnail.png")] [InlineData("thumb\tnail.png")]
    public void Unsafe_destinations_are_reported(string destination) => Assert.NotNull(PackagedFiles.DestinationIssue(destination));
    [Fact] public void Long_destinations_are_reported()
    {
        Assert.Null(PackagedFiles.DestinationIssue(new string('a', 236) + ".png"));
        Assert.NotNull(PackagedFiles.DestinationIssue(new string('a', 237) + ".png"));
    }
    [Theory]
    [InlineData("thumbnail.png")] [InlineData("images/thumbnail.png")] [InlineData("my images/thumb nail.png")] [InlineData(".thumbnail")] [InlineData("modding/thumbnail.png")] [InlineData("src/thumbnail.png")]
    public void Safe_destinations_are_accepted(string destination) => Assert.Null(PackagedFiles.DestinationIssue(destination));

    // 11. A path ModBuilder generates (in any case), a file under mod/, or a path that would be both file and folder is refused, never overwritten.
    [Theory]
    [InlineData("manifest.json", "ModBuilder generates manifest.json")] [InlineData("readme.MD", "ModBuilder generates README.md")]
    [InlineData("src/addon.lua", "ModBuilder generates src/addon.lua")] [InlineData("hd2runtime.json", "ModBuilder generates hd2runtime.json")]
    [InlineData("mod/thumbnail.png", "mod/ is reserved")] [InlineData("mod", "mod would be both a file and a folder")]
    [InlineData("src", "src would be both a file and a folder")] [InlineData("README.md/thumbnail.png", "README.md would be both a file and a folder")]
    public async Task A_destination_that_clashes_with_a_generated_file_refuses_the_export(string destination, string reason)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.ExportAsync(); var generated = Entries(w.LastExport!);
        await w.AddPackagedFileAsync(Source(e, "thumbnail.png", Binary(6)));
        await w.SetPackagedFileDestinationAsync(w.PackagedFileStates().Single(), destination);
        // Saved as typed, reported on its row, refused at export.
        Assert.Equal(destination, w.Project!.PackagedFiles!.Single().Destination);
        Assert.Contains(reason, w.PackagedFileStates().Single().Issue); Assert.True(w.PackagedFileStates().Single().DestinationIssue);
        var message = await Refused(w);
        Assert.Contains("Invalid ZIP destination: " + destination, message); Assert.Contains(reason, message);
        // The editor's list of generated files is the exporter's.
        Assert.Equal(GeneratedEntries, generated.Keys.Order(StringComparer.Ordinal));
    }

    // 12. Two additional files to the same ZIP path (case-insensitive, as Windows extracts) are refused.
    [Theory] [InlineData("thumbnail.png")] [InlineData("Thumbnail.PNG")] [InlineData("thumbnail.png/inner.png")]
    public async Task Duplicate_destinations_refuse_the_export(string second)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.AddPackagedFileAsync(Source(e, "one/thumbnail.png", Binary(8))); await w.AddPackagedFileAsync(Source(e, "two/thumbnail.png", Binary(9)));
        await w.SetPackagedFileDestinationAsync(w.PackagedFileStates()[1], second);
        var listed = w.PackagedFileStates();
        Assert.Null(listed[0].Issue); Assert.NotNull(listed[1].Issue);
        var message = await Refused(w);
        Assert.StartsWith("Cannot package additional file: thumbnail.png\n", message); Assert.Contains("Invalid ZIP destination: " + second, message);
        // Renaming the second file resolves it where it is listed.
        await w.SetPackagedFileDestinationAsync(w.PackagedFileStates()[1], "thumbnail-2.png");
        Assert.All(w.PackagedFileStates(), f => Assert.Null(f.Issue));
        await w.ExportAsync(); var zip = Entries(w.LastExport!);
        Assert.Equal(Binary(8), zip["thumbnail.png"]); Assert.Equal(Binary(9), zip["thumbnail-2.png"]);
    }

    // 13. The list is saved with the project (format 12), reopened, exported and copied with the project.
    [Fact] public async Task Save_reopen_and_export_keeps_the_additional_files()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var path = Source(e, "Arsenal art/thumbnail.png", Binary(10));
        await w.AddPackagedFileAsync(path); await w.SetPackagedFileDestinationAsync(w.PackagedFileStates().Single(), "images/thumbnail.png");
        await w.ExportAsync(); var exported = await File.ReadAllBytesAsync(w.LastExport!);
        Assert.Equal(12, w.Project!.FormatVersion);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id));
        Assert.Contains("\"packagedFiles\"", json); Assert.Contains("images/thumbnail.png", json);

        var reopened = e.Workspace(); await reopened.OpenAsync(w.Project.Id);
        var file = reopened.Project!.PackagedFiles!.Single();
        Assert.Equal(path, file.Source); Assert.Equal("images/thumbnail.png", file.Destination); Assert.Equal(12, reopened.Project.FormatVersion);
        await reopened.ExportAsync(); Assert.Equal(exported, await File.ReadAllBytesAsync(reopened.LastExport!));

        // A copy of the project packages the same file; removing the last file omits the section again.
        var copy = await e.Projects.DuplicateAsync(reopened.Project, new("Packaged Copy", "Tests", "mods/tests/packaged_copy", "1.0.0"));
        Assert.Equal("images/thumbnail.png", copy.PackagedFiles!.Single().Destination); Assert.Equal(12, copy.FormatVersion);
        await reopened.RemovePackagedFileAsync(reopened.PackagedFileStates().Single());
        Assert.Null(reopened.Project.PackagedFiles);
        Assert.DoesNotContain("packagedFiles", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GeneratedEntries, (await ExportEntries(reopened)).Keys.Order(StringComparer.Ordinal));
    }
    private static async Task<Dictionary<string, byte[]>> ExportEntries(BuilderWorkspace w) { await w.ExportAsync(); return Entries(w.LastExport!); }

    // 15. Exports stay deterministic: the same project and the same file give the same ZIP bytes; the file's timestamp does not matter.
    [Fact] public async Task Exports_with_additional_files_are_deterministic()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var path = Source(e, "thumbnail.png", Binary(11)); await w.AddPackagedFileAsync(path);
        await w.ExportAsync(); var first = await File.ReadAllBytesAsync(w.LastExport!);
        File.SetLastWriteTimeUtc(path, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        await w.ExportAsync(); Assert.Equal(first, await File.ReadAllBytesAsync(w.LastExport!));
        using (var zip = ZipFile.OpenRead(w.LastExport!))
        { var entry = zip.GetEntry("thumbnail.png")!; Assert.Equal(new DateTime(2000, 1, 1), entry.LastWriteTime.DateTime); Assert.Equal(0, entry.ExternalAttributes); }
        // A changed file is packaged as it is at export.
        File.WriteAllBytes(path, Binary(12)); Assert.Equal(Binary(12), (await ExportEntries(w))["thumbnail.png"]);
    }

    // Project files: older ModBuilder versions refuse format 12 instead of dropping the list; the list's shape is checked on load.
    [Fact] public async Task Project_validation_checks_the_list_shape_only()
    {
        using var e = new TestEnvironment(); var p = await e.Project(); p.FormatVersion = 12;
        Set(p, (@"C:\thumbnails\thumbnail.png", "../thumbnail.png"));
        ProjectIdentity.Validate(p); // An unsafe destination still opens; it is reported on its row and refused at export.
        Assert.Equal(12, ProjectIdentity.RequiredFormat(p));
        p.PackagedFiles = [new() { Source = "C:\\a\0.png", Destination = "a.png" }];
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.PackagedFiles = [.. Enumerable.Range(0, PackagedFiles.MaxFiles + 1).Select(i => new PackagedFile { Source = $"C:\\{i}.png", Destination = $"{i}.png" })];
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        // Format 13 (1.6.0, option-only edits) is the newest this version reads.
        p.FormatVersion = 13; p.PackagedFiles = null; ProjectIdentity.Validate(p);
        p.FormatVersion = 14;
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
    }

    // A second click on a row's Remove (the row already gone) removes nothing else.
    [Fact] public async Task A_stale_row_edit_changes_nothing()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.AddPackagedFileAsync(Source(e, "a.png", Binary(13))); await w.AddPackagedFileAsync(Source(e, "b.png", Binary(14)));
        var first = w.PackagedFileStates()[0];
        await w.RemovePackagedFileAsync(first); await w.RemovePackagedFileAsync(first);
        Assert.Equal("b.png", w.Project!.PackagedFiles!.Single().Destination);
    }

    [Fact] public async Task Adding_more_than_the_limit_is_refused()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        for (var i = 0; i < PackagedFiles.MaxFiles; i++) await w.AddPackagedFileAsync(Source(e, $"{i}.png", Binary(i)));
        Assert.Contains("at most", (await Assert.ThrowsAsync<InvalidDataException>(() => w.AddPackagedFileAsync(Source(e, "extra.png", Binary(1))))).Message);
        Assert.Equal(PackagedFiles.MaxFiles, w.Project!.PackagedFiles!.Count);
    }
}
