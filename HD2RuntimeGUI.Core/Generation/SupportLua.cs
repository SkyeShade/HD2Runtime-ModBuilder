using System.Text;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public interface ISupportLua { IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk); }
public sealed class SupportLua(ISupportChangeService changes) : ISupportLua
{
    public IReadOnlyList<string> Operations(ModProject p, SdkMetadata sdk)
    {
        if (!p.SupportChanges.Any(c => c.Enabled)) return [];
        var catalog = SupportChangeService.Catalog(sdk);
        var active = p.SupportChanges.Where(c => c.Enabled).OrderBy(c => c.InstanceKey, StringComparer.Ordinal).ToArray();
        foreach (var c in active) changes.Validate(p, sdk, c);
        var rows = active.Where(c => !SupportChangeService.NoOp(sdk, c)).Select(c => (Change: c, Field: catalog.Field(c.InstanceKey))).ToArray();
        foreach (var same in rows.GroupBy(r => (r.Field.Backing.ObjectKey, r.Field.ApiFieldConstant)))
        {
            var first = same.First();
            if (same.Any(r => !SupportScalar.Equal(first.Field, first.Change.ExpectedValue, r.Change.ExpectedValue)
                || !SupportScalar.Equal(first.Field, first.Change.DesiredValue, r.Change.DesiredValue)))
                throw new InvalidDataException("Conflicting support changes target the same published backing object and field. Resolve their values before building.");
        }
        // Published scope keys also connect branches that share a native object. Never run their plans concurrently.
        var plans = rows.GroupBy(r => r.Field.Operation.PlanGroupingKey).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        var parent = Enumerable.Range(0, plans.Length).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i]; return i; }
        for (var i = 0; i < plans.Length; i++) for (var j = i + 1; j < plans.Length; j++)
            if (plans[i].Select(r => r.Field.Backing.ObjectKey).Intersect(plans[j].Select(r => r.Field.Backing.ObjectKey)).Any()) parent[Root(j)] = Root(i);
        var output = new List<string>();
        foreach (var connected in Enumerable.Range(0, plans.Length).GroupBy(Root))
        {
            var entries = connected.SelectMany(i => plans[i]).ToArray();
            if (entries.Select(r => r.Change.EnsureEnabled).Distinct().Count() != 1) throw new InvalidDataException("Related support objects must use the same persistence setting.");
            var groups = entries.GroupBy(r => r.Field.Operation.TransactionGroupingKey).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            var usePlan = groups.Length > 1 || entries.Any(r => r.Field.Operation.PlanRequired);
            if (groups.Length > (sdk.Plans?.Limits.Operations ?? 0) || entries.Length > sdk.Plans!.Limits.PhysicalChangesPerPhase)
                throw new InvalidDataException("Support edit exceeds published plan limits.");
            var operations = new List<string>();
            foreach (var group in groups)
            {
                var f = group.First().Field; var contract = catalog.OperationGroups.Single(g => g.OperationGroupingKey == group.Key);
                if (group.Select(r => r.Field.Backing.ObjectKey).Distinct().Count() != 1 || group.Count() > 32) throw new InvalidDataException("Unsupported support transaction size or backing object.");
                // Current schema explicitly has phase 1, no dependencies, and no target_from. Reader rejects unknown future sequencing.
                if (contract.Phase != 1 || contract.Dependencies.Length != 0) throw new InvalidDataException("Unsupported support dependency contract.");
                var target = "hd2.support_weapon(" + LuaGenerator.Quote(f.SupportWeapon) + ")";
                foreach (var accessor in contract.Target.Accessor.Skip(1)) target += accessor == "attack"
                    ? ":attack(" + LuaGenerator.Quote(contract.Target.AttackRole!) + ")" : ":" + accessor + "()";
                var body = new StringBuilder("{\n    id=" + LuaGenerator.Quote("support-" + SupportChangeService.Hash(p.ResourceId + "\n" + group.Key)[..24]) + ",\n    target=" + target + ",\n");
                if (contract.AllowSharedRequired) body.Append("    allow_shared=true,\n");
                if (group.Count() == 1 && !f.Operation.TransactionRequired)
                {
                    var r = group.Single(); body.Append($"    field={r.Field.ApiFieldConstant},\n    expect={SupportScalar.Text(r.Field, r.Change.ExpectedValue)},\n    value={SupportScalar.Text(r.Field, r.Change.DesiredValue)},\n");
                }
                else
                {
                    body.Append("    changes={\n");
                    foreach (var r in group.OrderBy(r => r.Field.InstanceKey, StringComparer.Ordinal))
                        body.Append($"        {{field={r.Field.ApiFieldConstant},expect={SupportScalar.Text(r.Field, r.Change.ExpectedValue)},value={SupportScalar.Text(r.Field, r.Change.DesiredValue)}}},\n");
                    body.Append("    },\n");
                }
                operations.Add(body.Append('}').ToString());
            }
            var kind = usePlan ? "plan" : entries.Length == 1 && !entries[0].Field.Operation.TransactionRequired ? "patch" : "transaction";
            var request = usePlan ? "{\n    id=" + LuaGenerator.Quote("support-plan-" + SupportChangeService.Hash(p.ResourceId + "\n" + string.Join("\n", groups.Select(g => g.Key)))[..24])
                + ",\n    operations={\n" + string.Join(",\n", operations.Select(o => "        " + o.Replace("\n", "\n        "))) + "\n    },\n}" : operations.Single();
            output.Add(entries[0].Change.EnsureEnabled ? "hd2.ensure({\n    " + kind + "=" + request.Replace("\n", "\n    ") + "\n})" : "hd2." + kind + "(" + request + ")");
        }
        return output;
    }
}
