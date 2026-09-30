using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// SkyeShade/HD2Runtime-ModBuilder#2 (2026-09-30): "overriding any of the Halt's values causes every single other change made besides
// damage overrides to be nullified". Published Runtime 0.27.0 raised on the first refused operation, which aborted the generated addon.
// Runtime 0.28.0 resolves the Halt's generic feed fields and rejects instead of raising; ModBuilder 1.4.0 additionally builds every
// operation inside pcall, so an error while building one request skips only that operation. The variants are those of Runtime's
// tests/fixtures/user-reports/modbuilder-issue-2-halt: every writable Halt field with unrelated Liberator and Reprimand edits before and
// after it in project order.
public sealed class HaltRegressionTests
{
    public const string Halt = "SG-20 Halt";
    // A small valid change to a published scalar (as the Runtime harness perturbs it), or null for non-scalar types.
    private static string? Perturb(WeaponCapability f)
    {
        var v = f.CurrentDefault;
        if (f.Type == "boolean") return v.ValueKind == JsonValueKind.True ? "false" : "true";
        if (f.Type is not ("number" or "integer") || v.ValueKind != JsonValueKind.Number) return null;
        var d = v.GetDouble();
        if (f.Type == "integer") { var n = (long)d + 1; if (f.Max is double mx && n > mx) n = (long)d - 1; return n.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        var x = d == 0 ? 0.5 : d * 1.1;
        if (f.Max is double max && x > max) x = d * 0.9;
        if (f.Min is double min && x < min) x = min;
        return x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
    private static List<WeaponChange> Changes(SdkMetadata sdk, string weapon, Func<WeaponCapability, bool> keep)
    {
        var service = new WeaponChangeService(); var list = new List<WeaponChange>();
        foreach (var f in WeaponChangeService.Catalog(sdk).Weapon(weapon).Fields.Where(f => f.Editable && !f.DerivedReadOnly && f.AliasOf == null && keep(f)))
            if (Perturb(f) is { } value) list.Add(service.Create(sdk, weapon, f.SemanticFieldId, value, true));
        return list;
    }
    public static IReadOnlyDictionary<string, List<WeaponChange>> Variants(SdkMetadata sdk)
    {
        List<WeaponChange> Others(string weapon, params string[] fields) => Changes(sdk, weapon, f => fields.Contains(f.SemanticFieldId));
        var before = Others("AR-23 Liberator", "weapon.sway", "weapon.ergonomics");
        var after = Others("SMG-32 Reprimand", "weapon.sway", "weapon.fire_rate");
        return new Dictionary<string, List<WeaponChange>>
        {
            ["H0-control-no-halt"] = [.. before, .. after],
            ["H1-halt-all"] = [.. before, .. Changes(sdk, Halt, _ => true), .. after],
            ["H2-halt-damage-only"] = [.. before, .. Changes(sdk, Halt, f => f.Domain == "damage"), .. after],
            ["H3-halt-sway-only"] = [.. before, .. Changes(sdk, Halt, f => f.SemanticFieldId == "weapon.sway"), .. after],
        };
    }
    public static ModProject Project(SdkMetadata sdk, string name, IEnumerable<WeaponChange> changes, string exportDirectory)
    {
        var resource = "mods/tests/" + Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_");
        var p = new ModProject { DisplayName = name, Author = "Tests", ResourceId = resource, ManagerGuid = ProjectIdentity.ManagerGuid(resource),
            SdkVersion = sdk.Version, RuntimeApi = sdk.ApiVersion, Version = "0.1.0", ExportDirectory = exportDirectory };
        p.WeaponChanges.AddRange(changes);
        return p;
    }

    [Fact] public async Task Every_halt_variant_isolates_each_operation_and_keeps_unrelated_edits()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, SdkPin.Version);
        foreach (var (name, changes) in Variants(sdk))
        {
            var project = Project(sdk, name, changes, Path.Combine(e.Paths.Root, "exports"));
            var lua = e.Generator.Generate(project, sdk);
            // Each top-level operation is built inside pcall; a build error is printed and skips only that operation.
            Assert.Contains("local ok,operation=pcall(build)", lua);
            Assert.Contains("else print('" + LuaGenerator.SkippedPrefix + "'..tostring(operation)) end", lua);
            var added = Regex.Matches(lua, @"^add\(function\(\) return hd2\.(ensure|patch|transaction|plan)\(", RegexOptions.Multiline).Count;
            Assert.True(added >= 2, name);
            Assert.Equal(added, Regex.Matches(lua, @"^\S.* end\)$", RegexOptions.Multiline).Count);
            Assert.DoesNotContain("\nreturn hd2.", lua); Assert.EndsWith("return operations\n", lua);
            // Operation ids stay unique across the whole addon.
            var ids = Regex.Matches(lua, @"\bid='([^']+)'").Select(m => m.Groups[1].Value).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            // The unrelated edits before and after the Halt are all still generated.
            Assert.Contains("hd2.weapon('AR-23 Liberator')", lua); Assert.Contains("hd2.weapon('SMG-32 Reprimand')", lua);
            if (name != "H0-control-no-halt") Assert.Contains("hd2.weapon('SG-20 Halt')", lua);
            // Exports built on this SDK require it (0.28.0 resolves the Halt feed fields and never aborts on a refused operation).
            using var zip = System.IO.Compression.ZipFile.OpenRead(await e.Exporter.ExportAsync(project, sdk));
            using var manifest = JsonDocument.Parse(zip.GetEntry("hd2runtime.json")!.Open());
            Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        }
        // The Halt's feed projectile and damage edits are generated for both feeds.
        var all = e.Generator.Generate(Project(sdk, "H1", Variants(sdk)["H1-halt-all"], Path.Combine(e.Paths.Root, "exports")), sdk);
        Assert.Contains("attack('feed_primary')", all); Assert.Contains("attack('feed_alternate')", all);
    }
}
