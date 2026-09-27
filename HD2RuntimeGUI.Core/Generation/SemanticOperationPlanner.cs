using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// Record identity is supplied by the SDK. Field offsets and user-facing groups are
// deliberately absent: neither defines an independently guarded object.
public sealed record SemanticBackingObject(string Kind, string Identity)
{
    public static SemanticBackingObject For(SdkMetadata sdk, string weapon, WeaponCapability field)
    {
        var b = field.Backing ?? throw new InvalidDataException("Missing semantic backing owner.");
        if (b.Kind == "component" && b.Component != null)
            return new(b.Component, string.Join("|", sdk.PlayerWeapons!.Weapon(weapon).Resources.Order(StringComparer.Ordinal)));
        if (b.Kind == "settings" && b.SettingsType != null && b.Group != null && b.RecordType != null)
            return new(b.SettingsType, $"{b.Group}:{b.RecordType}");
        throw new InvalidDataException("SDK does not identify a supported backing object for " + field.SemanticFieldId);
    }
}

public sealed record PlannedSemanticChange(string Field, string Expected, string Desired);
public sealed record PlannedSemanticOperation(SemanticBackingObject Owner, string Target, bool Ensure, bool AllowShared,
    IReadOnlyList<PlannedSemanticChange> Changes)
{
    public string Lua(string resourceId)
    {
        var identity = resourceId + "\n" + Owner.Kind + "\n" + Owner.Identity;
        var id = "gui-object-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
        var body = new StringBuilder("{\n    id=" + LuaGenerator.Quote(id) + ",\n    target=" + Target + ",\n");
        if (AllowShared) body.Append("    allow_shared=true,\n");
        if (Changes.Count == 1)
        {
            var c = Changes[0]; body.Append($"    field={c.Field},\n    expect={c.Expected},\n    value={c.Desired},\n");
        }
        else
        {
            body.Append("    changes={\n");
            foreach (var c in Changes) body.Append($"        {{field={c.Field},expect={c.Expected},value={c.Desired}}},\n");
            body.Append("    },\n");
        }
        body.Append('}'); var kind = Changes.Count == 1 ? "patch" : "transaction";
        return Ensure ? $"hd2.ensure({{\n    {kind}={body.ToString().Replace("\n", "\n    ")}\n}})" : $"hd2.{kind}({body})";
    }
}

public interface ISemanticOperationPlanner
{
    IReadOnlyList<PlannedSemanticOperation> Plan(ModProject project, SdkMetadata sdk);
}

public sealed class SemanticOperationPlanner : ISemanticOperationPlanner
{
    private sealed record Edit(SemanticBackingObject Owner, string Weapon, string Family, string Target, string Semantic,
        string Field, string Expected, string Desired, bool Ensure, bool Shared);
    public IReadOnlyList<PlannedSemanticOperation> Plan(ModProject project, SdkMetadata sdk)
    {
        var edits = new List<Edit>();
        var weaponService = new WeaponChangeService(); var objects = new CompositionChangeService();
        objects.ValidateComposition(project, sdk);
        var aliases = WeaponAliasResolver.Group(sdk, project.WeaponChanges);
        if (aliases.FirstOrDefault(g => g.Conflict != null) is { } conflict) throw new InvalidDataException(conflict.Conflict);
        foreach (var change in project.WeaponChanges.Where(c => c.Enabled)) weaponService.Validate(sdk, change);
        foreach (var group in aliases.Where(g => g.Enabled))
        {
            var c = group.Sources.Where(c => c.Enabled).OrderBy(c => c.SemanticFieldId != group.FieldId).ThenBy(c => c.Id).First();
            if (WeaponScalar.IsNoOp(sdk, c)) continue;
            var f = sdk.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)!;
            var target = "hd2.weapon(" + LuaGenerator.Quote(c.Weapon) + ")"; var family = "weapon"; var semantic = f.SemanticFieldId;
            if (CompositionChangeService.ProjectileOwned(f))
            {
                var attack = sdk.Composition!.Projectiles.Weapons.Single(w => w.Weapon == c.Weapon).Attacks.SingleOrDefault(a => a.Role == f.Backing!.Branch || a.Role == "feed_" + f.Backing.Branch);
                if (attack != null) { target = CompositionChangeService.ProjectileLua(new(c.Weapon, attack.Role)); family = "projectile"; semantic = Generic(semantic); }
            }
            edits.Add(new(SemanticBackingObject.For(sdk, c.Weapon, f), c.Weapon, family, target, semantic, Accessor(sdk, semantic),
                Scalar(f, c.ExpectedValue), Scalar(f, c.DesiredValue), c.EnsureEnabled, f.AffectsMultipleWeapons));
        }
        foreach (var c in project.CompositionChanges.Where(c => c.Enabled))
        {
            if (objects.IsNoOp(sdk, c)) continue;
            var f = c.Scalar == null ? CompositionChangeService.TerminalField(sdk, c.Target, c.Phase!) : sdk.PlayerWeapons!.Field(c.Scalar.Weapon, c.Scalar.SemanticFieldId);
            var terminal = CompositionChangeService.ProjectileLua(c.Target) + ":terminal_action(" + LuaGenerator.Quote(c.Phase ?? "impact") + ")";
            string Explosion(ExplosionReference r) => r.IsNone ? terminal + ":no_explosion()" : CompositionChangeService.ProjectileLua(r.Projectile!) + ":terminal_action(" + LuaGenerator.Quote(r.Phase!) + "):explosion()";
            var target = c.Kind == "terminal" ? terminal : c.Kind == "explosion" ? Explosion(c.ExplosionTarget!) : CompositionChangeService.ProjectileLua(c.Target);
            var semantic = c.Kind == "terminal" ? "terminal.explosion" : Generic(f.SemanticFieldId);
            edits.Add(new(SemanticBackingObject.For(sdk, c.Scalar?.Weapon ?? c.Target.Weapon, f), c.Scalar?.Weapon ?? c.Target.Weapon, c.Kind == "terminal" ? "terminal:" + c.Phase : c.Kind,
                target, semantic, Accessor(sdk, semantic), c.Scalar == null ? Explosion(c.ExpectedExplosion!) : Scalar(f, c.Scalar.ExpectedValue),
                c.Scalar == null ? Explosion(c.DesiredExplosion!) : Scalar(f, c.Scalar.DesiredValue), c.EnsureEnabled, f.AffectsMultipleWeapons));
        }
        var swaps = project.ProjectileChanges.Where(c => c.Enabled).ToArray();
        foreach (var swap in swaps)
        {
            var sourceOwners = sdk.PlayerWeapons!.Weapon(swap.ReplacementProjectile.Weapon).Fields.Where(f => f.Backing != null &&
                (CompositionChangeService.ProjectileOwned(f) || f.Domain is "terminal" or "explosion") &&
                (f.Backing.Branch == swap.ReplacementProjectile.AttackRole || "feed_" + f.Backing.Branch == swap.ReplacementProjectile.AttackRole))
                .Select(f => SemanticBackingObject.For(sdk, swap.ReplacementProjectile.Weapon, f)).ToHashSet();
            // Public patch/transaction/ensure calls schedule asynchronous jobs. Calling
            // them in Lua statement order does not establish a completion dependency.
            if (edits.Any(e => sourceOwners.Contains(e.Owner)) || project.CompositionChanges.Any(c => c.Enabled && (c.Weapon == swap.Weapon && c.AttackRole == swap.AttackRole || c.Target == swap.ReplacementProjectile))
                || project.WeaponChanges.Any(c => c.Enabled && c.Weapon == swap.ReplacementProjectile.Weapon && CompositionChangeService.ProjectileOwned(sdk.PlayerWeapons!.Field(c.Weapon, c.SemanticFieldId))))
                throw new InvalidDataException("Composition dependency: Runtime 0.17 has no public write-completion dependency API. A projectile replacement and dependent object edits cannot be scheduled safely in one mod; keep the replacement or the object edits enabled, not both.");
            var f = sdk.PlayerWeapons!.Weapon(swap.Weapon).Fields.Single(f => f.Domain == "attack" && f.ReferenceRole == swap.AttackRole);
            if (edits.Any(e => e.Owner == SemanticBackingObject.For(sdk, swap.Weapon, f)))
                throw new InvalidDataException("Composition dependency: projectile replacement and another edit share the selector's backing object. Runtime 0.17 cannot combine these target kinds safely.");
        }
        if (swaps.GroupBy(s => SemanticBackingObject.For(sdk, s.Weapon, sdk.PlayerWeapons!.Weapon(s.Weapon).Fields.Single(f => f.Domain == "attack" && f.ReferenceRole == s.AttackRole))).Any(g => g.Count() > 1))
            throw new InvalidDataException("Composition conflict: multiple projectile selectors share a backing object but Runtime 0.17 supports only one attack target per transaction.");
        var result = new List<PlannedSemanticOperation>();
        foreach (var group in edits.GroupBy(e => e.Owner).OrderBy(g => g.Key.Kind, StringComparer.Ordinal).ThenBy(g => g.Key.Identity, StringComparer.Ordinal))
        {
            if (group.Select(e => e.Ensure).Distinct().Count() != 1)
                throw new InvalidDataException("One backing object has mixed persistence settings. Use the same persistence setting for all its fields; splitting them would race Runtime guards.");
            if (group.Select(e => e.Family).Distinct().Count() != 1)
                throw new InvalidDataException("Composition conflict: edits share one backing object but require different semantic targets. Runtime 0.17 transactions have one target. Impact + expiry, or terminal + scalar edits on the same ProjectileSettings, cannot be exported together safely.");
            var values = new List<PlannedSemanticChange>();
            foreach (var fields in group.GroupBy(e => e.Semantic).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (fields.Select(e => (e.Expected, e.Desired)).Distinct().Count() != 1)
                    throw new InvalidDataException("Conflicting baselines or values for one shared semantic object field.");
                var e = fields.First(); values.Add(new(e.Field, e.Expected, e.Desired));
            }
            if (values.Count > 32) throw new InvalidDataException("A backing object exceeds Runtime's 32-change transaction limit. Remove edits; splitting this object into separate jobs is unsafe.");
            // Multiple weapon handles may reach a shared record. Select a handle only
            // if its published catalog accepts every requested semantic field.
            var candidate = group.OrderBy(e => e.Target, StringComparer.Ordinal).FirstOrDefault(e => group.All(request =>
                sdk.PlayerWeapons!.Weapon(e.Weapon).Fields.Any(f => f.IsPreferred && f.Editable && f.Backing != null
                    && SemanticBackingObject.For(sdk, e.Weapon, f) == e.Owner
                    && (e.Family.StartsWith("terminal:", StringComparison.Ordinal) ? f.Domain == "terminal" && "terminal:" + f.ReferencePhase == e.Family
                        : (e.Family == "weapon" ? f.SemanticFieldId : Generic(f.SemanticFieldId)) == request.Semantic))));
            if (candidate == null) throw new InvalidDataException("No published semantic target accepts all fields on this shared backing object. Separate jobs would race; review these changes.");
            result.Add(new(group.Key, candidate.Target, group.First().Ensure, group.Any(e => e.Shared), values));
        }
        return result;
    }
    private static string Generic(string id) => Regex.Replace(id, @"\.(primary|alternate|feed_primary|feed_alternate|impact|expiry)(?=\.)", "");
    private static string Scalar(WeaponCapability f, JsonElement value) => f.SemanticFieldId == "weapon.default_fire_mode"
        ? "hd2.enums.fire_mode." + f.EnumValues!.Single(p => JsonElement.DeepEquals(p.Value, value)).Key
        : value.ValueKind == JsonValueKind.String ? LuaGenerator.Quote(value.GetString()!) : f.Format(value);
    private static string Accessor(SdkMetadata sdk, string semantic)
    {
        var parts = semantic.Split('.', 2); var key = parts[1].Replace('.', '_');
        if (sdk.Resources.Values.SelectMany(r => r.Fields.Values).Any(x => x.Domain == parts[0] && x.Name.Replace('.', '_') == key && x.Name != semantic)) key = "player_" + key;
        return "hd2.fields." + parts[0] + "." + key;
    }
}
