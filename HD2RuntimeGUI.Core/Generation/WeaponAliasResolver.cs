using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// This is a project view, not a migration of SDK objects or saved baselines.
public sealed record WeaponChangeGroup(string Weapon, string FieldId, WeaponCapability? Field, IReadOnlyList<WeaponChange> Sources)
{
    public WeaponChange Representative => Sources.OrderBy(c => c.SemanticFieldId != FieldId).ThenBy(c => c.Id).First();
    public bool Enabled => Sources.Any(c => c.Enabled);
    public string? Conflict => Sources.Select(c => c.SemanticFieldId).Distinct().Count() != Sources.Count
        ? "Migration conflict: duplicate saved field records. Choose which change to keep."
        : Sources.Skip(1).Any(c => Field == null || !WeaponScalar.Equal(Field, Sources[0].DesiredValue, c.DesiredValue))
            ? "Migration conflict: an alias and its canonical field have different desired values. Choose which change to keep."
            : Sources.Skip(1).Any(c => Field != null && !WeaponScalar.Equal(Field, Sources[0].ExpectedValue, c.ExpectedValue))
                ? "Migration conflict: saved alias baselines differ. Choose which change to keep; its baseline will still be reviewed against the SDK."
                : null;
}

public static class WeaponAliasResolver
{
    public static IReadOnlyList<WeaponChangeGroup> Group(SdkMetadata sdk, IEnumerable<WeaponChange> changes) => changes
        .GroupBy(c => (c.Weapon, Field: sdk.PlayerWeapons?.FindCanonicalField(c.Weapon, c.SemanticFieldId)?.SemanticFieldId ?? c.SemanticFieldId))
        .Select(g => new WeaponChangeGroup(g.Key.Weapon, g.Key.Field, sdk.PlayerWeapons?.FindCanonicalField(g.Key.Weapon, g.Key.Field), g.ToArray()))
        .OrderBy(g => g.Weapon, StringComparer.Ordinal).ThenBy(g => g.FieldId, StringComparer.Ordinal).ToArray();

    public static int RemoveNoOps(SdkMetadata sdk, List<WeaponChange> changes)
    {
        // A baseline-valued alias must not disappear and conceal a conflict with
        // another saved value. Leave the whole group for explicit resolution.
        var removable = Group(sdk, changes).Where(g => g.Conflict == null).SelectMany(g => g.Sources)
            .Where(c => WeaponScalar.IsNoOp(sdk, c)).Select(c => c.Id).ToHashSet();
        return changes.RemoveAll(c => removable.Contains(c.Id));
    }
}
