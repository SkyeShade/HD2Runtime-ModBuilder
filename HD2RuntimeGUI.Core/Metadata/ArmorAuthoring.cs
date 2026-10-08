using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// HD2Runtime 0.30.0 development: armor stats (ArmorStatsCapabilities.json, hd2.armor_stats; sdk/docs/armor-stats.md) and armor passives
// (ArmorPassiveCatalog.json, hd2.passives / hd2.player_passives; sdk/docs/armor-passives.md).
// - A kit's piece weights, a weight class's table values and the damage curve are published field instances: they become EntityFields
//   (resources armor_kit / armor_class / armor_damage_curve) and use the shared entity pipeline (editor, changes, Lua, options, audit).
// - The local player's own armor bonus and stamina factor (hd2.armor_stats.player():set) and the passive override (hd2.player_passives.set)
//   are not field patches but calls a mod makes at load: ModProject.PlayerArmor, written by Generation.ArmorPlayerLua.
public sealed record ArmorSlot(string Name, int Index, bool RatingSlot);
public sealed record ArmorClassDisplay(double Rating, double Speed, double StaminaRegen);
public sealed record ArmorClass(string Name, int Index, double ArmorValue, double SpeedFactor, double StaminaFactor, ArmorClassDisplay Display);
public sealed record ArmorCurvePoint(double ArmorValue, double Damage);
public sealed record ArmorDamageCurve(ArmorCurvePoint[] Points, string Interpolation, string ReadOn);
public sealed record ArmorKitPassive(int Id, string Name);
public sealed record ArmorStats(double Rating, double Speed, double Stamina, double ArmorValue, double SpeedFactor, double StaminaFactor, double DamageMultiplier);
public sealed record ArmorKit(string Id, string HexId, int Index, string? Name, bool NameUnique, ArmorKitPassive Passive, string Class, string[] Slots,
    Dictionary<string, string> Weights, int RatingPieces, ArmorStats Vanilla)
{
    // Unnamed kits (the game's text names 127 of 135) are listed by their id.
    [JsonIgnore] public string Label => Name ?? HexId;
}
public sealed record ArmorBonusRow(string Type, double Value);
public sealed record ArmorPassiveBonus(int Passive, string Name, ArmorBonusRow[] Rows);
public sealed record ArmorTargetSpec(string Accessor, string Scope, string Writes, string? Alias = null);
public sealed record ArmorTransaction(string[] Operations, int TargetsPerRequest, int MinChanges, int MaxChanges, string[] Acknowledgements, string Expect,
    Dictionary<string, int> WeightValues, string[] Refusals, bool Synced, string SyncNote);
public sealed record ArmorPlayerOption(string Type, double? Min = null, double? Max = null, string? Effect = null, string? Lasts = null);
public sealed record ArmorPlayerSpec(string Accessor, Dictionary<string, ArmorPlayerOption> Options, string[] AtLeastOne, string[] Acknowledgements,
    bool AllowSharedRequired, bool SoloOnly, bool Raises, string[] Statuses, string[] RefusalCodes, string ExpectedState, bool LiveTested);
public sealed record ArmorStatsSummary(int Kits, int NamedKits, int UniquelyNamedKits, int Classes, int CurvePoints, int FieldInstances, int KitFieldInstances,
    int ClassFieldInstances, int CurveFieldInstances, int PassiveArmorBonuses);
public sealed record ArmorSession(string Session, string Date, string Status, string Summary);
public sealed record ArmorEvidence(string Status, bool LiveTested, string LiveTest, ArmorSession[] Sessions, string Validation);
public sealed record ArmorRawTarget(string Resource, string Path,
    [property: JsonPropertyName("armor_kit")] string? ArmorKit = null, string? Slot = null,
    [property: JsonPropertyName("armor_class")] string? ArmorClass = null, int? ArmorValue = null);
public sealed record ArmorRawField(string SemanticFieldId, string ApiFieldConstant, string DisplayName, ArmorRawTarget Target, string Type, string Unit, string Storage,
    JsonElement CurrentDefault, bool Editable, bool ExecutableData, string Lifecycle, string LifecycleKind, bool Shared, bool AllowSharedRequired, string SharedReason,
    string Acknowledgement, string AcknowledgementReason, string[] Acknowledgements, string OperationGroup, bool LiveTested,
    string[]? AllowedValues = null, double? Min = null, double? Max = null);
public sealed record ArmorFormulas(string ArmorValue, string DamageMultiplier, string SpeedFactor, string StaminaFactor, string DisplayFactors, string ClassLabel);

public sealed record ArmorCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string Build, string Status, ArmorStatsSummary Summary,
    Dictionary<string, ArmorTargetSpec> Targets, ArmorTransaction Transaction, string ExecutableData, ArmorSlot[] Slots, ArmorClass[] Classes,
    ArmorDamageCurve DamageCurve, ArmorFormulas Formulas, ArmorPassiveBonus[] PassiveArmorBonuses, Dictionary<string, double[]> Ranges, ArmorPlayerSpec Player,
    ArmorEvidence Evidence, ArmorKit[] Kits, [property: JsonPropertyName("fieldInstances")] ArmorRawField[] Fields)
{
    // Published fields in the shared entity-authoring form; the passive catalog when the SDK publishes it.
    [JsonIgnore] public EntityField[] FieldInstances { get; init; } = [];
    [JsonIgnore] public ArmorPassiveCatalog? Passives { get; init; }
    public ArmorKit? Kit(string id) => Kits.FirstOrDefault(k => k.Id == id);
    public ArmorClass? Class(string name) => Classes.FirstOrDefault(c => c.Name == name);
    public IEnumerable<EntityField> KitFields(string id) => FieldInstances.Where(f => f.Target.Resource == ArmorAuthoringReader.KitResource && f.Target.Armor == id);
    public IEnumerable<EntityField> ClassFields(string name) => FieldInstances.Where(f => f.Target.Resource == ArmorAuthoringReader.ClassResource && f.Target.Armor == name);
    public IEnumerable<EntityField> CurveFields => FieldInstances.Where(f => f.Target.Resource == ArmorAuthoringReader.CurveResource);
    public double[] Range(string key) => Ranges[key];
}

// ArmorPassiveCatalog.json: the 32 passives, the 411 kits, the hd2.player_passives.set contract and the live-proven armor-slot swaps.
public sealed record ArmorPassiveModifier(string Key, string KeyName, string Type, double Value, string? Text, string Reader, bool? Follows, string? Observable,
    string? Note, bool ArmorSlotOnly);
public sealed record ArmorPassiveStat(int Stat, string Name, double Add, double Mul, string? DescribedBy);
public sealed record ArmorEffectPackage(string CatalogueKey, string Contents, string[] ArmorSlotOnly, string Loads);
public sealed record ArmorPassive(int Id, string Name, string Description, string Icon, string Summary, ArmorPassiveModifier[] Modifiers, ArmorPassiveStat[] Stats,
    string FollowsSwap, string? Note, ArmorEffectPackage? EffectPackage, int ArmorKits, bool ArmorSlotOnly, string[] ArmorSlotOnlyKeys, string AsSecond,
    bool LiveProvenArmorSwap, string? ArmorSwapAcknowledgement)
{
    public const string StandardIssue = "STANDARD ISSUE";
    // asSecond is "allowed" or "refused: <CODE> (...)": STANDARD ISSUE (use second = false) and INTEGRATED EXPLOSIVES (ARMOR_SLOT_ONLY).
    [JsonIgnore] public bool AllowedAsSecond => AsSecond == "allowed";
    [JsonIgnore] public string? SecondRefusal => AllowedAsSecond ? null : Regex.Match(AsSecond, @"\Arefused: ([A-Z_]+)").Groups[1].Value;
}
public sealed record ArmorPassiveKit(int Index, string Id, string Slot, string? Weight, int Passive, string PassiveName, string Rarity, string? Name, int SameName);
public sealed record ArmorPassiveSetSpec(string Accessor, string[] AtLeastOne, bool SoloOnly, bool Raises, string OnePerGame, string Stacking, string Writes,
    Dictionary<string, string> Statuses, string[] RefusalCodes, string[] WaitingCodes, string[] SuspendedCodes, string[] LostCodes);
public sealed record ArmorLiveSwap(string Family, string Status, string Scope, ArmorKitPassive[] Passives, string AcknowledgementRemoved, string[] NotPromoted, string NeedsAcknowledgement);
public sealed record ArmorPassiveSummary(int Passives, int UnusedIds, int Kits, Dictionary<string, int> KitsBySlot, int NamedKits, Dictionary<string, int> FollowsSwap,
    int EffectPackages, int LiveProvenArmorSwap);
public sealed record ArmorPassiveCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string Build, string Status, ArmorPassiveSummary Summary,
    int[] UnusedIds, ArmorPassive[] Passives, ArmorPassiveKit[] Kits, ArmorPassiveSetSpec Set, ArmorLiveSwap LiveArmorSwap)
{
    public ArmorPassive? Find(int id) => Passives.FirstOrDefault(p => p.Id == id);
    // The live-proven armor-slot swaps (SCOUT, ENGINEERING KIT, MED-KIT, SERVO-ASSISTED): alone in the armor slot they need no opt-in.
    public bool LiveProvenArmorSwap(int armor) => LiveArmorSwap.Passives.Any(p => p.Id == armor);
}

public static class ArmorAuthoringReader
{
    public const string StatsContract = "hd2runtime.armor_stats.capabilities.v1", PassiveContract = "hd2runtime.armor_passives.catalog.v1";
    public const string KitResource = "armor_kit", ClassResource = "armor_class", CurveResource = "armor_damage_curve";
    public const string KitPath = "piece_weight", ClassPath = "class_table", CurvePath = "damage_curve";
    // The damage curve has no name of its own: its EntityTarget.Armor (and EntityChange.Entity) is this token.
    public const string CurveEntity = "damage_curve";
    public static readonly string[] Resources = [KitResource, ClassResource, CurveResource];
    public static readonly string[] Weights = ["light", "medium", "heavy"];
    public static readonly string[] ClassFields = ["armor_class.rating", "armor_class.speed", "armor_class.stamina"];
    // No field carries a published evidence tier: the research pins every reader's code and replicates the vanilla values on seven snapshots,
    // and nothing is live-tested (every write carries allow_unverified_effect).
    public const string Tier = "native_code_proven";
    private static readonly Regex KitId = new(@"\A[0-9A-F]{8}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent armor capability metadata (check {line})."); }
    public static bool IsArmor(string resource) => resource is KitResource or ClassResource or CurveResource;
    public static bool ValidKitId(string? id) => id != null && KitId.IsMatch(id);
    // A saved armor change's target: a kit id with a piece weight slot field, a weight class with one of its three values, or a curve point.
    public static bool ValidTarget(string resource, string path, string entity, string semanticFieldId) => (resource, path) switch
    {
        (KitResource, KitPath) => ValidKitId(entity) && Regex.IsMatch(semanticFieldId, @"\Aarmor_kit\.piece_weight\.[a-z_]{1,32}\z"),
        (ClassResource, ClassPath) => Weights.Contains(entity) && ClassFields.Contains(semanticFieldId),
        (CurveResource, CurvePath) => entity == CurveEntity && Regex.IsMatch(semanticFieldId, @"\Aarmor_damage_curve\.at_(minus_)?[0-9]\z"),
        _ => false,
    };
    // A saved value: a weight name for a piece, a number for a class value or a curve point.
    public static bool ValidValue(string resource, string fieldType, JsonElement value) => resource == KitResource
        ? fieldType == "enum" && value.ValueKind == JsonValueKind.String && Weights.Contains(value.GetString())
        : fieldType == "number" && value.ValueKind == JsonValueKind.Number;

    public static ArmorCatalog Read(byte[] stats, byte[]? passives, string version)
    {
        try
        {
            var c = Parse<ArmorCatalog>(stats, StatsContract, PlayerArmorFiles.ArmorStats, s =>
                Check(!s.GetProperty("runtimeAddresses").GetBoolean() && !s.GetProperty("rawWrites").GetBoolean() && !s.GetProperty("nativeLayouts").GetBoolean()
                    && s.GetProperty("writesDuringGeneration").GetInt32() == 0));
            Check(c.Hd2RuntimeVersion == version && !string.IsNullOrWhiteSpace(c.Build));
            ValidateModel(c);
            ValidateKits(c);
            var fields = ValidateFields(c);
            var catalog = c with { FieldInstances = fields };
            return passives == null ? catalog : catalog with { Passives = ReadPassives(passives, version, catalog) };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed armor capability metadata.", e); }
    }
    private static T Parse<T>(byte[] bytes, string contract, string file, Action<JsonElement> safety)
    {
        if (bytes.Length > PlayerArmorFiles.MaxBytes(file)) throw new InvalidDataException("Armor capability file exceeds size limit.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
        var root = doc.RootElement;
        if (root.GetProperty("contract").GetString() != contract || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new UnsupportedSdkException("Unsupported armor authoring contract.");
        safety(root.GetProperty("safety"));
        return JsonSerializer.Deserialize<T>(bytes, Options)!;
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-4;
    private static void ValidateModel(ArmorCatalog c)
    {
        Check(c.Targets.Count == 3 && Resources.All(c.Targets.ContainsKey) && c.Targets.Values.All(t => t.Accessor.StartsWith("hd2.armor_stats.", StringComparison.Ordinal)));
        var t = c.Transaction;
        Check(t.TargetsPerRequest == 1 && t.MinChanges >= 1 && t.MaxChanges is >= 1 and <= 32 && t.Acknowledgements.Order(StringComparer.Ordinal).SequenceEqual(["allow_shared", "allow_unverified_effect"])
            && t.Operations.Contains("hd2.ensure") && t.Operations.Contains("hd2.transaction") && t.Operations.Contains("hd2.patch") && !string.IsNullOrWhiteSpace(t.SyncNote)
            && t.WeightValues.Count == 3 && Weights.Select((w, i) => t.WeightValues.GetValueOrDefault(w, -1) == i).All(x => x));
        Check(c.Slots.Length == 9 && c.Slots.Select(s => s.Name).Distinct().Count() == 9 && c.Slots.Select((s, i) => s.Index == i + 1).All(x => x)
            && c.Slots.All(s => Regex.IsMatch(s.Name, @"\A[a-z_]{1,32}\z")));
        Check(c.Classes.Length == 3 && c.Classes.Select((x, i) => x.Name == Weights[i] && x.Index == i).All(x => x));
        foreach (var x in c.Classes)
            Check(Near(x.Display.Rating, 50 + 50 * x.ArmorValue) && Near(x.Display.Speed, 500 * x.SpeedFactor) && Near(x.Display.StaminaRegen, 100 * (2 - x.StaminaFactor)));
        var p = c.DamageCurve.Points;
        Check(p.Length == 5 && p.Zip(p.Skip(1)).All(z => z.First.ArmorValue < z.Second.ArmorValue) && p.All(x => double.IsFinite(x.Damage) && x.Damage > 0));
        // Passive armor rows are Add rows on the passives Runtime publishes (Extra Padding +1, Unflinching +0.5, ...).
        Check(c.PassiveArmorBonuses.All(b => b.Rows.Length > 0 && b.Rows.All(r => r.Type is "Add" or "Set" or "Multiply" && double.IsFinite(r.Value)))
            && c.PassiveArmorBonuses.Select(b => b.Passive).Distinct().Count() == c.PassiveArmorBonuses.Length);
        string[] ranges = ["armor_class.rating", "armor_class.speed", "armor_class.stamina", "armor_damage_curve", "player.armor_bonus", "player.stamina_factor"];
        Check(ranges.All(k => c.Ranges.TryGetValue(k, out var r) && r.Length == 2 && double.IsFinite(r[0]) && double.IsFinite(r[1]) && r[0] < r[1]));
        // The local player's set(): both members, the unverified opt-in, solo only, never raising.
        var pl = c.Player;
        Check(pl.Accessor == "hd2.armor_stats.player()" && pl.AtLeastOne.Order(StringComparer.Ordinal).SequenceEqual(["armor_bonus", "stamina_factor"])
            && pl.Acknowledgements.SequenceEqual(["allow_unverified_effect"]) && !pl.AllowSharedRequired && pl.SoloOnly && !pl.Raises
            && pl.Options.TryGetValue("armor_bonus", out var bonus) && bonus.Type == "number" && bonus.Min == c.Ranges["player.armor_bonus"][0] && bonus.Max == c.Ranges["player.armor_bonus"][1]
            && pl.Options.TryGetValue("stamina_factor", out var stamina) && stamina.Type == "number" && stamina.Min == c.Ranges["player.stamina_factor"][0] && stamina.Max == c.Ranges["player.stamina_factor"][1]
            && pl.Options.ContainsKey("allow_unverified_effect") && pl.RefusalCodes.Contains("NOT_SOLO"));
    }
    private static void ValidateKits(ArmorCatalog c)
    {
        var slots = c.Slots.ToDictionary(s => s.Name, StringComparer.Ordinal);
        Check(c.Kits.Length is > 0 and <= 1000 && c.Kits.Select(k => k.Id).Distinct().Count() == c.Kits.Length && c.Kits.Select(k => k.Index).Distinct().Count() == c.Kits.Length);
        foreach (var k in c.Kits)
        {
            Check(KitId.IsMatch(k.Id) && k.HexId == "0x" + k.Id && k.Slots.Length > 0 && k.Slots.Distinct().Count() == k.Slots.Length && k.Slots.All(slots.ContainsKey)
                && k.Weights.Keys.Order(StringComparer.Ordinal).SequenceEqual(k.Slots.Order(StringComparer.Ordinal)) && k.Weights.Values.All(Weights.Contains)
                && Weights.Contains(k.Class) && (!k.Weights.TryGetValue("torso", out var torso) || torso == k.Class)
                && k.RatingPieces == k.Slots.Count(s => slots[s].RatingSlot) && k.RatingPieces > 0 && (k.Name == null || k.Name.Length is > 0 and <= 128)
                && !string.IsNullOrWhiteSpace(k.Passive.Name));
            // The published formulas give exactly Runtime's vanilla stats for every kit (the same averagers the domain replicates).
            var v = ArmorMath.Stats(c, k, k.Weights, ArmorMath.Vanilla(c)); var w = k.Vanilla;
            Check(Near(v.Rating, w.Rating) && Near(v.Speed, w.Speed) && Near(v.Stamina, w.Stamina) && Near(v.ArmorValue, w.ArmorValue)
                && Near(v.SpeedFactor, w.SpeedFactor) && Near(v.StaminaFactor, w.StaminaFactor) && Near(v.DamageMultiplier, w.DamageMultiplier));
        }
        var s = c.Summary;
        Check(s.Kits == c.Kits.Length && s.NamedKits == c.Kits.Count(k => k.Name != null) && s.UniquelyNamedKits == c.Kits.Count(k => k.NameUnique)
            && s.Classes == c.Classes.Length && s.CurvePoints == c.DamageCurve.Points.Length && s.PassiveArmorBonuses == c.PassiveArmorBonuses.Length
            && c.Kits.Where(k => k.NameUnique).All(k => k.Name != null && c.Kits.Count(o => o.Name == k.Name) == 1));
    }
    private static EntityField[] ValidateFields(ArmorCatalog c)
    {
        var kits = c.Kits.ToDictionary(k => k.Id, StringComparer.Ordinal);
        var result = new List<EntityField>();
        foreach (var f in c.Fields)
        {
            var t = f.Target;
            Check(Api.IsMatch(f.ApiFieldConstant) && !string.IsNullOrWhiteSpace(f.DisplayName) && f.Editable && f.Shared && f.AllowSharedRequired && !string.IsNullOrWhiteSpace(f.SharedReason)
                && f.Acknowledgements.Order(StringComparer.Ordinal).SequenceEqual(["allow_shared", "allow_unverified_effect"]) && f.Acknowledgement == "allow_unverified_effect"
                && !string.IsNullOrWhiteSpace(f.AcknowledgementReason) && !string.IsNullOrWhiteSpace(f.Lifecycle) && !f.LiveTested);
            string entity, key; EntityRange? range = null;
            switch (t.Resource)
            {
                case KitResource:
                    Check(t.Path == KitPath && t.ArmorKit != null && kits.TryGetValue(t.ArmorKit, out var kit) && t.Slot != null && kit!.Weights.ContainsKey(t.Slot)
                        && t.ArmorClass == null && t.ArmorValue == null && f.SemanticFieldId == "armor_kit.piece_weight." + t.Slot
                        && f.ApiFieldConstant == "hd2.fields.armor_kit.piece_weight_" + t.Slot && f.Type == "enum" && f.Storage == "u32" && !f.ExecutableData
                        && f.AllowedValues != null && f.AllowedValues.SequenceEqual(Weights) && f.Min == null && f.Max == null
                        && f.CurrentDefault.ValueKind == JsonValueKind.String && f.CurrentDefault.GetString() == kit.Weights[t.Slot] && f.OperationGroup == "armor_kit/" + kit.Id);
                    entity = t.ArmorKit!; key = KitResource + ":" + entity + ":" + t.Slot;
                    break;
                case ClassResource:
                    var cls = c.Class(t.ArmorClass ?? "");
                    Check(t.Path == ClassPath && cls != null && t.ArmorKit == null && t.Slot == null && t.ArmorValue == null && ClassFields.Contains(f.SemanticFieldId)
                        && f.ApiFieldConstant == "hd2.fields." + f.SemanticFieldId && f.Type == "number" && f.Storage == "f32" && f.ExecutableData && f.AllowedValues == null
                        && f.CurrentDefault.ValueKind == JsonValueKind.Number && Near(f.CurrentDefault.GetDouble(), ArmorMath.ClassValue(cls!, f.SemanticFieldId))
                        && f.Min == c.Ranges[f.SemanticFieldId][0] && f.Max == c.Ranges[f.SemanticFieldId][1]);
                    entity = cls!.Name; key = ClassResource + ":" + entity + ":" + f.SemanticFieldId.Split('.')[1];
                    range = new EntityRange(f.Min!.Value, f.Max!.Value, false);
                    break;
                case CurveResource:
                    var point = c.DamageCurve.Points.FirstOrDefault(p => t.ArmorValue != null && p.ArmorValue == t.ArmorValue);
                    var suffix = t.ArmorValue is < 0 ? "minus_" + (-t.ArmorValue!.Value).ToString(CultureInfo.InvariantCulture) : t.ArmorValue?.ToString(CultureInfo.InvariantCulture);
                    Check(t.Path == CurvePath && point != null && t.ArmorKit == null && t.ArmorClass == null && t.Slot == null && f.SemanticFieldId == "armor_damage_curve.at_" + suffix
                        && f.ApiFieldConstant == "hd2.fields." + f.SemanticFieldId && f.Type == "number" && f.Storage == "f32" && f.ExecutableData && f.AllowedValues == null
                        && f.CurrentDefault.ValueKind == JsonValueKind.Number && Near(f.CurrentDefault.GetDouble(), point!.Damage)
                        && f.Min == c.Ranges[CurveResource][0] && f.Max == c.Ranges[CurveResource][1] && f.OperationGroup == CurveResource);
                    entity = CurveEntity; key = CurveResource + ":" + f.SemanticFieldId.Split('.')[1];
                    range = new EntityRange(f.Min!.Value, f.Max!.Value, false);
                    break;
                default: Check(false); return [];
            }
            result.Add(Entity(c, f, entity, key, range));
        }
        Check(result.Select(f => f.InstanceKey).Distinct().Count() == result.Count);
        // One field per piece of every kit, three per class, one per curve point; one request target holds at most maxChanges of them.
        Check(c.Kits.All(k => k.Slots.All(s => result.Count(f => f.Target.Resource == KitResource && f.Target.Armor == k.Id && f.SemanticFieldId == "armor_kit.piece_weight." + s) == 1)
                && result.Count(f => f.Target.Resource == KitResource && f.Target.Armor == k.Id) == k.Slots.Length)
            && c.Classes.All(x => ClassFields.All(id => result.Count(f => f.Target.Resource == ClassResource && f.Target.Armor == x.Name && f.SemanticFieldId == id) == 1))
            && result.Count(f => f.Target.Resource == CurveResource) == c.DamageCurve.Points.Length
            && result.GroupBy(f => f.OperationGroup).All(g => g.Count() <= c.Transaction.MaxChanges && g.Select(f => f.Target).Distinct().Count() == 1));
        var s = c.Summary;
        Check(s.FieldInstances == c.Fields.Length && s.KitFieldInstances == result.Count(f => f.Target.Resource == KitResource)
            && s.ClassFieldInstances == result.Count(f => f.Target.Resource == ClassResource) && s.CurveFieldInstances == result.Count(f => f.Target.Resource == CurveResource));
        foreach (var f in result) _ = Generation.EntityScalar.Normalize(f, f.CurrentDefault);
        return [.. result];
    }
    // One request per target: a kit's pieces, a class's three values or the curve's five points are each one transaction (Runtime takes one target
    // per request). Notices carry the published scope, the lifecycle and, for the class tables and the curve, the reviewed-executable-data note.
    private static EntityField Entity(ArmorCatalog c, ArmorRawField f, string entity, string key, EntityRange? range)
    {
        var resource = f.Target.Resource; var group = resource == CurveResource ? CurveResource : resource + "/" + entity;
        var notices = new List<EntityNotice> { new("scope", f.SharedReason) };
        if (f.ExecutableData) notices.Add(new("executable_data", c.ExecutableData));
        if (!c.Transaction.Synced) notices.Add(new("not_synced", c.Transaction.SyncNote));
        return new(key, f.SemanticFieldId, f.DisplayName, f.Type, f.Unit, f.CurrentDefault.Clone(), true, null,
            new EntityTarget(resource, f.Target.Path, Armor: entity), group, group, group, "patch_or_transaction", true, true, [],
            "armor-scope/" + group, true, false, resource, f.SemanticFieldId.Split('.')[0], f.ApiFieldConstant, 1, [],
            new EntityEvidence(Tier, Proof: c.Evidence.Validation, GameplayWriteEffect: c.Evidence.LiveTest), c.Targets[resource].Scope,
            AllowedValues: f.AllowedValues, Acknowledgement: f.Acknowledgement, Range: range, AcknowledgementReason: f.AcknowledgementReason,
            UiGroup: f.Target.Slot ?? entity, Notices: [.. notices], Lifecycle: f.Lifecycle);
    }

    private static ArmorPassiveCatalog ReadPassives(byte[] bytes, string version, ArmorCatalog stats)
    {
        var p = Parse<ArmorPassiveCatalog>(bytes, PassiveContract, PlayerArmorFiles.ArmorPassives, s =>
            Check(!s.GetProperty("runtimeAddresses").GetBoolean() && !s.GetProperty("rawWrites").GetBoolean() && !s.GetProperty("packageIds").GetBoolean()
                && s.GetProperty("writesDuringGeneration").GetInt32() == 0));
        Check(p.Hd2RuntimeVersion == version && p.Build == stats.Build);
        var ids = p.Passives.Select(x => x.Id).ToHashSet();
        Check(p.Passives.Length is > 0 and <= 64 && ids.Count == p.Passives.Length && p.Passives.Select(x => x.Name).Distinct().Count() == p.Passives.Length
            && !p.UnusedIds.Any(ids.Contains) && p.Passives.Any(x => x.Id == 0 && x.Name == ArmorPassive.StandardIssue));
        foreach (var x in p.Passives)
            Check(x.Id is >= 0 and < 64 && Regex.IsMatch(x.Name, @"\A[A-Z0-9][A-Z0-9 ,'\-]{0,63}\z") && !string.IsNullOrWhiteSpace(x.Description)
                && x.FollowsSwap is "full" or "partial" or "unknown" && x.Modifiers.Length > 0 && x.Modifiers.All(m => m.Type is "Set" or "Add" or "Multiply" or "Time" && double.IsFinite(m.Value))
                && (x.AllowedAsSecond || x.SecondRefusal is { Length: > 0 }) && (x.FollowsSwap == "full" || !string.IsNullOrWhiteSpace(x.Note))
                // Every #BONUS of the game's description is one modifier row's value (PassiveText.Description fills them in order).
                && x.Description.Split("#BONUS").Length - 1 == x.Modifiers.Count(m => m.Text?.Contains("#BONUS", StringComparison.Ordinal) == true));
        // STANDARD ISSUE as the second passive is refused (use second = false); INTEGRATED EXPLOSIVES too (ARMOR_SLOT_ONLY).
        Check(p.Find(0)!.SecondRefusal == "INVALID_OPTION" && p.Passives.Where(x => x.ArmorSlotOnly).All(x => x.SecondRefusal == "ARMOR_SLOT_ONLY"));
        var live = p.LiveArmorSwap.Passives.Select(x => x.Id).ToHashSet();
        Check(p.LiveArmorSwap.Status == "live_proven" && p.LiveArmorSwap.AcknowledgementRemoved == "allow_unverified_effect"
            && p.LiveArmorSwap.Passives.All(x => p.Find(x.Id)?.Name == x.Name) && p.Passives.All(x => x.LiveProvenArmorSwap == live.Contains(x.Id)));
        var set = p.Set;
        Check(set.Accessor == "hd2.player_passives.set(spec)" && set.AtLeastOne.Order(StringComparer.Ordinal).SequenceEqual(["armor", "second"]) && set.SoloOnly && !set.Raises
            && new[] { "INVALID_OPTION", "ACKNOWLEDGEMENT_REQUIRED", "SAME_PASSIVE", "ARMOR_SLOT_ONLY", "ALREADY_SET", "NOT_SOLO" }.All(set.RefusalCodes.Contains)
            && set.Statuses.ContainsKey("active") && set.Statuses.ContainsKey("refused"));
        // The kits: one per index, each passive published; the armor kits are exactly the armor stats catalog's kits.
        Check(p.Kits.Length <= 4096 && p.Kits.Select((k, i) => k.Index == i).All(x => x) && p.Kits.Select(k => k.Id).Distinct().Count() == p.Kits.Length
            && p.Kits.All(k => k.Slot is "armor" or "helmet" or "cape" && ids.Contains(k.Passive) && p.Find(k.Passive)!.Name == k.PassiveName
                && (k.Slot == "armor" ? Weights.Contains(k.Weight) : k.Weight == null)));
        var armor = p.Kits.Where(k => k.Slot == "armor").ToDictionary(k => k.Id, StringComparer.Ordinal);
        Check(armor.Count == stats.Kits.Length && stats.Kits.All(k => armor.TryGetValue(k.HexId, out var a) && a.Index == k.Index && a.Passive == k.Passive.Id
            && a.PassiveName == k.Passive.Name && a.Name == k.Name && a.Weight == k.Class));
        Check(stats.PassiveArmorBonuses.All(b => p.Find(b.Passive)?.Name == b.Name));
        var s = p.Summary;
        Check(s.Passives == p.Passives.Length && s.UnusedIds == p.UnusedIds.Length && s.Kits == p.Kits.Length && s.NamedKits == p.Kits.Count(k => k.Name != null)
            && s.KitsBySlot.Count == p.Kits.Select(k => k.Slot).Distinct().Count() && s.KitsBySlot.All(x => p.Kits.Count(k => k.Slot == x.Key) == x.Value)
            && s.FollowsSwap.All(x => p.Passives.Count(y => y.FollowsSwap == x.Key) == x.Value) && s.FollowsSwap.Values.Sum() == p.Passives.Length
            && s.EffectPackages == p.Passives.Count(x => x.EffectPackage != null) && s.LiveProvenArmorSwap == live.Count);
        return p;
    }
}

// The published armor formulas (sdk/docs/armor-stats.md "Where the numbers come from"; ArmorStatsCapabilities.json formulas):
// - A (armor value) = the mean of the class armor value over the kit's pieces in the rating slots (not the cape or the hips), then the kit
//   passive's armor rows ((Set, else the mean) + the sum of Add, times the product of Multiply); damage x = curve(A).
// - S / F (speed / stamina factor) = the mean of the class factor over every piece (cape and hips included).
// - The armory shows RATING = 50 + 50 x A, SPEED = 500 x S' and STAMINA REGEN = 100 x (2 - F'), where S' and F' average the rating slots only.
public static class ArmorMath
{
    public sealed record ClassValues(double ArmorValue, double SpeedFactor, double StaminaFactor);
    public static IReadOnlyDictionary<string, ClassValues> Vanilla(ArmorCatalog c) => c.Classes.ToDictionary(x => x.Name, x => new ClassValues(x.ArmorValue, x.SpeedFactor, x.StaminaFactor), StringComparer.Ordinal);
    public static double ClassValue(ArmorClass c, string semanticFieldId) => semanticFieldId switch
    {
        "armor_class.rating" => c.ArmorValue, "armor_class.speed" => c.SpeedFactor, "armor_class.stamina" => c.StaminaFactor,
        _ => throw new InvalidDataException("Unknown armor class field."),
    };
    // Linear between neighbouring points; the top value above the top armor value, 1.0 below the lowest (as published).
    public static double Curve(IReadOnlyList<ArmorCurvePoint> points, double a)
    {
        if (a >= points[^1].ArmorValue) return points[^1].Damage;
        if (a < points[0].ArmorValue) return 1.0;
        for (var i = 0; i + 1 < points.Count; i++)
            if (a >= points[i].ArmorValue && a <= points[i + 1].ArmorValue)
                return points[i].Damage + (a - points[i].ArmorValue) / (points[i + 1].ArmorValue - points[i].ArmorValue) * (points[i + 1].Damage - points[i].Damage);
        return 1.0;
    }
    public static ArmorStats Stats(ArmorCatalog c, ArmorKit kit, IReadOnlyDictionary<string, string> weights, IReadOnlyDictionary<string, ClassValues> classes,
        IReadOnlyList<ArmorCurvePoint>? curve = null)
    {
        var rating = kit.Slots.Where(s => c.Slots.First(x => x.Name == s).RatingSlot).ToArray();
        ClassValues Of(string slot) => classes[weights[slot]];
        var a = rating.Average(s => Of(s).ArmorValue);
        if (c.PassiveArmorBonuses.FirstOrDefault(b => b.Passive == kit.Passive.Id) is { } bonus)
        {
            var set = bonus.Rows.LastOrDefault(r => r.Type == "Set");
            a = (set?.Value ?? a) + bonus.Rows.Where(r => r.Type == "Add").Sum(r => r.Value);
            foreach (var r in bonus.Rows.Where(r => r.Type == "Multiply")) a *= r.Value;
        }
        var s = kit.Slots.Average(x => Of(x).SpeedFactor); var f = kit.Slots.Average(x => Of(x).StaminaFactor);
        var shownSpeed = rating.Average(x => Of(x).SpeedFactor); var shownStamina = rating.Average(x => Of(x).StaminaFactor);
        return new(50 + 50 * a, 500 * shownSpeed, 100 * (2 - shownStamina), a, s, f, Curve(curve ?? c.DamageCurve.Points, a));
    }
}

// The game's passive descriptions carry #BONUS / #SIGN placeholders the game fills from the modifier rows, in order: shown filled in
// (Multiply as the percentage it changes, Add as the amount, Time in seconds), with any <c=...> colour markup removed.
public static class PassiveText
{
    public static string Description(ArmorPassive p)
    {
        var rows = p.Modifiers.Where(m => m.Text?.Contains("#BONUS", StringComparison.Ordinal) == true).ToArray(); var i = 0;
        var text = Regex.Replace(p.Description, "(#SIGN)?#BONUS", m => i < rows.Length ? Bonus(rows[i++], m.Groups[1].Success) : "…");
        return Regex.Replace(text, "<c=[^>]*>|</c>", "").Replace("#SIGN", "");
    }
    public static string Bonus(ArmorPassiveModifier m, bool sign) => m.Type switch
    {
        "Multiply" => (sign ? "+" : "") + Math.Round(Math.Abs(1 - m.Value) * 100).ToString(CultureInfo.InvariantCulture) + "%",
        "Time" => m.Value.ToString("0.##", CultureInfo.InvariantCulture) + "s",
        _ => (sign && m.Value >= 0 ? "+" : "") + m.Value.ToString("0.##", CultureInfo.InvariantCulture),
    };
}
