using System.Text.Json;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.22.1+ structural support weapon ↔ call-in linkage (hd2runtime.support_callin_linkage.v1).
// Joins use published semantic IDs and relationship IDs only; display names are never link evidence.
public sealed record SupportCompanionDelivery(string Kind);
public sealed record SupportDeliveryNode(string Node, string? Parent, string View, string? SemanticId, string? TargetPath, string? AttackRole,
    string? Object, string[]? Fields, string? CatalogBranch = null, string? State = null);
public sealed record SupportDeliveryGraph(string Composition, bool DuplicatesFields, SupportDeliveryNode[] Nodes);
public sealed record SupportCallInRelationship(string RelationshipId, string Kind, string Relationship, string Stratagem, string StratagemName,
    string SupportWeapon, string SupportWeaponName, string DeliveryObject, SupportCompanionDelivery[] CompanionDeliveries, string? Special,
    string WeaponIdentityStatus, SupportDeliveryGraph DeliveryGraph, string Confidence);
public sealed record SupportCallInJoinContract(string SupportWeaponKeyProperty, string StratagemKeyProperty, string ForwardLinkProperty,
    string ReverseLinkProperty, string RelationshipCollection, string MergeKey, bool DisplayNameMatchingRequired,
    bool RawNativeIdentifiersPublished, bool WriteTargetsRemainSeparate, string WriteGuardsUnchanged);
public sealed record SupportCallInAudit(int SupportWeapons, int KnownLinks, int UnresolvedLinks, string[] UnresolvedCallIns,
    string[] UnresolvedDeliveries, int SupportStratagems, int ReverseLinksKnown, int Relationships, int BidirectionalMismatches,
    int LinksRelyingOnDisplayNameOnly, int AmbiguousWeaponIdentityLinks, string[] AmbiguousWeaponIdentityNames, string[] SpecialLinks);
public sealed record SupportCallInLinkage(string Contract, int SchemaVersion, SupportCallInJoinContract JoinContract,
    SupportCallInRelationship[] Relationships, SupportCallInAudit Audit);
// Reverse link published on each stratagem root.
public sealed record StratagemDelivers(string Kind, bool Known, string State, string? SemanticId, string? RelationshipId,
    string? SupportWeaponName, string? DeliveryObject, SupportCompanionDelivery[] CompanionDeliveries, string? Special,
    string? Confidence, string? Blocker);

// A validated, bidirectionally consistent link. Write targets stay separate: the stratagem keeps hd2.stratagem(...)
// targets and the weapon keeps hd2.support_weapon(...) targets with their own operation groups and guards.
public sealed record SupportCallInLink(SupportCallInRelationship Relationship, StratagemDefinition Stratagem, SupportAuthoringWeapon Weapon);
public sealed class SupportCallInIndex
{
    public required SupportCallInLinkage Linkage { get; init; }
    public required IReadOnlyDictionary<string, SupportCallInLink> ByStratagem { get; init; }
    public required IReadOnlyDictionary<string, SupportCallInLink> ByWeapon { get; init; }
    public SupportCallInLink? ForStratagem(string name) => ByStratagem.GetValueOrDefault(name);
    public SupportCallInLink? ForWeapon(string name) => ByWeapon.GetValueOrDefault(name);
}

public static class SupportCallInLinker
{
    public const string Contract = "hd2runtime.support_callin_linkage.v1";
    // Returns null when neither catalog publishes linkage (SDK 0.22.0 and older). Any inconsistency fails closed.
    public static SupportCallInIndex? Link(StratagemCatalog? stratagems, SupportAuthoringCatalog? support)
    {
        if (stratagems?.SupportCallInLinks == null && support?.SupportCallInLinks == null) return null;
        void Check(bool valid) { if (!valid) throw new InvalidDataException("Inconsistent support call-in linkage metadata."); }
        Check(stratagems?.SupportCallInLinks != null && support?.SupportCallInLinks != null);
        var linkage = stratagems!.SupportCallInLinks!;
        if (linkage.Contract != Contract || linkage.SchemaVersion != 1) throw new UnsupportedSdkException("Unsupported support call-in linkage contract.");
        Check(JsonSerializer.Serialize(linkage) == JsonSerializer.Serialize(support!.SupportCallInLinks));
        var j = linkage.JoinContract;
        Check(j is { SupportWeaponKeyProperty: "weapons[].semanticId", StratagemKeyProperty: "stratagems[].semanticId",
            ForwardLinkProperty: "weapons[].linkedStratagem", ReverseLinkProperty: "stratagems[].delivers",
            RelationshipCollection: "supportCallInLinks.relationships", MergeKey: "relationshipId",
            DisplayNameMatchingRequired: false, RawNativeIdentifiersPublished: false, WriteTargetsRemainSeparate: true });

        var roots = stratagems.Stratagems.Where(s => s.Family == "support").ToArray();
        Check(roots.All(s => s.SemanticId != null && s.Delivers != null) && support.Weapons.All(w => w.SemanticId != null && w.LinkedStratagem != null));
        Check(stratagems.Stratagems.Where(s => s.SemanticId != null).Select(s => s.SemanticId).Distinct().Count() == stratagems.Stratagems.Count(s => s.SemanticId != null));
        Check(support.Weapons.Select(w => w.SemanticId).Distinct().Count() == support.Weapons.Length);
        var rootById = roots.ToDictionary(s => s.SemanticId!, StringComparer.Ordinal);
        var weaponById = support.Weapons.ToDictionary(w => w.SemanticId!, StringComparer.Ordinal);
        Check(linkage.Relationships.Select(r => r.RelationshipId).Distinct().Count() == linkage.Relationships.Length
            && linkage.Relationships.Select(r => r.Stratagem).Distinct().Count() == linkage.Relationships.Length
            && linkage.Relationships.Select(r => r.SupportWeapon).Distinct().Count() == linkage.Relationships.Length);

        var byStratagem = new Dictionary<string, SupportCallInLink>(StringComparer.Ordinal);
        var byWeapon = new Dictionary<string, SupportCallInLink>(StringComparer.Ordinal);
        foreach (var r in linkage.Relationships)
        {
            var root = rootById.GetValueOrDefault(r.Stratagem); var weapon = weaponById.GetValueOrDefault(r.SupportWeapon);
            Check(root != null && weapon != null);
            var forward = weapon!.LinkedStratagem!; var reverse = root!.Delivers!;
            // Both directions must name exactly this relationship and each other's semantic identity.
            Check(forward is { Known: true, State: "linked" } && forward.SemanticId == r.Stratagem && forward.RelationshipId == r.RelationshipId
                && reverse is { Known: true, State: "linked", Kind: "support_weapon" } && reverse.SemanticId == r.SupportWeapon && reverse.RelationshipId == r.RelationshipId
                && r.WeaponIdentityStatus == weapon.IdentityStatus && r.DeliveryGraph is { Composition: "reference", DuplicatesFields: false });
            var stratagemFields = stratagems.FieldInstances.Where(f => f.Target.Stratagem == root.Name).Select(f => f.SemanticFieldId).ToHashSet(StringComparer.Ordinal);
            var nodes = r.DeliveryGraph.Nodes.Select(n => n.Node).ToHashSet(StringComparer.Ordinal);
            Check(nodes.Count == r.DeliveryGraph.Nodes.Length && r.DeliveryGraph.Nodes.Count(n => n.Parent == null) == 1);
            foreach (var n in r.DeliveryGraph.Nodes)
                Check((n.Parent == null || nodes.Contains(n.Parent)) && n.View switch
                {
                    "stratagem" => n.SemanticId == r.Stratagem && (n.Fields ?? []).All(stratagemFields.Contains),
                    "support_weapon" => n.SemanticId == r.SupportWeapon && n.Fields == null,
                    _ => false,
                });
            var link = new SupportCallInLink(r, root, weapon);
            byStratagem.Add(root.Name, link); byWeapon.Add(weapon.Name, link);
        }
        // No one-sided links: every other entry must be explicitly unknown, without an identity, and carry Runtime's blocker.
        Check(support.Weapons.Where(w => !byWeapon.ContainsKey(w.Name)).All(w => w.LinkedStratagem is { Known: false, SemanticId: null, RelationshipId: null } l && !string.IsNullOrWhiteSpace(l.Blocker)));
        Check(roots.Where(s => !byStratagem.ContainsKey(s.Name)).All(s => s.Delivers is { Known: false, SemanticId: null, RelationshipId: null } d && !string.IsNullOrWhiteSpace(d.Blocker)));
        // Vehicle and backpack call-in links are validated by the entity authoring reader.
        Check(stratagems.Stratagems.Where(s => s.Family != "support").All(s => s.Delivers == null || !s.Delivers.Known || s.Delivers.Kind == s.Family && s.Family is "vehicle" or "backpack"));
        var a = linkage.Audit;
        Check(a.SupportWeapons == support.Weapons.Length && a.SupportStratagems == roots.Length && a.KnownLinks == byWeapon.Count
            && a.ReverseLinksKnown == byStratagem.Count && a.Relationships == linkage.Relationships.Length
            && a.UnresolvedLinks == support.Weapons.Length - byWeapon.Count && a.BidirectionalMismatches == 0 && a.LinksRelyingOnDisplayNameOnly == 0
            && a.AmbiguousWeaponIdentityLinks == byWeapon.Values.Count(l => !SupportAuthoringWeapon.Resolved(l.Weapon.IdentityStatus)));
        return new() { Linkage = linkage, ByStratagem = byStratagem, ByWeapon = byWeapon };
    }
}
