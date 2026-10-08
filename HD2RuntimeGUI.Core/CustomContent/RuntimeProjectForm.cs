using System.Globalization;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>HD2Runtime's custom stratagem project format (custom_stratagems.json, format hd2runtime-custom-stratagems/1,
/// docs/custom-stratagem-builder.md): the structured form the Runtime's own validator and compiler read. ModBuilder's semantic model is
/// written into it (and read back from it, for imports and templates) here; RuntimeProjectValidator and RuntimeRegistrationLua are faithful
/// ports of the SDK's sdk/tools/custom_stratagem_project.py over this form, so what ModBuilder builds is what the Runtime's tools accept and
/// compile. The scripted family has no project form (the Runtime's "script" family is Lua only): it is written as payload {family = "script"}
/// and only its metadata and carrier are checked.</summary>
public static class RuntimeProjectForm
{
    public const string Format = "hd2runtime-custom-stratagems/1";
    public const string ScriptFamily = "script";

    /// <summary>The project file: every custom stratagem (scripted ones included, as {family = "script"}).</summary>
    public static JsonObject Project(IEnumerable<CustomStratagem> stratagems, IReadOnlyList<PodItemEntry>? podItems = null, string? log = null, Func<string, bool>? sharedHost = null)
    {
        var project = new JsonObject { ["format"] = Format };
        if (log != null) project["log"] = log;
        project["stratagems"] = new JsonArray([.. stratagems.Select(s => (JsonNode)Stratagem(s, podItems, sharedHost))]);
        return project;
    }

    /// <summary>One custom stratagem as custom_stratagems.json holds it. Values are written as the model holds them, also invalid ones: the
    /// validator reports them where they are.</summary>
    /// <param name="sharedHost">Whether a weapon-variant host's type is shared (CustomStratagemSchema variantDonors[].shared): written with allow_shared = true.</param>
    public static JsonObject Stratagem(CustomStratagem s, IReadOnlyList<PodItemEntry>? podItems = null, Func<string, bool>? sharedHost = null)
    {
        var o = new JsonObject { ["id"] = s.Id, ["name"] = s.Name };
        if (s.NameCased != null) o["name_cased"] = s.NameCased;
        o["description"] = s.Description;
        if (s.Icon != null) o["icon"] = new JsonObject { ["image"] = s.Icon, ["source"] = "images/" + s.Icon + ".png" };
        o["code"] = new JsonArray([.. s.Code.Select(c => (JsonNode)c)]);
        Put(o, "cooldown", s.Cooldown);
        Put(o, "uses", s.Uses);
        Put(o, "max_per_player", s.MaxPerPlayer);
        if (s.Traits.Count > 0) o["traits"] = Strings(s.Traits);
        var carrier = new JsonObject();
        if (s.Carrier.Group != null) carrier["group"] = s.Carrier.Group;
        Put(carrier, "slots", s.Carrier.Slots);
        if (s.Carrier.Beacon != null) carrier["beacon"] = s.Carrier.Beacon;
        if (s.Carrier.Prefer.Count > 0) carrier["prefer_families"] = Strings(s.Carrier.Prefer);
        if (s.Carrier.Allow.Count > 0) carrier["allow_families"] = Strings(s.Carrier.Allow);
        if (s.Carrier.Exclude.Count > 0) carrier["exclude"] = Strings(s.Carrier.Exclude);
        o["carrier"] = carrier;
        o["payload"] = Payload(s, podItems, sharedHost);
        if (s.Assets.Count > 0) o["assets"] = Strings(s.Assets);
        return o;
    }

    private static JsonObject Payload(CustomStratagem s, IReadOnlyList<PodItemEntry>? podItems, Func<string, bool>? sharedHost)
    {
        var d = s.Delivery;
        switch (d.Family)
        {
            case DeliveryFamily.Support:
            {
                var item = new JsonObject { ["donor"] = d.Support?.Item ?? "" };
                Put(item, "count", d.Support?.Count);
                if (Modify(d.Support?.Weapon) is { } modify) item["modify"] = modify;
                return new() { ["family"] = "support", ["items"] = new JsonArray(item) };
            }
            case DeliveryFamily.Pod:
                return new() { ["family"] = "pod", ["items"] = PodItems(d.Pod?.Items ?? [], podItems) };
            case DeliveryFamily.Expendable:
            {
                var e = d.Expendable ?? new();
                var p = new JsonObject { ["family"] = "expendable", ["weapon"] = e.Weapon };
                if (Presentation(e.Presentation, donorAllowed: true) is { } presentation) p["presentation"] = presentation;
                if (Modify(e.Modify) is { } modify) p["modify"] = modify;
                if (e.Round != null) p["round"] = e.Round;
                if (e.Level != null) p["level"] = e.Level;
                if (e.Pod != null) p["pod"] = PodItems(e.Pod, podItems);
                return p;
            }
            case DeliveryFamily.Variant:
            {
                var v = d.Variant ?? new();
                var p = new JsonObject { ["family"] = "weapon", ["weapon"] = v.Weapon };
                if (Presentation(v.Presentation, donorAllowed: false) is { } presentation) p["presentation"] = presentation;
                if (v.Round != null) p["round"] = v.Round;
                if (v.Model != null) p["model"] = v.Model;
                if (v.ModelUse != null) p["model_use"] = v.ModelUse;
                if (sharedHost?.Invoke(v.Weapon) == true) p["allow_shared"] = true;
                else if (v.AllowShared is { } allow) p["allow_shared"] = allow;
                return p;
            }
            case DeliveryFamily.Sentry:
            {
                var p = new JsonObject { ["family"] = "sentry", ["donor"] = d.Sentry?.Chassis ?? "" };
                if (Modify(d.Sentry?.Weapon) is { } weapon) p["weapon"] = weapon;
                return p;
            }
            case DeliveryFamily.Eagle:
            {
                var e = d.Eagle ?? new();
                var p = new JsonObject { ["family"] = "eagle", ["donor"] = e.Strike };
                Put(p, "uses", e.Uses);
                Put(p, "rearm_seconds", e.RearmSeconds);
                if (e.ImpactPayload != null) p["payload"] = new JsonObject { ["impact_explosion"] = e.ImpactPayload.Id };
                return p;
            }
            case DeliveryFamily.Orbital:
            {
                var r = d.Orbital ?? new();
                var p = new JsonObject { ["family"] = "orbital" };
                if (r.Mode == OrbitalDelivery.NativeBarrage)
                {
                    p["native"] = true;
                    if (r.Pattern != null) p["pattern"] = r.Pattern;
                    if (r.ImpactPayload != null) p["impact_explosion"] = r.ImpactPayload.Id;
                    // Fields a native barrage does not take stay in the form, so the validator names them.
                    if (r.Shell != null) p["shell"] = r.Shell;
                    Put(p, "salvos", r.Salvos); Put(p, "shells_per_salvo", r.ShellsPerSalvo); Put(p, "shell_interval", r.ShellInterval);
                    Put(p, "salvo_interval", r.SalvoInterval); Put(p, "scatter", r.Scatter); Put(p, "salvo_scatter", r.SalvoScatter);
                    return p;
                }
                if (r.Shell != null) p["shell"] = r.Shell;
                if (r.Pattern != null) p["pattern"] = r.Pattern;
                Put(p, "salvos", r.Salvos); Put(p, "shells_per_salvo", r.ShellsPerSalvo); Put(p, "shell_interval", r.ShellInterval);
                Put(p, "salvo_interval", r.SalvoInterval); Put(p, "scatter", r.Scatter); Put(p, "salvo_scatter", r.SalvoScatter);
                if (r.ImpactPayload != null) p["impact_explosion"] = r.ImpactPayload.Id;
                return p;
            }
            case DeliveryFamily.Pelican:
            {
                var r = d.Pelican ?? new();
                var p = new JsonObject { ["family"] = "pelican" };
                Put(p, "hover", r.Hover);
                if (r.Orbit is { IsEmpty: false } orbit)
                {
                    var table = new JsonObject();
                    Put(table, "radius", orbit.Radius); Put(table, "altitude", orbit.Altitude); Put(table, "duration", orbit.Duration); Put(table, "period", orbit.Period); Put(table, "entry", orbit.Entry);
                    p["orbit"] = table;
                }
                if (r.Gun is { IsEmpty: false } gun)
                {
                    var table = new JsonObject();
                    if (gun.BehaveAs != null) table["behave_as"] = gun.BehaveAs;
                    Put(table, "rate_multiplier", gun.RateMultiplier);
                    Put(table, "rpm", gun.Rpm);
                    if (gun.Round != null) table["round"] = gun.Round;
                    if (gun.Casing != null) table["casing"] = gun.Casing;
                    Put(table, "spread", gun.Spread);
                    if (gun.NoRecoil == true) table["recoil"] = false;
                    if (gun.UnlimitedAmmo == true) table["unlimited_ammo"] = true;
                    if (gun.FaceTarget == true) table["face_target"] = true;
                    if (gun.Sound != null) table["sound"] = gun.Sound;
                    if (gun.ImpactExplosion != null) table["impact_explosion"] = gun.ImpactExplosion;
                    Put(table, "aim_height", gun.AimHeight);
                    p["gun"] = table;
                }
                if (r.Approach is { IsEmpty: false } approach)
                {
                    var table = new JsonObject();
                    Put(table, "distance", approach.Distance); Put(table, "height", approach.Height);
                    p["approach"] = table;
                }
                return p;
            }
            case DeliveryFamily.Silo:
            {
                var r = d.Silo ?? new();
                var p = new JsonObject { ["family"] = "silo", ["donor"] = r.Donor };
                if (r.Blast != null) p["blast"] = r.Blast;
                if (r.Fallback != null) p["fallback"] = r.Fallback;
                return p;
            }
            default:
                return new() { ["family"] = ScriptFamily };
        }
    }

    private static JsonArray PodItems(IEnumerable<PodItem> items, IReadOnlyList<PodItemEntry>? catalog) => new([.. items.Select(i =>
    {
        var o = new JsonObject { ["item"] = i.Item };
        Put(o, "count", i.Count);
        if (Modify(i.Weapon) is { } modify) o["modify"] = modify;
        // A pod item Runtime has not live-tested in a pod needs its opt-in: always written (ModBuilder never asks for acknowledgements; the
        // editor shows the risk as a warning).
        if (catalog?.FirstOrDefault(e => e.Key == i.Item) is { Unverified: true }) o["allow_unverified_effect"] = true;
        return (JsonNode)o;
    })]);

    private static JsonObject? Presentation(DeliveredPresentation? p, bool donorAllowed)
    {
        if (p == null || p.IsDefault) return null;
        var o = new JsonObject();
        if (p.NameMode == DeliveredPresentation.Donor && donorAllowed) o["name"] = "donor";
        else if (p.NameMode == DeliveredPresentation.Custom && p.Name != null) o["name"] = p.Name;
        if (p.IconMode == DeliveredPresentation.Donor && donorAllowed) o["icon"] = "donor";
        else if (p.IconMode == DeliveredPresentation.Custom && p.Icon != null) o["icon"] = p.Icon;
        return o.Count == 0 ? null : o;
    }

    private static JsonObject? Modify(WeaponOverrides? w)
    {
        if (w == null || w.IsEmpty) return null;
        var o = new JsonObject();
        // A custom projectile has no project form (the Runtime's projectile donors are vanilla support weapons): the validator refuses its id.
        if (w.Projectile != null) o["projectile"] = w.Projectile.Id;
        Put(o, "rpm", w.FireRate);
        Put(o, "spread", w.Spread);
        Put(o, "ammo", w.Magazine);
        if (w.Recoil != null) o["recoil"] = w.Recoil;
        if (w.Sound != null) o["sound"] = w.Sound;
        if (w.ImpactPayload != null) o["impact_explosion"] = w.ImpactPayload.Id;
        Put(o, "direct_damage", w.DirectDamage);
        Put(o, "rounds", w.ConvertedRounds);
        return o;
    }

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)v)]);
    // Whole numbers are written as integers (as an author writes them); the Runtime compares and compiles both alike.
    private static void Put(JsonObject o, string key, double? value)
    {
        if (value is not { } v) return;
        o[key] = double.IsFinite(v) && v == Math.Floor(v) && Math.Abs(v) < 1e15 ? JsonValue.Create((long)v) : JsonValue.Create(v);
    }
    private static void Put(JsonObject o, string key, int? value) { if (value is { } v) o[key] = v; }

    // ---- reading (imports and templates) ---------------------------------------------------------------------------------------------------

    /// <summary>A custom stratagem from its project form. Unknown keys are reported (they are not kept); values are taken as written, so the
    /// imported stratagem shows the same problems the Runtime's validator reports.</summary>
    public static CustomStratagem Read(JsonObject o, ICollection<string>? ignored = null)
    {
        void Ignore(string where, JsonObject table, params string[] known)
        {
            foreach (var key in table.Select(p => p.Key).Where(k => !known.Contains(k))) ignored?.Add(where + key);
        }
        Ignore("", o, "id", "name", "name_cased", "description", "icon", "code", "cooldown", "uses", "max_per_player", "traits", "carrier", "payload", "assets");
        var s = new CustomStratagem
        {
            Id = Str(o["id"]) ?? "", Name = Str(o["name"]) ?? "", NameCased = Str(o["name_cased"]), Description = Str(o["description"]) ?? "",
            Icon = o["icon"] is JsonObject icon ? Str(icon["image"]) : Str(o["icon"]),
            Code = StrList(o["code"]), Cooldown = Num(o["cooldown"]), Uses = Int(o["uses"]), MaxPerPlayer = Int(o["max_per_player"]), Traits = StrList(o["traits"]), Assets = StrList(o["assets"]),
        };
        if (o["carrier"] is JsonObject c)
        {
            Ignore("carrier.", c, "group", "slots", "beacon", "prefer_families", "allow_families", "exclude");
            s.Carrier = new() { Group = Str(c["group"]), Slots = Int(c["slots"]), Beacon = Str(c["beacon"]), Prefer = StrList(c["prefer_families"]),
                Allow = StrList(c["allow_families"]), Exclude = StrList(c["exclude"]) };
        }
        if (o["payload"] is JsonObject p) s.Delivery = ReadPayload(p, s => ignored?.Add("payload." + s));
        return s;
    }

    private static CustomDelivery ReadPayload(JsonObject p, Action<string> ignore)
    {
        void Ignore(string where, JsonObject? table, params string[] known)
        {
            if (table == null) return;
            foreach (var key in table.Select(x => x.Key).Where(k => !known.Contains(k))) ignore(where + key);
        }
        var family = Str(p["family"]);
        switch (family)
        {
            case "support":
            {
                var item = (p["items"] as JsonArray)?.FirstOrDefault() as JsonObject;
                Ignore("", p, "family", "items");
                Ignore("items[0].", item, "donor", "count", "modify");
                return new() { Family = DeliveryFamily.Support, Support = new() { Item = Str(item?["donor"]) ?? "", Count = Int(item?["count"]),
                    Weapon = ReadModify(item?["modify"] as JsonObject, x => ignore("items[0].modify." + x)) } };
            }
            case "pod":
                Ignore("", p, "family", "items");
                return new() { Family = DeliveryFamily.Pod, Pod = new() { Items = ReadPod(p["items"] as JsonArray, x => ignore("items" + x)) } };
            case "expendable":
                Ignore("", p, "family", "weapon", "presentation", "modify", "round", "level", "pod");
                return new() { Family = DeliveryFamily.Expendable, Expendable = new()
                {
                    Weapon = Str(p["weapon"]) ?? "", Presentation = ReadPresentation(p["presentation"] as JsonObject), Modify = ReadModify(p["modify"] as JsonObject, x => ignore("modify." + x)),
                    Round = Str(p["round"]), Level = Str(p["level"]), Pod = p["pod"] is JsonArray pod ? ReadPod(pod, x => ignore("pod" + x)) : null,
                } };
            case "weapon":
                Ignore("", p, "family", "weapon", "presentation", "round", "model", "model_use", "allow_shared");
                return new() { Family = DeliveryFamily.Variant, Variant = new()
                {
                    Weapon = Str(p["weapon"]) ?? "", Presentation = ReadPresentation(p["presentation"] as JsonObject), Round = Str(p["round"]),
                    Model = Str(p["model"]), ModelUse = Str(p["model_use"]),
                    AllowShared = p["allow_shared"] is JsonValue sv && sv.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? sv.GetValue<bool>() : null,
                } };
            case "sentry":
                Ignore("", p, "family", "donor", "weapon");
                return new() { Family = DeliveryFamily.Sentry, Sentry = new() { Chassis = Str(p["donor"]) ?? "", Weapon = ReadModify(p["weapon"] as JsonObject, x => ignore("weapon." + x)) } };
            case "eagle":
                Ignore("", p, "family", "donor", "uses", "rearm_seconds", "payload");
                return new() { Family = DeliveryFamily.Eagle, Eagle = new()
                {
                    Strike = Str(p["donor"]) ?? "", Uses = Int(p["uses"]), RearmSeconds = Num(p["rearm_seconds"]),
                    ImpactPayload = Str((p["payload"] as JsonObject)?["impact_explosion"]) is { } eagleImpact ? new(ContentRef.VanillaExplosion, eagleImpact) : null,
                } };
            case "orbital":
            {
                var native = p["native"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
                Ignore("", p, "family", "native", "shell", "pattern", "salvos", "shells_per_salvo", "shell_interval", "salvo_interval", "scatter", "salvo_scatter", "impact_explosion");
                return new() { Family = DeliveryFamily.Orbital, Orbital = new()
                {
                    Mode = native ? OrbitalDelivery.NativeBarrage : OrbitalDelivery.RuntimeBarrage, Shell = Str(p["shell"]), Pattern = Str(p["pattern"]),
                    Salvos = Int(p["salvos"]), ShellsPerSalvo = Int(p["shells_per_salvo"]), ShellInterval = Num(p["shell_interval"]), SalvoInterval = Num(p["salvo_interval"]),
                    Scatter = Num(p["scatter"]), SalvoScatter = Num(p["salvo_scatter"]),
                    ImpactPayload = Str(p["impact_explosion"]) is { } impact ? new(ContentRef.VanillaExplosion, impact) : null,
                } };
            }
            case "pelican":
            {
                var orbit = p["orbit"] as JsonObject; var gun = p["gun"] as JsonObject; var approach = p["approach"] as JsonObject;
                Ignore("", p, "family", "hover", "orbit", "gun", "approach");
                Ignore("orbit.", orbit, "radius", "altitude", "duration", "period", "entry");
                Ignore("approach.", approach, "distance", "height");
                Ignore("gun.", gun, "behave_as", "rate_multiplier", "rpm", "round", "casing", "spread", "recoil", "unlimited_ammo", "face_target", "sound", "impact_explosion", "aim_height");
                return new() { Family = DeliveryFamily.Pelican, Pelican = new()
                {
                    Hover = Num(p["hover"]),
                    Orbit = orbit == null ? null : new() { Radius = Num(orbit["radius"]), Altitude = Num(orbit["altitude"]), Duration = Num(orbit["duration"]),
                        Period = Num(orbit["period"]), Entry = Num(orbit["entry"]) },
                    Approach = approach == null ? null : new() { Distance = Num(approach["distance"]), Height = Num(approach["height"]) },
                    Gun = gun == null ? null : new()
                    {
                        BehaveAs = Str(gun["behave_as"]), RateMultiplier = Num(gun["rate_multiplier"]), Rpm = Num(gun["rpm"]), Round = Str(gun["round"]), Casing = Str(gun["casing"]),
                        Spread = Num(gun["spread"]), NoRecoil = gun["recoil"] is JsonValue r && r.TryGetValue<bool>(out var rb) && !rb ? true : null,
                        UnlimitedAmmo = Flag(gun["unlimited_ammo"]), FaceTarget = Flag(gun["face_target"]), Sound = Str(gun["sound"]),
                        ImpactExplosion = Str(gun["impact_explosion"]), AimHeight = Num(gun["aim_height"]),
                    },
                } };
            }
            case "silo":
                Ignore("", p, "family", "donor", "blast", "fallback");
                return new() { Family = DeliveryFamily.Silo, Silo = new() { Donor = Str(p["donor"]) ?? "", Blast = Str(p["blast"]), Fallback = Str(p["fallback"]) } };
            default:
                Ignore("", p, "family");
                return new() { Family = DeliveryFamily.Scripted };
        }
    }

    private static List<PodItem> ReadPod(JsonArray? items, Action<string> ignore) => items == null ? [] : [.. items.OfType<JsonObject>().Select((i, k) =>
    {
        foreach (var key in i.Select(x => x.Key).Where(x => x is not ("item" or "count" or "modify" or "allow_unverified_effect"))) ignore("[" + k + "]." + key);
        return new PodItem { Item = Str(i["item"]) ?? "", Count = Int(i["count"]), Weapon = ReadModify(i["modify"] as JsonObject, x => ignore("[" + k + "].modify." + x)) };
    })];

    private static DeliveredPresentation? ReadPresentation(JsonObject? p)
    {
        if (p == null) return null;
        var name = Str(p["name"]); var icon = Str(p["icon"]);
        return new()
        {
            NameMode = name == null ? DeliveredPresentation.Stratagem : name == "donor" ? DeliveredPresentation.Donor : DeliveredPresentation.Custom, Name = name is null or "donor" ? null : name,
            IconMode = icon == null ? DeliveredPresentation.Stratagem : icon == "donor" ? DeliveredPresentation.Donor : DeliveredPresentation.Custom, Icon = icon is null or "donor" ? null : icon,
        };
    }

    private static WeaponOverrides? ReadModify(JsonObject? m, Action<string> ignore)
    {
        if (m == null) return null;
        foreach (var key in m.Select(x => x.Key).Where(k => k is not ("projectile" or "rpm" or "spread" or "ammo" or "recoil" or "sound" or "impact_explosion" or "direct_damage" or "rounds"))) ignore(key);
        var w = new WeaponOverrides
        {
            Projectile = Str(m["projectile"]) is { } round ? new(ContentRef.VanillaRound, round) : null, FireRate = Num(m["rpm"]), Spread = Num(m["spread"]),
            Magazine = Int(m["ammo"]), Recoil = Str(m["recoil"]), Sound = Str(m["sound"]),
            ImpactPayload = Str(m["impact_explosion"]) is { } impact ? new(ContentRef.VanillaExplosion, impact) : null, ConvertedRounds = Int(m["rounds"]),
            DirectDamage = Num(m["direct_damage"]),
        };
        return w.IsEmpty ? null : w;
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static double? Num(JsonNode? n) => n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number ? Double(v) : null;
    /// <summary>A JSON number as a double, whether it was parsed or created (a value created from a long or an int has no double view).</summary>
    public static double Double(JsonValue v) => double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static int? Int(JsonNode? n) => Num(n) is { } d && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;
    private static bool? Flag(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b ? true : null;
    private static List<string> StrList(JsonNode? n) => n is JsonArray a ? [.. a.Select(Str).OfType<string>()] : [];

    /// <summary>A number as the Runtime's tools write it (Python's repr: 0.7, 1.5, 400).</summary>
    public static string Number(double value) => value == Math.Floor(value) && Math.Abs(value) < 1e16
        ? ((long)value).ToString(CultureInfo.InvariantCulture)
        : PythonRepr(value);
    private static string PythonRepr(double value)
    {
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        if (!r.Contains('E')) return r;
        // Python writes 1e-05 / 1e+16 where .NET writes 1E-05 / 1E+16.
        var (mantissa, exponent) = (r[..r.IndexOf('E')], int.Parse(r[(r.IndexOf('E') + 1)..], CultureInfo.InvariantCulture));
        return mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }
}
