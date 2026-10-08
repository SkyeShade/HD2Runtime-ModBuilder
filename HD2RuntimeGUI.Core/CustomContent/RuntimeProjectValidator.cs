using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>One problem the Runtime's project validator reports: where (stratagems[&lt;id&gt;].payload.gun.rpm) and what, in the Runtime's own
/// words.</summary>
public sealed record RuntimeProblem(string Where, string What)
{
    public override string ToString() => Where + ": " + What;
    /// <summary>The path inside one stratagem (payload.gun.rpm), or "" for the project or the stratagem itself.</summary>
    public string Field => Where.StartsWith("stratagems[", StringComparison.Ordinal) && Where.IndexOf(']') is var end and > 0
        ? end + 1 < Where.Length && Where[end + 1] == '.' ? Where[(end + 2)..] : "" : "";
}

/// <summary>A faithful port of the HD2Runtime SDK's custom stratagem project validator (sdk/tools/custom_stratagem_project.py validate, HD2Runtime
/// 0.30.0): the same checks in the same order with the same messages, over the same project form (RuntimeProjectForm), reading the same
/// schema. The tests hold it to the Python validator's exact output on a corpus of projects. One correction: where the SDK's validator
/// refuses what the Runtime itself registers, the port follows the Runtime (a group whose beacon is "any" implies that beacon; see
/// FamilyCarrier). One extension: with allowScript, a stratagem
/// whose payload is {family = "script"} (ModBuilder's scripted family, which the project format cannot represent) has its metadata and carrier
/// checked against the script family and its payload skipped.</summary>
public static class RuntimeProjectValidator
{
    public const string Format = RuntimeProjectForm.Format;
    private static readonly Regex Image = new(@"\A[a-z0-9_]{1,64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Control = new(@"[\x00-\x1f\x7f]", RegexOptions.CultureInvariant);
    private static readonly Regex Printable = new(@"\A[\x20-\x7e]+\z", RegexOptions.CultureInvariant);

    private sealed class Problems : List<RuntimeProblem> { public void Add(string where, string what) => Add(new RuntimeProblem(where, what)); }

    public static IReadOnlyList<RuntimeProblem> Validate(JsonNode? project, JsonElement schema, bool allowScript = false)
    {
        var problems = new Problems();
        if (project is not JsonObject root) { problems.Add("project", "must be an object"); return problems; }
        OnlyKeys(problems, "project", root, new HashSet<string>(["format", "log", "stratagems"], StringComparer.Ordinal));
        if (Str(root["format"]) != Format) problems.Add("project.format", "must be " + Format);
        if (root.ContainsKey("log") && !(root["log"] is JsonValue lv && lv.TryGetValue<string>(out var log) && log.Length > 0 && Len(log) <= 160))
            problems.Add("project.log", "must be a string of 1 to 160 characters");
        var limit = schema.GetProperty("limits").GetProperty("definitions").GetInt32();
        if (root["stratagems"] is not JsonArray items || items.Count < 1 || items.Count > limit)
        {
            problems.Add("project.stratagems", "must list 1 to " + limit + " custom stratagems");
            return problems;
        }
        var M = schema.GetProperty("metadata"); var C = schema.GetProperty("carrier");
        var families = Families(schema);
        var catalogs = schema.GetProperty("catalogs");
        var stratagems = StringSet(catalogs, "stratagems");
        var carrierFamilies = StringSet(catalogs, "carrierFamilies");
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var codes = new List<(string? Id, List<string> Code)>();
        var idPattern = new Regex(@"\A" + M.GetProperty("id").GetProperty("pattern").GetString()!.Trim('^', '$') + @"\z", RegexOptions.CultureInvariant);
        var idMax = M.GetProperty("id").GetProperty("maxLength").GetInt32();
        for (var index = 0; index < items.Count; index++)
        {
            var s = items[index];
            var where = "stratagems[" + index + "]";
            if (!OnlyKeys(problems, where, s, new HashSet<string>(["id", "name", "name_cased", "description", "icon", "code", "cooldown", "uses", "max_per_player", "traits", "assets", "carrier", "payload"], StringComparer.Ordinal))) continue;
            var o = (JsonObject)s!;
            var sid = Str(o["id"]);
            if (!(sid != null && idPattern.IsMatch(sid) && Len(sid) <= idMax))
                problems.Add(where + ".id", "must be 1-" + idMax + " characters of a-z, 0-9 and _, starting with a letter");
            else if (seenIds.Contains(sid)) problems.Add(where + ".id", "is used twice");
            if (sid != null) seenIds.Add(sid);
            where = "stratagems[" + (sid ?? index.ToString(CultureInfo.InvariantCulture)) + "]";
            foreach (var key in new[] { "name", "name_cased", "description" })
            {
                var v = o[key];
                if (v == null && key == "name_cased" && !o.ContainsKey(key)) continue;
                var max = M.GetProperty(key).GetProperty("maxLength").GetInt32();
                if (!(v is JsonValue jv && jv.TryGetValue<string>(out var text) && Len(text) >= 1 && Len(text) <= max && !Control.IsMatch(text)))
                    problems.Add(where + "." + key, "must be a string of 1 to " + max + " characters");
            }
            var icon = o["icon"] as JsonObject;
            var image = Str(icon?["image"]);
            if (!(icon != null && icon.All(p => p.Key is "image" or "source") && image != null && Image.IsMatch(image)
                  && (icon.ContainsKey("source") ? Str(icon["source"]) : "images/" + image + ".png") == "images/" + image + ".png"))
                problems.Add(where + ".icon", "must be {\"image\": \"<id>\", \"source\": \"images/<id>.png\"}: the mod's editable PNG (the source is optional and always images/<id>.png)");
            var directions = StringSet(catalogs, "directions");
            var codeField = M.GetProperty("code");
            int codeMin = codeField.GetProperty("min").GetInt32(), codeMax = codeField.GetProperty("max").GetInt32();
            var code = o["code"] is JsonArray ca && ca.All(x => Str(x) != null) ? ca.Select(x => Str(x)!).ToList() : null;
            if (!(o["code"] is JsonArray codeArray && codeArray.Count >= codeMin && codeArray.Count <= codeMax && codeArray.All(c => Str(c) is { } d && directions.Contains(d))))
                problems.Add(where + ".code", "must be " + codeMin + " to " + codeMax + " of " + string.Join(", ", directions.Order(StringComparer.Ordinal)));
            else
            {
                foreach (var (otherId, other) in codes)
                    if (Relation(code!, other) is { } rel) problems.Add(where + ".code", "collides with " + Py(otherId) + "'s code (" + rel + ")");
                codes.Add((sid, code!));
                if (catalogs.TryGetProperty("nativeCodes", out var natives))
                    foreach (var native in natives.EnumerateArray())
                    {
                        var nativeCode = native.GetProperty("code").EnumerateArray().Select(x => x.GetString()!).ToList();
                        var rel = Relation(code!, nativeCode);
                        var catalogued = native.GetProperty("catalogued").GetBoolean();
                        if (rel == "equal" || rel != null && catalogued)
                        {
                            var name = native.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString()!.Length > 0 ? n.GetString()!
                                : "stable id " + native.GetProperty("stableId").GetInt64().ToString(CultureInfo.InvariantCulture);
                            problems.Add(where + ".code", (rel == "equal" ? "equals" : code!.Count < nativeCode.Count ? "is the start of" : "starts with") + " " + name
                                + "'s native code (" + string.Join(" ", nativeCode) + "): "
                                + (rel == "equal" ? "the game would call that stratagem instead" : "refused in every mission where it can be picked"));
                        }
                    }
            }
            Number(problems, where + ".cooldown", o["cooldown"], M.GetProperty("cooldown"));
            Number(problems, where + ".uses", o["uses"], M.GetProperty("uses"));
            Number(problems, where + ".max_per_player", o["max_per_player"], M.GetProperty("max_per_player"));
            if (o.ContainsKey("traits"))
            {
                var traits = M.GetProperty("traits");
                var tmax = traits.GetProperty("max").GetInt32(); var tlen = traits.GetProperty("item").GetProperty("maxLength").GetInt32();
                if (!(o["traits"] is JsonArray ta && ta.Count <= tmax && ta.All(t => Str(t) is { } x && Len(x) >= 1 && Len(x) <= tlen && Printable.IsMatch(x))))
                    problems.Add(where + ".traits", "must list at most " + tmax + " labels of 1 to " + tlen + " printable characters (CUSTOM STRATAGEM comes first by itself)");
            }
            if (o["assets"] is JsonArray assets)
                for (var k = 0; k < assets.Count; k++)
                    if (!(Str(assets[k]) is { } a && stratagems.Contains(a))) problems.Add(where + ".assets[" + k + "]", "must name a catalogued stratagem");
            var carrierNode = o["carrier"];
            if (!OnlyKeys(problems, where + ".carrier", carrierNode, C.EnumerateObject().Select(p => p.Name).ToHashSet())) continue;
            var carrier = (JsonObject)carrierNode!;
            var beacons = catalogs.GetProperty("beacons").EnumerateArray().Select(x => x.GetString()!).ToList();
            if (!(Str(carrier["beacon"]) is { } beacon0 && beacons.Contains(beacon0)) && !(carrier.ContainsKey("group") && !carrier.ContainsKey("beacon")))
                problems.Add(where + ".carrier.beacon", "must be " + string.Join(", ", beacons));
            if (carrier.ContainsKey("slots")) Number(problems, where + ".carrier.slots", carrier["slots"], C.GetProperty("slots"));
            foreach (var key in new[] { "prefer_families", "allow_families" })
                foreach (var f in List(carrier[key]))
                    if (!(Str(f) is { } fam && carrierFamilies.Contains(fam))) problems.Add(where + ".carrier." + key, "unknown family " + PyStr(f));
            foreach (var name in List(carrier["exclude"]))
                if (!(Str(name) is { } x && stratagems.Contains(x))) problems.Add(where + ".carrier.exclude", "unknown stratagem " + PyStr(name));
            var payloadNode = o["payload"];
            var payloadFamily = Str((payloadNode as JsonObject)?["family"]);
            if (allowScript && payloadFamily == RuntimeProjectForm.ScriptFamily && families.TryGetValue(RuntimeProjectForm.ScriptFamily, out var script))
            {
                FamilyCarrier(problems, where, carrier, script, schema, (JsonObject)payloadNode!);
                continue;
            }
            if (payloadNode is not JsonObject payload || payloadFamily == null || !families.TryGetValue(payloadFamily, out var fam0) || !fam0.Builder)
            {
                problems.Add(where + ".payload.family", "must be one of " + string.Join(", ", families.Values.Where(f => f.Builder).Select(f => f.Name)));
                continue;
            }
            var pw = where + ".payload";
            FamilyCarrier(problems, where, carrier, fam0, schema, payload);
            var fields = fam0.Fields;
            OnlyKeys(problems, pw, payload, fields.Keys.Append("family").ToHashSet());
            Payload(problems, where, pw, o, payload, fam0, fields, schema, M);
        }
        return problems;
    }

    private static void Payload(Problems problems, string where, string pw, JsonObject s, JsonObject payload, Family fam, IReadOnlyDictionary<string, JsonElement> fields,
        JsonElement schema, JsonElement M)
    {
        var catalogs = schema.GetProperty("catalogs");
        switch (fam.Name)
        {
            case "support":
            {
                var donors = Names(catalogs, "supportDonors", d => d.GetProperty("deliverable").GetBoolean());
                if (!(payload["items"] is JsonArray lst && lst.Count == 1)) { problems.Add(pw + ".items", "must list exactly one donor (one native pod delivers one rack)"); return; }
                var item = lst[0];
                if (!OnlyKeys(problems, pw + ".items[0]", item, Keys(fields["items"].GetProperty("item")))) return;
                var io = (JsonObject)item!;
                if (!(Str(io["donor"]) is { } dn && donors.TryGetValue(dn, out var donor)))
                { problems.Add(pw + ".items[0].donor", "must be a support weapon or backpack with a reviewed pod rack"); return; }
                if (io.ContainsKey("count") && !Equal(io["count"], donor.GetProperty("count")))
                    problems.Add(pw + ".items[0].count", "the " + donor.GetProperty("name").GetString() + " pod holds " + donor.GetProperty("count").GetRawText() + "; another count is not supported");
                if (io.ContainsKey("modify"))
                {
                    if (!(donor.TryGetProperty("modifiable", out var m) && m.ValueKind == JsonValueKind.True))
                        problems.Add(pw + ".items[0].modify", donor.GetProperty("name").GetString() + " delivers no weapon; no backpack field is reviewed");
                    else
                    {
                        Weapon(problems, pw + ".items[0].modify", io["modify"], schema, "support");
                        RateSelector(problems, pw + ".items[0].modify", io["modify"], donor);
                    }
                }
                return;
            }
            case "pod":
                PodItems(problems, pw + ".items", payload["items"], schema, fields["items"], false);
                return;
            case "expendable":
            {
                if (payload.ContainsKey("pod")) PodItems(problems, pw + ".pod", payload["pod"], schema, fields["pod"], true);
                var clones = Names(catalogs, "cloneDonors");
                if (!(Str(payload["weapon"]) is { } w && clones.ContainsKey(w)))
                    problems.Add(pw + ".weapon", "must be a support weapon with a reviewed expendable clone class: " + string.Join(", ", clones.Keys.Order(StringComparer.Ordinal)));
                if (payload.ContainsKey("level") && !In(payload["level"], fields["level"].GetProperty("values")))
                    problems.Add(pw + ".level", "must be " + string.Join(", ", fields["level"].GetProperty("values").EnumerateArray().Select(x => x.GetString())));
                Presentation(problems, pw, payload["presentation"], fields, M, ", or \"donor\"", "must be an image id (images/<id>.png) or \"donor\"");
                if (payload.ContainsKey("modify")) Weapon(problems, pw + ".modify", payload["modify"], schema, "expendable");
                if (payload.ContainsKey("round"))
                {
                    var rounds = Str(payload["weapon"]) is { } cw && clones.TryGetValue(cw, out var clone) && clone.TryGetProperty("rounds", out var r)
                        ? r.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
                    if (!(Str(payload["round"]) is { } round && rounds.Contains(round)))
                        problems.Add(pw + ".round", "must be a round reviewed for the " + PyStr(payload["weapon"]) + "'s clone: " + (rounds.Count > 0 ? string.Join(", ", rounds) : "none"));
                    else if (payload["modify"] is JsonObject mo && mo.ContainsKey("impact_explosion"))
                        problems.Add(pw + ".round", "not with modify.impact_explosion: the round has its own explosions");
                    else if (payload["modify"] is JsonObject md && md.ContainsKey("direct_damage"))
                        problems.Add(pw + ".round", "not with modify.direct_damage: it is reviewed for the donor's own round");
                }
                // A direct damage override is reviewed per clone donor's round (cloneDonors[].directDamage).
                if (payload["modify"] is JsonObject dm && dm["direct_damage"] is { } dd && Str(payload["weapon"]) is { } dw && clones.TryGetValue(dw, out var donor))
                {
                    var reviewed = donor.TryGetProperty("directDamage", out var list) ? list : default;
                    if (reviewed.ValueKind != JsonValueKind.Array || !In(dd, reviewed))
                    {
                        var values = reviewed.ValueKind == JsonValueKind.Array ? string.Join(", ", reviewed.EnumerateArray().Select(x => x.GetRawText())) : "";
                        problems.Add(pw + ".modify.direct_damage", "is not reviewed for the " + dw + "'s round: " + (values.Length > 0 ? values : "none"));
                    }
                }
                return;
            }
            case "weapon":
            {
                var hosts = Names(catalogs, "variantDonors");
                JsonElement host = default;
                var hasHost = Str(payload["weapon"]) is { } hw && hosts.TryGetValue(hw, out host);
                // A host the Runtime cannot deliver (deliverable false) is named with its reason; the list offers only deliverable hosts.
                static bool Deliverable(JsonElement h) => !(h.TryGetProperty("deliverable", out var d) && d.ValueKind == JsonValueKind.False);
                if (!hasHost) problems.Add(pw + ".weapon", "must be a reviewed variant weapon: " + string.Join(", ", hosts.Where(h => Deliverable(h.Value)).Select(h => h.Key).Order(StringComparer.Ordinal)));
                else if (!Deliverable(host)) problems.Add(pw + ".weapon", "cannot be delivered: " + (host.TryGetProperty("refused", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : "None"));
                if (payload.ContainsKey("allow_shared") && !(payload["allow_shared"] is JsonValue av && av.GetValueKind() is JsonValueKind.True or JsonValueKind.False))
                    problems.Add(pw + ".allow_shared", "must be true or false");
                // A shared host (world loot, other pods bring its type) converts those copies too: the author's consent is allow_shared = true.
                if (hasHost && host.TryGetProperty("shared", out var shared) && shared.ValueKind == JsonValueKind.Array && shared.GetArrayLength() > 0
                    && !(payload["allow_shared"] is JsonValue asv && asv.GetValueKind() == JsonValueKind.True))
                    problems.Add(pw + ".allow_shared", "must be true: the " + host.GetProperty("name").GetString() + " variant converts a shared type ("
                        + string.Join("; ", shared.EnumerateArray().Select(x => x.GetString())) + ")");
                if (payload.ContainsKey("round") && hasHost && host.TryGetProperty("round", out var fires) && fires.ValueKind == JsonValueKind.False)
                    problems.Add(pw + ".round", "the " + host.GetProperty("name").GetString() + " fires no projectile: it has no round");
                else if (payload.ContainsKey("round") && hasHost)
                {
                    var outputs = host.TryGetProperty("rounds", out var rs) ? rs.EnumerateArray().Select(x => x.GetProperty("output").GetString()!).ToList() : [];
                    if (!(Str(payload["round"]) is { } round && outputs.Contains(round)))
                        problems.Add(pw + ".round", "must be a round of the " + host.GetProperty("name").GetString() + "'s own compatibility class: " + (outputs.Count > 0 ? string.Join(", ", outputs) : "none"));
                }
                Presentation(problems, pw, payload["presentation"], fields, M, "", "must be an image id (images/<id>.png)");
                var model = payload["model"];
                if (model != null && !(Str(model) is { } mid && Image.IsMatch(mid))) problems.Add(pw + ".model", "must be a model id (models/<id>.json)");
                if (payload.ContainsKey("model_use"))
                {
                    var values = fields["model_use"].GetProperty("values");
                    if (model == null) problems.Add(pw + ".model_use", "needs model");
                    else if (!In(payload["model_use"], values)) problems.Add(pw + ".model_use", "must be " + string.Join(" or ", values.EnumerateArray().Select(x => x.GetString())));
                }
                return;
            }
            case "sentry":
                if (!(Str(payload["donor"]) is { } sd && Names(catalogs, "sentryDonors", d => d.GetProperty("supported").GetBoolean()).ContainsKey(sd)))
                    problems.Add(pw + ".donor", "must be a catalogued sentry");
                if (payload.ContainsKey("weapon")) Weapon(problems, pw + ".weapon", payload["weapon"], schema, "sentry");
                return;
            case "silo":
            {
                var silos = Names(catalogs, "siloDonors");
                if (!(Str(payload["donor"]) is { } dn && Names(catalogs, "siloDonors", d => d.GetProperty("packageKnown").GetBoolean()).ContainsKey(dn)))
                    problems.Add(pw + ".donor", "must be a reviewed missile silo: " + string.Join(", ", silos.Keys.Order(StringComparer.Ordinal)));
                var blasts = Names(catalogs, "blastExplosions");
                foreach (var key in new[] { "blast", "fallback" })
                    if (payload.ContainsKey(key) && !(Str(payload[key]) is { } b && blasts.ContainsKey(b)))
                        problems.Add(pw + "." + key, "must be a catalogued explosion whose packages are known (catalogs.blastExplosions)");
                if (!payload.ContainsKey("blast")) problems.Add(pw + ".blast", "is required: the explosion requested where the missile detonates");
                else if (JsonNode.DeepEquals(payload["fallback"], payload["blast"])) problems.Add(pw + ".fallback", "must be another explosion than the blast");
                return;
            }
            case "eagle":
            {
                var eagles = Names(catalogs, "eagleDonors");
                JsonElement donor = default;
                var hasDonor = Str(payload["donor"]) is { } en && eagles.TryGetValue(en, out donor);
                if (!hasDonor) problems.Add(pw + ".donor", "must be a reviewed Eagle");
                Number(problems, pw + ".uses", payload["uses"], fields["uses"]);
                Number(problems, pw + ".rearm_seconds", payload["rearm_seconds"], fields["rearm_seconds"]);
                if (payload.ContainsKey("payload"))
                {
                    var inner = payload["payload"];
                    if (OnlyKeys(problems, pw + ".payload", inner, Keys(fields["payload"].GetProperty("fields"))) && (inner as JsonObject)!["impact_explosion"] is { } impact)
                    {
                        if (!(Str(impact) is { } i && Names(catalogs, "explosionDonors").ContainsKey(i)))
                            problems.Add(pw + ".payload.impact_explosion", "must be a reviewed explosion donor");
                        else if (hasDonor && !donor.GetProperty("impactExplosionReplaceable").GetBoolean())
                            problems.Add(pw + ".payload.impact_explosion", donor.GetProperty("name").GetString() + "'s strike projectile has no impact explosion only: its impact cannot be replaced");
                    }
                }
                if (s["cooldown"] != null) problems.Add(where + ".cooldown", "an Eagle keeps its native cooldown and rearm: no cooldown");
                if (s["uses"] != null) problems.Add(where + ".uses", "an Eagle's uses are per rearm: payload.uses");
                return;
            }
            case "pelican":
            {
                Number(problems, pw + ".hover", payload["hover"], fields["hover"], required: true);
                foreach (var key in new[] { "orbit", "approach" })
                {
                    var part = payload[key];
                    var sub = fields[key].GetProperty("fields");
                    if (part != null && OnlyKeys(problems, pw + "." + key, part, Keys(sub)))
                        // An unknown key is reported above (the SDK's validator fails on it here: KeyError).
                        foreach (var (k, v) in (JsonObject)part)
                            if (sub.TryGetProperty(k, out var kf)) Number(problems, pw + "." + key + "." + k, v, kf);
                }
                if (payload["orbit"] is JsonObject orbit && IsNumber(orbit["entry"]))
                {
                    var duration = orbit.ContainsKey("duration") ? orbit["duration"] : JsonNode.Parse(fields["orbit"].GetProperty("fields").GetProperty("duration").GetProperty("default").GetRawText());
                    if (IsNumber(duration) && Dbl(orbit["entry"]) >= Dbl(duration)) problems.Add(pw + ".orbit.entry", "must be shorter than orbit.duration");
                }
                var gun = payload["gun"];
                var gunFields = fields["gun"].GetProperty("fields");
                if (gun != null && OnlyKeys(problems, pw + ".gun", gun, Keys(gunFields))) PelicanGun(problems, pw + ".gun", (JsonObject)gun, gunFields, catalogs);
                return;
            }
            case "orbital" when payload["native"] != null:
            {
                var orbitals = Names(catalogs, "orbitals");
                if (!(payload["native"] is JsonValue nv && nv.GetValueKind() == JsonValueKind.True)) problems.Add(pw + ".native", "must be true (or absent)");
                foreach (var (key, _) in payload)
                    if (key is not ("family" or "native" or "pattern" or "impact_explosion"))
                        problems.Add(pw + "." + key, "not supported with native = true: the donor's own barrage is used exactly as the game reads it");
                if (!(Str(payload["pattern"]) is { } pn && orbitals.TryGetValue(pn, out var pattern) && pattern.TryGetProperty("pattern", out var pp) && Truthy(pp)))
                    problems.Add(pw + ".pattern", "must be a reviewed orbital bombardment");
                if (!(Str(payload["impact_explosion"]) is { } ie && Names(catalogs, "explosionDonors").ContainsKey(ie)))
                    problems.Add(pw + ".impact_explosion", "is required with native = true: a reviewed explosion donor");
                return;
            }
            case "orbital":
            {
                var orbitals = Names(catalogs, "orbitals");
                if (!(Str(payload["shell"]) is { } sh && orbitals.ContainsKey(sh))) problems.Add(pw + ".shell", "must be a reviewed orbital");
                var patternName = payload.ContainsKey("pattern") ? Str(payload["pattern"]) : fields["pattern"].GetProperty("default").GetString();
                JsonElement pattern = default;
                var hasPattern = patternName != null && orbitals.TryGetValue(patternName, out pattern);
                if (!hasPattern) problems.Add(pw + ".pattern", "must be a reviewed orbital");
                foreach (var key in new[] { "salvos", "shells_per_salvo", "shell_interval", "salvo_interval", "scatter", "salvo_scatter" })
                    Number(problems, pw + "." + key, payload[key], fields[key]);
                if (payload.ContainsKey("impact_explosion") && !(Str(payload["impact_explosion"]) is { } ie && Names(catalogs, "explosionDonors").ContainsKey(ie)))
                    problems.Add(pw + ".impact_explosion", "must be a reviewed explosion donor");
                if (hasPattern && pattern.TryGetProperty("pattern", out var pp) && pp.ValueKind == JsonValueKind.Object)
                {
                    var salvos = payload.ContainsKey("salvos") ? payload["salvos"] : JsonNode.Parse(pp.GetProperty("salvos").GetRawText());
                    var shells = payload.ContainsKey("shells_per_salvo") ? payload["shells_per_salvo"] : JsonNode.Parse(pp.GetProperty("shells_per_salvo").GetRawText());
                    if (PyNum(salvos) is { } sv && PyNum(shells) is { } hv && IsNumberOrBool(salvos) && IsNumberOrBool(shells))
                    {
                        var total = sv * hv;
                        var limit = schema.GetProperty("limits").GetProperty("orbitalShells").GetInt32();
                        if (total > limit) problems.Add(pw, ((long)Math.Truncate(total)).ToString(CultureInfo.InvariantCulture) + " shells in all; at most " + limit);
                    }
                }
                return;
            }
        }
    }

    // ---- the family carrier rules (_family_carrier) ----------------------------------------------------------------------------------------

    private static void FamilyCarrier(Problems problems, string where, JsonObject carrier, Family family, JsonElement schema, JsonObject payload)
    {
        var group = carrier["group"];
        var groups = schema.GetProperty("catalogs").TryGetProperty("carrierGroups", out var cg)
            ? cg.EnumerateArray().ToDictionary(g => g.GetProperty("name").GetString()!, StringComparer.Ordinal) : [];
        var beacon = Str(carrier["beacon"]);
        var beaconPresent = carrier["beacon"] != null;
        if (group != null)
        {
            if (!(Str(group) is { } gn && groups.TryGetValue(gn, out var g)))
            {
                problems.Add(where + ".carrier.group", "must be one of " + string.Join(", ", groups.Keys));
                return;
            }
            var key = PayloadKey(payload);
            var payloads = g.GetProperty("payloads").EnumerateArray().Select(x => x.GetString()!).ToList();
            if (!payloads.Contains(key)) problems.Add(where + ".carrier.group", "the group " + gn + " cannot carry a " + key + " payload (it carries: " + string.Join(", ", payloads) + ")");
            var gb = g.GetProperty("beacon").GetString();
            if (beaconPresent && gb != "any" && beacon != gb) problems.Add(where + ".carrier.beacon", "the group " + gn + " throws a " + gb + " beacon");
            // A group implies its beacon, "any" included: the Runtime registers {group = 'orbital'} and {group = 'any'} without one
            // (carrier_allocator.check_policy; the group's policy has beacon 'any'). The SDK's validator implies only offensive or support and
            // so refuses those two groups for every payload they carry; the port follows the Runtime.
            if (!beaconPresent) { beacon = gb; beaconPresent = true; }
            var gf = g.TryGetProperty("families", out var gfe) ? gfe.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
            foreach (var k in new[] { "prefer_families", "allow_families" })
                foreach (var f in List(carrier[k]))
                    if (gf.Count > 0 && !(Str(f) is { } fs && gf.Contains(fs))) problems.Add(where + ".carrier." + k, PyStr(f) + " is outside the group " + gn);
        }
        if (carrier.ContainsKey("slots"))
        {
            var g = Str(group) is { } gn2 && groups.TryGetValue(gn2, out var ge) ? ge : default;
            var podOrWeapon = g.ValueKind == JsonValueKind.Object && (Truthy(g.GetProperty("pod")) || Truthy(g.GetProperty("weapon")));
            if (group == null || !podOrWeapon) problems.Add(where + ".carrier.slots", "a pod capacity: only with the support_pod or expendable group");
        }
        if (family.Groups.Count > 0 && group != null && !(Str(group) is { } gs && family.Groups.Contains(gs)))
            problems.Add(where + ".carrier.group", "a " + family.Name + " payload needs the group " + string.Join(" or ", family.Groups));
        if (family.Beacons.Count > 0 && !(beaconPresent && beacon != null && family.Beacons.Contains(beacon)))
            problems.Add(where + ".carrier.beacon", "a " + family.Name + " payload needs a " + string.Join(" or ", family.Beacons) + " beacon");
        if (family.AllowFamilies.Count > 0)
        {
            var allowed = List(carrier["allow_families"]).Count > 0 ? List(carrier["allow_families"]) : List(carrier["prefer_families"]);
            if (allowed.Count == 0 || allowed.Any(f => !(Str(f) is { } fs && family.AllowFamilies.Contains(fs))))
                problems.Add(where + ".carrier", "a " + family.Name + " payload allows the " + string.Join("/", family.AllowFamilies) + " family only: set allow_families to ["
                    + string.Join(", ", family.AllowFamilies.Select(f => "'" + f + "'")) + "]");
        }
    }

    private static string PayloadKey(JsonObject payload)
    {
        var family = Str(payload["family"]);
        if (family == "orbital") return payload["native"] is JsonValue v && v.GetValueKind() == JsonValueKind.True ? "orbital_native" : "orbital";
        if (family == RuntimeProjectForm.ScriptFamily) return "runtime";
        return family ?? "None";
    }

    // ---- weapons and pods ------------------------------------------------------------------------------------------------------------------

    private static void Weapon(Problems problems, string where, JsonNode? value, JsonElement schema, string family)
    {
        var fam = Families(schema)[family];
        var fields = family is "support" or "pod" ? fam.Fields["items"].GetProperty("item").GetProperty("modify").GetProperty("fields")
            : family == "expendable" ? fam.Fields["modify"].GetProperty("fields") : fam.Fields["weapon"].GetProperty("fields");
        if (!OnlyKeys(problems, where, value, Keys(fields))) return;
        var catalogs = schema.GetProperty("catalogs");
        var projectiles = Names(catalogs, "projectileDonors");
        var explosions = Names(catalogs, "explosionDonors");
        foreach (var (key, v) in (JsonObject)value!)
        {
            if (!fields.TryGetProperty(key, out var f)) continue;
            switch (key)
            {
                case "projectile":
                    if (!(Str(v) is { } p && projectiles.ContainsKey(p))) problems.Add(where + ".projectile", "must name a support weapon with a reviewed round (CustomStratagemSchema catalogs.projectileDonors)");
                    break;
                case "impact_explosion":
                    if (!(Str(v) is { } e && explosions.ContainsKey(e))) problems.Add(where + ".impact_explosion", "must be a reviewed explosion donor: " + string.Join(", ", explosions.Keys.Order(StringComparer.Ordinal)));
                    break;
                case "direct_damage":
                    if (!In(v, f.GetProperty("values")) || v is JsonValue bv && bv.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                        problems.Add(where + ".direct_damage", "must be a reviewed direct damage override: " + string.Join(", ", f.GetProperty("values").EnumerateArray().Select(x => x.GetRawText())));
                    else if (!((JsonObject)value!).ContainsKey("impact_explosion"))
                        problems.Add(where + ".direct_damage", "needs impact_explosion (written with each round's impact explosion)");
                    break;
                case "recoil":
                    if (!In(v, f.GetProperty("values"))) problems.Add(where + ".recoil", "must be 'zero'");
                    break;
                case "sound":
                    if (!In(v, f.GetProperty("values"))) problems.Add(where + ".sound", "must be a firing sound of the catalogue (CustomStratagemSchema catalogs.pelicanSounds; docs/weapon-sounds.md)");
                    break;
                default:
                    Number(problems, where + "." + key, v, f);
                    break;
            }
        }
    }

    private static void RateSelector(Problems problems, string where, JsonNode? modify, JsonElement donor)
    {
        if (modify is JsonObject m && m.ContainsKey("rpm") && donor.ValueKind == JsonValueKind.Object && donor.TryGetProperty("rateSelector", out var rs) && Truthy(rs))
            problems.Add(where + ".rpm", "the " + donor.GetProperty("name").GetString() + " binds the rate-of-fire selector: a per-call rpm is refused (its modes are hd2.fields.fire_rate.modes)");
    }

    private static void PodItems(Problems problems, string where, JsonNode? lst, JsonElement schema, JsonElement field, bool cloneOk)
    {
        var items = schema.GetProperty("catalogs").GetProperty("podItems").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!, StringComparer.Ordinal);
        int min = field.GetProperty("min").GetInt32(), max = field.GetProperty("max").GetInt32();
        if (!(lst is JsonArray list && list.Count >= min && list.Count <= max)) { problems.Add(where, "must list " + min + " to " + max + " items"); return; }
        var itemField = field.GetProperty("item");
        var countField = itemField.GetProperty("count");
        var seen = new List<JsonNode?>();
        int total = 0, clones = 0;
        for (var k = 0; k < list.Count; k++)
        {
            var at = where + "[" + k + "]";
            if (!OnlyKeys(problems, at, list[k], Keys(itemField))) continue;
            var item = (JsonObject)list[k]!;
            var key = item["item"];
            var count = item.ContainsKey("count") ? item["count"] : JsonValue.Create(1);
            Number(problems, at + ".count", count, countField);
            if (Str(key) == "clone")
            {
                if (!cloneOk) problems.Add(at + ".item", "\"clone\" is an expendable pod's carrier weapon only");
                else if (item.ContainsKey("modify")) problems.Add(at + ".modify", "the clone takes the payload's modify");
                clones += IsInt(count) ? (int)Dbl(count) : 0;
            }
            else
            {
                if (!(Str(key) is { } ks && items.TryGetValue(ks, out var entry)))
                {
                    problems.Add(at + ".item", "must be \"clone\" (expendable) or a podItems key (support_weapon/<name>, backpack/<name>, player_weapon/<name>)");
                    continue;
                }
                var unverified = entry.GetProperty("status").GetString() == "unverified";
                if (unverified && !(item["allow_unverified_effect"] is JsonValue ue && ue.GetValueKind() == JsonValueKind.True))
                    problems.Add(at + ".allow_unverified_effect", entry.GetProperty("name").GetString() + " (" + entry.GetProperty("kind").GetString() + ") is not live-tested in a pod: it needs allow_unverified_effect = true");
                if (!unverified && item.ContainsKey("allow_unverified_effect")) problems.Add(at + ".allow_unverified_effect", "only an unverified kind takes it");
                if (item.ContainsKey("modify"))
                {
                    if (entry.GetProperty("kind").GetString() == "backpack") problems.Add(at + ".modify", "a backpack: no instance-local backpack field is reviewed");
                    else
                    {
                        Weapon(problems, at + ".modify", item["modify"], schema, "pod");
                        RateSelector(problems, at + ".modify", item["modify"], entry);
                    }
                }
            }
            if (seen.Any(x => JsonNode.DeepEquals(x, key))) problems.Add(at + ".item", "listed twice (give it a count)");
            seen.Add(key);
            total += IsInt(count) ? (int)Dbl(count) : 0;
        }
        var countMax = (int)countField.GetProperty("max").GetDouble();
        if (total > countMax) problems.Add(where, total + " items in all; at most " + countMax);
        if (cloneOk && clones < 1) problems.Add(where, "must hold the clone ({\"item\": \"clone\", \"count\": n})");
    }

    private static void Presentation(Problems problems, string pw, JsonNode? pres, IReadOnlyDictionary<string, JsonElement> fields, JsonElement M, string nameSuffix, string iconMessage)
    {
        if (pres == null || !OnlyKeys(problems, pw + ".presentation", pres, Keys(fields["presentation"].GetProperty("fields")))) return;
        var p = (JsonObject)pres;
        var max = M.GetProperty("name").GetProperty("maxLength").GetInt32();
        if (p["name"] is { } name && !(Str(name) is { } n && Len(n) >= 1 && Len(n) <= max && !Control.IsMatch(n)))
            problems.Add(pw + ".presentation.name", "must be a string of 1 to " + max + " characters" + nameSuffix);
        if (p["icon"] is { } icon && !(Str(icon) is { } i && Image.IsMatch(i))) problems.Add(pw + ".presentation.icon", iconMessage);
    }

    private static void PelicanGun(Problems problems, string where, JsonObject gun, JsonElement fields, JsonElement catalogs)
    {
        foreach (var key in new[] { "round", "behave_as", "rate_multiplier", "casing", "recoil", "unlimited_ammo", "face_target", "sound" })
            if (gun.ContainsKey(key) && !In(gun[key], fields.GetProperty(key).GetProperty("values")))
                problems.Add(where + "." + key, "must be " + string.Join(", ", fields.GetProperty(key).GetProperty("values").EnumerateArray().Take(12).Select(JsonDumps))
                    + (key == "sound" ? " (the pelicanSounds catalogue)" : ""));
        foreach (var key in new[] { "rpm", "spread", "aim_height" }) Number(problems, where + "." + key, gun[key], fields.GetProperty(key));
        var gatling = Str(gun["behave_as"]) == "gatling_sentry";
        foreach (var (key, text) in new[] { ("rpm", "the Gatling AI's rate"), ("face_target", "the Gatling AI's target"), ("aim_height", "the Gatling AI's target") })
            if (gun.ContainsKey(key) && !gatling && !(key == "aim_height" && PyNum(gun[key]) == 0))
                problems.Add(where + "." + key, "follows " + text + ": it needs behave_as = \"gatling_sentry\"");
        var multiplier = gun.ContainsKey("rate_multiplier") ? gun["rate_multiplier"] : JsonValue.Create(1);
        var notOne = PyNum(multiplier) != 1;
        if (notOne && !gatling) problems.Add(where + ".rate_multiplier", "multiplies the Gatling rate: it needs behave_as = \"gatling_sentry\"");
        if (gun.ContainsKey("rpm") && notOne) problems.Add(where + ".rpm", "replaces the Gatling rate: no rate_multiplier");
        if (Str(gun["casing"]) == "gatling" && !gatling) problems.Add(where + ".casing", "\"gatling\" goes with behave_as = \"gatling_sentry\"");
        if (gun.ContainsKey("impact_explosion"))
        {
            if (!(Str(gun["impact_explosion"]) is { } name && Names(catalogs, "pelicanExplosionDonors").TryGetValue(name, out var donor)))
                problems.Add(where + ".impact_explosion", "must be a pelicanExplosionDonors entry");
            else if (donor.GetProperty("kind").GetString() == "slow" && !(PyNum(gun["rpm"]) is { } rpm && rpm <= donor.GetProperty("maxRpm").GetDouble()))
                problems.Add(where + ".impact_explosion", donor.GetProperty("name").GetString() + " is a slow donor: it needs rpm at most " + (int)donor.GetProperty("maxRpm").GetDouble());
        }
    }

    // ---- numbers (_number) -----------------------------------------------------------------------------------------------------------------

    private static void Number(Problems problems, string where, JsonNode? value, JsonElement field, bool required = false)
    {
        if (value == null) { if (required) problems.Add(where, "required"); return; }
        var integer = field.TryGetProperty("type", out var t) && t.GetString() == "integer";
        if (!IsNumber(value) || integer && !IsWhole(value)) { problems.Add(where, "must be " + (integer ? "a whole number" : "a number")); return; }
        var v = Dbl(value);
        var exclusive = field.TryGetProperty("exclusiveMin", out var ex) && ex.ValueKind == JsonValueKind.True;
        if (field.TryGetProperty("min", out var lo) && lo.ValueKind == JsonValueKind.Number && (exclusive ? v <= lo.GetDouble() : v < lo.GetDouble()))
            problems.Add(where, "must be " + (exclusive ? "above" : "at least") + " " + lo.GetRawText());
        if (field.TryGetProperty("max", out var hi) && hi.ValueKind == JsonValueKind.Number && v > hi.GetDouble())
            problems.Add(where, "must be at most " + hi.GetRawText());
    }

    // ---- the families (_families) ------------------------------------------------------------------------------------------------------

    private sealed record Family(string Name, bool Builder, IReadOnlyDictionary<string, JsonElement> Fields, IReadOnlyList<string> Beacons, IReadOnlyList<string> AllowFamilies,
        IReadOnlyList<string> Groups);

    private static Dictionary<string, Family> Families(JsonElement schema)
    {
        var specs = schema.GetProperty("families").EnumerateArray().ToList();
        foreach (var extra in new[] { "podFamily", "variantFamily" })
            if (schema.TryGetProperty(extra, out var f) && f.ValueKind == JsonValueKind.Object) specs.Add(f);
        var options = schema.TryGetProperty("familyOptions", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
        var result = new Dictionary<string, Family>(StringComparer.Ordinal);
        foreach (var f in specs)
        {
            var name = f.GetProperty("family").GetString()!;
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (f.TryGetProperty("fields", out var ff) && ff.ValueKind == JsonValueKind.Object) foreach (var p in ff.EnumerateObject()) fields[p.Name] = p.Value;
            var carrier = f.TryGetProperty("carrier", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
            var groups = Strings(carrier, "groups");
            if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty(name, out var fo) && fo.ValueKind == JsonValueKind.Object)
                foreach (var p in fo.EnumerateObject())
                {
                    if (p.Name == "carrier") { if (Strings(p.Value, "groups") is { Count: > 0 } g) groups = g; }
                    else fields[p.Name] = p.Value;
                }
            result[name] = new(name, f.TryGetProperty("builder", out var b) && b.ValueKind == JsonValueKind.True, fields, Strings(carrier, "beacon"),
                Strings(carrier, "allowFamilies"), groups);
        }
        return result;
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------------

    private static bool OnlyKeys(Problems problems, string where, JsonNode? value, IReadOnlySet<string> keys)
    {
        if (value is not JsonObject o) { problems.Add(where, "must be an object"); return false; }
        foreach (var (key, _) in o)
            if (!keys.Contains(key)) problems.Add(where, "unsupported field " + key + " (supported: " + string.Join(", ", keys.Order(StringComparer.Ordinal)) + ")");
        return true;
    }
    private static HashSet<string> Keys(JsonElement table) => table.ValueKind == JsonValueKind.Object ? table.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal) : [];
    private static Dictionary<string, JsonElement> Names(JsonElement catalogs, string catalog, Func<JsonElement, bool>? predicate = null)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (catalogs.TryGetProperty(catalog, out var list))
            foreach (var item in list.EnumerateArray())
                if (predicate == null || predicate(item)) result[item.GetProperty("name").GetString()!] = item;
        return result;
    }
    private static HashSet<string> StringSet(JsonElement e, string key) => e.GetProperty(key).EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
    private static List<string> Strings(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
    // carrier.get(key) or []: a missing, null or empty value is no list; a non-list value is iterated as Python would (a string by character).
    private static List<JsonNode?> List(JsonNode? n) => n switch
    {
        JsonArray a => [.. a],
        JsonValue v when v.TryGetValue<string>(out var s) => [.. s.Select(c => (JsonNode?)JsonValue.Create(c.ToString()))],
        _ => [],
    };
    private static string? Relation(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.SequenceEqual(b)) return "equal";
        var n = Math.Min(a.Count, b.Count);
        return a.Take(n).SequenceEqual(b.Take(n)) ? "a prefix" : null;
    }
    private static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
    private static bool IsNumber(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number;
    private static bool IsWhole(JsonNode? n) => IsNumber(n) && Dbl(n) == Math.Floor(Dbl(n));
    // An integer as Python's json reads it (no decimal point or exponent).
    private static bool IsInt(JsonNode? n) => IsNumber(n) && n!.ToJsonString() is var t && !t.Contains('.') && !t.Contains('e') && !t.Contains('E');
    private static double Dbl(JsonNode? n) => n is JsonValue v ? RuntimeProjectForm.Double(v) : double.NaN;
    // A value Python treats as a number (an int, a float, or a bool, which is an int there); null otherwise.
    private static double? PyNum(JsonNode? n) => n is JsonValue v ? v.GetValueKind() switch
    {
        JsonValueKind.Number => RuntimeProjectForm.Double(v), JsonValueKind.True => 1, JsonValueKind.False => 0, _ => null,
    } : null;
    private static bool IsNumberOrBool(JsonNode? n) => PyNum(n) != null;
    private static bool Truthy(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Object => e.EnumerateObject().Any(),
        JsonValueKind.Array => e.GetArrayLength() > 0,
        JsonValueKind.String => e.GetString()!.Length > 0,
        JsonValueKind.Number => e.GetDouble() != 0,
        _ => false,
    };
    // Python's `value in list` over JSON scalars (1 == 1.0, True is not "true").
    private static bool In(JsonNode? value, JsonElement values)
    {
        foreach (var v in values.EnumerateArray())
        {
            if (value is not JsonValue jv) continue;
            var kind = jv.GetValueKind();
            if (v.ValueKind == JsonValueKind.String && kind == JsonValueKind.String && jv.GetValue<string>() == v.GetString()) return true;
            // Python: True == 1 and False == 0, so a boolean matches a number of the same value.
            double? a = kind switch { JsonValueKind.Number => RuntimeProjectForm.Double(jv), JsonValueKind.True => 1, JsonValueKind.False => 0, _ => null };
            double? b = v.ValueKind switch { JsonValueKind.Number => v.GetDouble(), JsonValueKind.True => 1, JsonValueKind.False => 0, _ => null };
            if (a != null && b != null && a == b) return true;
        }
        return false;
    }
    private static bool Equal(JsonNode? a, JsonElement b) => a is JsonValue v && v.GetValueKind() == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number && RuntimeProjectForm.Double(v) == b.GetDouble();
    private static string JsonDumps(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(e.GetString(), PyJson),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText(),
    };
    // json.dumps escapes every non-ASCII character (ensure_ascii).
    private static readonly JsonSerializerOptions PyJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.BasicLatin) };
    // Python's str() of a value from the project (a string as is, None, a number, a list ...), as "%s" formats it.
    private static string PyStr(JsonNode? n) => n switch
    {
        null => "None",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.True => "True",
        JsonValue v when v.GetValueKind() == JsonValueKind.False => "False",
        JsonValue v => v.ToJsonString(),
        _ => n.ToJsonString(),
    };
    private static string Py(string? id) => id ?? "None";
    // Python's len() counts code points.
    private static int Len(string s) => s.EnumerateRunes().Count();
}
