using System.Security.Cryptography;
using System.Text;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Generation;

// Converts the existing semantic object groups into connected composition plans.
// No plan requests or native identities are added to project persistence.
public static class CompositionPlanLua
{
    public static IReadOnlyList<string> Operations(string resourceId, SdkMetadata sdk, IReadOnlyList<PlannedSemanticOperation> operations)
    {
        var contract = sdk.Plans ?? throw new InvalidDataException("Composition plan capabilities are unavailable.");
        var parent = Enumerable.Range(0, operations.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i]; return i; }
        void Join(int a, int b) => parent[Root(b)] = Root(a);
        var dependencies = new Dictionary<int, List<int>>();
        HashSet<SemanticBackingObject> Owners(HD2RuntimeGUI.Core.Models.ProjectileReference p) => sdk.PlayerWeapons!.Weapon(p.Weapon).Fields
            .Where(f => f.Backing != null && (CompositionChangeService.ProjectileOwned(f) || f.Domain is "terminal" or "explosion")
                && (f.Backing.Branch == p.AttackRole || "feed_" + f.Backing.Branch == p.AttackRole))
            .Select(f => SemanticBackingObject.For(sdk, p.Weapon, f)).ToHashSet();
        for (var a = 0; a < operations.Count; a++)
        {
            var op = operations[a];
            for (var b = a + 1; b < operations.Count; b++)
                if (op.Owner == operations[b].Owner || op.Contexts.Intersect(operations[b].Contexts).Any()) Join(a, b);
            if (op.Replacement == null) continue;
            var next = Owners(op.Replacement); var previous = Owners(op.Contexts.Single());
            for (var b = 0; b < operations.Count; b++)
            {
                if (a == b || operations[b].Replacement != null) continue;
                var other = operations[b];
                if (previous.Contains(other.Owner)) Join(a, b);
                if (next.Contains(other.Owner) || other.Family != "weapon" && other.Contexts.Intersect(op.Contexts).Any())
                {
                    Join(a, b);
                    if (!dependencies.TryGetValue(b, out var list)) dependencies[b] = list = [];
                    list.Add(a);
                }
            }
        }
        var output = new List<string>();
        foreach (var component in Enumerable.Range(0, operations.Count).GroupBy(Root).OrderBy(g => g.Min()))
        {
            var indices = component.ToArray();
            if (indices.Length == 1) { output.Add(operations[indices[0]].Lua(resourceId)); continue; }
            if (indices.Select(i => operations[i].Ensure).Distinct().Count() != 1)
                throw new InvalidDataException("Related composition objects have mixed persistence settings. Use the same persistence setting so they can share one guarded plan.");
            if (indices.Length > contract.Limits.Operations) throw new InvalidDataException("Composition exceeds the SDK's plan operation limit.");
            string Id(int i) => "op-" + Hash(resourceId + "\n" + operations[i].Owner + "\n" + operations[i].Family);
            var phased = indices.Any(dependencies.ContainsKey);
            var phases = phased ? new[] { indices.Where(i => !dependencies.ContainsKey(i)).ToArray(), indices.Where(dependencies.ContainsKey).ToArray() } : [indices];
            if (phases.Length > contract.Limits.Phases) throw new InvalidDataException("Composition exceeds the SDK's phase limit.");
            foreach (var phase in phases)
            {
                // Prior selector captures participate in a dependent phase's write set.
                var captures = phase.Where(dependencies.ContainsKey).SelectMany(i => dependencies[i]).Distinct();
                if (phase.Sum(i => operations[i].Changes.Count) + captures.Sum(i => operations[i].Changes.Count) > contract.Limits.PhysicalChangesPerPhase)
                    throw new InvalidDataException("Composition exceeds the SDK's changes-per-phase limit.");
            }
            string Operation(int index)
            {
                var op = operations[index]; var body = new StringBuilder("{\n    id=" + LuaGenerator.Quote(Id(index)) + ",\n");
                if (dependencies.TryGetValue(index, out var deps) && (op.Family == "projectile" || op.Family.StartsWith("terminal:", StringComparison.Ordinal)))
                {
                    var path = op.Family == "projectile" ? "projectile" : "terminal." + op.Family[9..];
                    if (!contract.TargetFromPaths.ContainsKey(path)) throw new InvalidDataException("SDK does not support this composition dependency path.");
                    var dependency = deps.OrderBy(Id, StringComparer.Ordinal).First();
                    body.Append("    target_from={operation=").Append(LuaGenerator.Quote(Id(dependency))).Append(",path=").Append(LuaGenerator.Quote(path)).Append("},\n");
                }
                else body.Append("    target=").Append(op.Target).Append(",\n");
                if (op.AllowShared) body.Append("    allow_shared=true,\n");
                if (op.Changes.Count == 1)
                {
                    var c = op.Changes[0]; body.Append($"    field={c.Field},\n    expect={c.Expected},\n    value={c.Desired},\n");
                }
                else
                {
                    body.Append("    changes={\n");
                    foreach (var c in op.Changes) body.Append($"        {{field={c.Field},expect={c.Expected},value={c.Desired}}},\n");
                    body.Append("    },\n");
                }
                return body.Append('}').ToString();
            }
            string List(int[] phase) => "{\n" + string.Join(",\n", phase.Select(i => Indent(Operation(i)))) + "\n}";
            var plan = new StringBuilder("{\n    id='plan-" + Hash(resourceId + "\n" + string.Join("\n", indices.Select(Id))) + "',\n");
            if (!phased) plan.Append("    operations=").Append(List(indices).Replace("\n", "\n    ")).Append(",\n");
            else
            {
                plan.Append("    phases={\n");
                for (var i = 0; i < phases.Length; i++) plan.Append("        {id='phase-").Append(i + 1).Append("',operations=").Append(List(phases[i]).Replace("\n", "\n        ")).Append("},\n");
                plan.Append("    },\n");
            }
            plan.Append('}');
            output.Add(operations[indices[0]].Ensure ? "hd2.ensure({\n    plan=" + plan.ToString().Replace("\n", "\n    ") + "\n})" : "hd2.plan(" + plan + ")");
        }
        return output;
    }
    private static string Indent(string value) => "    " + value.Replace("\n", "\n    ");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
}
