using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/sample-workspace-0.14");
var paths = new AppPaths(root);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
var github = new GitHubReleaseClient(http);
var cache = new SdkCache(paths, new MetadataReader(), github);
if (args.FirstOrDefault(a => a.StartsWith("--import-icons")) is { } iconArg)
{
    // Developer check of the local, read-only icon import (the GUI's Settings page does the same).
    var dataDir = iconArg.Contains('=') ? iconArg[(iconArg.IndexOf('=') + 1)..] : HD2RuntimeGUI.Core.GameAssets.GameIconStore.DefaultGameDataPath() ?? throw new DirectoryNotFoundException("Game data folder not found.");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var manifest = await new HD2RuntimeGUI.Core.GameAssets.GameIconStore(paths).ImportAsync(dataDir);
    foreach (var s in manifest.Sources) Console.WriteLine($"{s.Resource}: {s.Icons} icons, sha256 {s.Sha256}");
    Console.WriteLine($"Type bindings: {manifest.StratagemTypeIcons.Count} stratagem, {manifest.BoosterTypeIcons.Count} booster; {watch.Elapsed.TotalSeconds:F1}s");
    return 0;
}
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
if (args.Contains("--runtime-validate")) return RuntimeValidation.Run(sdk, new LuaGenerator(new ChangeService()), Arg("--runtime-src=") ?? throw new ArgumentException("--runtime-src=<HD2Runtime source> is required"),
    Arg("--lua-dll=") ?? Path.Combine(Environment.GetEnvironmentVariable("HD2_GAME_ROOT") ?? @"C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2", "bin", "lua51.dll"), root);
if (args.Contains("--stratagems") || args.Contains("--defensive")) return await StratagemSamples.Run(paths, sdk, cache, github, store, service, generator, args.Contains("--defensive"));
if (args.Contains("--grouping")) return await GroupingSamples.Run(paths, sdk, cache, github, store, service, generator);
if (args.Contains("--support")) return await SupportSamples.Run(paths, sdk, cache, github, store, service, generator);
if (args.Contains("--plans")) return await PlanSamples.Run(paths, sdk, cache, github, store, service, generator);
var presets = JsonSerializer.Deserialize<List<Sample>>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, args.Contains("--heat") ? "player-weapon-heat.json" : args.Contains("--ammo") ? "player-weapon-ammo.json" : "player-weapons.json")), JsonStorage.Options)!;
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
string? Arg(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
internal sealed record Sample(string Name, string Description, string Weapon, IReadOnlyList<SampleChange> Changes);
internal sealed record SampleChange(string Field, JsonElement Value);
