using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// Record identity is supplied by the SDK. Field offsets and user-facing groups are
// deliberately absent: neither defines an independently guarded object.
public sealed record SemanticBackingObject(string Kind, string Identity)
{
    public static SemanticBackingObject For(SdkMetadata sdk, string weapon, WeaponCapability field)
    {
        var b = field.Backing ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.MissingOwner"));
        // 1.4.0: Runtime writes a weapon's rate slots, weapon-function bindings and function projectile in one transaction (operation group
        // weapon_selector) across its ProjectileWeapon and WeaponData components, so the group is one owner of its own.
        if (field.InWeaponSelector && b.Kind == "component")
            return new(WeaponCapability.WeaponSelectorGroup, string.Join("|", sdk.PlayerWeapons!.Weapon(weapon).Resources.Order(StringComparer.Ordinal)));
        if (b.Kind == "component" && b.Component != null)
            return new(b.Component, string.Join("|", sdk.PlayerWeapons!.Weapon(weapon).Resources.Order(StringComparer.Ordinal)));
        if (b.Kind == "settings" && b.SettingsType != null && b.Group != null && b.RecordType != null)
            return new(b.SettingsType, $"{b.Group}:{b.RecordType}");
        throw new InvalidDataException(CoreText.Format("Messages.Build.Plan.UnsupportedOwner", field.SemanticFieldId));
    }
}

// Keys: in-game option binding keys of the edits merged into this value (ModOptionsService).
public sealed record PlannedSemanticChange(string Field, string Expected, string Desired)
{
    public IReadOnlyList<string?> Keys { get; init; } = [];
}
public sealed record PlannedSemanticOperation(SemanticBackingObject Owner, string Target, bool Ensure, bool AllowShared,
    IReadOnlyList<PlannedSemanticChange> Changes)
{
    // 0.26.0: Runtime requires allow_unverified_effect for at least one field of this operation (reticle, fire modes).
    public bool AllowUnverifiedEffect { get; init; }
    // 1.4.0: a donor function projectile (function_ammo.projectile = hd2.attack_output(...)) outside a live-proven pair.
    public bool AllowUnverifiedReference { get; init; }
    public string Family { get; init; } = "";
    public IReadOnlyList<ProjectileReference> Contexts { get; init; } = [];
    public ProjectileReference? Replacement { get; init; }
    public string Lua(string resourceId, OptionBindings? options = null)
    {
        var identity = resourceId + "\n" + Owner.Kind + "\n" + Owner.Identity;
        var id = "gui-object-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
        var body = new StringBuilder("{\n    id=" + LuaGenerator.Quote(id) + ",\n    target=" + Target + ",\n");
        if (AllowShared) body.Append("    allow_shared=true,\n");
        if (AllowUnverifiedEffect) body.Append("    allow_unverified_effect=true,\n");
        if (AllowUnverifiedReference) body.Append("    allow_unverified_reference=true,\n");
        if (Changes.Count == 1)
        {
            var c = Changes[0]; body.Append($"    field={c.Field},\n    expect={c.Expected},\n    value={options?.Value(c.Keys, c.Desired) ?? c.Desired},\n");
        }
        else
        {
            body.Append("    changes={\n");
            foreach (var c in Changes) body.Append($"        {{field={c.Field},expect={c.Expected},value={options?.Value(c.Keys, c.Desired) ?? c.Desired}}},\n");
            body.Append("    },\n");
        }
        body.Append('}'); var kind = Changes.Count == 1 ? "patch" : "transaction";
        return OptionBindings.Wrap(options, kind, body.ToString(), Ensure, Changes.SelectMany(c => c.Keys));
    }
}

public interface ISemanticOperationPlanner
{
    IReadOnlyList<PlannedSemanticOperation> Plan(ModProject project, SdkMetadata sdk);
}

public sealed class SemanticOperationPlanner : ISemanticOperationPlanner
{
    private sealed record Edit(SemanticBackingObject Owner, string Weapon, string Family, string Target, string Semantic,
        string Field, string Expected, string Desired, bool Ensure, bool Shared, ProjectileReference? Context = null, ProjectileReference? Replacement = null, string? Key = null, bool Unverified = false,
        bool UnverifiedReference = false);
    public IReadOnlyList<PlannedSemanticOperation> Plan(ModProject project, SdkMetadata sdk)
    {
        var edits = new List<Edit>();
        var weaponService = new WeaponChangeService(); var objects = new CompositionChangeService();
        objects.ValidateComposition(project, sdk);
        var aliases = WeaponAliasResolver.Group(sdk, project.WeaponChanges);
        if (aliases.FirstOrDefault(g => g.Conflict != null) is { } conflict) throw new InvalidDataException(conflict.Conflict);
        foreach (var change in project.WeaponChanges.Where(c => c.Enabled)) weaponService.Validate(sdk, change);
        foreach (var weapon in project.WeaponChanges.Select(c => c.Weapon).Distinct())
            if (WeaponChangeService.FireModeConflict(project.WeaponChanges, weapon) is { } modeConflict) throw new InvalidDataException(modeConflict);
        if (WeaponSelectorRules.PlayerIssues(sdk, project.WeaponChanges).FirstOrDefault() is { Message: { } selectorIssue }) throw new InvalidDataException(selectorIssue);
        foreach (var group in aliases.Where(g => g.Enabled))
        {
            var c = group.Sources.Where(c => c.Enabled).OrderBy(c => c.SemanticFieldId != group.FieldId).ThenBy(c => c.Id).First();
            if (WeaponScalar.IsNoOp(sdk, c)) continue;
            var f = sdk.PlayerWeapons!.FindCanonicalField(c.Weapon, c.SemanticFieldId)!;
            var target = WeaponTargets.Lua(sdk, c.Weapon); var family = "weapon"; var semantic = f.SemanticFieldId; ProjectileReference? context = null;
            if (CompositionChangeService.ProjectileOwned(f))
            {
                var attack = sdk.Composition!.Projectiles.Weapons.Single(w => w.Weapon == c.Weapon).Attacks.SingleOrDefault(a => a.Role == f.Backing!.Branch || a.Role == "feed_" + f.Backing.Branch);
                if (attack != null) { context = new(c.Weapon, attack.Role); target = CompositionChangeService.ProjectileLua(context); family = "projectile"; semantic = Generic(semantic); }
            }
            // 1.4.0 value types are written with the constant the field publishes (the older ones keep their accessor spelling).
            var accessor = AuthoredTypes.Composition.Contains(f.Type) && family == "weapon" && !string.IsNullOrEmpty(f.ApiFieldConstant) ? f.ApiFieldConstant : Accessor(sdk, semantic);
            var handle = WeaponTargets.Lua(sdk, c.Weapon);
            edits.Add(new(SemanticBackingObject.For(sdk, c.Weapon, f), c.Weapon, family, target, semantic, accessor,
                Scalar(f, c.ExpectedValue, handle), Scalar(f, c.DesiredValue, handle), c.EnsureEnabled, f.AffectsMultipleWeapons, context, Key: ModOptionsService.WeaponKey(c.Weapon, group.FieldId),
                Unverified: CompositionOptIns.EffectFor(f, c.DesiredValue), UnverifiedReference: CompositionOptIns.ReferenceFor(f, c.DesiredValue)));
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
                c.Scalar == null ? Explosion(c.DesiredExplosion!) : Scalar(f, c.Scalar.DesiredValue), c.EnsureEnabled, f.AffectsMultipleWeapons, new(c.Weapon, c.AttackRole),
                Key: c.Scalar == null ? null : ModOptionsService.ObjectKey(c),
                // Object fields Runtime has not proven in play (including status slots outside the live-proven direct-hit rows) carry the opt-in.
                Unverified: c.Scalar != null && CompositionOptIns.EffectFor(f, c.Scalar.DesiredValue)));
        }
        var swaps = project.ProjectileChanges.Where(c => c.Enabled).ToArray();
        if (sdk.Plans != null)
        {
            if (project.ProjectileChanges.GroupBy(c => (c.Weapon, c.AttackRole)).Any(g => g.Count() > 1)) throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.Conflicting"));
            var service = new ProjectileChangeService();
            foreach (var swap in swaps)
            {
                service.Validate(sdk, swap);
                if (service.IsBaseline(sdk, swap.Weapon, swap.AttackRole, swap.ReplacementProjectile)) continue;
                var field = sdk.PlayerWeapons!.Weapon(swap.Weapon).Fields.Single(f => f.Domain == "attack" && f.ReferenceRole == swap.AttackRole);
                var target = "hd2.weapon(" + LuaGenerator.Quote(swap.Weapon) + "):attack(" + LuaGenerator.Quote(swap.AttackRole) + ")";
                edits.Add(new(SemanticBackingObject.For(sdk, swap.Weapon, field), swap.Weapon, "attack:" + target, target, "attack.projectile",
                    "hd2.fields.attack.projectile", target + ":projectile()", CompositionChangeService.ProjectileLua(swap.ReplacementProjectile), swap.EnsureEnabled, false,
                    new(swap.Weapon, swap.AttackRole), swap.ReplacementProjectile));
            }
        }
        else
        {
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
                    throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.NoDependencyApi"));
                var f = sdk.PlayerWeapons!.Weapon(swap.Weapon).Fields.Single(f => f.Domain == "attack" && f.ReferenceRole == swap.AttackRole);
                if (edits.Any(e => e.Owner == SemanticBackingObject.For(sdk, swap.Weapon, f)))
                    throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.SharedSelectorOwner"));
            }
            if (swaps.GroupBy(s => SemanticBackingObject.For(sdk, s.Weapon, sdk.PlayerWeapons!.Weapon(s.Weapon).Fields.Single(f => f.Domain == "attack" && f.ReferenceRole == s.AttackRole))).Any(g => g.Count() > 1))
                throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.SharedSelectors"));
        }
        var result = new List<PlannedSemanticOperation>();
        // Terminal phases need distinct typed targets even when their native owner is
        // the same. A 0.19 plan coordinates these operations as one complete write set.
        foreach (var group in edits.GroupBy(e => (e.Owner, Family: sdk.Plans == null ? "" : e.Family)).OrderBy(g => g.Key.Owner.Kind, StringComparer.Ordinal).ThenBy(g => g.Key.Owner.Identity, StringComparer.Ordinal).ThenBy(g => g.Key.Family, StringComparer.Ordinal))
        {
            if (group.Select(e => e.Ensure).Distinct().Count() != 1)
                throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.MixedPersistence"));
            if (group.Select(e => e.Family).Distinct().Count() != 1)
                throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.DifferentTargets"));
            if (group.Select(e => e.Semantic).Distinct().Count() > 32) throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.TransactionLimit"));
            // Multiple weapon handles may reach a shared record. Select a handle only
            // if its published catalog accepts every requested semantic field.
            var candidate = group.OrderBy(e => e.Target, StringComparer.Ordinal).FirstOrDefault(e => group.All(request =>
                sdk.PlayerWeapons!.Weapon(e.Weapon).Fields.Any(f => f.IsPreferred && f.Editable && f.Backing != null
                    && SemanticBackingObject.For(sdk, e.Weapon, f) == e.Owner
                    && (e.Family.StartsWith("terminal:", StringComparison.Ordinal) ? f.Domain == "terminal" && "terminal:" + f.ReferencePhase == e.Family
                        : (e.Family == "weapon" ? f.SemanticFieldId : Generic(f.SemanticFieldId)) == request.Semantic))));
            if (candidate == null) throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.NoTarget"));
            var values = new List<PlannedSemanticChange>();
            foreach (var fields in group.GroupBy(e => e.Semantic).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (fields.Select(e => (e.Expected, e.Desired)).Distinct().Count() != 1)
                    throw new InvalidDataException(CoreText.Get("Messages.Build.Plan.ConflictingValues"));
                var e = fields.First(); values.Add(new(BranchConstant(sdk, candidate, e) ?? e.Field, e.Expected, e.Desired) { Keys = fields.Select(x => x.Key).ToArray() });
            }
            result.Add(new(group.Key.Owner, candidate.Target, group.First().Ensure, group.Any(e => e.Shared), values)
            { AllowUnverifiedEffect = group.Any(e => e.Unverified), AllowUnverifiedReference = group.Any(e => e.UnverifiedReference), Family = candidate.Family, Contexts = group.Where(e => e.Context != null).Select(e => e.Context!).Distinct().OrderBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.AttackRole, StringComparer.Ordinal).ToArray(), Replacement = candidate.Replacement });
        }
        return result;
    }
    // A projectile-object field the chosen target names per branch (the SG-20 Halt's two feeds: damage.primary.*, damage.alternate.*) is
    // written with the exact constant that target publishes. The generic name only resolves on Runtimes that map a feed role back to its
    // branch; the published constant resolves on every Runtime that publishes it (HD2Runtime user report, 2026-09-29).
    private static string? BranchConstant(SdkMetadata sdk, Edit candidate, Edit request) => candidate.Family != "projectile" ? null
        : sdk.PlayerWeapons!.Weapon(candidate.Weapon).Fields.Where(f => f.IsPreferred && f.Editable && f.Backing != null && Generic(f.SemanticFieldId) == request.Semantic
                && f.SemanticFieldId != request.Semantic && SemanticBackingObject.For(sdk, candidate.Weapon, f) == request.Owner && !string.IsNullOrEmpty(f.ApiFieldConstant))
            .Select(f => f.ApiFieldConstant).FirstOrDefault();
    private static string Generic(string id) => Regex.Replace(id, @"\.(primary|alternate|feed_primary|feed_alternate|impact|expiry)(?=\.)", "");
    private static string Scalar(WeaponCapability f, JsonElement value, string weaponTarget) => CompositionLua.Value(f.Type, value, weaponTarget) ?? Scalar(f, value);
    private static string Scalar(WeaponCapability f, JsonElement value) => f.Type == WeaponCapability.FireModeSet ? FireModes.Lua(value) : f.SemanticFieldId == "weapon.default_fire_mode"
        ? "hd2.enums.fire_mode." + f.EnumValues!.Single(p => JsonElement.DeepEquals(p.Value, value)).Key
        : value.ValueKind == JsonValueKind.String ? LuaGenerator.Quote(value.GetString()!) : f.Format(value);
    private static string Accessor(SdkMetadata sdk, string semantic)
    {
        var parts = semantic.Split('.', 2); var key = parts[1].Replace('.', '_');
        if (sdk.Resources.Values.SelectMany(r => r.Fields.Values).Any(x => x.Domain == parts[0] && x.Name.Replace('.', '_') == key && x.Name != semantic)) key = "player_" + key;
        return "hd2.fields." + parts[0] + "." + key;
    }
}
