using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// HD2Runtime 0.30.0 muzzle, optics and underbarrel stat modifiers (hd2runtime.weapon_attachment.modifier.v1,
// WeaponAttachmentModifierCapabilities.json, sdk/docs/weapon-attachments.md). Each definition carries up to eight
// {WeaponStatModifierType, value} pairs; the pairs it already carries are writable fields of that shared definition, edited through
// hd2.weapon_attachment(<semantic id>) like magazine attachments (same target resource, path = slot). Adding a pair, the read-only
// patches and which attachment a weapon has equipped are not writable.
public sealed record AttachmentModifier(string Modifier, int Index, double Value, string? Field, bool Writable, string? Blocker);
// Another member the definition's delta patches (a resource reference or an unproven member): informational only.
public sealed record AttachmentPatch(string Component, int Offset, int Size, string? Label, bool Writable, string Status, string? Blocker);
public sealed record AttachmentModifierConsumers(string[] NativeDefaultOf, string[] UnlockListedFor, bool ScopeComplete, string ScopeNote);
public sealed record ModifierAttachment(string SemanticId, string Name, bool NameUnique, string Slot, string NativeSlot, bool HasEntityDelta,
    AttachmentModifier[] Modifiers, string[] FieldInstanceKeys, AttachmentPatch[] ReadOnlyPatches, string[] CompatibleWeapons, AttachmentModifierConsumers Consumers)
{
    [JsonIgnore] public IEnumerable<AttachmentModifier> Blocked => Modifiers.Where(m => !m.Writable);
}
// One option of a weapon's slot: the native resource default (listed first) or a definition its unlock list names.
public sealed record AttachmentSlotOption(string Attachment, string Name, string Relationship)
{
    public const string NativeDefault = "native_resource_default", UnlockListed = "unlock_listed";
    [JsonIgnore] public bool Default => Relationship == NativeDefault;
}
public sealed record AttachmentWeaponSlot(string Slot, string? DefaultAttachment, AttachmentSlotOption[] Options);
public sealed record AttachmentModifierWeapon(string Weapon, AttachmentWeaponSlot[] Slots);
public sealed record AttachmentModifierSlot(string NativeSlot);
public sealed record AttachmentModifierOwnership(string[] Chain, string WriteScope, string[] DistinctFrom, string Adding);
public sealed record AttachmentModifierSummary(int Attachments, Dictionary<string, int> BySlot, int AttachmentsWithWritableModifiers,
    Dictionary<string, int> AttachmentsWithWritableModifiersBySlot, int FieldInstances, int WritableFieldInstances, Dictionary<string, int> FieldInstancesByField,
    Dictionary<string, int> FieldInstancesBySlot, int BlockedModifiers, int ReadOnlyPatches, int Weapons, bool SelectionWritable, int ResearchWrites,
    int ProtectionChanges, string FixtureFallback);
public sealed record AttachmentModifierCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string CanonicalCollection,
    Dictionary<string, AttachmentModifierSlot> Slots, AttachmentModifierOwnership OwnershipModel, Dictionary<string, string> Acknowledgements,
    MagazineSelection Selection, string ReadOnlyPatches, ModifierAttachment[] Attachments, AttachmentModifierWeapon[] Weapons, EntityField[] FieldInstances,
    AttachmentModifierSummary Summary)
{
    public ModifierAttachment? Attachment(string semanticId) => Attachments.FirstOrDefault(a => a.SemanticId == semanticId);
    public AttachmentModifierWeapon? Weapon(string name) => Weapons.FirstOrDefault(w => w.Weapon == name);
    // The writable modifiers of a definition in the order it carries them (pair index).
    public IEnumerable<EntityField> Fields(ModifierAttachment a) => a.Modifiers.Where(m => m.Writable)
        .Select(m => FieldInstances.Single(f => f.Target.Attachment == a.SemanticId && f.SemanticFieldId == m.Field));
    // A published display name for a modifier field id (a blocked modifier has no field instance of its own).
    public string DisplayName(string field) => FieldInstances.FirstOrDefault(f => f.SemanticFieldId == field)?.DisplayName ?? field;
}

public static class AttachmentModifierReader
{
    public const string FileName = PlayerArmorFiles.AttachmentModifiers, Contract = "hd2runtime.weapon_attachment.modifier.v1";
    public const string Resource = "weapon_attachment", Ergonomics = "attachment.ergonomics_modifier";
    public static readonly string[] Slots = ["muzzle", "optics", "underbarrel"];
    // WeaponStatModifierType -> published field. Add_Ergonomics keeps its magazine field id in every slot; Runtime has no alias for it.
    public static readonly IReadOnlyDictionary<string, string> Modifiers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Add_Ergonomics"] = Ergonomics, ["Mul_Sway"] = "attachment.modifier.sway",
        ["Mul_RecoilHorizontal"] = "attachment.modifier.recoil_horizontal", ["Mul_RecoilVertical"] = "attachment.modifier.recoil_vertical",
        ["Mul_ClimbHorizontal"] = "attachment.modifier.climb_horizontal", ["Mul_ClimbVertical"] = "attachment.modifier.climb_vertical",
        ["Mul_SpreadHorizontal"] = "attachment.modifier.spread_horizontal", ["Mul_SpreadVertical"] = "attachment.modifier.spread_vertical",
    };
    public const int PairsPerDefinition = 8;
    private static readonly Regex SemanticId = new(@"\Aweapon-attachment/v1/(muzzle|optics|underbarrel)/[a-z0-9-]{1,96}/[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    // A saved edit of a muzzle, optics or underbarrel definition (Entity is the semantic ID of that slot); magazines keep path "magazine".
    public static bool ValidChange(string path, string entity) => Slots.Contains(path) && SemanticId.IsMatch(entity) && entity.StartsWith("weapon-attachment/v1/" + path + "/", StringComparison.Ordinal);
    public static bool IsModifierPath(string resource, string path) => resource == Resource && Slots.Contains(path);
    // Lua field constant: hd2.fields.attachment.ergonomics_modifier, hd2.fields.attachment.modifier_sway (the stub's dot-free member names).
    public static string ApiConstant(string field) => "hd2.fields.attachment." + field["attachment.".Length..].Replace('.', '_');
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    { if (!valid) throw new InvalidDataException($"Inconsistent weapon attachment modifier capability metadata (check {line})."); }

    /// <param name="magazines">The magazine attachment catalog of the same SDK: no instance key, backing object, operation group, plan group or
    /// shared scope may be shared with it (both target hd2.weapon_attachment, and ergonomics has the same field id in both).</param>
    public static AttachmentModifierCatalog Read(byte[] bytes, string version, PlayerWeaponCatalog weapons, MagazineAttachmentCatalog? magazines)
    {
        try
        {
            if (bytes.Length > PlayerArmorFiles.MaxBytes(FileName)) throw new InvalidDataException("Weapon attachment modifier capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported weapon attachment modifier authoring contract.");
            // The fields are new in the HD2Runtime 0.30 line (scripts/validate_examples.py UNRELEASED_FIELDS); no older SDK publishes them.
            Check(Models.SemVersion.Parse(version).CompareTo(Models.SemVersion.Parse("0.30.0-dev")) >= 0);
            var safety = root.GetProperty("safety");
            Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && !safety.GetProperty("rawResourceIdentifiers").GetBoolean()
                && safety.GetProperty("writesDuringGeneration").GetInt32() == 0 && safety.GetProperty("protectionChangesDuringGeneration").GetInt32() == 0
                && safety.GetProperty("fixtureFallback").GetString() == "disabled");
            var c = JsonSerializer.Deserialize<AttachmentModifierCatalog>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && c.CanonicalCollection == "fieldInstances" && c.Slots.Keys.Order(StringComparer.Ordinal).SequenceEqual(Slots)
                && c.Slots.Values.All(s => !string.IsNullOrWhiteSpace(s.NativeSlot)) && c.Acknowledgements.Keys.Order(StringComparer.Ordinal).SequenceEqual(["allow_shared", "allow_unverified_effect"])
                && c.Acknowledgements.Values.All(v => !string.IsNullOrWhiteSpace(v)) && !c.Selection.Writable && !string.IsNullOrWhiteSpace(c.Selection.Reason)
                && !string.IsNullOrWhiteSpace(c.OwnershipModel.WriteScope) && !string.IsNullOrWhiteSpace(c.ReadOnlyPatches));
            var attachments = c.Attachments.ToDictionary(a => a.SemanticId, StringComparer.Ordinal);
            Check(attachments.Count == c.Attachments.Length && c.FieldInstances.Length <= 4000 && c.FieldInstances.Select(f => f.InstanceKey).Distinct(StringComparer.Ordinal).Count() == c.FieldInstances.Length);
            // The published sharing of each field (sharedConsumers: [{weapon}]) is checked against its definition's compatible weapons.
            var consumers = root.GetProperty("fieldInstances").EnumerateArray().ToDictionary(f => f.GetProperty("instanceKey").GetString()!,
                f => f.GetProperty("sharedConsumers").EnumerateArray().Select(x => x.GetProperty("weapon").GetString()!).ToArray(), StringComparer.Ordinal);
            foreach (var f in c.FieldInstances)
            {
                Check(attachments.TryGetValue(f.Target.Attachment ?? "", out var a));
                var range = f.SemanticFieldId == Ergonomics ? (-100.0, 100.0) : (0.0, 10.0);
                // One field per carried, writable modifier; the key is the definition's semantic ID without its prefix, then the field.
                Check(f.InstanceKey == "attachment:" + a!.SemanticId["weapon-attachment/v1/".Length..] + ":" + f.SemanticFieldId && f.InstanceKey.Length <= 512
                    && f.Target == new EntityTarget(Resource, a.Slot, Attachment: a.SemanticId)
                    && Modifiers.Values.Contains(f.SemanticFieldId) && a.Modifiers.Count(m => m.Writable && m.Field == f.SemanticFieldId) == 1
                    && f.Type == "number" && f.Unit == (f.SemanticFieldId == Ergonomics ? "ergonomics" : "multiplier") && !string.IsNullOrWhiteSpace(f.DisplayName)
                    && f.Min == range.Item1 && f.Max == range.Item2 && f.Range == null && f.AllowedValues == null
                    && f.Editable && f.Reason == null && f.CurrentDefault.ValueKind == JsonValueKind.Number
                    && (float)f.CurrentDefault.GetDouble() == (float)a.Modifiers.Single(m => m.Field == f.SemanticFieldId).Value
                    && f.CurrentDefault.GetDouble() >= range.Item1 && f.CurrentDefault.GetDouble() <= range.Item2
                    // A definition is shared by every weapon that equips it, and the set is not proven complete: both opt-ins are always required.
                    && f.Shared && f.AllowSharedRequired && !f.ReviewedScopeComplete && f.DynamicConsumersPossible && f.Acknowledgement == "allow_unverified_effect"
                    && consumers[f.InstanceKey].Order(StringComparer.Ordinal).SequenceEqual(a.CompatibleWeapons.Order(StringComparer.Ordinal))
                    && f.ApiFieldConstant == ApiConstant(f.SemanticFieldId) && f.Domain == "attachment" && f.PlanPhase == 1 && f.DependsOn.Length == 0
                    && f.Requires == "patch_or_transaction" && MagazineAttachmentReader.Tiers.Contains(f.Evidence.Tier) && !string.IsNullOrWhiteSpace(f.Provenance)
                    && f.LiveProvenValues == null && f.SharedRow == null && f.Order == null
                    && !string.IsNullOrWhiteSpace(f.BackingObjectId) && !string.IsNullOrWhiteSpace(f.OperationGroup) && !string.IsNullOrWhiteSpace(f.PlanGroup)
                    && !string.IsNullOrWhiteSpace(f.SharedScopeKey));
                _ = Generation.EntityScalar.Normalize(f, f.CurrentDefault);
            }
            // One definition = one backing object, one operation group (one transaction of at most eight pairs), one plan group and one shared scope.
            foreach (var g in c.FieldInstances.GroupBy(f => f.Target.Attachment))
                Check(g.Select(f => f.BackingObjectId).Distinct().Count() == 1 && g.Select(f => f.OperationGroup).Distinct().Count() == 1
                    && g.Select(f => f.SharedScopeKey).Distinct().Count() == 1 && g.Select(f => f.PlanGroup).Distinct().Count() == 1 && g.Count() <= PairsPerDefinition);
            Check(c.FieldInstances.GroupBy(f => f.OperationGroup).All(g => g.Select(f => f.Target.Attachment).Distinct().Count() == 1)
                && c.FieldInstances.GroupBy(f => f.BackingObjectId).All(g => g.Select(f => f.Target.Attachment).Distinct().Count() == 1)
                && c.FieldInstances.GroupBy(f => f.PlanGroup).All(g => g.Select(f => f.Target.Attachment).Distinct().Count() == 1));
            foreach (var a in c.Attachments)
            {
                var path = a.SemanticId.Split('/');
                Check(SemanticId.IsMatch(a.SemanticId) && a.Slot == path[2] && c.Slots.TryGetValue(a.Slot, out var slot) && a.NativeSlot == slot!.NativeSlot
                    && !string.IsNullOrWhiteSpace(a.Name) && a.Name.Length <= 256 && !a.Name.Any(char.IsControl)
                    && a.Modifiers.Length <= PairsPerDefinition && a.Modifiers.Select(m => m.Index).Order().SequenceEqual(Enumerable.Range(0, a.Modifiers.Length))
                    && a.Modifiers.Select(m => m.Modifier).Distinct(StringComparer.Ordinal).Count() == a.Modifiers.Length
                    && a.Modifiers.All(m => Modifiers.TryGetValue(m.Modifier, out var field) && m.Field == field && double.IsFinite(m.Value)
                        && (m.Writable ? m.Blocker == null : !string.IsNullOrWhiteSpace(m.Blocker)))
                    && a.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(c.FieldInstances.Where(f => f.Target.Attachment == a.SemanticId).Select(f => f.InstanceKey).Order(StringComparer.Ordinal))
                    && a.FieldInstanceKeys.Length == a.Modifiers.Count(m => m.Writable)
                    && a.ReadOnlyPatches.All(p => !p.Writable && p.Status is "resource_reference" or "unproven" && !string.IsNullOrWhiteSpace(p.Blocker) && !string.IsNullOrWhiteSpace(p.Component)
                        && p.Offset >= 0 && p.Size > 0)
                    && !a.Consumers.ScopeComplete && !string.IsNullOrWhiteSpace(a.Consumers.ScopeNote)
                    && a.CompatibleWeapons.Distinct(StringComparer.Ordinal).Count() == a.CompatibleWeapons.Length
                    && a.CompatibleWeapons.Order(StringComparer.Ordinal).SequenceEqual(a.Consumers.NativeDefaultOf.Union(a.Consumers.UnlockListedFor, StringComparer.Ordinal).Order(StringComparer.Ordinal)));
            }
            Check(c.Attachments.Where(a => a.NameUnique).Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() == c.Attachments.Count(a => a.NameUnique));
            Check(c.Weapons.Select(w => w.Weapon).Distinct(StringComparer.Ordinal).Count() == c.Weapons.Length);
            foreach (var w in c.Weapons)
            {
                // Weapon identities come from the player-weapon catalog; options name published definitions of the same slot.
                Check(weapons.Weapons.Any(p => p.Name == w.Weapon) && w.Slots.Select(s => s.Slot).Distinct(StringComparer.Ordinal).Count() == w.Slots.Length);
                foreach (var s in w.Slots)
                {
                    Check(Slots.Contains(s.Slot) && s.Options.Select(o => o.Attachment).Distinct(StringComparer.Ordinal).Count() == s.Options.Length
                        // Native defaults come first. defaultAttachment names the one native default; with two (the LAS-5 Scythe's muzzle) it is null.
                        && s.Options.SkipWhile(o => o.Default).All(o => !o.Default)
                        && (s.DefaultAttachment == null ? s.Options.Count(o => o.Default) != 1
                            : s.Options.Length > 0 && s.Options[0].Attachment == s.DefaultAttachment && s.Options.Count(o => o.Default) == 1));
                    foreach (var o in s.Options)
                        Check(attachments.TryGetValue(o.Attachment, out var a) && a!.Slot == s.Slot && a.Name == o.Name
                            && (o.Default ? a.Consumers.NativeDefaultOf.Contains(w.Weapon) : o.Relationship == AttachmentSlotOption.UnlockListed && a.Consumers.UnlockListedFor.Contains(w.Weapon)));
                }
            }
            // Every compatible weapon lists the definition in that slot.
            Check(c.Attachments.All(a => a.CompatibleWeapons.All(n => c.Weapon(n)?.Slots.Any(s => s.Slot == a.Slot && s.Options.Any(o => o.Attachment == a.SemanticId)) == true)));
            // The magazine catalog is the other half of hd2.weapon_attachment: nothing either catalog edits may be the other's.
            if (magazines != null)
            {
                var magazineFields = magazines.FieldInstances;
                Check(!c.Attachments.Any(a => magazines.Attachment(a.SemanticId) != null)
                    && !c.FieldInstances.Select(f => f.InstanceKey).Intersect(magazineFields.Select(f => f.InstanceKey), StringComparer.Ordinal).Any()
                    && !c.FieldInstances.Select(f => f.BackingObjectId).Intersect(magazineFields.Select(f => f.BackingObjectId), StringComparer.Ordinal).Any()
                    && !c.FieldInstances.Select(f => f.OperationGroup).Intersect(magazineFields.Select(f => f.OperationGroup), StringComparer.Ordinal).Any()
                    && !c.FieldInstances.Select(f => f.PlanGroup).Intersect(magazineFields.Select(f => f.PlanGroup), StringComparer.Ordinal).Any()
                    && !c.FieldInstances.Select(f => f.SharedScopeKey).Intersect(magazineFields.Select(f => f.SharedScopeKey), StringComparer.Ordinal).Any());
            }
            var sum = c.Summary; var withFields = c.Attachments.Where(a => a.FieldInstanceKeys.Length > 0).ToArray();
            Check(sum.Attachments == c.Attachments.Length && Same(sum.BySlot, Count(c.Attachments, a => a.Slot))
                && sum.AttachmentsWithWritableModifiers == withFields.Length && Same(sum.AttachmentsWithWritableModifiersBySlot, Count(withFields, a => a.Slot))
                && sum.FieldInstances == c.FieldInstances.Length && sum.WritableFieldInstances == c.FieldInstances.Count(f => f.Editable)
                && Same(sum.FieldInstancesByField, Count(c.FieldInstances, f => f.SemanticFieldId)) && Same(sum.FieldInstancesBySlot, Count(c.FieldInstances, f => f.Target.Path))
                && sum.BlockedModifiers == c.Attachments.Sum(a => a.Blocked.Count()) && sum.ReadOnlyPatches == c.Attachments.Sum(a => a.ReadOnlyPatches.Length)
                && sum.Weapons == c.Weapons.Length && sum.SelectionWritable == c.Selection.Writable && sum.ResearchWrites == 0 && sum.ProtectionChanges == 0
                && sum.FixtureFallback == "disabled");
            return c;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed weapon attachment modifier capability metadata.", e); }
    }
    private static Dictionary<string, int> Count<T>(IEnumerable<T> items, Func<T, string> key) => items.GroupBy(key).ToDictionary(g => g.Key, g => g.Count());
    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
}
