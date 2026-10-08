using System.Globalization;
using System.Text.Json;

namespace HD2RuntimeGUI.Core.CustomContent;

// HD2Runtime's machine-readable custom stratagem schema (sdk/CustomStratagemSchema.json, HD2Runtime 0.30.0): the fields a custom
// stratagem has with their types, units, ranges, defaults and support status, the payload families and their carrier rules, the
// fields Runtime refuses (with the reason) and the donor catalogues. Read as published, keyed by Runtime's own field paths
// ("orbital.shell_interval", "support.items[].modify.rpm", "pelican.gun.rpm"); only the target adapter maps them to ModBuilder's
// semantic fields. Unknown keys are ignored (Runtime keeps schemaVersion 1 for additive changes), so a newer schema with more fields still
// reads; a schema whose format the adapter does not know is refused.
public sealed record SchemaField(string Path, string Type, bool Required, string? Unit, double? Min, double? Max, bool ExclusiveMin,
    string? Default, string? Catalog, bool ReviewedDonor, IReadOnlyList<string> Values, string? Doc, int? MinLength, int? MaxLength,
    string? Pattern, IReadOnlyList<object> RawValues, object? RawDefault);
// Groups: the carrier groups a family's payload must use (pod, weapon variant, expendable); empty: any group that carries it.
public sealed record SchemaFamily(string Family, bool Builder, string ApiField, IReadOnlyList<string> Beacons, IReadOnlyList<string> AllowFamilies,
    string Doc, IReadOnlyList<string> Notes, IReadOnlyList<string> Groups);
public sealed record SchemaRefusal(string Field, string Reason);
// What Runtime has shown for a payload family in play: live, partial or offline; its multiplayer status; the live evidence ids.
public sealed record FamilyEvidence(string Status, string Multiplayer, string Doc, IReadOnlyList<string> Evidence);

public sealed record SupportDonorItem(string Kind, int? Projectile);
public sealed record SupportDonor(string Name, string Family, bool Deliverable, int? Count, IReadOnlyList<SupportDonorItem> Items, bool Modifiable,
    bool PackageKnown, string? Reason, bool RateSelector = false)
{
    public bool DeliversWeapon => Items.Any(i => i.Kind == "weapon");
    public bool DeliversBackpack => Items.Any(i => i.Kind == "backpack");
    // A weapon whose round Runtime catalogues fires projectiles (launchers, rockets, guns); beams and sprays publish none.
    public bool FiresProjectiles => Items.Any(i => i.Kind == "weapon" && i.Projectile != null);
}
public sealed record RoundDonor(string Name, int Projectile);
public sealed record SentryDonor(string Name, bool Supported);
public sealed record EagleDonor(string Name, int? Uses, double? Cooldown, bool ImpactExplosionReplaceable);
public sealed record OrbitalPattern(int Salvos, int ShellsPerSalvo, double ShellInterval, double SalvoInterval, double Scatter, double SalvoScatter);
public sealed record OrbitalDonor(string Name, bool PackageKnown, OrbitalPattern? Pattern);
public sealed record ExplosionDonor(string Name, string? Effect);
// A carrier group: a structural pool of vanilla carriers. Payloads: the payload keys it may carry (orbital, orbital_native, pelican, runtime,
// support, pod, expendable, sentry, eagle, silo, weapon); Defaults: those whose default it is.
public sealed record CarrierGroup(string Name, string Beacon, IReadOnlyList<string> Families, bool Pod, bool Weapon, bool Eagle, string Doc,
    IReadOnlyList<string> Payloads, IReadOnlyList<string> Defaults);
// An item a carrier pod may hold (key support_weapon/<name>, backpack/<name>, player_weapon/<name>); unverified ones need an opt-in.
public sealed record PodItemEntry(string Key, string Name, string Kind, string Role, string Status, bool RateSelector)
{
    public bool Unverified => Status == "unverified";
}
public sealed record PodCarrier(string Name, int Capacity, IReadOnlyList<string> Roles);
// DirectDamage: the reviewed direct-hit damage overrides of the donor's own round (modify.direct_damage).
public sealed record CloneDonor(string Name, bool PackageKnown, IReadOnlyList<string> Pool, IReadOnlyList<string> Rounds, IReadOnlyList<double> DirectDamage);
public sealed record VariantRound(string Label, string Output);
// A weapon-variant host: Deliverable false is refused at registration (Refused says why); FiresRound false has no round to change;
// Shared names who else brings its type (world loot, other pods), whose copies the variant converts too (allow_shared).
public sealed record VariantDonor(string Name, string CompatibilityClass, bool PackageKnown, IReadOnlyList<VariantRound> Rounds, bool Deliverable = true,
    bool FiresRound = true, IReadOnlyList<string>? Shared = null, string? Refused = null)
{
    public bool IsShared => Shared is { Count: > 0 };
}
// Kind automatic: one blast a round; slow: a volume, which needs the gun's rpm at most MaxRpm.
public sealed record PelicanExplosionDonor(string Name, string Kind, double? MaxRpm);
public sealed record SiloDonor(string Name, bool PackageKnown);
public sealed record BlastExplosion(string Name, string Source, bool Objective);
public sealed record NativeCode(string? Name, long StableId, bool Catalogued, IReadOnlyList<string> Code);

public sealed record SchemaCatalogs(IReadOnlyList<SupportDonor> SupportDonors, IReadOnlyList<RoundDonor> Rounds, IReadOnlyList<SentryDonor> Sentries,
    IReadOnlyList<EagleDonor> Eagles, IReadOnlyList<OrbitalDonor> Orbitals, IReadOnlyList<ExplosionDonor> Explosions,
    IReadOnlyList<string> CarrierFamilies, IReadOnlyList<string> Beacons, IReadOnlyList<string> Directions, IReadOnlyList<string> Stratagems,
    IReadOnlyList<CarrierGroup> CarrierGroups, IReadOnlyList<PodItemEntry> PodItems, IReadOnlyList<PodCarrier> PodCarriers,
    IReadOnlyList<CloneDonor> CloneDonors, IReadOnlyList<VariantDonor> VariantDonors, IReadOnlyList<string> Sounds,
    IReadOnlyList<PelicanExplosionDonor> PelicanExplosions, IReadOnlyList<SiloDonor> Silos, IReadOnlyList<BlastExplosion> Blasts,
    IReadOnlyList<NativeCode> NativeCodes);

public sealed record CustomSchema(int SchemaVersion, string Format, string RuntimeVersion, string Api, string Status, string Source,
    IReadOnlyDictionary<string, SchemaField> Fields, IReadOnlyList<SchemaFamily> Families, IReadOnlyList<SchemaRefusal> Refused,
    int MaxDefinitions, int MaxOrbitalShells, SchemaCatalogs Catalogs, string Sha256,
    IReadOnlyDictionary<string, FamilyEvidence>? Evidence = null, string? Selection = null)
{
    public SchemaField? Field(string path) => Fields.GetValueOrDefault(path);
    public SchemaFamily? Family(string family) => Families.FirstOrDefault(f => f.Family == family);
    public SchemaRefusal? Refusal(string field) => Refused.FirstOrDefault(r => r.Field == field);
    public FamilyEvidence? EvidenceOf(string family) => Evidence?.GetValueOrDefault(family);
    public SupportDonor? SupportDonor(string? name) => Catalogs.SupportDonors.FirstOrDefault(d => d.Name == name);
    public RoundDonor? Round(string? name) => Catalogs.Rounds.FirstOrDefault(d => d.Name == name);
    public SentryDonor? Sentry(string? name) => Catalogs.Sentries.FirstOrDefault(d => d.Name == name);
    public EagleDonor? Eagle(string? name) => Catalogs.Eagles.FirstOrDefault(d => d.Name == name);
    public OrbitalDonor? Orbital(string? name) => Catalogs.Orbitals.FirstOrDefault(d => d.Name == name);
    public ExplosionDonor? Explosion(string? name) => Catalogs.Explosions.FirstOrDefault(d => d.Name == name);
    public CarrierGroup? Group(string? name) => Catalogs.CarrierGroups.FirstOrDefault(g => g.Name == name);
    public PodItemEntry? PodItem(string? key) => Catalogs.PodItems.FirstOrDefault(i => i.Key == key);
    public PodCarrier? PodCarrier(string? name) => Catalogs.PodCarriers.FirstOrDefault(c => c.Name == name);
    public CloneDonor? CloneDonor(string? name) => Catalogs.CloneDonors.FirstOrDefault(d => d.Name == name);
    public VariantDonor? VariantDonor(string? name) => Catalogs.VariantDonors.FirstOrDefault(d => d.Name == name);
    public PelicanExplosionDonor? PelicanExplosion(string? name) => Catalogs.PelicanExplosions.FirstOrDefault(d => d.Name == name);
    public SiloDonor? Silo(string? name) => Catalogs.Silos.FirstOrDefault(d => d.Name == name);
    public BlastExplosion? Blast(string? name) => Catalogs.Blasts.FirstOrDefault(d => d.Name == name);
}

public static class CustomSchemaReader
{
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>Reads a CustomStratagemSchema.json. Throws InvalidDataException when it is not one.</summary>
    public static CustomSchema Read(byte[] json, string source)
    {
        if (json.Length > MaxBytes) throw new InvalidDataException("Custom stratagem schema exceeds 4 MiB.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            Metadata.MetadataReader.RejectDuplicates(document.RootElement);
            var root = document.RootElement;
            var fields = new Dictionary<string, SchemaField>(StringComparer.Ordinal);
            void Collect(string prefix, JsonElement table)
            {
                foreach (var property in table.EnumerateObject())
                {
                    var path = prefix + property.Name;
                    var f = property.Value;
                    if (f.ValueKind != JsonValueKind.Object) continue;
                    fields[path] = Field(path, f);
                    if (f.TryGetProperty("fields", out var nested) && nested.ValueKind == JsonValueKind.Object) Collect(path + ".", nested);
                    if (f.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object && !item.TryGetProperty("type", out _)) Collect(path + "[].", item);
                    else if (f.TryGetProperty("item", out item) && item.ValueKind == JsonValueKind.Object) fields[path + "[]"] = Field(path + "[]", item);
                }
            }
            Collect("metadata.", root.GetProperty("metadata"));
            Collect("carrier.", root.GetProperty("carrier"));
            // familyOptions: options added to a family since (expendable.pod, the expendable carrier group), merged into its fields as the
            // Runtime's own validator merges them.
            var options = root.TryGetProperty("familyOptions", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
            var families = new List<SchemaFamily>();
            var specs = root.GetProperty("families").EnumerateArray().ToList();
            foreach (var extra in new[] { "podFamily", "variantFamily" })
                if (root.TryGetProperty(extra, out var spec) && spec.ValueKind == JsonValueKind.Object) specs.Add(spec);
            foreach (var f in specs)
            {
                var family = Text(f, "family");
                if (f.TryGetProperty("fields", out var familyFields) && familyFields.ValueKind == JsonValueKind.Object) Collect(family + ".", familyFields);
                var carrier = f.TryGetProperty("carrier", out var c) ? c : default;
                var groups = Strings(carrier, "groups").ToList();
                if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty(family, out var familyOptions) && familyOptions.ValueKind == JsonValueKind.Object)
                    foreach (var option in familyOptions.EnumerateObject())
                    {
                        if (option.Name == "carrier") groups.AddRange(Strings(option.Value, "groups").Where(g => !groups.Contains(g)));
                        else if (option.Value.ValueKind == JsonValueKind.Object) Collect(family + ".", JsonDocument.Parse("{\"" + option.Name + "\":" + option.Value.GetRawText() + "}").RootElement);
                    }
                families.Add(new(family, f.TryGetProperty("builder", out var b) && b.ValueKind == JsonValueKind.True, Text(f, "apiField"),
                    Strings(carrier, "beacon"), Strings(carrier, "allowFamilies"), OptionalText(f, "doc") ?? "", Strings(f, "notes"), groups));
            }
            var refused = root.GetProperty("unsupported").EnumerateArray().Select(u => new SchemaRefusal(Text(u, "field"), Text(u, "reason"))).ToArray();
            var limits = root.GetProperty("limits");
            var catalogs = root.GetProperty("catalogs");
            var evidence = new Dictionary<string, FamilyEvidence>(StringComparer.Ordinal);
            if (root.TryGetProperty("familyEvidence", out var ev) && ev.ValueKind == JsonValueKind.Object)
                foreach (var e in ev.EnumerateObject())
                    evidence[e.Name] = new(OptionalText(e.Value, "status") ?? "", OptionalText(e.Value, "multiplayer") ?? "", OptionalText(e.Value, "doc") ?? "",
                        Strings(e.Value, "evidence"));
            var schema = new CustomSchema(root.GetProperty("schemaVersion").GetInt32(), Text(root, "format"), Text(root, "hd2RuntimeVersion"),
                Text(root, "api"), OptionalText(root, "status") ?? "", source, fields, families, refused,
                limits.GetProperty("definitions").GetInt32(), limits.GetProperty("orbitalShells").GetInt32(), Catalogs(catalogs),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(json)).ToLowerInvariant(), evidence,
                root.TryGetProperty("selection", out var sel) ? OptionalText(sel, "doc") : null);
            Models.SemVersion.Parse(schema.RuntimeVersion);
            return schema;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Malformed custom stratagem schema: " + e.Message, e); }
    }

    private static SchemaCatalogs Catalogs(JsonElement c) => new(
        c.GetProperty("supportDonors").EnumerateArray().Select(d => new SupportDonor(Text(d, "name"), OptionalText(d, "family") ?? "support",
            Bool(d, "deliverable"), Int(d, "count"),
            d.TryGetProperty("items", out var items) ? items.EnumerateArray().Select(i => new SupportDonorItem(Text(i, "kind"), Int(i, "projectile"))).ToArray() : [],
            Bool(d, "modifiable"), Bool(d, "packageKnown"), OptionalText(d, "reason"), Bool(d, "rateSelector"))).ToArray(),
        c.GetProperty("projectileDonors").EnumerateArray().Select(d => new RoundDonor(Text(d, "name"), d.GetProperty("projectile").GetInt32())).ToArray(),
        c.GetProperty("sentryDonors").EnumerateArray().Select(d => new SentryDonor(Text(d, "name"), Bool(d, "supported"))).ToArray(),
        c.GetProperty("eagleDonors").EnumerateArray().Select(d => new EagleDonor(Text(d, "name"), Int(d, "uses"), Number(d, "cooldown"),
            Bool(d, "impactExplosionReplaceable"))).ToArray(),
        c.GetProperty("orbitals").EnumerateArray().Select(d => new OrbitalDonor(Text(d, "name"), Bool(d, "packageKnown"),
            d.TryGetProperty("pattern", out var p) && p.ValueKind == JsonValueKind.Object
                ? new OrbitalPattern(p.GetProperty("salvos").GetInt32(), p.GetProperty("shells_per_salvo").GetInt32(), p.GetProperty("shell_interval").GetDouble(),
                    p.GetProperty("salvo_interval").GetDouble(), p.GetProperty("scatter").GetDouble(), p.GetProperty("salvo_scatter").GetDouble())
                : null)).ToArray(),
        c.GetProperty("explosionDonors").EnumerateArray().Select(d => new ExplosionDonor(Text(d, "name"), OptionalText(d, "effect"))).ToArray(),
        StringArray(c, "carrierFamilies"), StringArray(c, "beacons"), StringArray(c, "directions"), StringArray(c, "stratagems"),
        Array(c, "carrierGroups").Select(g => new CarrierGroup(Text(g, "name"), OptionalText(g, "beacon") ?? "any", Strings(g, "families"), Bool(g, "pod"),
            Bool(g, "weapon"), Bool(g, "eagle"), OptionalText(g, "doc") ?? "", Strings(g, "payloads"), Strings(g, "defaults"))).ToArray(),
        Array(c, "podItems").Select(i => new PodItemEntry(Text(i, "key"), Text(i, "name"), Text(i, "kind"), OptionalText(i, "role") ?? "",
            OptionalText(i, "status") ?? "confirmed", Bool(i, "rateSelector"))).ToArray(),
        Array(c, "podCarriers").Select(i => new PodCarrier(Text(i, "name"), Int(i, "capacity") ?? 1, Strings(i, "roles"))).ToArray(),
        Array(c, "cloneDonors").Select(d => new CloneDonor(Text(d, "name"), Bool(d, "packageKnown"),
            d.TryGetProperty("pool", out var pool) && pool.ValueKind == JsonValueKind.Array ? pool.EnumerateArray().Select(x => Text(x, "name")).ToArray() : [],
            Strings(d, "rounds"), d.TryGetProperty("directDamage", out var dd) && dd.ValueKind == JsonValueKind.Array ? dd.EnumerateArray().Select(x => x.GetDouble()).ToArray() : [])).ToArray(),
        Array(c, "variantDonors").Select(d => new VariantDonor(Text(d, "name"), OptionalText(d, "compatibilityClass") ?? "", Bool(d, "packageKnown"),
            d.TryGetProperty("rounds", out var rounds) && rounds.ValueKind == JsonValueKind.Array
                ? rounds.EnumerateArray().Select(r => new VariantRound(OptionalText(r, "label") ?? Text(r, "output"), Text(r, "output"))).ToArray() : [],
            !(d.TryGetProperty("deliverable", out var dv) && dv.ValueKind == JsonValueKind.False), !(d.TryGetProperty("round", out var fr) && fr.ValueKind == JsonValueKind.False),
            Strings(d, "shared"), OptionalText(d, "refused"))).ToArray(),
        c.TryGetProperty("pelicanSounds", out var sounds) && sounds.ValueKind == JsonValueKind.Array ? sounds.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
        Array(c, "pelicanExplosionDonors").Select(d => new PelicanExplosionDonor(Text(d, "name"), OptionalText(d, "kind") ?? "automatic", Number(d, "maxRpm"))).ToArray(),
        Array(c, "siloDonors").Select(d => new SiloDonor(Text(d, "name"), Bool(d, "packageKnown"))).ToArray(),
        Array(c, "blastExplosions").Select(d => new BlastExplosion(Text(d, "name"), OptionalText(d, "source") ?? "", Bool(d, "objective"))).ToArray(),
        Array(c, "nativeCodes").Select(d => new NativeCode(OptionalText(d, "name"), d.GetProperty("stableId").GetInt64(), Bool(d, "catalogued"), Strings(d, "code"))).ToArray());

    private static SchemaField Field(string path, JsonElement f)
    {
        var raw = f.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array ? v
            : f.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("values", out var iv) && iv.ValueKind == JsonValueKind.Array ? iv : default;
        var rawValues = raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(Value).OfType<object>().ToArray() : [];
        return new(path, OptionalText(f, "type") ?? "object", Bool(f, "required"),
            OptionalText(f, "unit"), Number(f, "min"), Number(f, "max"), Bool(f, "exclusiveMin"), Default(f), OptionalText(f, "catalog"),
            Bool(f, "reviewedDonor"), rawValues.Select(x => x as string ?? Lua(x)).ToArray(),
            OptionalText(f, "doc"), Int(f, "minLength"), Int(f, "maxLength"), OptionalText(f, "pattern"), rawValues,
            f.TryGetProperty("default", out var d) ? Value(d) : null);
    }
    // A JSON scalar as a .NET value (string, double, bool), as the Runtime's validator compares them.
    private static object? Value(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
    private static string Lua(object value) => value switch
    {
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
    // A default is a value (60, "Orbital 120mm HE Barrage") or Runtime's description of one ("the carrier's own").
    private static string? Default(JsonElement f) => f.TryGetProperty("default", out var d) ? d.ValueKind switch
    {
        JsonValueKind.String => d.GetString(),
        JsonValueKind.Number => d.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    } : null;
    private static IEnumerable<JsonElement> Array(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    private static string Text(JsonElement e, string key) => e.GetProperty(key).GetString() is { Length: > 0 and <= 4096 } s ? s : throw new InvalidDataException("Invalid " + key);
    private static string? OptionalText(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static int? Int(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    private static double? Number(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static string[] Strings(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
    private static string[] StringArray(JsonElement e, string key) => e.GetProperty(key).EnumerateArray().Select(x => x.GetString()!).ToArray();
}
