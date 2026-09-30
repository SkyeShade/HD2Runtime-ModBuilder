using System.Diagnostics;
using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;
using Xunit.Abstractions;

namespace HD2RuntimeGUI.Tests;

// Stage-by-stage timings for a ~610-change project. Opt-in (HD2_PERF=1): it measures, it does not assert timings.
public sealed class LargeProjectBenchmark(ITestOutputHelper output)
{
    private static double Time(Action a, int runs = 1) { a(); var c = Stopwatch.StartNew(); for (var i = 0; i < runs; i++) a(); return c.Elapsed.TotalMilliseconds / runs; }
    private static async Task<double> TimeAsync(Func<Task> a) { var c = Stopwatch.StartNew(); await a(); return c.Elapsed.TotalMilliseconds; }

    // Data folders for the Changes-page UI benchmark (tools/changes-ui-benchmark.mjs): one project with N changes each, same mix as the
    // 610-change fixture scaled to N. HD2_PERF_UI_OUT=<dir> writes <dir>\ui-<N>\library.json and Projects\<id>\project.hd2mod.json.
    [Fact] public async Task Write_ui_benchmark_data()
    {
        if (Environment.GetEnvironmentVariable("HD2_PERF_UI_OUT") is not { Length: > 0 } dir) return;
        foreach (var n in new[] { 100, 300, 600, 1000 })
        {
            using var e = new TestEnvironment(); var m = LargeProjectFixture.Mix.User610; double k = n / 610.0;
            var mix = new LargeProjectFixture.Mix((int)Math.Round(m.Weapon * k), (int)Math.Round(m.Object * k), (int)Math.Round(m.Support * k), (int)Math.Round(m.Stratagem * k),
                (int)Math.Round(m.Vehicle * k), (int)Math.Round(m.Enemy * k));
            var b = await LargeProjectFixture.CreateAsync(e, mix, "mods/tests/ui_" + n); var p = b.Workspace.Project!;
            var root = Path.Combine(dir, "ui-" + n); if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(Path.Combine(root, "Projects", p.Id.ToString()));
            File.Copy(e.Paths.ProjectFile(p.Id), Path.Combine(root, "Projects", p.Id.ToString(), "project.hd2mod.json"));
            File.Copy(e.Paths.Library, Path.Combine(root, "library.json"));
            output.WriteLine($"ui-{n}: {b.Applied.Values.Sum()} changes ({string.Join(", ", b.Applied.Select(kv => kv.Key + "=" + kv.Value))}), {b.Workspace.LuaPreview.Split("add(function() return ").Length - 1} operations");
        }
    }

    [Fact] public async Task Stage_timings_for_610_changes()
    {
        if (Environment.GetEnvironmentVariable("HD2_PERF") != "1") return;
        using var e = new TestEnvironment();
        var total = Stopwatch.StartNew();
        var b = await LargeProjectFixture.CreateAsync(e, LargeProjectFixture.Mix.User610); var w = b.Workspace; var sdk = b.Sdk; var p = w.Project!;
        var build = total.Elapsed.TotalMilliseconds;
        void Log(string s) => output.WriteLine(s);
        Log($"applied: {string.Join(", ", b.Applied.Select(kv => kv.Key + "=" + kv.Value))} (total {b.Applied.Values.Sum()})");
        Log($"lists: weapon={p.WeaponChanges.Count} composition={p.CompositionChanges.Count} projectile={p.ProjectileChanges.Count} support={p.SupportChanges.Count} stratagem={p.StratagemChanges.Count} entity={p.EntityChanges.Count}");
        foreach (var s in b.Skipped.OrderByDescending(kv => kv.Value).Take(8)) Log($"  skipped {s.Value}x {s.Key}");
        var t = b.EditMilliseconds;
        Log($"[A] building by editing: {build:F0} ms for {t.Count} edits; per edit: first 50 avg {t.Take(50).Average():F1} ms, last 50 avg {t.TakeLast(50).Average():F1} ms, max {t.Max():F1} ms");
        for (var i = 0; i < t.Count; i += 100) Log($"    edits {i + 1}-{Math.Min(i + 100, t.Count)}: avg {t.Skip(i).Take(100).Average():F1} ms");

        var lua = w.LuaPreview; var ops = lua.Split("add(function() return ").Length - 1;
        Log($"generated: {ops} operations, {lua.Length / 1024} KiB Lua, BuildError={w.BuildError ?? "none"}");

        // 1. Loading the project (fresh workspace, SDK already cached in memory) and the cold SDK load.
        var fresh = e.Workspace(); var load = await TimeAsync(() => fresh.OpenAsync(p.Id));
        var storeLoad = await TimeAsync(() => e.Store.LoadAsync(p.Id));
        var coldCache = new SdkCache(e.Paths, e.Reader, e.GitHub) { AdoptNewerBundled = false };
        var sdkLoad = await TimeAsync(() => coldCache.GetVersionAsync(sdk.Version));
        Log($"[1] open project: {load:F0} ms (store load {storeLoad:F0} ms; cold SDK load {sdkLoad:F0} ms, excluded above when cached)");

        // 2-5. Generation stages.
        var composition = new CompositionChangeService(); var planner = new SemanticOperationPlanner();
        var identity = Time(() => ProjectIdentity.Validate(p), 3);
        var validateComposition = Time(() => composition.ValidateComposition(p, sdk), 3);
        IReadOnlyList<PlannedSemanticOperation> plan = [];
        var planning = Time(() => plan = planner.Plan(p, sdk), 3);
        var planLua = Time(() => CompositionPlanLua.Operations(p.ResourceId, sdk, plan, null), 3);
        var support = Time(() => new SupportLua(new SupportChangeService()).Operations(p, sdk), 3);
        var strat = Time(() => new StratagemLua(new StratagemChangeService()).Operations(p, sdk), 3);
        var entity = Time(() => new EntityLua(new EntityChangeService()).Operations(p, sdk), 3);
        var entityValidate = Time(() => { var s = new EntityChangeService(); foreach (var c in p.EntityChanges) s.Validate(p, sdk, c); }, 3);
        var generate = Time(() => e.Generator.Generate(p, sdk), 3);
        Log($"[2-5] full Generate: {generate:F0} ms = identity {identity:F1} + composition validation {validateComposition:F1} + weapon/object planning {planning:F1} + plan Lua {planLua:F1}"
            + $" + support {support:F1} + stratagem {strat:F1} + entity {entity:F1} (of which entity validation {entityValidate:F1}) ms");

        // 6. Serialization and the save path an edit runs.
        var json = ""; var serialize = Time(() => json = JsonSerializer.Serialize(p, JsonStorage.Options), 3);
        var save = await TimeAsync(() => e.Store.SaveAsync(p));
        var list = await TimeAsync(() => e.Store.ListAsync());
        Log($"[6] serialize {serialize:F1} ms ({json.Length / 1024} KiB); store save {save:F0} ms; library list {list:F0} ms");

        // 7. Export.
        var export = await TimeAsync(() => w.ExportAsync());
        Log($"[7] export: {export:F0} ms ({new FileInfo(w.LastExport!).Length / 1024} KiB zip)");
        // For the Runtime-side probe (tools outside the repo): keep the export.
        if (Environment.GetEnvironmentVariable("HD2_PERF_OUT") is { Length: > 0 } dir)
        { Directory.CreateDirectory(dir); File.Copy(w.LastExport!, Path.Combine(dir, "large-610.zip"), true); Log($"export copied to {dir}"); }

        // The per-edit save path (BuilderWorkspace.SaveChangesAsync), piece by piece, and what generation spends its time on.
        var copy = p.WeaponChanges.ToList();
        var noOps = Time(() => WeaponAliasResolver.RemoveNoOps(sdk, copy), 3); var required = Time(() => ProjectIdentity.RequiredFormat(p), 3);
        var storeSave = await TimeAsync(() => e.Store.SaveAsync(p)); var relist = await TimeAsync(() => e.Store.ListAsync());
        var weaponService = new WeaponChangeService();
        var aliasGroups = Time(() => WeaponAliasResolver.Group(sdk, p.WeaponChanges), 3);
        var weaponValidation = Time(() => { foreach (var c in p.WeaponChanges) weaponService.Validate(sdk, c); }, 3);
        var fireModes = Time(() => { foreach (var weapon in p.WeaponChanges.Select(c => c.Weapon).Distinct()) WeaponChangeService.FireModeConflict(p.WeaponChanges, weapon); }, 3);
        var selectors = Time(() => WeaponSelectorRules.PlayerIssues(sdk, p.WeaponChanges), 3);
        var objectValidation = Time(() => { foreach (var c in p.CompositionChanges) composition.Validate(p, sdk, c); }, 3);
        Log($"[save path] remove no-ops {noOps:F1} + format {required:F2} + store save {storeSave:F1} + library relist {relist:F1} + preview (full Generate) {generate:F0} ms");
        Log($"[generate detail] composition validation {validateComposition:F1} (of which per-object Validate {objectValidation:F1}), planning {planning:F1} (runs composition validation again, plus alias groups {aliasGroups:F1}"
            + $", weapon validation {weaponValidation:F1}, fire modes {fireModes:F1}, selector rules {selectors:F1})");

        // One more edit on the full project (the per-edit cost a user pays at 610 changes).
        var extra = sdk.Stratagems!.FieldInstances.Where(f => f.Editable && p.StratagemChanges.All(c => c.InstanceKey != f.InstanceKey))
            .First(f => LargeProjectFixture.Next(f.CurrentDefault, f.Type, f.Min, f.Max) != null);
        var edit = await TimeAsync(() => w.SetStratagemAsync(extra.InstanceKey, LargeProjectFixture.Next(extra.CurrentDefault, extra.Type, extra.Min, extra.Max)!));
        Log($"[edit] one more edit at {p.StratagemChanges.Count + p.EntityChanges.Count + p.SupportChanges.Count + p.WeaponChanges.Count + p.CompositionChanges.Count} changes: {edit:F0} ms");

        // 8. What the Changes page reads per render (the Core side of it): grouped weapon edits, issues and one issue check per change.
        var groups = Time(() => _ = w.WeaponGroups, 3); var issues = Time(() => _ = w.WeaponIssues, 3);
        var perChange = Time(() =>
        {
            foreach (var c in p.EntityChanges) w.EntityIssue(c);
            foreach (var c in p.SupportChanges) w.SupportIssue(c);
            foreach (var c in p.StratagemChanges) w.StratagemIssue(c);
            foreach (var c in p.CompositionChanges) w.CompositionIssue(c);
        }, 3);
        Log($"[8 core] WeaponGroups {groups:F1} ms, WeaponIssues {issues:F1} ms, one issue check for every non-weapon change {perChange:F1} ms");

        // 8. The weapon section of the Changes page (WeaponChanges.razor), its row logic replayed on the Core objects: as 1.4.1 wrote it (the
        // WeaponGroups / WeaponIssues properties re-evaluated inside the per-weapon and per-row lambdas) and with both read once per render.
        int reads = 0;
        IReadOnlyList<WeaponChangeGroup> Groups() { reads++; return w.WeaponGroups; }
        IReadOnlyList<WeaponChangeIssue> Issues() { reads++; return w.WeaponIssues; }
        static bool Modified(WeaponCapability? f, WeaponChange? c) => c != null && (f != null ? !WeaponScalar.Equal(f, f.CurrentDefault, c.DesiredValue) : !JsonElement.DeepEquals(c.ExpectedValue, c.DesiredValue));
        int Render(Func<IReadOnlyList<WeaponChangeGroup>> groups, Func<IReadOnlyList<WeaponChangeIssue>> issues)
        {
            var shown = 0;
            foreach (var name in groups().Select(g => g.Weapon).Distinct().ToArray())
            {
                var group = groups().Where(g => g.Weapon == name).ToArray(); var weapon = sdk.PlayerWeapons!.Find(name);
                var rows = (weapon?.Fields.Where(f => f.IsPreferred && f.Type is not ("projectile_reference" or "explosion_reference")).Select(f => (Field: (WeaponCapability?)f, Group: group.SingleOrDefault(g => g.FieldId == f.SemanticFieldId))) ?? [])
                    .Where(r => r.Group?.Conflict != null || Modified(r.Field, r.Group?.Representative) || issues().Any(i => r.Group?.Sources.Any(c => c.Id == i.ChangeId) == true));
                foreach (var section in rows.GroupBy(r => r.Field?.SemanticFieldId.Split('.')[0])) foreach (var row in section) { shown++; _ = issues().Where(i => row.Group?.Sources.Any(s => s.Id == i.ChangeId) == true).ToArray(); }
                _ = rows.Any();
            }
            return shown;
        }
        reads = 0; var asWritten = Stopwatch.StartNew(); var rowsShown = Render(Groups, Issues); asWritten.Stop(); var readsAsWritten = reads;
        reads = 0; var once = Stopwatch.StartNew(); var g1 = Groups(); var i1 = Issues(); var rowsOnce = Render(() => g1, () => i1); once.Stop();
        Log($"[8 render] weapon section with the 1.4.1 page logic (reads inside the per-row lambdas) {asWritten.Elapsed.TotalMilliseconds:F0} ms ({readsAsWritten} WeaponGroups/WeaponIssues evaluations, {rowsShown} rows);"
            + $" read once per render {once.Elapsed.TotalMilliseconds:F1} ms ({reads} evaluations, {rowsOnce} rows)");
    }
}
