using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;

namespace HD2RuntimeGUI.Core.Generation;

public interface ILuaGenerator { string Generate(ModProject project, SdkMetadata sdk); }

public sealed class LuaGenerator(IChangeService changes) : ILuaGenerator
{
    private readonly IWeaponChangeService weaponChanges = new WeaponChangeService();
    public LuaGenerator(IChangeService changes, IWeaponChangeService weaponChanges) : this(changes) => this.weaponChanges = weaponChanges;
    private readonly IProjectileChangeService projectileChanges = new ProjectileChangeService();
    public LuaGenerator(IChangeService changes, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges) : this(changes, weaponChanges) => this.projectileChanges = projectileChanges;
    private readonly ICompositionChangeService compositionChanges = new CompositionChangeService();
    private readonly ISemanticOperationPlanner planner = new SemanticOperationPlanner();
    public LuaGenerator(IChangeService changes, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges, ICompositionChangeService compositionChanges)
        : this(changes, weaponChanges, projectileChanges) => this.compositionChanges = compositionChanges;
    public LuaGenerator(IChangeService changes, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges, ICompositionChangeService compositionChanges, ISemanticOperationPlanner planner)
        : this(changes, weaponChanges, projectileChanges, compositionChanges) => this.planner = planner;
    private readonly ISupportLua supportLua = new SupportLua(new SupportChangeService());
    public LuaGenerator(IChangeService changes, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges, ICompositionChangeService compositionChanges, ISemanticOperationPlanner planner, ISupportLua supportLua)
        : this(changes, weaponChanges, projectileChanges, compositionChanges, planner) => this.supportLua = supportLua;
    private readonly IStratagemLua stratagemLua = new StratagemLua(new StratagemChangeService());
    public LuaGenerator(IChangeService changes, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges, ICompositionChangeService compositionChanges, ISemanticOperationPlanner planner, ISupportLua supportLua, IStratagemLua stratagemLua)
        : this(changes, weaponChanges, projectileChanges, compositionChanges, planner, supportLua) => this.stratagemLua = stratagemLua;
    private readonly IEntityLua entityLua = new EntityLua(new EntityChangeService());
    public string Generate(ModProject project, SdkMetadata sdk)
    {
        ProjectIdentity.Validate(project);
        compositionChanges.ValidateComposition(project, sdk);
        if (project.SdkVersion != sdk.Version || project.RuntimeApi != sdk.ApiVersion) throw new InvalidDataException("Project SDK mismatch.");
        var active = project.Changes.Where(c => c.Enabled).OrderBy(c => c.Target, StringComparer.Ordinal).ThenBy(c => c.Group, StringComparer.Ordinal).ThenBy(c => c.Field, StringComparer.Ordinal).ToArray();
        foreach (var change in active) changes.Validate(sdk, change);
        if (sdk.Advanced != null && active.Any(c => sdk.Resources[c.Target].Kind == "weapon") &&
            (project.WeaponChanges.Any(c => c.Enabled) || project.CompositionChanges.Any(c => c.Enabled) || project.ProjectileChanges.Any(c => c.Enabled)))
            throw new InvalidDataException("Migrate legacy weapon modifications before combining them with semantic edits; their shared backing scope cannot be safely planned together.");
        if (active.Any(c => sdk.Resources[c.Target].Kind == "weapon" && project.WeaponChanges.Any(w => w.Enabled && w.Weapon == sdk.Resources[c.Target].Label))) throw new InvalidDataException("Remove legacy weapon modifications before using semantic overrides for the same weapon.");
        if (active.GroupBy(c => (c.Target, c.Field)).Any(g => g.Count() > 1)) throw new InvalidDataException("A target field may only be modified once.");
        // In-game options (0.25.0+): null unless the project enabled them and at least one active edit is bound.
        var options = ModOptionsService.Bindings(project, sdk);
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
        // Composition was validated above; planning does not validate it again.
        if (sdk.Plans != null)
            operations.AddRange(CompositionPlanLua.Operations(project.ResourceId, sdk, planner.PlanValidated(project, sdk), options));
        else
        {
            operations.AddRange(ProjectileChangeService.Operations(project, sdk, projectileChanges));
            if (sdk.Advanced != null)
                operations.AddRange(planner.PlanValidated(project, sdk).Select(o => o.Lua(project.ResourceId)));
            else
            {
                operations.AddRange(PlayerWeaponLua.Operations(project, sdk, weaponChanges));
                operations.AddRange(CompositionChangeService.Operations(project, sdk, compositionChanges));
            }
        }
        // Attack outputs (0.28.0 development SDKs): each a patch through the attack's active projectile source.
        if (project.AttackOutputChanges is { Count: > 0 }) operations.AddRange(AttackOutputChangeService.Operations(project, sdk));
        // Projectile-builder row writes (slots, mode label and icon) on hd2.attack_output(row): independent of any host swap.
        if (project.OutputRowChanges is { Count: > 0 }) operations.AddRange(OutputRowChangeService.Operations(project, sdk));
        operations.AddRange(supportLua.Operations(project, sdk, options));
        operations.AddRange(stratagemLua.Operations(project, sdk, options));
        operations.AddRange(entityLua.Operations(project, sdk, options));
        string prefix = "local hd2=require('mods/skyeshade/hd2runtime')\n\n";
        // One options page, its master toggle, then one handle per bound field, in registration (display) order.
        if (options != null) prefix += options.Header + "\n";
        var custom = CustomSource(project);
        if (custom == null)
        {
            if (operations.Count == 0) return prefix + "-- No enabled modifications.\nreturn {}\n";
            return prefix + Isolated(operations, "operations") + "return operations\n";
        }
        // Custom Lua (src/addon.lua) runs after the generated modifications are registered, as its own function so its locals and return
        // stay its own. Its text is copied exactly (line endings normalized to LF as the SDK builder does); ModBuilder never edits it.
        var generated = operations.Count == 0 ? "local generated={}\n" : Isolated(operations, "generated");
        return prefix + generated + "\n-- Custom Lua: " + Models.CustomLuaSettings.RelativePath + " (hand-written; ModBuilder copies it unchanged)\nlocal function addon(...)\n"
            + custom + (custom.EndsWith('\n') ? "" : "\n") + "end\naddon()\nreturn generated\n";
    }
    // Every generated operation is built inside pcall, in project order. Runtime (0.28.0+) rejects a refused operation without raising,
    // but a Lua error while building a request (an unknown weapon or role name, a missing field constant) happens before hd2.ensure is
    // called: it is printed to the Runtime log and skips only that operation, so the ones after it still register. Ids stay unique.
    public const string SkippedPrefix = "[ModBuilder] operation skipped: ";
    private static string Isolated(IReadOnlyList<string> operations, string list) =>
        $"local {list}={{}}\nlocal function add(build)\n    local ok,operation=pcall(build)\n    if ok then {list}[#{list}+1]=operation\n"
        + $"    else print({Quote(SkippedPrefix)}..tostring(operation)) end\nend\n"
        + string.Concat(operations.Select(o => "add(function() return " + o + " end)\n"));
    // The enabled custom Lua, checked for syntax (a syntax error blocks the build and export, with its line); null when there is none.
    public static string? CustomSource(ModProject project)
    {
        if (project.CustomLua is not { Enabled: true } lua || string.IsNullOrWhiteSpace(lua.Source)) return null;
        var source = lua.Source.Replace("\r\n", "\n").Replace('\r', '\n');
        if (source.StartsWith('\uFEFF')) source = source[1..];
        foreach (var d in Scripting.LuaScriptAnalyzer.Analyze(source, null).Where(d => d.Severity == Scripting.LuaDiagnostic.Error))
            throw new InvalidDataException(Localization.CoreText.Format("CustomLua.BuildError", Models.CustomLuaSettings.RelativePath, d.Line, d.Message));
        return source;
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
