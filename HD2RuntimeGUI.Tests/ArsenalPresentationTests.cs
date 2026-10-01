using System.IO.Compression;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Arsenal presentation (format 12). HD2Arsenal 0.36.2 builds its library entry from manifest.json: Description (plain text) and
// IconPath (an image file in the ZIP, relative to its root). ModBuilder writes the user's description before the dependency line every
// manifest description states, and packages the icon at the ZIP root under its own file name, as Arsenal's own Manifest Builder does.
public sealed class ArsenalPresentationTests
{
    private const string Dependencies = "Requires Bingus Shared Loader v15+ / API 1 and HD2Runtime 0.28.1+ / API 1; install dependencies separately.";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        var (w, _) = await ExportFixtureTests.Fresh(e, "Arsenal Look");
        w.Project!.ExportDirectory = e.Paths.Exports;
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "720", false);
        return w;
    }
    private static string Source(TestEnvironment e, string name, byte[] bytes)
    {
        var path = Path.GetFullPath(Path.Combine(e.Paths.Root, "images", name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); return path;
    }
    // Minimal files with the signature of each image type Arsenal accepts.
    private static byte[] Png(int seed) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, (byte)seed, .. Enumerable.Range(0, 64).Select(i => (byte)(i * seed))];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
    private static readonly byte[] Gif = [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0];
    private static readonly byte[] Webp = [.. "RIFF"u8, 4, 0, 0, 0, .. "WEBPVP8 "u8];
    private static Dictionary<string, byte[]> Entries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.ToDictionary(z => z.FullName, z => { using var s = z.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }
    private static async Task<Dictionary<string, byte[]>> Export(BuilderWorkspace w) { await w.ExportAsync(); return Entries(w.LastExport!); }
    private static JsonObject Manifest(Dictionary<string, byte[]> zip) => JsonNode.Parse(zip["manifest.json"])!.AsObject();
    private static async Task<string> Refused(BuilderWorkspace w)
    {
        var path = Path.Combine(w.Project!.ExportDirectory, "Arsenal-Look-" + w.Project.Version + ".zip");
        if (File.Exists(path)) File.Delete(path);
        var message = (await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync)).Message;
        Assert.False(File.Exists(path)); Assert.Null(w.LastExport);
        return message;
    }

    // Projects without Arsenal presentation export exactly as before: the manifest description is the dependency line, no IconPath.
    [Fact] public async Task A_project_without_Arsenal_presentation_exports_unchanged()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var zip = await Export(w); var manifest = Manifest(zip);
        Assert.Equal(Dependencies, (string)manifest["Description"]!); Assert.False(manifest.ContainsKey("IconPath"));
        Assert.Equal(new[] { "Version", "Guid", "Name", "Description", "Options" }, manifest.Select(p => p.Key));
        Assert.Equal(PackagedFiles.Generated.Order(StringComparer.Ordinal), zip.Keys.Order(StringComparer.Ordinal));
        Assert.Null(w.Project!.Arsenal); Assert.False(w.ArsenalState().Configured);
        Assert.DoesNotContain("\"arsenal\"", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)));
        Assert.True(w.Project.FormatVersion < 12);
    }

    // A description: shown first, then the dependency line; the option, hd2runtime.json, README and the game files are unchanged.
    [Fact] public async Task A_description_is_written_before_the_dependency_line()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var before = await Export(w);
        await w.SetArsenalDescriptionAsync("  Faster Liberator.\r\nFire rate 720 RPM.\r\n\r\n ");
        Assert.Equal("Faster Liberator.\nFire rate 720 RPM.", w.Project!.Arsenal!.Description); Assert.Equal(12, w.Project.FormatVersion);
        var after = await Export(w); var manifest = Manifest(after);
        Assert.Equal("Faster Liberator.\nFire rate 720 RPM.\n\n" + Dependencies, (string)manifest["Description"]!);
        Assert.Equal(Dependencies, (string)manifest["Options"]![0]!["Description"]!);
        Assert.False(manifest.ContainsKey("IconPath"));
        foreach (var name in before.Keys.Where(n => n is not ("manifest.json" or "build-report.json"))) Assert.Equal(before[name], after[name]);
        // Clearing it removes the section again.
        await w.SetArsenalDescriptionAsync("   ");
        Assert.Null(w.Project.Arsenal); Assert.Equal(before["manifest.json"], (await Export(w))["manifest.json"]);
    }

    // An icon: packaged at the ZIP root under its own name, with IconPath naming it, byte for byte.
    [Theory] [InlineData("My Icon.png")] [InlineData("icon.JPG")] [InlineData("icon.jpeg")] [InlineData("icon.gif")] [InlineData("icon.webp")]
    public async Task An_icon_is_packaged_and_named_by_IconPath(string name)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var bytes = Path.GetExtension(name).ToLowerInvariant() switch { ".png" => Png(3), ".gif" => Gif, ".webp" => Webp, _ => Jpeg };
        await w.SetArsenalIconAsync(Source(e, "art/" + name, bytes));
        var state = w.ArsenalState();
        Assert.Equal(name, state.IconEntry); Assert.Null(state.IconIssue); Assert.True(state.Configured);
        var zip = await Export(w); var manifest = Manifest(zip);
        Assert.Equal(name, (string)manifest["IconPath"]!); Assert.Equal(bytes, zip[name]);
        // Arsenal's Manifest Builder order: Version, Guid, Name, Description, IconPath, Options.
        Assert.Equal(new[] { "Version", "Guid", "Name", "Description", "IconPath", "Options" }, manifest.Select(p => p.Key));
        Assert.Equal(Dependencies, (string)manifest["Description"]!);
    }

    // Saved with the project and reopened: the same presentation, the same export bytes; repeated exports are identical.
    [Fact] public async Task The_presentation_is_saved_reopened_and_exported_identically()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var icon = Source(e, "Arsenal art/thumbnail.png", Png(5));
        await w.SetArsenalDescriptionAsync("Line one.\nLine two."); await w.SetArsenalIconAsync(icon);
        await w.ExportAsync(); var first = await File.ReadAllBytesAsync(w.LastExport!);
        await w.ExportAsync(); Assert.Equal(first, await File.ReadAllBytesAsync(w.LastExport!));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id)))!.AsObject();
        Assert.Equal(12, (int)json["formatVersion"]!);
        Assert.Equal("Line one.\nLine two.", (string)json["arsenal"]!["description"]!); Assert.Equal(icon, (string)json["arsenal"]!["icon"]!);

        var reopened = e.Workspace(); await reopened.OpenAsync(w.Project.Id);
        Assert.Equal("Line one.\nLine two.", reopened.Project!.Arsenal!.Description); Assert.Equal(icon, reopened.Project.Arsenal.Icon);
        await reopened.ExportAsync(); Assert.Equal(first, await File.ReadAllBytesAsync(reopened.LastExport!));
        using (var zip = ZipFile.OpenRead(reopened.LastExport!))
        { var entry = zip.GetEntry("thumbnail.png")!; Assert.Equal(new DateTime(2000, 1, 1), entry.LastWriteTime.DateTime); Assert.Equal(0, entry.ExternalAttributes); }

        // A copy of the project keeps it; removing the icon and the description omits the section.
        var copy = await e.Projects.DuplicateAsync(reopened.Project, new("Arsenal Copy", "Tests", "mods/tests/arsenal_copy", "1.0.0"));
        Assert.Equal(icon, copy.Arsenal!.Icon); Assert.Equal(12, copy.FormatVersion);
        await reopened.SetArsenalIconAsync(null); Assert.Equal("Line one.\nLine two.", reopened.Project.Arsenal!.Description);
        await reopened.SetArsenalDescriptionAsync("");
        Assert.Null(reopened.Project.Arsenal);
        Assert.DoesNotContain("\"arsenal\"", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)));
    }

    // A missing icon refuses the export, naming the icon and its path; nothing is written.
    [Fact] public async Task A_missing_icon_refuses_the_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var icon = Source(e, "icon.png", Png(7)); await w.SetArsenalIconAsync(icon); File.Delete(icon);
        Assert.Contains("Source file does not exist: " + icon, w.ArsenalState().IconIssue);
        var message = await Refused(w);
        Assert.StartsWith("Cannot use the Arsenal icon: icon.png\n", message); Assert.Contains("Source file does not exist: " + icon, message);
        // Selecting a file that does not exist is refused where it is picked.
        Assert.Contains("does not exist", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetArsenalIconAsync(icon))).Message);
    }

    // Metadata Arsenal could not show as written is rejected clearly: in the section, and by the export.
    [Theory]
    [InlineData("Uses <b>bold</b> text.", "Arsenal removes HTML-like tags from descriptions, such as <b>")]
    [InlineData("Bell \u0007 character.", "contains a control character")]
    public async Task An_invalid_description_is_reported_and_refuses_the_export(string text, string reason)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetArsenalDescriptionAsync(text);
        Assert.Contains(reason, w.ArsenalState().DescriptionIssue);
        var message = await Refused(w);
        Assert.StartsWith("Cannot write the Arsenal description.\n", message); Assert.Contains(reason, message);
    }
    [Fact] public async Task A_too_long_description_is_reported()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetArsenalDescriptionAsync(new string('a', Arsenal.MaxDescription)); Assert.Null(w.ArsenalState().DescriptionIssue);
        await w.SetArsenalDescriptionAsync(new string('a', Arsenal.MaxDescription + 1));
        Assert.Contains("longer than 4000 characters", w.ArsenalState().DescriptionIssue);
        Assert.Contains("longer than 4000", await Refused(w));
        // A lone angle bracket survives Arsenal's tag removal (its pattern needs '<' and a later '>'); '<' ... '>' does not, as Arsenal
        // would show "Damage 5  50 m".
        await w.SetArsenalDescriptionAsync("Damage 5 < 6 & more.\tTabbed."); Assert.Null(w.ArsenalState().DescriptionIssue);
        await w.SetArsenalDescriptionAsync("Range > 50 m."); Assert.Null(w.ArsenalState().DescriptionIssue);
        await w.SetArsenalDescriptionAsync("Damage 5 < 6, range > 50 m"); Assert.Contains("such as < 6, range >", w.ArsenalState().DescriptionIssue);
    }
    [Theory]
    [InlineData("icon.bmp", "is not one of them")] [InlineData("icon", "is not one of them")]
    [InlineData("text.png", "text.png is not a PNG image")] [InlineData("png.jpg", "png.jpg is not a JPG image")]
    public async Task An_icon_Arsenal_cannot_show_is_refused_where_it_is_picked(string name, string reason)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var bytes = name == "png.jpg" ? Png(1) : "not an image"u8.ToArray();
        Assert.Contains(reason, (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetArsenalIconAsync(Source(e, name, bytes)))).Message);
        Assert.Null(w.Project!.Arsenal);
        // A hand-edited project with such an icon is refused at export.
        w.Project.Arsenal = new() { Icon = Source(e, name, bytes) };
        var message = await Refused(w);
        Assert.StartsWith($"Cannot use the Arsenal icon: {name}\n", message); Assert.Contains(reason, message);
    }
    [Fact] public async Task An_icon_replaced_by_another_file_type_is_refused_at_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var icon = Source(e, "icon.png", Png(2)); await w.SetArsenalIconAsync(icon);
        File.WriteAllBytes(icon, Jpeg);
        Assert.Contains("icon.png is not a PNG image", w.ArsenalState().IconIssue);
        Assert.Contains("icon.png is not a PNG image", await Refused(w));
    }

    // Additional files keep working next to the icon; one at the icon's ZIP path is refused, saying it is the Arsenal icon.
    [Fact] public async Task Additional_files_work_alongside_the_icon_and_cannot_replace_it()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetArsenalIconAsync(Source(e, "arsenal/thumbnail.png", Png(9)));
        await w.AddPackagedFileAsync(Source(e, "nexus/preview.png", Png(10)));
        await w.SetPackagedFileDestinationAsync(w.PackagedFileStates().Single(), "images/preview.png");
        var zip = await Export(w);
        Assert.Equal(Png(9), zip["thumbnail.png"]); Assert.Equal(Png(10), zip["images/preview.png"]);
        Assert.Equal("thumbnail.png", (string)Manifest(zip)["IconPath"]!);
        await w.SetPackagedFileDestinationAsync(w.PackagedFileStates().Single(), "Thumbnail.PNG");
        Assert.Contains("thumbnail.png is the Arsenal icon", w.PackagedFileStates().Single().Issue);
        var message = await Refused(w);
        Assert.StartsWith("Cannot package additional file: preview.png\n", message); Assert.Contains("thumbnail.png is the Arsenal icon", message);
    }

    // Project files: format 12 for any Arsenal presentation; the section's shape is checked on load.
    [Fact] public async Task Project_validation_checks_the_presentation_shape()
    {
        using var e = new TestEnvironment(); var p = await e.Project(); p.FormatVersion = 12;
        p.Arsenal = new() { Description = "<b>kept for review</b>" };
        ProjectIdentity.Validate(p); Assert.Equal(12, ProjectIdentity.RequiredFormat(p));
        p.Arsenal = new() { Icon = "C:\\icons\\a\u0001.png" };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.Arsenal = new() { Description = new string('a', 16 * Arsenal.MaxDescription + 1) };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
    }
}
