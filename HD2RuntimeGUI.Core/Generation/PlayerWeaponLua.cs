using System.Security.Cryptography;
using System.Text;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

internal static class PlayerWeaponLua
{
    internal static IEnumerable<string> Operations(ModProject project, SdkMetadata sdk, IWeaponChangeService validator)
    {
        var resolved = WeaponAliasResolver.Group(sdk, project.WeaponChanges);
        if (resolved.FirstOrDefault(g => g.Conflict != null) is { } conflict) throw new InvalidDataException(conflict.Conflict);
        // Validate every enabled source, including saved baselines and approvals,
        // before coalescing equal alias/canonical requests into one operation.
        foreach (var c in project.WeaponChanges.Where(c => c.Enabled)) validator.Validate(sdk, c);
        var active = resolved.Where(g => g.Enabled).Select(g => g.Sources.Where(c => c.Enabled).OrderBy(c => c.SemanticFieldId != g.FieldId).ThenBy(c => c.Id).First())
            .OrderBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.Group, StringComparer.Ordinal)
            .ThenBy(c => sdk.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)!.SemanticFieldId, StringComparer.Ordinal).ToArray();
        var catalog = sdk.PlayerWeapons;
        WeaponCapability Field(WeaponChange c) => catalog!.FindCanonicalField(c.Weapon, c.SemanticFieldId)!;
        // Shared backing aliases cannot declare conflicting desired values in one mod.
        foreach (var group in active.Where(c => Field(c).AffectsMultipleWeapons).GroupBy(c => (Field(c).Backing!.Settings, Field(c).Backing!.Group, Field(c).Backing!.Row, Field(c).Backing!.Offset)))
            if (group.Select(c => Field(c).Format(c.DesiredValue)).Distinct().Count() > 1) throw new InvalidDataException("Conflicting desired values for a shared backing setting.");
        foreach (var group in active.GroupBy(c => (c.Weapon, c.Group, c.EnsureEnabled, Shared: Field(c).AffectsMultipleWeapons)))
        {
            // Runtime 0.13 transactions accept at most 32 fields; never silently split an atomic group.
            var list = group.ToArray();
            if (list.Length > 32) throw new InvalidDataException("A Runtime transaction supports at most 32 fields. Divide this weapon's changes into explicit groups.");
            var idInput = project.ResourceId + "\n" + group.Key.Weapon + "\n" + group.Key.Group + "\n" + group.Key.EnsureEnabled + "\n" + group.Key.Shared;
            var id = "gui-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput)))[..24].ToLowerInvariant();
            var body = new StringBuilder("{\n    id=" + LuaGenerator.Quote(id) + ",\n    target=hd2.weapon(" + LuaGenerator.Quote(group.Key.Weapon) + "),\n");
            if (group.Key.Shared) body.Append("    allow_shared=true,\n");
            string Value(WeaponCapability f, System.Text.Json.JsonElement value) => f.SemanticFieldId == "weapon.default_fire_mode"
                ? "hd2.enums.fire_mode." + f.EnumValues!.Single(p => System.Text.Json.JsonElement.DeepEquals(p.Value, value)).Key
                : value.ValueKind == System.Text.Json.JsonValueKind.String ? LuaGenerator.Quote(value.GetString()!) : f.Format(value);
            string Accessor(WeaponChange c)
            {
                var semanticId = Field(c).SemanticFieldId;
                var parts = semanticId.Split('.', 2); var key = parts[1].Replace('.', '_');
                // The public API prefixes collisions with legacy field constants with player_.
                if (sdk.Resources.Values.SelectMany(r => r.Fields.Values).Any(f => f.Domain == parts[0] && f.Name.Replace('.', '_') == key && f.Name != semanticId)) key = "player_" + key;
                return "hd2.fields." + parts[0] + "." + key;
            }
            if (list.Length == 1)
            {
                var c = list[0]; var f = Field(c);
                body.Append($"    field={Accessor(c)},\n    expect={Value(f, c.ExpectedValue)},\n    value={Value(f, c.DesiredValue)},\n");
            }
            else
            {
                body.Append("    changes={\n");
                foreach (var c in list) { var f = Field(c); body.Append($"        {{field={Accessor(c)},expect={Value(f, c.ExpectedValue)},value={Value(f, c.DesiredValue)}}},\n"); }
                body.Append("    },\n");
            }
            body.Append('}'); var operation = list.Length == 1 ? "patch" : "transaction";
            yield return group.Key.EnsureEnabled ? $"hd2.ensure({{\n    {operation}={body.ToString().Replace("\n", "\n    ")}\n}})" : $"hd2.{operation}({body})";
        }
    }
}
