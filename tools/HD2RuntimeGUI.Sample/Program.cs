using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/sample-workspace-0.13");
var paths = new AppPaths(root);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
var github = new GitHubReleaseClient(http);
var cache = new SdkCache(paths, new MetadataReader(), github);
var sdk = await cache.GetCurrentAsync();
if (args.Contains("--online"))
{
    var status = await new SdkUpdateService(cache, github, paths).CheckAsync();
    Console.WriteLine(status.Message);
    if (!status.VerifiedOnline || status.Latest == null) return 1;
    sdk = await cache.InstallAsync(status.Latest);
    Console.WriteLine($"Verified installed SDK: {sdk.Version}");
}
var catalog = WeaponChangeService.Catalog(sdk);
Console.WriteLine($"Catalog: {catalog.Weapons.Count} weapons; {catalog.Summary.FieldInstances} capability entries.");
var store = new JsonProjectStore(paths); var service = new ProjectService(store, paths);
var changes = new WeaponChangeService(); var generator = new LuaGenerator(new ChangeService(), changes);
var presets = JsonSerializer.Deserialize<List<Sample>>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "player-weapons.json")), JsonStorage.Options)!;
foreach (var sample in presets)
{
    var resource = "mods/skyeshade/" + sample.Name.ToLowerInvariant();
    var existing = (await store.ListAsync()).SingleOrDefault(p => p.ResourceId == resource);
    var project = existing == null ? await service.CreateAsync(new(sample.Name, "SkyeShade", resource, "0.1.0", sample.Description), sdk) : await store.LoadAsync(existing.Id);
    if (project.SdkVersion != sdk.Version) throw new InvalidDataException("Use a fresh sample workspace when changing SDK versions.");
    project.WeaponChanges = sample.Changes.Select(c => changes.Create(sdk, sample.Weapon, c.Field, c.Value.GetRawText(), false)).ToList();
    await store.SaveAsync(project);
    Console.WriteLine(await new ModExporter(generator).ExportAsync(project, sdk));
    Console.WriteLine(paths.ProjectFile(project.Id));
}
return 0;
internal sealed record Sample(string Name, string Description, string Weapon, IReadOnlyList<SampleChange> Changes);
internal sealed record SampleChange(string Field, JsonElement Value);
