using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/sample-workspace");
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
var store = new JsonProjectStore(paths);
var service = new ProjectService(store, paths);
var existing = (await store.ListAsync()).SingleOrDefault(p => p.ResourceId == "mods/skyeshade/jar5_ap4");
var project = existing == null ? await service.CreateAsync(new("JAR-5 AP4", "SkyeShade", "mods/skyeshade/jar5_ap4", "0.1.0", "JAR-5 armor penetration 3 → 4, using the public SDK contract."), sdk) : await store.LoadAsync(existing.Id);
project.Changes = [new ChangeService().Create(sdk, "jar5", "armor_penetration", "4", true, "JAR-5")];
project.Changes[0].Id = Guid.Parse("011cce6c-1568-4bcf-88af-c0235318d988");
await store.SaveAsync(project);
var generator = new LuaGenerator(new ChangeService());
var output = await new ModExporter(generator).ExportAsync(project, sdk);
Console.WriteLine(output);
Console.WriteLine(paths.ProjectFile(project.Id));
Console.WriteLine(generator.Generate(project, sdk));
return 0;
