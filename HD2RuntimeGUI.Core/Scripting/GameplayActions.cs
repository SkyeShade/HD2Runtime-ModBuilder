using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Scripting;

// HD2Runtime 0.30.0 gameplay actions for hand-written Lua (docs/runtime030-integration-plan.md, Phase 8.2): homing shots
// (hd2.projectiles.homing), enemy spawn weights (hd2.enemies.spawn_weight), limb injury and heal, a velocity change, the local player's
// loadout, held weapon and ammunition, resupply from a worn Supply Pack, the Runtime Pelican (hd2.pelican.spawn) and the weapon sound
// catalogue (hd2.sounds). Everything is read from the bound SDK's stub, event catalog and sound catalogue: a section exists only where the
// stub publishes its call, an options table is the stub's own ---@field list with the range and default its doc states, and every
// inserted call is built from the published signature. The inserted code never depends on the UI language; none of these actions is
// live-tested, so nothing here is presented as proven unless the live evidence says so.

/// <summary>One field of an action's options table as the stub declares it, with the range and default its doc publishes
/// ("(1..1440, default 90)", "(5 to 150, default 40)") and the literal choices of its type ("enemy"|"friendly"|nil).</summary>
public sealed record ActionOption(string Name, string Type, string? Doc, double? Min, double? Max, string? Default, IReadOnlyList<string> Choices)
{
    public bool IsBoolean => LuaApiIndex.Union(Type).Any(t => t.Trim() is "boolean" or "true" or "false");
    public bool IsNumber => LuaApiIndex.Union(Type).Any(t => t.Trim() is "number" or "integer");
    /// <summary>Null when the value is inside the published range, else the range as "min..max".</summary>
    public string? OutOfRange(double value) => Min is { } min && value < min || Max is { } max && value > max
        ? Number(Min ?? double.NegativeInfinity) + ".." + Number(Max ?? double.PositiveInfinity) : null;
    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public static partial class GameplayActions
{
    public const string Homing = "hd2.projectiles.homing", SpawnWeight = "hd2.enemies.spawn_weight", Injure = "hd2.actions.injure", HealLimb = "hd2.actions.heal_limb",
        HealLimbs = "hd2.actions.heal_limbs", AddVelocity = "hd2.actions.add_velocity", ResupplyFromPack = "hd2.actions.resupply_from_pack",
        PelicanSpawn = "hd2.pelican.spawn", SoundList = "hd2.sounds.list", SoundDescribe = "hd2.sounds.describe";
    public const string HomingOptions = "HD2HomingOptions", SpawnWeightOptions = "HD2SpawnWeightOptions", PelicanOptions = "HD2PelicanOptions",
        PelicanOrbit = "HD2PelicanOrbit", PelicanGun = "HD2PelicanGun", PlayerHandle = "HD2PlayerHandle";
    // The live-evidence families Runtime records for these actions (both pending: offline-proven, not live-tested).
    public const string HomingEvidence = "projectile_homing", SpawnWeightEvidence = "enemy_spawn_weights";
    // The player-state reads of HD2PlayerHandle (0.30.0), in the order they are offered.
    public static readonly string[] PlayerReads = ["loadout", "held_weapon", "backpack", "ammo"];

    public static bool Available(LuaApiIndex? api, string path) => api?.Resolve(path) != null;
    /// <summary>The stub's call (signature and doc) for an API path ("hd2.projectiles.homing", "HD2PlayerHandle:loadout").</summary>
    public static LuaMember? Call(LuaApiIndex? api, string path) => api?.Resolve(path) is { Kind: not "field" } m ? m : null;

    [GeneratedRegex(@"\((-?\d+(?:\.\d+)?)\s*(?:\.\.|to)\s*(-?\d+(?:\.\d+)?)(?:\s*,\s*default\s+([^),]+))?\)", RegexOptions.CultureInvariant)] private static partial Regex RangePattern();
    [GeneratedRegex(@"default[:\s]+([^),;]+)", RegexOptions.CultureInvariant)] private static partial Regex DefaultPattern();
    [GeneratedRegex("^\"([^\"]*)\"$", RegexOptions.CultureInvariant)] private static partial Regex LiteralPattern();
    /// <summary>The fields of an options class (HD2HomingOptions, HD2SpawnWeightOptions, …) in their published order, from the event catalog's API
    /// (the stub index sorts members by name), else from the stub; owner is left out (it defaults to the calling mod).</summary>
    public static IReadOnlyList<ActionOption> Options(SdkMetadata? sdk, string type) =>
        (sdk?.Events?.Api.Classes.GetValueOrDefault(type)?.Fields is { Length: > 0 } fields
            ? fields.Select(f => new LuaMember(f[0].TrimEnd('?'), "field", null, f.Length > 1 ? f[1] : null, f.Length > 2 ? f[2] : null))
            : sdk?.LuaApi?.Members(type) ?? [])
        .Where(m => m.Kind == "field" && m.Name != "owner").Select(Option).ToArray();
    public static ActionOption Option(LuaMember m)
    {
        var doc = m.Doc;
        var range = doc == null ? Match.Empty : RangePattern().Match(doc);
        double? Parse(Group g) => g.Success && double.TryParse(g.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        var fallback = doc == null ? null : DefaultPattern().Match(doc) is { Success: true } d ? d.Groups[1].Value.Trim() : null;
        var choices = LuaApiIndex.Union(m.Type ?? "").Select(t => LiteralPattern().Match(t.Trim())).Where(x => x.Success).Select(x => x.Groups[1].Value).ToArray();
        return new(m.Name, m.Type ?? "", doc, Parse(range.Groups[1]), Parse(range.Groups[2]), range.Groups[3].Success ? range.Groups[3].Value.Trim() : fallback, choices);
    }
    /// <summary>The literal values a parameter's type allows ("head"|"chest"|… for a limb, "primary"|"secondary"|"support" for an ammo slot).</summary>
    public static IReadOnlyList<string> Literals(string? type) => type == null ? []
        : LuaApiIndex.Union(type).Select(t => LiteralPattern().Match(t.Trim())).Where(x => x.Success).Select(x => x.Groups[1].Value).ToArray();
    /// <summary>The literal values of one parameter of a stub call, from its signature ("injure(player: HD2PlayerHandle, limb: "head"|…, …)").</summary>
    public static IReadOnlyList<string> ParameterLiterals(LuaMember? call, string parameter)
    {
        if (call?.Signature is not { } signature) return [];
        var at = signature.IndexOf(parameter + ": ", StringComparison.Ordinal);
        if (at < 0) return [];
        var rest = signature[(at + parameter.Length + 2)..];
        var depth = 0; var end = 0; var quote = false;
        for (; end < rest.Length; end++)
        {
            var c = rest[end];
            if (c == '"') quote = !quote;
            else if (quote) continue;
            else if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') { if (depth == 0) break; depth--; }
            else if (c == ',' && depth == 0) break;
        }
        return Literals(rest[..end]);
    }
    /// <summary>The first "at most N m/s" a call's doc states (add_velocity: the change is at most 25 m/s).</summary>
    public static double? MaxSpeed(LuaMember? call) => call?.Doc is { } doc && Regex.Match(doc, @"change is at most (\d+(?:\.\d+)?) m/s", RegexOptions.CultureInvariant) is { Success: true } m
        ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    // ---- inserted calls (UI language independent) -------------------------------------------------------------------------------------
    private static string Quote(string value) => Generation.LuaGenerator.Quote(value);
    private static string Literal(ActionOption option, string value) =>
        option.IsBoolean && value is "true" or "false" ? value
        : option.IsNumber && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? ActionOption.Number(n)
        : Quote(value);
    /// <summary>An options table with the given values in the published order ({target = 'enemy', turn_rate = 120}); empty values are left out.</summary>
    public static string Table(IReadOnlyList<ActionOption> options, IReadOnlyDictionary<string, string> values)
    {
        var parts = options.Where(o => values.TryGetValue(o.Name, out var v) && !string.IsNullOrWhiteSpace(v)).Select(o => o.Name + " = " + Literal(o, values[o.Name].Trim())).ToArray();
        return "{" + string.Join(", ", parts) + "}";
    }
    private const string LocalPlayer = "local player = hd2.local_player()\nif player then\n";
    private static string Refusal(string handle, string what) =>
        "if " + handle + ".status == 'refused' then hd2.mod():log(" + Quote(what + " refused: ") + " .. tostring(" + handle + ".code) .. ' ' .. tostring(" + handle + ".reason)) end\n";

    public static string HomingCall(string weapon, IReadOnlyList<ActionOption> options, IReadOnlyDictionary<string, string> values)
    {
        var table = Table(options, values);
        return "local homing = hd2.projectiles.homing(" + Quote(weapon) + (table == "{}" ? "" : ", " + table) + ")\n" + Refusal("homing", "homing")
            + "-- homing:stop() ends it (shots in flight fly on); homing:stats() reports this mission's shots\n";
    }
    /// <summary>One enemy type's spawn weight; opts always carry allow_unverified_effect, which Runtime requires until the live test passes.</summary>
    public static string SpawnWeightCall(string enemy, double multiplier, IReadOnlyList<int>? difficulties = null) =>
        "local weight = hd2.enemies.spawn_weight(" + Quote(enemy) + ", " + ActionOption.Number(multiplier) + ", {allow_unverified_effect = true"
        + (difficulties is { Count: > 0 } d ? ", difficulties = {" + string.Join(", ", d.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "}" : "") + "})\n"
        + Refusal("weight", "spawn weight") + "-- weight:stop() writes the vanilla weights back\n";
    public static string InjureCall(string limb, int damage) => LocalPlayer + "    local action = hd2.actions.injure(player, " + Quote(limb) + ", " + damage.ToString(CultureInfo.InvariantCulture) + ")\n    "
        + Refusal("action", "injure") + "end\n";
    public static string HealLimbCall(string? limb) => LocalPlayer + (limb == null ? "    local action = hd2.actions.heal_limbs(player)\n" : "    local action = hd2.actions.heal_limb(player, " + Quote(limb) + ")\n")
        + "    " + Refusal("action", limb == null ? "heal_limbs" : "heal_limb") + "end\n";
    public static string AddVelocityCall(double x, double y, double z) => LocalPlayer + "    local action = hd2.actions.add_velocity(player, {x = " + ActionOption.Number(x) + ", y = " + ActionOption.Number(y)
        + ", z = " + ActionOption.Number(z) + "})\n    " + Refusal("action", "add_velocity") + "end\n";
    public static string ResupplyCall() => LocalPlayer + "    local action = hd2.actions.resupply_from_pack(player)\n    " + Refusal("action", "resupply_from_pack") + "end\n";
    /// <summary>One read of the local player's state (player:loadout(), :held_weapon(), :backpack(), :ammo(slot)): a table, or nil and why.</summary>
    public static string PlayerReadCall(string read, string? slot = null) => LocalPlayer
        + "    local value, why = player:" + read + "(" + (slot == null ? "" : Quote(slot)) + ")\n"
        + "    if not value then hd2.mod():log(" + Quote(read + " unavailable: ") + " .. tostring(why)) end\nend\n";
    /// <summary>An empty Runtime Pelican over the local player, hovering for hover seconds; its chin gun takes a catalogued firing sound when one is given.</summary>
    public static string PelicanCall(double? hover, string? sound) =>
        "local player = hd2.local_player()\nlocal here = player and player:position()\nif here then\n    local pelican = hd2.pelican.spawn({position = here"
        + (hover is { } h ? ", hover = " + ActionOption.Number(h) : "") + (sound != null ? ", gun = {sound = " + Quote(sound) + "}" : "")
        + ", on_event = function(event)\n        if event.kind == 'refused' then hd2.mod():log('pelican refused: ' .. tostring(event.code) .. ' ' .. tostring(event.reason)) end\n    end})\nend\n";
    public static string SoundCall(string name) => "local sound = hd2.sounds.describe(" + Quote(name) + ")\n";
}

/// <summary>One firing sound of HD2Runtime's weapon sound catalogue (sdk/WeaponSoundCatalogue.json; hd2.sounds.list / describe): its semantic
/// name (what a Pelican gun's sound takes), label, family, kind, the stratagem whose package provides its bank, and its published facts.</summary>
public sealed record WeaponSound(string Name, string Label, string Family, string Kind, string? Stratagem, bool ResidentOnly, bool PelicanDefault, bool Midi,
    double DesignedRpm, double? RangeM);

/// <summary>The weapon sound catalogue of the SDK this build pins (0.30.3, embedded byte for byte as Scripting/Bundled/WeaponSoundCatalogue-
/// 0.30.3.json). It is offered only for an SDK whose stub publishes hd2.sounds; the names are Runtime's, never translated.</summary>
public static class WeaponSounds
{
    public const string Resource = "HD2RuntimeGUI.Core.Scripting.Bundled.WeaponSoundCatalogue-0.30.3.json";
    private static readonly Lazy<byte[]> BundledBytes = new(() =>
    {
        using var stream = typeof(WeaponSounds).Assembly.GetManifestResourceStream(Resource) ?? throw new InvalidDataException("Weapon sound catalogue missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    });
    private static readonly Lazy<IReadOnlyList<WeaponSound>> Bundled = new(() => Read(BundledBytes.Value));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> BundledAliases = new(() => Aliases(BundledBytes.Value));
    public static IReadOnlyList<WeaponSound> For(LuaApiIndex? api) => GameplayActions.Available(api, GameplayActions.SoundList) ? Bundled.Value : [];
    /// <summary>Older sound names the Runtime still resolves (the catalogue's aliases): 0.30.2 renamed primary/smg37/seaf to seaf/3 and
    /// secondary/gp31/alt to other/wep_grenadier_rifle/1 when the SMG-37 Defender and the GP-31 resolved to their proven roots. The Runtime
    /// plays the old names; the SDK's custom stratagem schema lists only the new ones, so a saved custom stratagem with an old name is
    /// offered its new name where the sound is chosen.</summary>
    public static IReadOnlyDictionary<string, string> Renamed => BundledAliases.Value;
    public static IReadOnlyDictionary<string, string> Aliases(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (!doc.RootElement.TryGetProperty("aliases", out var aliases)) return new Dictionary<string, string>(StringComparer.Ordinal);
        var names = doc.RootElement.GetProperty("sounds").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        var map = aliases.EnumerateObject().ToDictionary(a => a.Name, a => a.Value.GetString()!, StringComparer.Ordinal);
        // An alias names a catalogued sound and is not itself one.
        if (map.Any(a => string.IsNullOrWhiteSpace(a.Key) || names.Contains(a.Key) || !names.Contains(a.Value))) throw new InvalidDataException("Inconsistent weapon sound aliases.");
        return map;
    }
    public static IReadOnlyList<WeaponSound> Read(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported weapon sound catalogue.");
        string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var sounds = root.GetProperty("sounds").EnumerateArray().Select(s => new WeaponSound(Text(s, "name")!, Text(s, "label") ?? Text(s, "name")!, Text(s, "family") ?? "other",
            Text(s, "kind") ?? "shot", Text(s, "stratagem"), s.GetProperty("residentOnly").GetBoolean(), s.GetProperty("pelicanDefault").GetBoolean(), s.GetProperty("midi").GetBoolean(),
            s.GetProperty("designedRpm").GetDouble(), s.TryGetProperty("rangeM", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null)).ToArray();
        if (sounds.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.Kind is not ("shot" or "loop")) || sounds.Select(s => s.Name).Distinct().Count() != sounds.Length
            || root.GetProperty("summary").GetProperty("entries").GetInt32() != sounds.Length)
            throw new InvalidDataException("Inconsistent weapon sound catalogue.");
        return sounds;
    }
}
