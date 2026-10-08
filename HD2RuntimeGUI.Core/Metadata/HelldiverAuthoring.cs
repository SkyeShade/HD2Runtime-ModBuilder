using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// HD2Runtime 0.30.0 development SDK (b3f42ec and later): the Helldiver type, hd2.helldiver() (hd2runtime.helldiver.type_fields.v1,
// sdk/docs/helldiver-fields.md). Movement speeds and stamina on the entity, the explosion share every zone takes, and five fields on each of the
// six body zones. These are TYPE records: a write changes every Helldiver this machine simulates (in practice the local player), so every write
// carries allow_shared, and nothing is live-tested, so every write carries allow_unverified_effect. The ship's avatar keeps a private copy of the
// record, so changes show only in a mission; zone health is copied at spawn, so it applies from the next spawn.
public sealed record HelldiverZone(string Id, int Index);
public sealed record HelldiverTypeTarget(string Accessor, string Type, string Scope, HelldiverZone[] Zones);
public sealed record HelldiverEnum(string[] Values, Dictionary<string, double>? Factors = null);
public sealed record HelldiverNotExposed(string Member, string Reason, int? Offset = null, int? ZoneOffset = null);
public sealed record HelldiverSummary(int Fields, int Instances, int Zones);
public sealed record HelldiverFieldTarget(string Resource, string Path, string? Zone = null);
public sealed record HelldiverField(string SemanticFieldId, string DisplayName, HelldiverFieldTarget Target, string Type, string? Unit, string Storage,
    JsonElement CurrentDefault, string Grade, string Lifecycle, string Reads, string? ShadowedByPrivateCopy, string Semantics, bool Shared,
    bool AllowSharedRequired, string[] Acknowledgements, bool LiveTested, double? Min = null, double? Max = null, string[]? AllowedValues = null);
public sealed record HelldiverCatalog(string Contract, int SchemaVersion, HelldiverTypeTarget Target, Dictionary<string, HelldiverEnum> Enums,
    double SpeedCapMetersPerSecond, string PrivateCopies, HelldiverNotExposed[] NotExposed, HelldiverSummary Summary,
    [property: JsonPropertyName("fieldInstances")] HelldiverField[] Fields)
{
    // Published fields in the shared entity-authoring form (changes, reset, Lua, options and export reuse the entity pipeline).
    [JsonIgnore] public EntityField[] FieldInstances { get; init; } = [];
    public HelldiverField? Raw(string instanceKey) => Fields.FirstOrDefault(f => HelldiverAuthoringReader.Key(f) == instanceKey);
    public IEnumerable<EntityField> Entity => FieldInstances.Where(f => f.Target.Path == HelldiverAuthoringReader.EntityPath);
    public IEnumerable<EntityField> Zone(string zone) => FieldInstances.Where(f => f.Target.Path == HelldiverAuthoringReader.ZonePath && f.Target.Zone == zone);
    // The multiplier a published damage multiplier name stands for (none 0, critical 1.5, ...), when Runtime publishes it.
    public double? Factor(string field, string value) => Enums.GetValueOrDefault(field.Split('.')[^1])?.Factors?.TryGetValue(value, out var f) == true ? f : null;
}

public static class HelldiverAuthoringReader
{
    public const string FileName = PlayerArmorFiles.Helldiver, Contract = "hd2runtime.helldiver.type_fields.v1", Resource = "helldiver";
    // EntityTarget.Helldiver / EntityChange.Entity: Runtime's single Helldiver type ("Helldiver", the stub's name argument).
    public const string Entity = "Helldiver", EntityPath = "entity", ZonePath = "damage_zone", Accessor = "hd2.helldiver()";
    public const string Enum = "enum";
    // CONFIRMED: the reader and its branch are pinned. STRONG: the reader is pinned, the state bit that selects it is inferred.
    public static readonly string[] Grades = ["CONFIRMED", "STRONG"];
    public static readonly string[] Lifecycles = ["live", "spawn"];
    // Notice kinds the reader adds (EntityField.Notices).
    public const string SpawnNotice = "lifecycle", PrivateCopyNotice = "private_copy", SpeedCapNotice = "speed_cap";
    private static readonly string[] OptIns = ["allow_shared", "allow_unverified_effect"];
    private static readonly Regex SemanticId = new(@"\A(helldiver|entity|zone)\.[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)?\z", RegexOptions.CultureInvariant);
    private static readonly Regex Name = new(@"\A[a-z][a-z0-9_]{0,31}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent Helldiver field metadata (check {line})."); }
    // A published zone or enum name (head, arm_left, ...; normal, reduced, ...).
    public static bool ValidZone(string? zone) => zone != null && Name.IsMatch(zone);
    public static string Key(HelldiverField f) => Resource + ":" + f.SemanticFieldId + (f.Target.Zone is { } zone ? ":" + zone : "");
    // Runtime's field constant: hd2.fields.<namespace>.<rest with underscores> (helldiver.speed.jog -> hd2.fields.helldiver.speed_jog,
    // zone.health -> hd2.fields.zone.health, entity.explosive_damage_percentage -> hd2.fields.entity.explosive_damage_percentage).
    public static string FieldConstant(string semanticFieldId)
    {
        var dot = semanticFieldId.IndexOf('.');
        return "hd2.fields." + semanticFieldId[..dot] + "." + semanticFieldId[(dot + 1)..].Replace('.', '_');
    }
    // hd2.helldiver() for the entity, hd2.helldiver():zone('<zone>') for one body zone.
    public static string Target(EntityTarget t) => t.Path switch
    {
        EntityPath when t.Zone == null => Accessor,
        ZonePath when ValidZone(t.Zone) => Accessor + ":zone(" + Generation.LuaGenerator.Quote(t.Zone!) + ")",
        _ => throw new InvalidDataException(Localization.CoreText.Get("Messages.Build.Entity.UnsupportedTarget")),
    };

    public static HelldiverCatalog Read(byte[] bytes)
    {
        try
        {
            if (bytes.Length > PlayerArmorFiles.MaxBytes(FileName)) throw new InvalidDataException("Helldiver field file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported Helldiver field contract.");
            var c = JsonSerializer.Deserialize<HelldiverCatalog>(bytes, Options)!;
            // The type: one accessor, its scope, and six uniquely named and indexed zones.
            var zones = c.Target.Zones;
            Check(c.Target.Accessor == Accessor && !string.IsNullOrWhiteSpace(c.Target.Type) && !string.IsNullOrWhiteSpace(c.Target.Scope) && !string.IsNullOrWhiteSpace(c.PrivateCopies)
                && zones.Length is > 0 and <= 16 && zones.All(z => ValidZone(z.Id) && z.Index >= 0) && zones.Select(z => z.Id).Distinct().Count() == zones.Length
                && zones.Select(z => z.Index).Distinct().Count() == zones.Length);
            Check(double.IsFinite(c.SpeedCapMetersPerSecond) && c.SpeedCapMetersPerSecond > 0 && c.NotExposed.All(n => !string.IsNullOrWhiteSpace(n.Member) && !string.IsNullOrWhiteSpace(n.Reason)));
            Check(c.Enums.Count > 0 && c.Enums.All(e => Name.IsMatch(e.Key) && e.Value.Values is { Length: >= 2 and <= 32 } v && v.All(Name.IsMatch) && v.Distinct().Count() == v.Length
                && (e.Value.Factors == null || e.Value.Factors.All(f => v.Contains(f.Key) && double.IsFinite(f.Value)))));
            Check(c.Fields.Length is > 0 and <= 500 && c.Fields.Select(Key).Distinct().Count() == c.Fields.Length);
            var zoneIds = zones.Select(z => z.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var f in c.Fields)
            {
                var zoneField = f.SemanticFieldId.StartsWith("zone.", StringComparison.Ordinal);
                Check(SemanticId.IsMatch(f.SemanticFieldId) && !string.IsNullOrWhiteSpace(f.DisplayName) && f.DisplayName.Length <= 128 && !string.IsNullOrWhiteSpace(f.Semantics)
                    && f.Target.Resource == Resource && (f.Target.Path, f.Target.Zone) switch
                    {
                        (EntityPath, null) => !zoneField,
                        (ZonePath, { } zone) => zoneField && zoneIds.Contains(zone),
                        _ => false,
                    });
                // Every write needs both opt-ins: the record is shared by every simulated Helldiver, and no field is live-tested.
                Check(f.Shared && f.AllowSharedRequired && !f.LiveTested && f.Acknowledgements.Order(StringComparer.Ordinal).SequenceEqual(OptIns)
                    && Grades.Contains(f.Grade) && Lifecycles.Contains(f.Lifecycle) && !string.IsNullOrWhiteSpace(f.Reads) && f.ShadowedByPrivateCopy is null or "avatar" or "health");
                Check(f.Type switch
                {
                    // A name from the published enum the field is named after (damage_multiplier, damage_multiplier_dps); no numeric bounds.
                    Enum => f.Min == null && f.Max == null && f.Storage == "u32" && f.AllowedValues is { Length: > 0 } allowed
                        && c.Enums.TryGetValue(f.SemanticFieldId.Split('.')[^1], out var e) && allowed.SequenceEqual(e.Values)
                        && f.CurrentDefault.ValueKind == JsonValueKind.String && allowed.Contains(f.CurrentDefault.GetString()),
                    // A reviewed range that contains the vanilla value; zone health is a whole number.
                    "number" or "integer" => f.AllowedValues == null && f.Storage == (f.Type == "integer" ? "i32" : "f32") && f.Min is double min && f.Max is double max
                        && double.IsFinite(min) && double.IsFinite(max) && min < max && f.CurrentDefault.ValueKind == JsonValueKind.Number
                        && f.CurrentDefault.GetDouble() >= min && f.CurrentDefault.GetDouble() <= max,
                    _ => false,
                });
            }
            // Every zone publishes the same fields; one target never exceeds a transaction (32 changes).
            var perZone = c.Fields.Where(f => f.Target.Path == ZonePath).GroupBy(f => f.Target.Zone!).ToArray();
            Check(perZone.Length == zones.Length && perZone.All(g => g.Select(f => f.SemanticFieldId).Order(StringComparer.Ordinal)
                .SequenceEqual(perZone[0].Select(f => f.SemanticFieldId).Order(StringComparer.Ordinal))));
            Check(c.Fields.GroupBy(f => (f.Target.Path, f.Target.Zone)).All(g => g.Count() <= 32));
            Check(c.Summary.Instances == c.Fields.Length && c.Summary.Zones == zones.Length && c.Summary.Fields == c.Fields.Select(f => f.SemanticFieldId).Distinct().Count());
            var fields = c.Fields.Select(f => Adapt(f, c)).ToArray();
            foreach (var f in fields) _ = Generation.EntityScalar.Normalize(f, f.CurrentDefault);
            return c with { FieldInstances = fields };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed Helldiver field metadata.", e); }
    }
    // One operation and plan group per target (the entity, or one zone): a patch or one transaction each, never a plan across targets.
    private static EntityField Adapt(HelldiverField f, HelldiverCatalog c)
    {
        var target = new EntityTarget(Resource, f.Target.Path, Zone: f.Target.Zone, Helldiver: Entity);
        var group = "helldiver/" + c.Target.Type + "/" + (f.Target.Zone is { } zone ? "zone/" + zone : EntityPath);
        var record = f.SemanticFieldId.StartsWith("helldiver.", StringComparison.Ordinal) ? "AvatarComponentData" : "HealthComponentData";
        // Notes shown in the field's ⓘ panel, built from the published facts (the page explains them in the UI language).
        var notices = new List<EntityNotice>();
        if (f.Lifecycle == "spawn") notices.Add(new(SpawnNotice, "Applies from the next spawn (lifecycle: spawn): a Helldiver already alive keeps its current value."));
        if (f.ShadowedByPrivateCopy != null) notices.Add(new(PrivateCopyNotice, c.PrivateCopies));
        if (f.Unit == "meters_per_second") notices.Add(new(SpeedCapNotice, "The game caps the target speed at " + c.SpeedCapMetersPerSecond.ToString(CultureInfo.InvariantCulture) + " m/s."));
        return new(Key(f), f.SemanticFieldId, f.DisplayName, f.Type, f.Unit, f.CurrentDefault.Clone(), true, null, target,
            "helldiver/" + c.Target.Type, group, group, "patch_or_transaction", f.AllowSharedRequired, f.Shared, [], "helldiver-scope/" + c.Target.Type, false, true,
            record, f.SemanticFieldId.Split('.')[0], FieldConstant(f.SemanticFieldId), 1, [],
            new EntityEvidence(f.Grade, Proof: f.Semantics, NativeOwner: record), f.Semantics,
            AllowedValues: f.Type == Enum ? f.AllowedValues : null, Acknowledgement: "allow_unverified_effect",
            Range: f.Min is double min && f.Max is double max ? new EntityRange(min, max, f.Type == "integer") : null,
            AcknowledgementReason: "not live-tested (liveTested: false)", Notices: notices.Count > 0 ? [.. notices] : null);
    }
}
