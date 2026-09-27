using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

internal static class SupportSamples
{
    public static async Task<int> Run(AppPaths paths, SdkMetadata sdk, ISdkCache cache, IGitHubReleaseClient github,
        IProjectStore store, IProjectService projects, ILuaGenerator generator)
    {
        var catalog = SupportChangeService.Catalog(sdk); var desktop = new NoDesktop();
        var samples = new[] {
            ("RecoillessTuning020", "GR-8 Recoilless Rifle", new[] { ("projectile.velocity", "350"), ("explosion.outer_radius", "10") }),
            ("ArcThrowerTuning020", "ARC-3 Arc Thrower", new[] { ("arc.range", "75") }),
            ("C4Explosion020", "B/MD C4 Pack", new[] { ("explosion.outer_radius", "15"), ("explosion.damage.standard_damage", "1500") }),
            ("SupportAMRTuning020", "APW-1 Anti-Materiel Rifle", new[] { ("weapon.ergonomics", "80"), ("weapon.sway", "0.5"), ("projectile.velocity", "1100") }) };
        foreach (var (name, weapon, edits) in samples)
        {
            var w = new BuilderWorkspace(store, projects, cache, new SdkUpdateService(cache, github, paths), new ChangeService(), generator, new ModExporter(generator), desktop, desktop, paths);
            var existing = (await store.ListAsync()).SingleOrDefault(p => p.ResourceId == "mods/skyeshade/" + name.ToLowerInvariant());
            if (existing == null) await w.CreateAsync(new(name, "SkyeShade", "mods/skyeshade/" + name.ToLowerInvariant(), "0.1.0"), sdk);
            else { await w.OpenAsync(existing.Id); if (w.Project!.SdkVersion != sdk.Version) throw new InvalidDataException("Use a fresh sample workspace for a new SDK."); }
            foreach (var (field, value) in edits)
            {
                var f = catalog.FieldInstances.Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == field);
                await w.SetSupportAsync(f.InstanceKey, value);
                // Explicit permission for the requested proof project, same scope action as the editor checkbox.
                if (f.SharedScope.RequiresAcknowledgement) await w.SetSupportApprovalAsync(f.InstanceKey, true);
            }
            if (w.BuildError != null) throw new InvalidDataException(w.BuildError);
            await w.OpenAsync(w.Project!.Id); await w.ExportAsync();
            await File.WriteAllTextAsync(Path.Combine(paths.Root, name + ".lua"), w.LuaPreview);
            Console.WriteLine(w.LastExport);
        }
        return 0;
    }
    private sealed class NoDesktop : IFolderOpener, IProjectFilePicker
    {
        public Task OpenAsync(string path, bool selectFile = false) => throw new NotSupportedException();
        public Task<string?> PickAsync() => Task.FromResult<string?>(null);
    }
}
