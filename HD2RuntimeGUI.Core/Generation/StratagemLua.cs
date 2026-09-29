using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public interface IStratagemLua { IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk, OptionBindings? options = null); }
public sealed class StratagemLua(IStratagemChangeService service) : IStratagemLua
{
    public IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk, OptionBindings? options = null)
    {
        var active = project.StratagemChanges.Where(c => c.Enabled).OrderBy(c => c.InstanceKey, StringComparer.Ordinal).ToArray();
        if (active.Length == 0) return [];
        var catalog = StratagemChangeService.Catalog(sdk);
        foreach (var c in active) service.Validate(project, sdk, c);
        var rows = active.Where(c => !StratagemChangeService.NoOp(sdk, c)).Select(c => (Change: c, Field: StratagemChangeService.Resolve(catalog, c))).ToArray();
        var plans = rows.GroupBy(r => r.Field.PlanGroup).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        var parent = Enumerable.Range(0, plans.Length).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i]; return i; }
        for (var i = 0; i < plans.Length; i++) for (var j = i + 1; j < plans.Length; j++)
            if (plans[i].Select(r => r.Field.BackingObjectId).Intersect(plans[j].Select(r => r.Field.BackingObjectId)).Any()) parent[Root(j)] = Root(i);
        var output = new List<string>();
        foreach (var connected in Enumerable.Range(0, plans.Length).GroupBy(Root))
        {
            var entries = connected.SelectMany(i => plans[i]).ToArray();
            if (entries.Select(r => r.Change.EnsureEnabled).Distinct().Count() != 1) throw new InvalidDataException("Related stratagem objects require the same persistence setting.");
            var groups = entries.GroupBy(r => r.Field.OperationGroup).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            if (sdk.Plans == null || groups.Length > sdk.Plans.Limits.Operations || entries.Length > sdk.Plans.Limits.PhysicalChangesPerPhase)
                throw new InvalidDataException("Stratagem edit exceeds the published plan limits.");
            var operations = new List<string>();
            foreach (var group in groups)
            {
                var f = group.First().Field;
                if (group.Select(r => r.Field.BackingObjectId).Distinct().Count() != 1 || group.Count() > 32
                    || group.Any(r => r.Field.PlanPhase != 1 || r.Field.DependsOn.Length != 0)) throw new InvalidDataException("Unsupported stratagem operation contract.");
                // A transaction has one semantic target. Slot-specific targets are not interchangeable.
                var targets = group.Select(r => r.Field.Target).Distinct().ToArray();
                if (targets.Length > 1 && !group.All(r => r.Field.Target.Path == "eagle_rearm"))
                    throw new InvalidDataException("Stratagem composition conflict: the SDK operation group requires different branch targets. Keep these branch edits separate until Runtime publishes compatible operation groups. No fields were merged.");
                var unique = group.GroupBy(r => r.Field.ApiFieldConstant).Select(g =>
                {
                    var first = g.First();
                    if (g.Any(r => !StratagemScalar.Equal(first.Field, first.Change.ExpectedValue, r.Change.ExpectedValue)
                        || !StratagemScalar.Equal(first.Field, first.Change.DesiredValue, r.Change.DesiredValue)))
                        throw new InvalidDataException("Conflicting stratagem values on the same shared object. Resolve the changes before building.");
                    return first;
                }).OrderBy(r => r.Field.InstanceKey, StringComparer.Ordinal).ToArray();
                var body = "{\n    id=" + LuaGenerator.Quote("stratagem-" + SupportChangeService.Hash(project.ResourceId + "\n" + group.Key)[..24])
                    + ",\n    target=" + Target(f.Target) + ",\n";
                if (f.AllowSharedRequired) body += "    allow_shared=true,\n";
                // 0.26.0: emitted only where Runtime requires it for the desired value (mission uses outside gameplay-proven targets).
                if (unique.Any(r => StratagemChangeService.EffectRequired(r.Field, r.Change.DesiredValue))) body += "    allow_unverified_effect=true,\n";
                if (unique.Length == 1)
                { var r = unique[0]; body += $"    field={r.Field.ApiFieldConstant},\n    expect={StratagemScalar.Text(r.Field, r.Change.ExpectedValue)},\n    value={Value(r, group)},\n"; }
                else body += "    changes={\n" + string.Join("\n", unique.Select(r => $"        {{field={r.Field.ApiFieldConstant},expect={StratagemScalar.Text(r.Field, r.Change.ExpectedValue)},value={Value(r, group)}}},")) + "\n    },\n";
                operations.Add(body + "}");
            }
            var kind = groups.Length > 1 ? "plan" : operations[0].Contains("    changes={", StringComparison.Ordinal) ? "transaction" : "patch";
            var request = kind == "plan" ? "{\n    id=" + LuaGenerator.Quote("stratagem-plan-" + SupportChangeService.Hash(project.ResourceId + "\n" + string.Join("\n", groups.Select(g => g.Key)))[..24])
                + ",\n    operations={\n" + string.Join(",\n", operations.Select(o => "        " + o.Replace("\n", "\n        "))) + "\n    },\n}" : operations[0];
            output.Add(OptionBindings.Wrap(options, kind, request, entries[0].Change.EnsureEnabled, entries.Select(r => (string?)ModOptionsService.StratagemKey(r.Change.InstanceKey))));
        }
        return output;
        // Instances sharing one Runtime value (e.g. the Eagle rearm system) are written once; at most one of them may be bound.
        string Value((StratagemChange Change, StratagemField Field) r, IEnumerable<(StratagemChange Change, StratagemField Field)> group)
        {
            var literal = StratagemScalar.Text(r.Field, r.Change.DesiredValue);
            return options?.Value(group.Where(x => x.Field.ApiFieldConstant == r.Field.ApiFieldConstant).Select(x => (string?)ModOptionsService.StratagemKey(x.Change.InstanceKey)), literal) ?? literal;
        }
    }
    // Graph path targets follow the public API: stratagem → deployed_entity() → weapon(id) → attack(role).
    // Runtime 0.22 exposes only the "main" deployed entity, and attack handles resolve under the "primary" mounted weapon.
    // Any other published identity cannot be expressed through the public API and fails closed.
    public static string Target(StratagemTarget target)
    {
        var root = "hd2.stratagem(" + LuaGenerator.Quote(target.Stratagem) + ")";
        string Entity() => target.Entity == "main" ? root + ":deployed_entity()"
            : throw new InvalidDataException("The public Runtime API cannot target deployed entity '" + target.Entity + "'.");
        string Weapon() => Entity() + ":weapon(" + LuaGenerator.Quote(target.Weapon!) + ")";
        return target.Path switch
        {
            "stratagem" => root,
            "eagle_rearm" => root + ":eagle_rearm()",
            "deployed_entity" => Entity(),
            _ when StratagemCatalog.EntityComponents.Contains(target.Path) => Entity() + ":" + target.Path + "()",
            "damage_zone" => Entity() + ":damage_zone(" + LuaGenerator.Quote(target.Zone!) + ")",
            "weapon" => Weapon(),
            "attack" when target.Weapon == null => root + ":attack(" + LuaGenerator.Quote(target.Attack!) + ")",
            "attack" when target.Weapon == "primary" => Weapon() + ":attack(" + LuaGenerator.Quote(target.Attack!) + ")",
            // Unreleased Runtime (0.28.0 development): a mine stratagem's explosion is hd2.stratagem(name):mine(); its DamageInfo and
            // status rows are stratagem-level attack roles.
            "attack" when target.Weapon == StratagemMine.Weapon && target.Entity == "main" => target.Attack == StratagemMine.Weapon ? root + ":mine()" : root + ":attack(" + LuaGenerator.Quote(target.Attack!) + ")",
            "attack" => throw new InvalidDataException("The public Runtime API resolves attack branches only under the primary mounted weapon."),
            _ => throw new InvalidDataException("Unsupported stratagem target."),
        };
    }
    // Distinct instances on one backing object may be distinct fields (for example status slots stored on a parent DamageInfo)
    // or the same field reached through different consumers. The GUI never merges them; it only warns when enabled edits
    // with the same API field on one object disagree, because Runtime rejects the plan if they resolve to the same bytes.
    public static IReadOnlyList<(StratagemChange[] Changes, string Message)> SharedOverlaps(ModProject project, SdkMetadata sdk)
    {
        if (sdk.Stratagems is not { } catalog) return [];
        return project.StratagemChanges.Where(c => c.Enabled).Select(c => (Change: c, Field: catalog.Resolve(c))).Where(r => r.Field != null)
            .GroupBy(r => (r.Field!.BackingObjectId, r.Field.ApiFieldConstant)).Where(g => g.Count() > 1
                && g.Select(r => StratagemScalar.Text(r.Field!, r.Change.DesiredValue)).Distinct().Count() > 1)
            .Select(g => (g.Select(r => r.Change).ToArray(), "These edits use the same API field on one shared " + g.First().Field!.BackingObjectKind
                + " object. If Runtime resolves them to the same field, it rejects the differing values; otherwise they apply independently."))
            .ToArray();
    }
}
