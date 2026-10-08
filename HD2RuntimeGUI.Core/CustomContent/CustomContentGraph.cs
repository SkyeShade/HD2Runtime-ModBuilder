using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

public enum CustomNodeKind { Stratagem, Projectile, Image, Weapon, VanillaStratagem, VanillaRound, VanillaExplosion, VanillaProjectile, Model, VanillaItem }

/// <summary>One thing in the project's custom content graph: a custom object (stratagem, projectile, image), an existing weapon attack that
/// fires a custom projectile, or a vanilla catalogue entry something uses.</summary>
public sealed record CustomNode(CustomNodeKind Kind, string Id)
{
    public bool IsCustom => Kind is CustomNodeKind.Stratagem or CustomNodeKind.Projectile or CustomNodeKind.Image or CustomNodeKind.Model;
}
/// <summary>From uses To in a role ("icon", "weaponIcon", "model", "round", "payload", "donor", "item", "pattern", "blast", "asset", "base", "visual",
/// "damage", "impactExplosion", "ballistics", "fires"). Roles are ids; the UI names them.</summary>
public sealed record CustomEdge(CustomNode From, CustomNode To, string Role, Guid? Assignment = null);

/// <summary>What uses what in the project's custom content: custom stratagem -> icon / donors / payloads / custom projectile; custom projectile
/// -> its vanilla base and component donors; weapon attack -> custom projectile. Deletion and id changes consult it, so a referenced custom
/// object is never removed or renamed behind a reference's back.</summary>
public sealed class CustomContentGraph
{
    public IReadOnlyList<CustomEdge> Edges { get; }
    private CustomContentGraph(IReadOnlyList<CustomEdge> edges) => Edges = edges;

    public static CustomContentGraph Build(CustomContentSection? content)
    {
        var edges = new List<CustomEdge>();
        if (content == null) return new(edges);
        foreach (var s in content.Stratagems)
        {
            var from = new CustomNode(CustomNodeKind.Stratagem, s.Id);
            void Add(CustomNodeKind kind, string? id, string role) { if (!string.IsNullOrEmpty(id)) edges.Add(new(from, new(kind, id), role)); }
            void Ref(ContentRef? r, string role)
            {
                if (r == null) return;
                Add(r.Kind switch { ContentRef.CustomProjectile => CustomNodeKind.Projectile, ContentRef.VanillaRound => CustomNodeKind.VanillaRound, _ => CustomNodeKind.VanillaExplosion }, r.Id, role);
            }
            Add(CustomNodeKind.Image, s.Icon, "icon");
            var d = s.Delivery;
            void Weapon(WeaponOverrides? w) { Ref(w?.Projectile, "round"); Ref(w?.ImpactPayload, "payload"); }
            void Pod(IEnumerable<PodItem>? items) { foreach (var i in items ?? []) { if (i.Item != PodItem.Clone) Add(CustomNodeKind.VanillaItem, i.Item, "item"); Weapon(i.Weapon); } }
            void Presentation(DeliveredPresentation? p) { if (p is { IconMode: DeliveredPresentation.Custom }) Add(CustomNodeKind.Image, p.Icon, "weaponIcon"); }
            switch (d.Family)
            {
                case DeliveryFamily.Support:
                    Add(CustomNodeKind.VanillaStratagem, d.Support?.Item, "donor"); Weapon(d.Support?.Weapon);
                    break;
                case DeliveryFamily.Sentry:
                    Add(CustomNodeKind.VanillaStratagem, d.Sentry?.Chassis, "donor"); Ref(d.Sentry?.Weapon?.Projectile, "round");
                    break;
                case DeliveryFamily.Eagle:
                    Add(CustomNodeKind.VanillaStratagem, d.Eagle?.Strike, "donor"); Ref(d.Eagle?.ImpactPayload, "payload");
                    break;
                case DeliveryFamily.Orbital:
                    Add(CustomNodeKind.VanillaStratagem, d.Orbital?.Shell, "donor"); Add(CustomNodeKind.VanillaStratagem, d.Orbital?.Pattern, "pattern");
                    Ref(d.Orbital?.ImpactPayload, "payload");
                    break;
                case DeliveryFamily.Pod:
                    Pod(d.Pod?.Items);
                    break;
                case DeliveryFamily.Expendable:
                    Add(CustomNodeKind.VanillaStratagem, d.Expendable?.Weapon, "donor"); Add(CustomNodeKind.VanillaRound, d.Expendable?.Round, "round");
                    Weapon(d.Expendable?.Modify); Pod(d.Expendable?.Pod); Presentation(d.Expendable?.Presentation);
                    break;
                case DeliveryFamily.Variant:
                    Add(CustomNodeKind.VanillaStratagem, d.Variant?.Weapon, "donor"); Add(CustomNodeKind.VanillaProjectile, d.Variant?.Round, "round");
                    Presentation(d.Variant?.Presentation); Add(CustomNodeKind.Model, d.Variant?.Model, "model");
                    break;
                case DeliveryFamily.Pelican:
                    Add(CustomNodeKind.VanillaExplosion, d.Pelican?.Gun?.ImpactExplosion, "payload");
                    break;
                case DeliveryFamily.Silo:
                    Add(CustomNodeKind.VanillaStratagem, d.Silo?.Donor, "donor"); Add(CustomNodeKind.VanillaExplosion, d.Silo?.Blast, "blast"); Add(CustomNodeKind.VanillaExplosion, d.Silo?.Fallback, "blast");
                    break;
                case DeliveryFamily.Scripted:
                    Add(CustomNodeKind.VanillaStratagem, d.Scripted?.VanillaPod, "donor");
                    break;
            }
            foreach (var asset in s.Assets) Add(CustomNodeKind.VanillaStratagem, asset, "asset");
        }
        foreach (var p in content.Projectiles)
        {
            var from = new CustomNode(CustomNodeKind.Projectile, p.Id);
            foreach (var (role, id) in new[] { ("base", p.Base), ("visual", p.Visual), ("damage", p.Damage), ("impactExplosion", p.ImpactExplosion), ("ballistics", p.Ballistics) })
                if (!string.IsNullOrEmpty(id)) edges.Add(new(from, new(CustomNodeKind.VanillaProjectile, id), role));
        }
        foreach (var a in content.Assignments)
            edges.Add(new(new(CustomNodeKind.Weapon, WeaponKey(a)), new(a.Projectile.IsCustom ? CustomNodeKind.Projectile : CustomNodeKind.VanillaProjectile, a.Projectile.Id), "fires", a.Id));
        return new(edges);
    }
    public static string WeaponKey(CustomProjectileAssignment a) => a.Weapon + "#" + a.AttackRole;
    public IReadOnlyList<CustomEdge> UsedBy(CustomNodeKind kind, string id) => Edges.Where(e => e.To.Kind == kind && e.To.Id == id).ToArray();
    public IReadOnlyList<CustomEdge> Uses(CustomNodeKind kind, string id) => Edges.Where(e => e.From.Kind == kind && e.From.Id == id).ToArray();

    /// <summary>The typed references a custom stratagem holds (delivered weapon rounds and impact payloads).</summary>
    public static IEnumerable<ContentRef> ReferencesOf(CustomStratagem s)
    {
        var d = s.Delivery;
        foreach (var w in Weapons(s))
            foreach (var r in new[] { w.Projectile, w.ImpactPayload }) if (r != null) yield return r;
        foreach (var r in new[] { d.Eagle?.ImpactPayload, d.Orbital?.ImpactPayload }) if (r != null) yield return r;
    }
    /// <summary>Every delivered weapon whose own instance the stratagem reconfigures (support item, sentry gun, expendable clone, pod items).</summary>
    public static IEnumerable<WeaponOverrides> Weapons(CustomStratagem s)
    {
        var d = s.Delivery;
        foreach (var w in new[] { d.Support?.Weapon, d.Sentry?.Weapon, d.Expendable?.Modify }) if (w != null) yield return w;
        foreach (var i in (d.Pod?.Items ?? []).Concat(d.Expendable?.Pod ?? [])) if (i.Weapon != null) yield return i.Weapon;
    }
    /// <summary>Replaces every reference to one custom projectile (in stratagem rounds and weapon assignments) by another reference, or removes
    /// them (replacement null). Returns how many references changed.</summary>
    public static int Replace(CustomContentSection content, string projectileId, ContentRef? replacement)
    {
        var old = ContentRef.Custom(projectileId);
        // A delivered weapon's round may become another custom projectile or a vanilla round; anything else clears it.
        var round = replacement is { IsCustom: true } || replacement?.Kind == ContentRef.VanillaRound ? replacement : null;
        var changed = 0;
        foreach (var s in content.Stratagems)
        {
            foreach (var w in Weapons(s).Where(w => w.Projectile == old)) { w.Projectile = round; changed++; }
            if (s.Delivery.Support?.Weapon is { IsEmpty: true }) s.Delivery.Support.Weapon = null;
            if (s.Delivery.Sentry?.Weapon is { IsEmpty: true }) s.Delivery.Sentry.Weapon = null;
            if (s.Delivery.Expendable?.Modify is { IsEmpty: true }) s.Delivery.Expendable.Modify = null;
            foreach (var i in (s.Delivery.Pod?.Items ?? []).Concat(s.Delivery.Expendable?.Pod ?? [])) if (i.Weapon is { IsEmpty: true }) i.Weapon = null;
        }
        foreach (var a in content.Assignments.Where(a => a.Projectile == old).ToArray())
        {
            changed++;
            if (replacement is { IsCustom: true }) a.Projectile = replacement; else content.Assignments.Remove(a);
        }
        return changed;
    }
}
