using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class ProjectGenerationTests
{
    [Theory]
    [InlineData("mods/skyeshade/my_mod")][InlineData("mods/Author/mod_1/child")]
    public void Valid_resource_ids(string resource) { ProjectIdentity.ValidateResource(resource); Assert.NotEqual(Guid.Empty, ProjectIdentity.ManagerGuid(resource)); }
    [Theory]
    [InlineData("mods/author")][InlineData("mods/a/../evil")][InlineData("mods/å/test")][InlineData("mods/a/with-hyphen")][InlineData("mods/skyeshade/hd2runtime")][InlineData("mods/skyeshade/hd2runtime/child")][InlineData("mods/codex/loader")][InlineData("mods/a/test\n")]
    public void Invalid_or_reserved_ids_rejected(string resource) => Assert.Throws<InvalidDataException>(() => ProjectIdentity.ValidateResource(resource));
    [Fact]
    public async Task Project_creation_and_reload_preserves_human_readable_structured_changes()
    {
        using var env = new TestEnvironment(); var project = await env.Project(); project.Changes.Add(env.Jar5()); await env.Store.SaveAsync(project);
        var reloaded = await new JsonProjectStore(env.Paths).LoadAsync(project.Id);
        Assert.Equal(project.ManagerGuid, reloaded.ManagerGuid); Assert.Equal("0.5.1", reloaded.SdkVersion);
        // Independently verified against the upstream starter's guid="auto" build.
        Assert.Equal(Guid.Parse("707295c4-83e8-5c66-ad03-a648c82fb693"), reloaded.ManagerGuid);
        Assert.Equal(4, reloaded.Changes.Single().NewValue!.GetValue<int>());
        Assert.Contains("\n", await File.ReadAllTextAsync(env.Paths.ProjectFile(project.Id)));
        Assert.Single(await env.Store.ListAsync());
    }
    [Fact]
    public async Task Duplicate_gets_new_resource_and_guid_and_removal_keeps_files()
    {
        using var env = new TestEnvironment(); var original = await env.Project(); original.Changes.Add(env.Jar5()); await env.Store.SaveAsync(original);
        var duplicate = await env.Projects.DuplicateAsync(original, new("Copy", "Skye", "mods/skyeshade/jar5_copy", "0.1.0"));
        Assert.NotEqual(original.Id, duplicate.Id); Assert.NotEqual(original.ManagerGuid, duplicate.ManagerGuid); Assert.NotEqual(original.Changes[0].Id, duplicate.Changes[0].Id);
        await env.Projects.RenameAsync(duplicate, "Renamed"); Assert.Equal("Renamed", (await env.Store.LoadAsync(duplicate.Id)).DisplayName);
        await env.Store.RemoveFromLibraryAsync(original.Id); Assert.True(File.Exists(env.Paths.ProjectFile(original.Id))); Assert.Single(await env.Store.ListAsync());
        await env.Store.ImportAsync(env.Paths.ProjectFile(original.Id)); Assert.Equal(2, (await env.Store.ListAsync()).Count);
    }
    [Fact]
    public void Jar5_change_is_metadata_driven()
    {
        using var env = new TestEnvironment(); var change = env.Jar5();
        Assert.Equal("damage", change.Domain); Assert.Equal("integer", change.FieldType); Assert.Equal(3, change.ExpectedValue!.GetValue<int>());
        Assert.Equal(4, change.NewValue!.GetValue<int>()); Assert.Equal("Gameplay proven", change.Confidence); Assert.True(change.EnsureEnabled);
    }
    [Fact]
    public async Task Single_patch_generation_uses_public_builder_path()
    {
        using var env = new TestEnvironment(); var project = await env.Project(); project.Changes.Add(env.Jar5(false));
        var lua = env.Generator.Generate(project, env.Metadata);
        Assert.Contains("return hd2.patch({", lua); Assert.Contains("hd2.weapon('JAR-5 Dominator'):projectile():damage()", lua);
        Assert.Contains("hd2.fields.damage.armor_penetration", lua); Assert.Contains("expect=3", lua); Assert.Contains("value=4", lua); Assert.DoesNotContain("hd2.ensure", lua);
    }
    [Fact]
    public async Task Ensure_uses_runtime_defaults()
    {
        using var env = new TestEnvironment(); var project = await env.Project(); project.Changes.Add(env.Jar5());
        var lua = env.Generator.Generate(project, env.Metadata); Assert.Contains("return hd2.ensure({", lua); Assert.Contains("patch={", lua);
        Assert.DoesNotContain("interval=", lua); Assert.DoesNotContain("startup_delay=", lua);
    }
    [Fact]
    public async Task Related_fields_generate_atomic_transaction()
    {
        using var env = new TestEnvironment(); var project = await env.Project();
        project.Changes.Add(env.Changes.Create(env.Metadata, "shield_relay", "radius", "8", false, "Shield"));
        project.Changes.Add(env.Changes.Create(env.Metadata, "shield_relay", "durability", "40000", false, "Shield"));
        var lua = env.Generator.Generate(project, env.Metadata); Assert.Contains("return hd2.transaction({", lua); Assert.Contains("hd2.stratagem('Shield Relay')", lua);
        Assert.Contains("hd2.fields.shield.radius,expect=15,value=8", lua); Assert.Contains("hd2.fields.shield.durability,expect=4000,value=40000", lua);
    }
    [Fact]
    public async Task Single_transaction_only_field_respects_upstream_contract()
    { using var env = new TestEnvironment(); var p = await env.Project(); p.Changes.Add(env.Changes.Create(env.Metadata, "shield_relay", "radius", "8", true, "Shield")); Assert.Contains("transaction={", env.Generator.Generate(p, env.Metadata)); }
    [Fact]
    public async Task Disabled_changes_omitted_and_duplicate_field_rejected()
    {
        using var env = new TestEnvironment(); var p = await env.Project(); var c = env.Jar5(); c.Enabled = false; p.Changes.Add(c);
        Assert.DoesNotContain("value=4", env.Generator.Generate(p, env.Metadata)); c.Enabled = true; p.Changes.Add(env.Jar5());
        Assert.Throws<InvalidDataException>(() => env.Generator.Generate(p, env.Metadata));
    }
    [Theory]
    [InlineData("4.5")][InlineData("5")][InlineData("os.execute('x')")]
    public void Unsupported_or_injected_values_rejected(string value)
    { using var env = new TestEnvironment(); Assert.Throws<InvalidDataException>(() => env.Changes.Create(env.Metadata, "jar5", "armor_penetration", value, true, "Gameplay")); }
    [Fact]
    public async Task Export_zip_inventory_is_deterministic_and_contains_only_gameplay()
    {
        using var env = new TestEnvironment(); var p = await env.Project(); p.Changes.Add(env.Jar5());
        var path = await env.Exporter.ExportAsync(p, env.Metadata); var first = await File.ReadAllBytesAsync(path);
        await env.Exporter.ExportAsync(p, env.Metadata); Assert.Equal(first, await File.ReadAllBytesAsync(path));
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(new[] { "README.md", "build-report.json", "hd2runtime.json", "manifest.json", "mod/9ba626afa44a3aa3.patch_0", "mod/9ba626afa44a3aa3.patch_0.gpu_resources", "mod/9ba626afa44a3aa3.patch_0.stream", "src/addon.lua" }, zip.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(zip.GetEntry("hd2runtime.json")!.Open()); using var metadata = JsonDocument.Parse(await reader.ReadToEndAsync());
        Assert.Equal("mods/skyeshade/hd2runtime", metadata.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("module").GetString());
        Assert.Equal(15, metadata.RootElement.GetProperty("requires").GetProperty("bingus").GetProperty("min_release").GetInt32());
        using (var reportReader = new StreamReader(zip.GetEntry("build-report.json")!.Open()))
            Assert.StartsWith("HD2Runtime ModBuilder ", JsonDocument.Parse(await reportReader.ReadToEndAsync()).RootElement.GetProperty("builder").GetString());
        using var archiveStream = zip.GetEntry("mod/" + GameplayArchive.ArchiveName)!.Open(); using var buffer = new MemoryStream(); archiveStream.CopyTo(buffer); var archive = buffer.ToArray();
        Assert.Equal(0xF0000011U, BitConverter.ToUInt32(archive)); Assert.Equal(1U, BitConverter.ToUInt32(archive, 8));
        Assert.Equal(GameplayArchive.ResourceHash(p.ResourceId), BitConverter.ToUInt64(archive, 104));
        var body = Encoding.UTF8.GetString(archive, 200, (int)BitConverter.ToUInt32(archive, 192));
        Assert.StartsWith("-- HD2-Addon: mods/skyeshade/jar5_ap4\n", body); Assert.Contains("runtime.api_version==1", body); Assert.Contains("loader.version>=16", body);
        Assert.DoesNotContain("VirtualProtect", body); Assert.DoesNotContain("ReadProcessMemory", body); Assert.DoesNotContain("---@meta", body); Assert.DoesNotContain("function hd2.patch", body);
    }
    [Fact]
    public async Task First_workflow_relaunch_and_export_folder_abstraction()
    {
        using var env = new TestEnvironment(); env.GitHub.Offline = true; var app = env.Workspace(); await app.InitializeAsync();
        var ticket = await app.BeginCreationAsync(); var sdk = await app.ResolveCreationAsync(ticket, UpdateDecision.UseInstalled);
        await app.CreateAsync(new("JAR-5 AP4", "SkyeShade", "mods/skyeshade/jar5_ap4", "0.1.0"), sdk);
        await app.AddOrEditChangeAsync("jar5", "armor_penetration", "4", true, "Gameplay", null);
        Assert.Single(app.Project!.Changes); Assert.Contains("value=4", app.LuaPreview);
        await app.ExportAsync(); await app.OpenExportAsync(); Assert.Equal(app.LastExport, env.Desktop.OpenedPath); Assert.True(env.Desktop.SelectedFile);
        var next = env.Workspace(); await next.InitializeAsync(); await next.OpenAsync(next.Library.Single().Id);
        Assert.Equal(app.Project.Id, next.Project!.Id); Assert.Equal(app.LuaPreview, next.LuaPreview);
        await next.OpenProjectFolderAsync(next.Project.Id); Assert.False(env.Desktop.SelectedFile);
    }
}
