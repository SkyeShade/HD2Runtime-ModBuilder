using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

internal static class PlanSamples
{
    public static async Task<int> Run(AppPaths paths, SdkMetadata sdk, ISdkCache cache, IGitHubReleaseClient github,
        IProjectStore store, IProjectService projects, ILuaGenerator generator)
    {
        if (sdk.Plans == null) throw new InvalidDataException("Install SDK 0.19 or newer with composition plan capabilities.");
        var desktop = new NoDesktop();
        foreach (var name in new[] { "ConcussiveComposition019", "ProjectileSwapAndTune019" })
        {
            var w = new BuilderWorkspace(store, projects, cache, new SdkUpdateService(cache, github, paths), new ChangeService(), generator, new ModExporter(generator), desktop, desktop, paths);
            await w.CreateAsync(new(name, "SkyeShade", "mods/skyeshade/" + name.ToLowerInvariant(), "0.1.0"), sdk);
            if (name == "ConcussiveComposition019")
            {
                const string weapon = "AR-23C Liberator Concussive";
                await w.SetWeaponChangeAsync(weapon, "weapon.fire_rate", "1100", false);
                await w.SetObjectScalarAsync(weapon, "primary", "projectile", null, "damage.push_force", "30", true);
                foreach (var lane in new[] { "direct", "slight", "large", "extreme" }) await w.SetObjectScalarAsync(weapon, "primary", "projectile", null, "damage.ap_" + lane, "3", false);
                var source = w.ExplosionSources.First(s => s.Projectile?.Weapon == "R-36 Eruptor");
                await w.SetTerminalAsync(weapon, "primary", "impact", source, true);
                await w.SetTerminalAsync(weapon, "primary", "expiry", source, false);
            }
            else
            {
                const string weapon = "P-113 Verdict";
                await w.SetProjectileAsync(weapon, "primary", new("JAR-5 Dominator", "primary"));
                await w.SetObjectScalarAsync(weapon, "primary", "projectile", null, "projectile.velocity", "350", true);
                await w.SetObjectScalarAsync(weapon, "primary", "projectile", null, "projectile.drag", "0.2", false);
            }
            if (w.BuildError != null) throw new InvalidDataException(w.BuildError);
            await File.WriteAllTextAsync(Path.Combine(paths.Root, name + ".lua"), w.LuaPreview);
            await w.ExportAsync(); Console.WriteLine(w.LastExport);
        }
        return 0;
    }
    private sealed class NoDesktop : IFolderOpener, IProjectFilePicker
    {
        public Task OpenAsync(string path, bool selectFile = false) => throw new NotSupportedException();
        public Task<string?> PickAsync() => Task.FromResult<string?>(null);
    }
}
