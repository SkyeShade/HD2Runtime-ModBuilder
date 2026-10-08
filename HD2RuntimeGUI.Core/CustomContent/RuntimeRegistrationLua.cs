using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>A faithful port of the HD2Runtime SDK's custom stratagem compiler (sdk/tools/custom_stratagem_project.py compile_lua, _payload_lua
/// and _lua_value, HD2Runtime 0.30.0): one valid stratagem's project form becomes exactly the lines of its hd2.custom_stratagem.register
/// call the SDK writes, field for field, byte for byte. The tests hold it to the SDK compiler's output for every Runtime example.</summary>
public static class RuntimeRegistrationLua
{
    public const string GeneratedHeader = "-- Generated from custom_stratagems.json by the HD2Runtime SDK";
    private static readonly string[] WeaponOrder = ["projectile", "rpm", "spread", "ammo", "recoil", "sound", "impact_explosion", "direct_damage", "rounds"];
    private static readonly string[] PelicanGunOrder = ["behave_as", "rate_multiplier", "rpm", "round", "casing", "spread", "recoil", "unlimited_ammo",
        "face_target", "sound", "impact_explosion", "aim_height"];
    private static readonly Regex Identifier = new(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);

    /// <summary>A Lua expression written as is (a typed handle such as hd2.support_weapon('MG-43 Machine Gun')).</summary>
    private sealed record Expr(string Code);
    /// <summary>An ordered table: ((key, value), ...).</summary>
    private sealed record Pairs(IReadOnlyList<(string Key, object? Value)> Items);

    /// <summary>The field lines of one register call ("    id='hmg_sentry',", ...), as the SDK writes them between
    /// "hd2.custom_stratagem.register({" and "})". The payload of ModBuilder's scripted family (no project form) writes delivery='runtime' or a
    /// vanilla support pod.</summary>
    public static IReadOnlyList<string> FieldLines(JsonObject s, string? scriptedPod = null)
    {
        var lines = new List<string>();
        var body = new List<(string, object?)> { ("id", Value(s["id"])), ("name", Value(s["name"])) };
        if (s.ContainsKey("name_cased")) body.Add(("name_cased", Value(s["name_cased"])));
        body.Add(("description", Value(s["description"])));
        foreach (var (k, v) in body) lines.Add("    " + k + "=" + Lua(v) + ",");
        lines.Add("    icon=hd2.resources.image(" + LuaString(((JsonObject)s["icon"]!)["image"]!.GetValue<string>()) + "),");
        lines.Add("    code=" + Lua(Value(s["code"])) + ",");
        if (s["cooldown"] != null) lines.Add("    cooldown=" + Lua(Value(s["cooldown"])) + ",");
        if (s["uses"] != null) lines.Add("    uses=" + Lua(Value(s["uses"])) + ",");
        if (s["max_per_player"] != null) lines.Add("    max_per_player=" + Lua(Value(s["max_per_player"])) + ",");
        if (s["traits"] is JsonArray { Count: > 0 } traits) lines.Add("    traits=" + Lua(Value(traits)) + ",");
        lines.Add("    carrier=" + Lua(OrderedTable((JsonObject)s["carrier"]!, ["group", "slots", "beacon", "prefer_families", "allow_families", "exclude"])) + ",");
        var payload = (JsonObject)s["payload"]!;
        if (payload["family"]?.GetValue<string>() == RuntimeProjectForm.ScriptFamily)
            lines.Add("    delivery=" + (scriptedPod == null ? "'runtime'" : Lua(new Pairs([("stratagem", scriptedPod)]))) + ",");
        else
        {
            var (key, value) = Payload(payload);
            lines.Add("    " + key + "=" + value + ",");
        }
        if (s["assets"] is JsonArray { Count: > 0 } assets) lines.Add("    assets=" + Lua(Value(assets)) + ",");
        return lines;
    }

    /// <summary>The SDK's whole register call for one stratagem.</summary>
    public static string Register(JsonObject s) => "hd2.custom_stratagem.register({\n" + string.Join("\n", FieldLines(s)) + "\n})";

    /// <summary>The SDK's src/addon.lua for a valid project (compile_lua): header, the Runtime require, the optional log line, one register call
    /// per stratagem.</summary>
    public static string Addon(JsonObject project, string sourceSha256 = "")
    {
        var lines = new List<string>
        {
            GeneratedHeader + " (hd2.py custom-stratagem compile; format " + RuntimeProjectForm.Format + ").",
            "-- Edit custom_stratagems.json, not this file: the build compiles it again." + (sourceSha256.Length > 0 ? " Source SHA-256 " + sourceSha256 + "." : ""),
            "local hd2=require('mods/skyeshade/hd2runtime')",
        };
        if (project["log"] is JsonValue log && log.TryGetValue<string>(out var text) && text.Length > 0) { lines.Add("local mod=hd2.mod()"); lines.Add("mod:log(" + LuaString(text) + ")"); }
        foreach (var s in ((JsonArray)project["stratagems"]!).OfType<JsonObject>()) lines.Add(Register(s));
        return string.Join("\n", lines) + "\n";
    }

    private static (string Key, string Value) Payload(JsonObject payload)
    {
        var family = payload["family"]!.GetValue<string>();
        switch (family)
        {
            case "support":
            {
                var items = ((JsonArray)payload["items"]!).Select(i =>
                {
                    var item = Copy((JsonObject)i!);
                    if (item.TryGetValue("modify", out var m) && m is Dictionary<string, object?> modify) item["modify"] = Ordered(modify, WeaponOrder);
                    return (object?)Ordered(item, ["donor", "count", "modify"]);
                }).ToList();
                return ("delivery", Lua(new Pairs([("family", "support"), ("items", items)])));
            }
            case "pod":
                return ("delivery", Lua(new Pairs([("family", "support"), ("items", PodLua((JsonArray)payload["items"]!))])));
        }
        var body = Copy(payload);
        body.Remove("family");
        switch (family)
        {
            case "expendable":
                if (body.TryGetValue("modify", out var em) && em is Dictionary<string, object?> modify) body["modify"] = Ordered(modify, WeaponOrder);
                if (body.TryGetValue("presentation", out var ep) && ep is Dictionary<string, object?> pres) body["presentation"] = Ordered(pres, ["name", "icon"]);
                if (payload["pod"] is JsonArray pod) body["pod"] = PodLua(pod);
                return ("delivery", Lua(new Pairs([("family", "expendable"), .. Ordered(body, ["weapon", "presentation", "modify", "round", "level", "pod"]).Items])));
            case "weapon":
                if (body.TryGetValue("presentation", out var wp) && wp is Dictionary<string, object?> wpres) body["presentation"] = Ordered(wpres, ["name", "icon"]);
                if (body.ContainsKey("round")) body["round"] = new Expr("hd2.attack_output(" + LuaString((string)body["round"]!) + ")");
                return ("delivery", Lua(new Pairs([("family", "weapon"), .. Ordered(body, ["weapon", "presentation", "round", "model", "model_use", "allow_shared"]).Items])));
            case "pelican":
                foreach (var (key, order) in new[] { ("orbit", new[] { "radius", "altitude", "duration", "period", "entry" }), ("approach", ["distance", "height"]), ("gun", PelicanGunOrder) })
                    if (body.TryGetValue(key, out var part) && part is Dictionary<string, object?> table) body[key] = Ordered(table, order);
                return ("pelican", Lua(Ordered(body, ["hover", "orbit", "gun", "approach"])));
            case "sentry":
                if (body.TryGetValue("weapon", out var sw) && sw is Dictionary<string, object?> weapon) body["weapon"] = Ordered(weapon, WeaponOrder);
                return ("sentry", Lua(Ordered(body, ["donor", "weapon"])));
            case "silo":
                return ("silo", Lua(Ordered(body, ["donor", "blast", "fallback"])));
            case "eagle":
                if (body.TryGetValue("payload", out var ep2) && ep2 is Dictionary<string, object?> inner) body["payload"] = Ordered(inner, ["impact_explosion"]);
                return ("eagle", Lua(Ordered(body, ["donor", "uses", "rearm_seconds", "payload"])));
            case "orbital":
                return ("orbital", Lua(Ordered(body, ["native", "shell", "pattern", "salvos", "shells_per_salvo", "shell_interval", "salvo_interval", "scatter", "salvo_scatter", "impact_explosion"])));
        }
        throw new InvalidDataException("unsupported payload family " + family);
    }

    private static List<object?> PodLua(JsonArray items) => [.. items.OfType<JsonObject>().Select(i =>
    {
        var item = Copy(i);
        item["item"] = Handle((string)item["item"]!);
        if (item.TryGetValue("modify", out var m) && m is Dictionary<string, object?> modify) item["modify"] = Ordered(modify, WeaponOrder);
        return (object?)Ordered(item, ["item", "count", "modify", "allow_unverified_effect"]);
    })];

    private static object Handle(string key)
    {
        if (key == "clone") return "clone";
        var slash = key.IndexOf('/');
        var (kind, name) = slash < 0 ? (key, "") : (key[..slash], key[(slash + 1)..]);
        var fn = kind switch { "support_weapon" => "hd2.support_weapon", "backpack" => "hd2.backpack", "player_weapon" => "hd2.weapon", _ => throw new InvalidDataException("unknown pod item " + key) };
        return new Expr(fn + "(" + LuaString(name) + ")");
    }

    // _ordered: the known keys in their canonical order, then any other key sorted.
    private static Pairs Ordered(Dictionary<string, object?> table, IReadOnlyList<string> order) =>
        new([.. order.Where(table.ContainsKey).Select(k => (k, table[k])), .. table.Keys.Where(k => !order.Contains(k)).Order(StringComparer.Ordinal).Select(k => (k, table[k]))]);
    private static Pairs OrderedTable(JsonObject table, IReadOnlyList<string> order) => Ordered(Copy(table), order);

    // The project form as Python values: dict, list, str, int, float, bool.
    private static Dictionary<string, object?> Copy(JsonObject o) => o.ToDictionary(p => p.Key, p => Value(p.Value), StringComparer.Ordinal);
    private static object? Value(JsonNode? n) => n switch
    {
        null => null,
        JsonObject o => Copy(o),
        JsonArray a => a.Select(Value).ToList(),
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // A JSON integer stays an integer (Python's int), anything else is a float.
            JsonValueKind.Number => v.ToJsonString() is var t && !t.Contains('.') && !t.Contains('e') && !t.Contains('E') && long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : RuntimeProjectForm.Double(v),
            _ => null,
        },
        _ => null,
    };

    // _lua_value
    private static string Lua(object? v) => v switch
    {
        Expr e => e.Code,
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => d == Math.Floor(d) && !double.IsInfinity(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : RuntimeProjectForm.Number(d),
        string s => LuaString(s),
        Pairs o => "{" + string.Join(",", o.Items.Select(p => LuaKey(p.Key) + "=" + Lua(p.Value))) + "}",
        IEnumerable<object?> list => "{" + string.Join(",", list.Select(Lua)) + "}",
        // A table the compiler has no canonical order for: its keys in their written order (Python dicts keep insertion order).
        Dictionary<string, object?> table => "{" + string.Join(",", table.Select(p => LuaKey(p.Key) + "=" + Lua(p.Value))) + "}",
        _ => throw new InvalidDataException("unsupported value: " + v),
    };
    private static string LuaKey(string k) => Identifier.IsMatch(k) ? k : "[" + LuaString(k) + "]";
    /// <summary>A Lua string as the SDK writes it: single quotes; backslash, quote and newline escaped; other control characters as \ddd.</summary>
    public static string LuaString(string s)
    {
        var b = new StringBuilder("'");
        foreach (var ch in s)
        {
            if (ch == '\\') b.Append("\\\\");
            else if (ch == '\'') b.Append("\\'");
            else if (ch == '\n') b.Append("\\n");
            else if (ch < 32 || ch == 127) b.Append('\\').Append(((int)ch).ToString("000", CultureInfo.InvariantCulture));
            else b.Append(ch);
        }
        return b.Append('\'').ToString();
    }
}
