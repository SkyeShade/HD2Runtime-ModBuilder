using System.Globalization;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record StratagemFieldGroup(string Key, string Title, StratagemField[] Fields);
public sealed record StratagemAttackNode(StratagemAttack Attack, string Title, StratagemField[] Fields);
public sealed record StratagemWeaponNode(string Entity, string Weapon, string Title, StratagemFieldGroup[] Groups, StratagemAttackNode[] Attacks);
public sealed record StratagemZoneNode(StratagemDamageZone Zone, StratagemField[] Fields);
public sealed record StratagemShieldNode(StratagemShieldConfig Config, StratagemField[] Fields);
// Stats are the physical base (entity health/armor and other deployed-entity fields); zones and the shield projector stay separate nodes.
public sealed record StratagemEntityNode(string Entity, StratagemEntityIdentity Identity, StratagemField[] Stats, StratagemWeaponNode[] Weapons,
    StratagemZoneNode[] Zones, StratagemShieldNode? Shield);

// Presentation graph for one stratagem: Stratagem → Deployed Entity → Mounted Weapon(s) → Attack branches.
// Nodes are grouped only by the published semantic target identity of each canonical instance; no native relationship
// is inferred and repeated fields on different branches remain separate instances.
public sealed record StratagemGraph(StratagemDefinition Root, StratagemField[] Definition, StratagemField[] EagleRearm,
    StratagemEntityNode[] Entities, StratagemAttackNode[] Attacks, StratagemBlockedField[] Blocked)
{
    public IEnumerable<StratagemField> AllFields => Definition.Concat(EagleRearm).Concat(Attacks.SelectMany(a => a.Fields))
        .Concat(Entities.SelectMany(e => e.Stats.Concat(e.Zones.SelectMany(z => z.Fields)).Concat(e.Shield?.Fields ?? [])
            .Concat(e.Weapons.SelectMany(w => w.Groups.SelectMany(g => g.Fields).Concat(w.Attacks.SelectMany(a => a.Fields))))));
    public static StratagemGraph Build(StratagemCatalog c, string name, Func<StratagemField, bool>? include = null)
    {
        var root = c.Root(name) ?? throw new InvalidDataException("Unknown stratagem.");
        var fields = c.FieldInstances.Where(f => f.Target.Stratagem == name && (include == null || include(f))).ToArray();
        var attacks = c.Attacks.Where(a => a.Stratagem == name).ToArray();
        StratagemAttackNode[] AttackNodes(IEnumerable<StratagemAttack> scope) => scope
            .Select(a => new StratagemAttackNode(a, AttackTitle(a, attacks), fields.Where(f => f.Target.Path == "attack" && f.Target.Attack == a.Role).ToArray()))
            .Where(n => n.Fields.Length > 0).ToArray();
        var entities = fields.Where(f => f.Target.Entity != null).Select(f => f.Target.Entity!)
            .Concat(attacks.Where(a => a.Entity != null).Select(a => a.Entity!)).Distinct().Order(StringComparer.Ordinal)
            .Select(entity =>
            {
                var published = root.DeployedEntity?.WeaponBranches ?? [];
                var weapons = published.Concat(fields.Where(f => f.Target.Entity == entity && f.Target.Weapon != null).Select(f => f.Target.Weapon!))
                    .Concat(attacks.Where(a => a.Entity == entity && a.Weapon != null).Select(a => a.Weapon!)).Distinct()
                    .Select(weapon => new StratagemWeaponNode(entity, weapon, Title(weapon),
                        fields.Where(f => f.Target.Path == "weapon" && f.Target.Entity == entity && f.Target.Weapon == weapon)
                            .GroupBy(WeaponGroup).OrderBy(g => WeaponGroupOrder(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal)
                            .Select(g => new StratagemFieldGroup(g.Key, WeaponGroupTitle(g.Key), g.ToArray())).ToArray(),
                        AttackNodes(attacks.Where(a => a.Entity == entity && a.Weapon == weapon))))
                    .Where(w => w.Groups.Length > 0 || w.Attacks.Length > 0).ToArray();
                var stats = fields.Where(f => f.Target.Path == "deployed_entity" && f.Target.Entity == entity).OrderBy(f => EntityStats.Order(f.SemanticFieldId)).ToArray();
                var zones = (root.DeployedEntity!.DamageZones ?? []).Select(z => new StratagemZoneNode(z,
                        fields.Where(f => f.Target.Path == "damage_zone" && f.Target.Entity == entity && f.Target.Zone == z.ZoneId).ToArray()))
                    .Where(z => include == null || z.Fields.Length > 0).ToArray();
                var shieldFields = fields.Where(f => f.Target.Path == "shield" && f.Target.Entity == entity).ToArray();
                var shield = root.DeployedEntity.Shield is { } config && (include == null || shieldFields.Length > 0) ? new StratagemShieldNode(config, shieldFields) : null;
                return new StratagemEntityNode(entity, root.DeployedEntity, stats, weapons, zones, shield);
            }).Where(e => include == null || e.Stats.Length > 0 || e.Weapons.Length > 0 || e.Zones.Length > 0 || e.Shield != null).ToArray();
        return new(root, fields.Where(f => f.Target.Path == "stratagem").ToArray(), fields.Where(f => f.Target.Path == "eagle_rearm").ToArray(),
            entities, AttackNodes(attacks.Where(a => a.Entity == null)), root.DeployedEntity?.BlockedFields ?? []);
    }
    public static string Title(string identity) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(identity.Replace('_', ' '));
    public static string WeaponGroup(StratagemField f) => f.SemanticFieldId == "weapon.capacity" || f.Domain is "magazine" or "rounds" ? "ammo" : f.Domain;
    private static int WeaponGroupOrder(string key) => key switch { "ammo" => 0, "weapon" => 1, "heat" => 2, "heatsink" => 3, _ => 4 };
    private static string WeaponGroupTitle(string key) => key switch { "ammo" => "Ammo", "weapon" => "Weapon", "heat" => "Heat", "heatsink" => "Heatsinks", _ => Title(key) };
    // Labels come from the published settings kind and path; the parent role is used only when it names a published attack.
    public static string AttackTitle(StratagemAttack a, IReadOnlyCollection<StratagemAttack> siblings)
    {
        var parent = siblings.FirstOrDefault(s => s.Role == a.ParentRole);
        var slot = a.Path.Split('/').LastOrDefault(p => p.StartsWith("status:", StringComparison.Ordinal))?["status:".Length..];
        return a.Kind switch
        {
            "ProjectileSettings" => "Projectile",
            "ExplosionSettings" => a.Path.EndsWith("/expiry", StringComparison.Ordinal) ? "Explosion (expiry)" : a.Path.EndsWith("/impact", StringComparison.Ordinal) ? "Explosion (impact)" : "Explosion",
            "DamageInfo" => parent?.Kind == "ExplosionSettings" ? "Explosion Damage" : "Damage",
            "StatusEffectSettings" => slot == null ? "Status" : "Status · slot " + slot,
            "BeamSettings" or "Beam" => "Beam",
            "ArcSettings" => "Arc",
            _ => a.Kind,
        };
    }
}

// Reusable semantic presentation for spawned-entity statistics (deployed entities today; vehicles and other spawned entities later).
public static class EntityStats
{
    public static int Order(string semanticFieldId) => semanticFieldId switch { "entity.health" => 0, "entity.armor" => 1, _ => 2 };
    public static string? Label(string semanticFieldId) => semanticFieldId switch
    {
        "entity.health" => "Health",
        "entity.armor" => "Armor",
        _ => null,
    };
    public static string KindLabel(StratagemEntityIdentity identity) => identity.Kind == "DeployableSystem" ? "Deployment system" : identity.Kind;
}

// Stratagem browser filters. Families come from metadata; known families use the editor's tab labels and order.
public static class StratagemBrowser
{
    private static readonly string[] KnownOrder = ["orbital", "eagle", "support", "sentry", "emplacement", "mine"];
    public static string FamilyLabel(string family) => family switch
    {
        "orbital" => "Orbital", "eagle" => "Eagle", "support" => "Support Call-Ins", "sentry" => "Sentries",
        "emplacement" => "Emplacements", "mine" => "Mines / Deployables", _ => StratagemGraph.Title(family),
    };
    public static IReadOnlyList<(string Family, string Label, int Count)> Tabs(StratagemCatalog c) =>
        [("", "All", c.Stratagems.Length), ..c.Stratagems.GroupBy(s => s.Family)
            .OrderBy(g => Array.IndexOf(KnownOrder, g.Key) is var i && i < 0 ? int.MaxValue : i).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, FamilyLabel(g.Key), g.Count()))];
    public static bool Matches(StratagemCatalog c, StratagemDefinition s, string family = "", string search = "", string system = "", string availability = "")
    {
        var fields = c.FieldInstances.Where(f => f.Target.Stratagem == s.Name).ToArray();
        return s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && (family == "" || s.Family == family)
            && (system == "" || fields.Any(f => f.Domain == system || system == "cooldown" && f.SemanticFieldId == "stratagem.cooldown")
                || system == "beam" && c.Attacks.Any(a => a.Stratagem == s.Name && a.Kind is "Beam" or "BeamSettings"))
            && (availability == "" || availability == "writable" && fields.Any(f => f.Editable)
                || availability == "blocked" && (s.RootResolution != "UNIQUE" || fields.Any(f => !f.Editable) || s.DeployedEntity?.BlockedFields.Length > 0)
                || availability == "shared" && fields.Any(f => f.Shared));
    }
}
