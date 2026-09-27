using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

internal static class GroupingSamples
{
    public static async Task<int> Run(AppPaths paths, SdkMetadata sdk, ISdkCache cache, IGitHubReleaseClient github,
        IProjectStore store, IProjectService projects, ILuaGenerator generator)
    {
        const string weapon = "AR-23C Liberator Concussive";
        var desktop = new NoDesktop();
        foreach (var mode in new[] { "", "Impact", "Expiry", "BothTerminals" })
        {
            var name = "ConcussiveGrouped" + mode;
            var workspace = new BuilderWorkspace(store, projects, cache, new SdkUpdateService(cache, github, paths), new ChangeService(), generator, new ModExporter(generator), desktop, desktop, paths);
            await workspace.CreateAsync(new(name, "SkyeShade", "mods/skyeshade/" + name.ToLowerInvariant(), "0.1.0"), sdk);
            await workspace.SetWeaponChangeAsync(weapon, "weapon.fire_rate", "1100", false);
            await workspace.SetObjectScalarAsync(weapon, "primary", "projectile", null, "damage.push_force", "30", true);
            foreach (var lane in new[] { "direct", "slight", "large", "extreme" })
                await workspace.SetObjectScalarAsync(weapon, "primary", "projectile", null, "damage.ap_" + lane, "3", false);
            var source = workspace.ExplosionSources.First(s => s.Projectile?.Weapon == "R-36 Eruptor");
            if (mode is "Impact" or "BothTerminals") await workspace.SetTerminalAsync(weapon, "primary", "impact", source, true);
            if (mode is "Expiry" or "BothTerminals") await workspace.SetTerminalAsync(weapon, "primary", "expiry", source, mode == "Expiry");
            var project = workspace.Project!;
            // Diagnostic reproduction of the old per-field emission; never package it.
            var saved = project.CompositionChanges; project.CompositionChanges = [];
            var weaponLua = generator.Generate(project, sdk); project.CompositionChanges = saved;
            var old = new[] { weaponLua[(weaponLua.IndexOf("return ", StringComparison.Ordinal) + 7)..].Trim() }
                .Concat(CompositionChangeService.Operations(project, sdk, new CompositionChangeService()));
            await File.WriteAllTextAsync(Path.Combine(paths.Root, name + ".before.lua"), "local hd2=require('mods/skyeshade/hd2runtime')\nreturn {\n" + string.Join(",\n", old) + "\n}\n");
            if (workspace.BuildError is { } error)
            {
                await File.WriteAllTextAsync(Path.Combine(paths.Root, name + ".blocked.txt"), error);
                Console.WriteLine(name + " BLOCKED: " + error); continue;
            }
            await File.WriteAllTextAsync(Path.Combine(paths.Root, name + ".after.lua"), workspace.LuaPreview);
            await workspace.ExportAsync(); Console.WriteLine(workspace.LastExport);
            Console.WriteLine($"Ensures: {saved.Count + 1} before -> {workspace.LuaPreview.Split("hd2.ensure(").Length - 1} after");
        }
        return 0;
    }
    private sealed class NoDesktop : IFolderOpener, IProjectFilePicker
    {
        public Task OpenAsync(string path, bool selectFile = false) => throw new NotSupportedException();
        public Task<string?> PickAsync() => Task.FromResult<string?>(null);
    }
}
