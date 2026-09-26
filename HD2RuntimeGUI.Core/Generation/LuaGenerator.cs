using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;

namespace HD2RuntimeGUI.Core.Generation;

public interface ILuaGenerator { string Generate(ModProject project, SdkMetadata sdk); }

public sealed class LuaGenerator(IChangeService changes) : ILuaGenerator
{
    public string Generate(ModProject project, SdkMetadata sdk)
    {
        ProjectIdentity.Validate(project);
        if (project.SdkVersion != sdk.Version || project.RuntimeApi != sdk.ApiVersion) throw new InvalidDataException("Project SDK mismatch.");
        var active = project.Changes.Where(c => c.Enabled).OrderBy(c => c.Target, StringComparer.Ordinal).ThenBy(c => c.Group, StringComparer.Ordinal).ThenBy(c => c.Field, StringComparer.Ordinal).ToArray();
        foreach (var change in active) changes.Validate(sdk, change);
        if (active.GroupBy(c => (c.Target, c.Field)).Any(g => g.Count() > 1)) throw new InvalidDataException("A target field may only be modified once.");
        var operations = new List<string>();
        foreach (var group in active.GroupBy(c => (c.Target, c.Group, c.EnsureEnabled)))
        {
            var list = group.ToArray(); var resource = sdk.Resources[group.Key.Target];
            // API 1's single-field patch and transaction domains differ. Honor the published contract.
            bool patch = list.Length == 1 && sdk.Transitions.Any(t => t.Resource == list[0].Target && t.Field == list[0].Field && t.Operation == "patch");
            if (!patch && list.Any(c => !sdk.Transitions.Any(t => t.Resource == c.Target && t.Field == c.Field && t.Operation == "transaction")))
                throw new InvalidDataException("The SDK does not support this atomic field combination.");
            var target = $"hd2.{resource.Kind}({Quote(resource.Label)})";
            if (patch) foreach (var method in sdk.MethodPath(resource, list[0].Domain)) target += $":{method}()";
            string Field(ModChange c) => $"hd2.fields.{c.Domain}.{c.Field.Replace('.', '_')}";
            var body = new StringBuilder();
            body.Append("{\n    id=").Append(Quote("gui-" + list[0].Id.ToString("N"))).Append(",\n    target=").Append(target).Append(",\n");
            if (patch) body.Append($"    field={Field(list[0])},\n    expect={Value(list[0].ExpectedValue)},\n    value={Value(list[0].NewValue)},\n");
            else
            {
                body.Append("    changes={\n");
                foreach (var c in list) body.Append($"        {{field={Field(c)},expect={Value(c.ExpectedValue)},value={Value(c.NewValue)}}},\n");
                body.Append("    },\n");
            }
            body.Append('}');
            var operation = patch ? "patch" : "transaction";
            operations.Add(group.Key.EnsureEnabled ? $"hd2.ensure({{\n    {operation}={body.ToString().Replace("\n", "\n    ")}\n}})" : $"hd2.{operation}({body})");
        }
        string prefix = "local hd2=require('mods/skyeshade/hd2runtime')\n\n";
        if (operations.Count == 0) return prefix + "-- No enabled modifications.\nreturn {}\n";
        return prefix + (operations.Count == 1 ? "return " + operations[0] : "return {\n" + string.Join(",\n", operations.Select(o => "    " + o.Replace("\n", "\n    "))) + "\n}") + "\n";
    }
    public static string Quote(string value)
    {
        var result = new StringBuilder("'");
        foreach (var c in value)
            result.Append(c switch { '\\' => "\\\\", '\'' => "\\'", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ when char.IsControl(c) => "\\" + ((int)c).ToString("D3"), _ => c.ToString() });
        return result.Append('\'').ToString();
    }
    private static string Value(JsonNode? value) => value?.ToJsonString() ?? "nil";
}
