using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

internal static class StratagemSamples
{
    public static async Task<int> Run(AppPaths paths, SdkMetadata sdk, ISdkCache cache, IGitHubReleaseClient github,
        IProjectStore store, IProjectService projects, ILuaGenerator generator)
    {
        var catalog = StratagemChangeService.Catalog(sdk); var desktop = new NoDesktop();
        var samples = new[] {
            ("OrbitalLaser021", "Orbital Laser", new[] { ("stratagem.cooldown", "180", (string?)null), ("damage.standard_damage", "400", "beam_damage") }),
            ("OrbitalPrecision021", "Orbital Precision Strike", new[] { ("stratagem.cooldown", "40", (string?)null), ("explosion.outer_radius", "20", "delivery_1_projectile_impact") }),
            ("Eagle021", "Eagle Airstrike", new[] { ("stratagem.cooldown", "10", (string?)null), ("eagle.uses_per_rearm", "4", (string?)null), ("eagle.rearm_time", "90", (string?)null) }),
            ("SupportCallIn021", "GR-8 Recoilless Rifle", new[] { ("stratagem.cooldown", "120", (string?)null) }),
            ("GasOrNapalm021", "Orbital Gas Strike", new[] { ("status.duration", "30", (string?)"delivery_1_projectile_impact_damage_status_1") }) };
        foreach (var (name, target, edits) in samples)
        {
            var w = new BuilderWorkspace(store, projects, cache, new SdkUpdateService(cache, github, paths), new ChangeService(), generator, new ModExporter(generator), desktop, desktop, paths);
            var existing = (await store.ListAsync()).SingleOrDefault(p => p.ResourceId == "mods/skyeshade/" + name.ToLowerInvariant());
            if (existing == null) await w.CreateAsync(new(name, "SkyeShade", "mods/skyeshade/" + name.ToLowerInvariant(), "0.1.0"), sdk);
            else { await w.OpenAsync(existing.Id); if (w.Project!.SdkVersion != sdk.Version) throw new InvalidDataException("Use a fresh workspace for a new SDK."); }
            foreach (var (field, value, attack) in edits)
            {
                var f = catalog.FieldInstances.Single(f => f.Target.Stratagem == target && f.SemanticFieldId == field && (attack == null || f.Target.Attack == attack));
                await w.SetStratagemAsync(f.InstanceKey, value);
                // Explicit approval for the user's requested proof samples, using the same editor action.
                if (f.Shared) await w.SetStratagemApprovalAsync(f.InstanceKey, true);
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
