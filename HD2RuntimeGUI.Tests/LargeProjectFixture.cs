using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;

namespace HD2RuntimeGUI.Tests;

// A deterministic ~610-change project on the pinned SDK, built through the same workspace calls the editors make: player weapon fields,
// projectile/explosion object fields, support weapons, stratagems, vehicles/backpacks and enemy zones. Used by the large-project
// correctness tests and the performance benchmark.
public static class LargeProjectFixture
{
    public sealed record Mix(int Weapon, int Object, int Support, int Stratagem, int Vehicle, int Enemy)
    {
        public int Total => Weapon + Object + Support + Stratagem + Vehicle + Enemy;
        public static readonly Mix User610 = new(230, 60, 110, 90, 40, 80);
    }
    public sealed record Build(BuilderWorkspace Workspace, SdkMetadata Sdk, IReadOnlyDictionary<string, int> Applied, IReadOnlyList<double> EditMilliseconds,
        IReadOnlyDictionary<string, int> Skipped);

    // The next value a user might type: one step up (or down at the top of a published range), never the baseline.
    public static string? Next(JsonElement current, string type, double? min, double? max)
    {
        if (current.ValueKind != JsonValueKind.Number || type is not ("integer" or "number")) return null;
        var v = current.GetDouble();
        double n = type == "integer" ? v + 1 : v == 0 ? 0.5 : Math.Round(v * 1.1, 3);
        if (max is { } hi && n > hi) n = type == "integer" ? v - 1 : Math.Round(v * 0.9, 3);
        if (min is { } lo && n < lo || max is { } top && n > top || n == v || type == "integer" && (n < int.MinValue || n > int.MaxValue)) return null;
        return n.ToString(CultureInfo.InvariantCulture);
    }

    public static async Task<Build> CreateAsync(TestEnvironment e, Mix mix, string resource = "mods/tests/large_project")
    {
        var sdk = await SdkFixtures.Install(e, "0.28.0"); var w = e.Workspace();
        await w.CreateAsync(new("Large Project", "Tests", resource, "0.1.0"), sdk);
        var applied = new Dictionary<string, int>(); var skipped = new Dictionary<string, int>(); var times = new List<double>();
        async Task<bool> Try(string family, Func<Task> edit)
        {
            var before = w.Project!.WeaponChanges.Count + w.Project.CompositionChanges.Count + w.Project.SupportChanges.Count + w.Project.StratagemChanges.Count + w.Project.EntityChanges.Count;
            var clock = Stopwatch.StartNew();
            try { await edit(); }
            catch (InvalidDataException x) { skipped[family + ": " + x.Message[..Math.Min(60, x.Message.Length)]] = skipped.GetValueOrDefault(family + ": " + x.Message[..Math.Min(60, x.Message.Length)]) + 1; return false; }
            clock.Stop();
            var after = w.Project!.WeaponChanges.Count + w.Project.CompositionChanges.Count + w.Project.SupportChanges.Count + w.Project.StratagemChanges.Count + w.Project.EntityChanges.Count;
            if (after <= before) { skipped[family + ": no new change"] = skipped.GetValueOrDefault(family + ": no new change") + 1; return false; }
            times.Add(clock.Elapsed.TotalMilliseconds); applied[family] = applied.GetValueOrDefault(family) + 1; return true;
        }
        static bool ObjectField(string id) => id.StartsWith("projectile.", StringComparison.Ordinal) || id.StartsWith("damage.", StringComparison.Ordinal)
            || id.StartsWith("explosion.", StringComparison.Ordinal) || id.StartsWith("terminal.", StringComparison.Ordinal) || id.StartsWith("status.", StringComparison.Ordinal);

        // Player weapon fields (WeaponFieldEditor → SetWeaponChangeAsync), a few per weapon across the catalog.
        var weapons = sdk.PlayerWeapons!.Weapons.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
        for (var round = 0; applied.GetValueOrDefault("weapon") < mix.Weapon && round < 12; round++)
            foreach (var weapon in weapons)
            {
                if (applied.GetValueOrDefault("weapon") >= mix.Weapon) break;
                var fields = weapon.Fields.Where(f => f.Editable && !f.DerivedReadOnly && f.AliasOf == null && f.Deprecated != true && f.AcceptedForWrites != false
                    && f.EnumValues == null && f.AllowedValues == null && !ObjectField(f.SemanticFieldId)).OrderBy(f => f.SemanticFieldId, StringComparer.Ordinal).ToArray();
                if (round >= fields.Length) continue;
                var f = fields[round];
                if (Next(f.CurrentDefault, f.Type, f.Min, f.Max) is { } v) await Try("weapon", () => w.SetWeaponChangeAsync(weapon.Name, f.SemanticFieldId, v, acknowledge: true));
            }
        // Projectile and impact-explosion object fields (ObjectFieldEditor → SetObjectScalarAsync).
        foreach (var weapon in weapons)
        {
            if (applied.GetValueOrDefault("object") >= mix.Object) break;
            if (weapon.Fields.FirstOrDefault(f => f.SemanticFieldId == "explosion.primary.impact.outer_radius" && f.Editable) is { } radius && Next(radius.CurrentDefault, radius.Type, radius.Min, radius.Max) is { } r)
                await Try("object", () => w.SetObjectScalarAsync(weapon.Name, "primary", "explosion", "impact", radius.SemanticFieldId, r, true));
            if (applied.GetValueOrDefault("object") >= mix.Object) break;
            if (weapon.Fields.FirstOrDefault(f => f.SemanticFieldId == "projectile.velocity" && f.Editable) is { } velocity && Next(velocity.CurrentDefault, velocity.Type, velocity.Min, velocity.Max) is { } vel)
                await Try("object", () => w.SetObjectScalarAsync(weapon.Name, "primary", "projectile", null, "projectile.velocity", vel, true));
        }
        // Support weapons, stratagems, vehicles/backpacks and enemy zones (their editors → Set*Async by published instance key).
        foreach (var f in sdk.SupportAuthoring!.FieldInstances.Where(f => f.Writable).OrderBy(f => f.InstanceKey, StringComparer.Ordinal))
        {
            if (applied.GetValueOrDefault("support") >= mix.Support) break;
            if (Next(f.Value.Baseline, f.Value.Type, null, null) is { } v) await Try("support", () => w.SetSupportAsync(f.InstanceKey, v));
        }
        foreach (var f in sdk.Stratagems!.FieldInstances.Where(f => f.Editable).OrderBy(f => f.InstanceKey, StringComparer.Ordinal))
        {
            if (applied.GetValueOrDefault("stratagem") >= mix.Stratagem) break;
            if (Next(f.CurrentDefault, f.Type, f.Min, f.Max) is { } v) await Try("stratagem", () => w.SetStratagemAsync(f.InstanceKey, v));
        }
        var entities = sdk.Entities!;
        foreach (var f in entities.AllFields.Where(f => f.Editable && f.Target.Resource is "vehicle" or "backpack").OrderBy(f => f.InstanceKey, StringComparer.Ordinal))
        {
            if (applied.GetValueOrDefault("vehicle") >= mix.Vehicle) break;
            if (Next(f.CurrentDefault, f.Type, f.EffectiveRange?.Min, f.EffectiveRange?.Max) is { } v) await Try("vehicle", () => w.SetEntityAsync(f.InstanceKey, v));
        }
        // Enemy zones: zone health and armor on several zones of many classes (the 1.4.1 case at scale).
        foreach (var f in entities.Enemies!.FieldInstances.Where(f => f.Editable && f.Target.Path == EnemyAuthoringReader.ZonePath && f.SemanticFieldId is "zone.health" or "zone.armor")
            .OrderBy(f => f.InstanceKey, StringComparer.Ordinal))
        {
            if (applied.GetValueOrDefault("enemy") >= mix.Enemy) break;
            if (Next(f.CurrentDefault, f.Type, f.EffectiveRange?.Min, f.EffectiveRange?.Max) is { } v) await Try("enemy", () => w.SetEntityAsync(f.InstanceKey, v));
        }
        return new(w, sdk, applied, times, skipped);
    }
}
