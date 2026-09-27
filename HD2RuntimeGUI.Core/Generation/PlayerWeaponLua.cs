using System.Security.Cryptography;
using System.Text;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

internal static class PlayerWeaponLua
{
    internal static IEnumerable<string> Operations(ModProject project, SdkMetadata sdk, IWeaponChangeService validator)
    {
        var active = project.WeaponChanges.Where(c => c.Enabled).OrderBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.Group, StringComparer.Ordinal).ThenBy(c => c.SemanticFieldId, StringComparer.Ordinal).ToArray();
        foreach (var c in active) validator.Validate(sdk, c);
        if (active.GroupBy(c => (c.Weapon, c.SemanticFieldId)).Any(g => g.Count() > 1)) throw new InvalidDataException("A weapon field may only be modified once.");
        var catalog = sdk.PlayerWeapons;
        WeaponCapability Field(WeaponChange c) => catalog!.Field(c.Weapon, c.SemanticFieldId);
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
            string Value(WeaponCapability f, System.Text.Json.JsonElement value) => value.ValueKind == System.Text.Json.JsonValueKind.String ? LuaGenerator.Quote(value.GetString()!) : f.Format(value);
            string Accessor(WeaponChange c)
            {
                var parts = c.SemanticFieldId.Split('.', 2); var key = parts[1].Replace('.', '_');
                // The public API prefixes collisions with legacy field constants with player_.
                if (sdk.Resources.Values.SelectMany(r => r.Fields.Values).Any(f => f.Domain == parts[0] && f.Name.Replace('.', '_') == key && f.Name != c.SemanticFieldId)) key = "player_" + key;
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
