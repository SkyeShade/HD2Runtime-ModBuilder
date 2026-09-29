using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Event scripting (hd2runtime.events.v1, sdk/EventCatalog.json; unreleased HD2Runtime 0.28.0 development SDKs): every event a mod can
// subscribe to (with its payload, phase, source and status), the live and snapshot handles, the scripting API (events, timers, keybinds,
// mod context) and the gameplay actions (explosions, projectiles, statuses, healing) with their limits. ModBuilder uses it for the
// reference panel, the action pickers, the snippets and the custom Lua checks; it never interprets custom Lua beyond that.
public sealed record EventPayloadField(string Name, string Type, string? Doc)
{
    [JsonIgnore] public bool Nullable => Type.EndsWith("|nil", StringComparison.Ordinal) || Type.Contains("|nil|", StringComparison.Ordinal);
}
public sealed record EventDefinition(string Name, string Status, string? Source, string Phase, bool Hot, string? Summary, EventPayloadField[]? Payload, string? Reason)
{
    public const string Available = "available";
    [JsonIgnore] public bool IsAvailable => Status == Available;
}
public sealed record ApiMethod(string Name, string[][] Params, string? Returns, string? Doc)
{
    [JsonIgnore] public string Signature => Name + "(" + string.Join(", ", Params.Select(p => p.Length > 1 ? p[0] + ": " + p[1] : p[0])) + ")" + (string.IsNullOrEmpty(Returns) ? "" : " → " + Returns);
}
public sealed record ApiClass(string? Doc, string[][]? Fields, ApiMethod[]? Methods);
public sealed record EventHandle(string? Doc, string[][] Fields, ApiMethod[] Methods);
public sealed record EventApi(Dictionary<string, ApiClass> Classes, ApiMethod[] Functions, string[][] Fields);
public sealed record EventCatalogSummary(int Events, string[] Available, string[] Blocked);
public sealed record NamedExplosion(string Name, string[] Aliases, bool SharedType, bool LiveProven);
public sealed record ActionRateLimit(int Burst, int PerSecond, int? PerTargetBurst = null, int? PerTargetPerSecond = null);
public sealed record ExplosionActions(bool HostOnly, bool InMissionOnly, string CreditedTo, string OtherPlayersSee, string Api, ActionRateLimit RateLimit,
    NamedExplosion[] Named, string[] Weapons, string[] LiveProvenWeapons, string Assets);
public sealed record ProjectileActions(bool HostOnly, bool InMissionOnly, string CreditedTo, string OtherPlayersSee, string Api, ActionRateLimit RateLimit,
    string[] Weapons, string[] LiveProven, Dictionary<string, string> Options, string[] SideEffects, string Assets);
public sealed record StatusAction(string Id, string Name, string Family, double? Duration, bool LiveProven);
public sealed record StatusActions(bool HostOnly, bool InMissionOnly, string CreditedTo, string OtherPlayersSee, string Api, ActionRateLimit RateLimit,
    StatusAction[] Statuses, JsonElement Options, string[] Unproven);
public sealed record BlockedAction(string Status, string Reason);
public sealed record EventActions(ExplosionActions Explosions, ProjectileActions Projectiles, StatusActions StatusEffects, BlockedAction SpawnEntity);
public sealed record EventCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, EventCatalogSummary Summary, Dictionary<string, string> Model,
    Dictionary<string, string> Sources, EventDefinition[] Events, Dictionary<string, EventHandle> Handles, EventApi Api, EventActions Actions);

public sealed class EventCatalog
{
    public required EventDefinition[] Events { get; init; }
    public required IReadOnlyDictionary<string, string> Model { get; init; }
    public required IReadOnlyDictionary<string, string> Sources { get; init; }
    public required IReadOnlyDictionary<string, EventHandle> Handles { get; init; }
    public required EventApi Api { get; init; }
    public required EventActions Actions { get; init; }
    public EventDefinition? Event(string name) => Events.FirstOrDefault(e => e.Name == name);
    // Names hd2.explosions.spawn accepts: the named explosions (and their aliases) and the weapons with a catalogued explosion.
    public IEnumerable<string> ExplosionNames => Actions.Explosions.Named.SelectMany(n => n.Aliases.Prepend(n.Name)).Concat(Actions.Explosions.Weapons);
    public bool ExplosionLiveProven(string name) => Actions.Explosions.Named.Any(n => (n.Name == name || n.Aliases.Contains(name)) && n.LiveProven)
        || Actions.Explosions.LiveProvenWeapons.Contains(name);
}

public static class EventCatalogReader
{
    public const string FileName = "EventCatalog.json", Contract = "hd2runtime.events.v1";
    public const int MaxBytes = 4 * 1024 * 1024;
    private static readonly Regex EventName = new(@"\A[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly Regex StatusId = new(@"\A[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);
    // Research notes are not part of the reference.
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent event catalog (check {line})."); }

    public static EventCatalog Read(byte[] bytes, string version)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Event catalog exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported event catalog contract.");
            var c = JsonSerializer.Deserialize<EventCatalogJson>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && c.Events.Length > 0 && c.Events.Select(e => e.Name).Distinct().Count() == c.Events.Length);
            foreach (var e in c.Events)
            {
                Check(EventName.IsMatch(e.Name) && e.Phase is "state" or "post" or "pre");
                // Available events publish their source and payload; blocked ones say why, and publish nothing a mod could subscribe to.
                Check(e.IsAvailable
                    ? e.Source != null && c.Sources.ContainsKey(e.Source) && e.Payload != null && e.Payload.All(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Type))
                        && e.Payload.Select(p => p.Name).Distinct().Count() == e.Payload.Length
                    : e.Status == "blocked" && !string.IsNullOrWhiteSpace(e.Reason));
            }
            var s = c.Summary;
            Check(s.Events == c.Events.Length && s.Available.Order(StringComparer.Ordinal).SequenceEqual(c.Events.Where(e => e.IsAvailable).Select(e => e.Name).Order(StringComparer.Ordinal))
                && s.Blocked.Order(StringComparer.Ordinal).SequenceEqual(c.Events.Where(e => !e.IsAvailable).Select(e => e.Name).Order(StringComparer.Ordinal)));
            Check(c.Api.Functions.All(f => !string.IsNullOrWhiteSpace(f.Name)) && c.Api.Fields.All(f => f.Length >= 2 && !string.IsNullOrWhiteSpace(f[0])));
            var a = c.Actions;
            Check(a.Explosions.Api == "hd2.explosions.spawn" && a.Projectiles.Api == "hd2.projectiles.spawn" && a.StatusEffects.Api == "hd2.status.apply"
                && a.Explosions.Named.All(n => !string.IsNullOrWhiteSpace(n.Name)) && a.Explosions.LiveProvenWeapons.All(a.Explosions.Weapons.Contains)
                && a.Projectiles.LiveProven.All(a.Projectiles.Weapons.Contains) && a.StatusEffects.Statuses.All(x => StatusId.IsMatch(x.Id))
                && a.StatusEffects.Statuses.Select(x => x.Id).Distinct().Count() == a.StatusEffects.Statuses.Length && a.SpawnEntity.Status is "blocked" or "available");
            return new() { Events = c.Events, Model = c.Model, Sources = c.Sources, Handles = c.Handles, Api = c.Api, Actions = c.Actions };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed event catalog.", e); }
    }
}
