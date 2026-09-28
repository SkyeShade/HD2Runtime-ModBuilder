using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record SdkType(string Name, Dictionary<string, string> Methods);
public sealed record SdkField(string Name, string Domain, string ValueType, bool Writable, JsonNode? Expected,
    Dictionary<string, JsonNode?> EnumValues, string Confidence, string Provenance, string Storage)
{
    public string Label => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Name.Replace('_', ' '));
    public string ControlType => EnumValues.Count > 0 ? "enum" : ValueType;
}
public sealed record SdkResource(string Key, string Label, string Kind, string[] Domains, Dictionary<string, SdkField> Fields);
public sealed record SdkTransition(string Resource, string Field, JsonNode? Expected, JsonNode? Value, string Operation);
public sealed record SdkMetadata(string Version, int ApiVersion, Dictionary<string, SdkType> Types,
    Dictionary<string, string> Builders, Dictionary<string, SdkResource> Resources, List<SdkTransition> Transitions, string EvidenceNote)
{
    public PlayerWeaponCatalog? PlayerWeapons { get; init; }
    public PlayerWeaponAmmoCatalog? PlayerAmmo { get; init; }
    public PlayerWeaponHeatCatalog? PlayerHeat { get; init; }
    public PlayerWeaponComposition? Composition { get; init; }
    public AdvancedCapabilities? Advanced { get; init; }
    public CompositionPlanCapabilities? Plans { get; init; }
    public SupportAuthoringCatalog? SupportAuthoring { get; init; }
    public StratagemCatalog? Stratagems { get; init; }
    public SupportCallInIndex? SupportLinks { get; init; }
    public string CategoryName(string key)
    {
        var name = Types[key].Name;
        if (name.EndsWith('s') || name.EndsWith("ment", StringComparison.Ordinal)) return name;
        return name.EndsWith('y') ? name[..^1] + "ies" : name + "s";
    }

    public string[] MethodPath(SdkResource resource, string domain)
    {
        var queue = new Queue<(string Domain, string[] Path)>();
        var visited = new HashSet<string>();
        queue.Enqueue((Builders[resource.Kind], []));
        while (queue.TryDequeue(out var entry))
        {
            if (entry.Domain == domain) return entry.Path;
            if (!visited.Add(entry.Domain)) continue;
            foreach (var method in Types[entry.Domain].Methods)
                if (resource.Domains.Contains(method.Value)) queue.Enqueue((method.Value, [..entry.Path, method.Key]));
        }
        throw new InvalidDataException("SDK field has no public builder path.");
    }
}

public interface IMetadataReader { SdkMetadata Read(byte[] json); }
public sealed class UnsupportedSdkException(string message) : NotSupportedException(message);

public sealed class MetadataReader : IMetadataReader
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public SdkMetadata Read(byte[] json)
    {
        try
        {
            if (json.Length > MaxBytes) throw new InvalidDataException("Metadata exceeds 4 MiB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            RejectDuplicates(document.RootElement);
            var root = JsonNode.Parse(json)!.AsObject();
            if (root["schema_version"] == null || root["api_version"] == null) throw new InvalidDataException("Missing metadata schema or API version.");
            if (root["schema_version"]!.GetValue<int>() != 1) throw new UnsupportedSdkException("Unsupported SDK metadata schema; this builder supports schema 1.");
            if (root["api_version"]!.GetValue<int>() != 1) throw new UnsupportedSdkException("Unsupported runtime API; this builder supports API 1.");
            var version = Text(root, "runtime_version");
            if (SemVersion.Parse(version).ToString() != version || SemVersion.Parse(version).Prerelease != null) throw new InvalidDataException("SDK must use a stable canonical version.");
            var types = new Dictionary<string, SdkType>();
            foreach (var (key, node) in Object(root, "types"))
            {
                Identifier(key);
                var methods = Object(node!, "methods").ToDictionary(x => x.Key, x => x.Value!.GetValue<string>());
                foreach (var (method, target) in methods) { Identifier(method); Identifier(target); }
                types.Add(key, new(Text(node!, "name"), methods));
            }
            var builders = Object(root, "builders").ToDictionary(x => x.Key, x => x.Value!.GetValue<string>());
            foreach (var (key, target) in builders) { Identifier(key); if (!types.ContainsKey(target)) throw new InvalidDataException("Unknown builder type."); }
            foreach (var type in types.Values) if (type.Methods.Values.Any(t => !types.ContainsKey(t))) throw new InvalidDataException("Unknown method return type.");
            var resources = new Dictionary<string, SdkResource>();
            foreach (var (key, node) in Object(root, "resources"))
            {
                Identifier(key);
                var kind = Text(node!, "kind");
                if (!builders.ContainsKey(kind)) throw new InvalidDataException("Unknown resource builder.");
                var domains = node!["domains"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
                if (domains.Length == 0 || domains.Any(d => !types.ContainsKey(d))) throw new InvalidDataException("Invalid resource domains.");
                var fields = new Dictionary<string, SdkField>();
                foreach (var (name, f) in Object(node, "fields"))
                {
                    Identifier(name.Replace('.', '_'));
                    if (Text(f!, "name") != name) throw new InvalidDataException("Field name mismatch.");
                    var domain = Text(f!, "domain");
                    if (!domains.Contains(domain)) throw new InvalidDataException("Field domain mismatch.");
                    var valueType = Text(f!, "value_type");
                    if (valueType is not ("integer" or "number" or "float" or "bool" or "boolean" or "enum")) throw new InvalidDataException("Unsupported field type.");
                    var enums = new Dictionary<string, JsonNode?>();
                    if (f!["enum"] is JsonValue enumName)
                        foreach (var (enumKey, enumValue) in Object(Object(root, "enums")[enumName.GetValue<string>()]!, "values")) enums.Add(enumKey, enumValue?.DeepClone());
                    var evidence = Object(f, "evidence");
                    bool Flag(string n) => evidence[n]?.GetValue<bool>() == true;
                    var confidence = Flag("gameplay_proven") ? "Gameplay proven" : Flag("current_live_ownership_proven") ? "Live ownership proven" : Flag("schema_labelled") ? "Schema-labelled" : "Experimental";
                    var expected = f["expected"]?.DeepClone();
                    ValidateValue(valueType, expected, allowNull: true);
                    fields.Add(name, new(name, domain, valueType, f["writable"]!.GetValue<bool>(), expected, enums, confidence,
                        evidence["source"]?.GetValue<string>() ?? "Unspecified source", f["storage"]?.GetValue<string>() ?? ""));
                }
                resources.Add(key, new(key, Text(node, "label"), kind, domains, fields));
            }
            if (resources.Count == 0 || resources.Count > 10000) throw new InvalidDataException("Invalid resource count.");
            var transitions = new List<SdkTransition>();
            var contracts = Object(root, "contracts");
            if (contracts["patch"] is JsonObject patch)
                transitions.Add(new(Text(patch, "resource"), Text(patch, "field"), patch["expect"]?.DeepClone(), patch["value"]?.DeepClone(), "patch"));
            if (contracts["transaction"] is JsonObject transaction)
                foreach (var (field, values) in Object(transaction, "fields")) transitions.Add(new(Text(transaction, "resource"), field, values!["expect"]?.DeepClone(), values["value"]?.DeepClone(), "transaction"));
            foreach (var t in transitions)
            {
                if (!resources.TryGetValue(t.Resource, out var r) || !r.Fields.TryGetValue(t.Field, out var f) || !f.Writable) throw new InvalidDataException("Invalid SDK generation contract.");
                ValidateValue(f.ValueType, t.Expected); ValidateValue(f.ValueType, t.Value);
            }
            var metadata = new SdkMetadata(version, 1, types, builders, resources, transitions, Text(root, "evidence_note"));
            foreach (var r in resources.Values) foreach (var f in r.Fields.Values) metadata.MethodPath(r, f.Domain);
            return metadata;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Malformed SDK metadata: " + e.Message, e); }
    }

    public static void ValidateValue(string type, JsonNode? value, bool allowNull = false)
    {
        if (value == null && allowNull) return;
        if (value is not JsonValue scalar) throw new InvalidDataException("A scalar field value is required.");
        var json = scalar.ToJsonString();
        if (type is "bool" or "boolean") { if (json is not ("true" or "false")) throw new InvalidDataException("Expected a boolean."); }
        else if (!double.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || (type == "integer" && (Math.Truncate(number) != number || Math.Abs(number) > 9007199254740991)))
            throw new InvalidDataException("Expected a finite numeric value of the field's type.");
    }
    private static void Identifier(string value) { if (value.Length > 128 || !Regex.IsMatch(value, @"\A[a-zA-Z_][a-zA-Z_0-9]*\z")) throw new InvalidDataException("Unsafe SDK identifier."); }
    private static string Text(JsonNode node, string key)
    { var s = node[key]!.GetValue<string>(); if (string.IsNullOrWhiteSpace(s) || s.Length > 4096) throw new InvalidDataException("Invalid " + key); return s; }
    private static JsonObject Object(JsonNode node, string key) => node[key]!.AsObject();
    internal static void RejectDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>();
            foreach (var property in node.EnumerateObject()) { if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property."); RejectDuplicates(property.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) RejectDuplicates(child);
    }
}
