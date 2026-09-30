using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Audit;

// Machine-readable capability audit of one loaded SDK: every published capability (field value types per catalog, projectile hosts,
// donor outputs, builder slots, mode presentation, events and actions, SDK files) classified by what this ModBuilder build does with
// it. It is driven by the SDK data ModBuilder loaded (after its own gates), never by a hand-written checklist: only the mapping from an
// authored (catalog, value type) to the editor that shows it, and the justified exemptions, are declared here.
public enum CapabilityClass
{
    SUPPORTED_UI, SUPPORTED_CUSTOM_LUA_ONLY, MISSING_UI, MISSING_CODEGEN, MISSING_PROJECT_MODEL, RUNTIME_READ_ONLY, INTENTIONALLY_NOT_EXPOSED, NOT_APPLICABLE,
}
public sealed record AuditEntry(string Area, string Capability, string Type, CapabilityClass Class, int Instances, int WritableInstances, int ReadOnlyInstances,
    string? Surface, string? Reason);
public sealed record AuditFile(string Name, CapabilityClass Class, string Use);
public sealed record CapabilityAuditReport(string SdkVersion, string? ContentFingerprint, bool? PinnedBuild, IReadOnlyList<AuditEntry> Entries, IReadOnlyList<AuditFile> Files)
{
    public static readonly CapabilityClass[] Missing = [CapabilityClass.MISSING_UI, CapabilityClass.MISSING_CODEGEN, CapabilityClass.MISSING_PROJECT_MODEL];
    public IReadOnlyDictionary<CapabilityClass, int> Totals => Enum.GetValues<CapabilityClass>().ToDictionary(c => c, c => Entries.Count(e => e.Class == c));
    public IReadOnlyDictionary<CapabilityClass, int> InstanceTotals => Enum.GetValues<CapabilityClass>().ToDictionary(c => c, c => Entries.Where(e => e.Class == c).Sum(e => e.Instances));
    // Missing capabilities no exemption accounts for. A release requires zero.
    public IReadOnlyList<AuditEntry> UnexpectedMissing => Entries.Where(e => Missing.Contains(e.Class)).ToArray();
}

/// <summary>Where an authored capability is edited in the UI. A capability the gates keep writable but no surface lists is MISSING_UI.</summary>
public static class CapabilitySurfaces
{
    public static readonly IReadOnlyDictionary<(string Area, string Type), string> Fields = new Dictionary<(string, string), string>
    {
        [("player_weapon", "number")] = "Player Weapons › weapon fields", [("player_weapon", "integer")] = "Player Weapons › weapon fields",
        [("player_weapon", "boolean")] = "Player Weapons › weapon fields", [("player_weapon", "enum")] = "Player Weapons › weapon fields",
        [("player_weapon", "projectile_reference")] = "Player Weapons › projectile selector", [("player_weapon", "explosion_reference")] = "Player Weapons › impact / expiry actions",
        [("player_weapon", WeaponCapability.FireModeSet)] = "Player Weapons › fire modes",
        [("support_weapon", "scalar/number")] = "Stratagems › Support › weapon fields", [("support_weapon", "scalar/integer")] = "Stratagems › Support › weapon fields",
        [("support_weapon", "scalar/boolean")] = "Stratagems › Support › weapon fields", [("support_weapon", "scalar/" + WeaponCapability.FireModeSet)] = "Stratagems › Support › fire modes",
        [("stratagem", "number")] = "Stratagems › stratagem fields", [("stratagem", "integer")] = "Stratagems › stratagem fields",
        [("stratagem", "stratagem_uses")] = "Stratagems › mission uses",
        [("vehicle", "number")] = "Stratagems › Support › vehicle", [("vehicle", "integer")] = "Stratagems › Support › vehicle",
        [("vehicle", EntityField.ReferenceType)] = "Stratagems › Support › vehicle mounts",
        [("backpack", "number")] = "Stratagems › Support › backpack", [("backpack", "integer")] = "Stratagems › Support › backpack",
        [("vehicle_weapon", "number")] = "Stratagems › Support › vehicle weapons", [("vehicle_weapon", "integer")] = "Stratagems › Support › vehicle weapons",
        [("weapon_attachment", "number")] = "Player Weapons › magazine attachments", [("weapon_attachment", "integer")] = "Player Weapons › magazine attachments",
        [("booster", "number")] = "Boosters", [("booster", "integer")] = "Boosters",
        [("pod_rack", "integer")] = "Stratagems › drop-pod contents", [("pod_rack", EntityField.PickupType)] = "Stratagems › drop-pod contents",
        [("throwable", "number")] = "Throwables", [("throwable", "integer")] = "Throwables",
        [("enemy", "number")] = "Enemies", [("enemy", "integer")] = "Enemies", [("structure", "number")] = "Structures", [("structure", "integer")] = "Structures",
    };
    // Player-weapon underbarrels (hd2.weapon(parent):underbarrel()) have their own nested section once this build authors them.
    public static readonly IReadOnlyDictionary<string, string> Subweapons = new Dictionary<string, string>();
    // Projectile host kinds the attack-output editor swaps (AttackProjectileSource.HostKind), and the builder / presentation editors.
    public static readonly IReadOnlyDictionary<string, string> ProjectileHosts = new Dictionary<string, string>
    {
        [AttackProjectileSource.PlayerKind] = "Player Weapons › projectile output",
    };
    public static string? BuilderSlots => null;
    public static string? ModePresentation => null;
    // Custom Lua (src/addon.lua) with the event reference, pickers, snippets and autocomplete.
    public const string CustomLua = "Custom Lua › event reference, pickers and snippets";
}

/// <summary>Capabilities deliberately left out, each with its justification. Keys are "area|capability|type" (type may be "*").</summary>
public static class CapabilityExemptions
{
    public static readonly IReadOnlyDictionary<string, (CapabilityClass Class, string Reason)> Entries = new Dictionary<string, (CapabilityClass, string)>
    {
        ["attack_output|builder class CUSTOM_ROW|*"] = (CapabilityClass.RUNTIME_READ_ONLY, "Runtime 0.28.0 does not support custom rows (projectileBuilder.classes.CUSTOM_ROW.supported = false)."),
    };
    public static (CapabilityClass Class, string Reason)? Find(string area, string capability, string type) =>
        Entries.TryGetValue(area + "|" + capability + "|" + type, out var e) || Entries.TryGetValue(area + "|" + capability + "|*", out e) ? e : null;
}

public static class CapabilityAudit
{
    // SDK files and what this build does with them. Every file in the SDK archive must be listed (the audit test enforces it).
    private static readonly Dictionary<string, (CapabilityClass Class, string Use)> FileUses = new(StringComparer.Ordinal)
    {
        ["metadata.json"] = (CapabilityClass.SUPPORTED_UI, "SDK identity, API types, builders and legacy resources"),
        ["build-report.json"] = (CapabilityClass.NOT_APPLICABLE, "Runtime's own build and test report; not an authoring surface"),
        ["README.md"] = (CapabilityClass.NOT_APPLICABLE, "SDK readme"),
        ["hd2.cmd"] = (CapabilityClass.NOT_APPLICABLE, "Runtime's command-line mod builder; ModBuilder never runs SDK scripts"),
        ["hd2.py"] = (CapabilityClass.NOT_APPLICABLE, "Runtime's command-line mod builder; ModBuilder never runs SDK scripts"),
        ["AttachmentEffectOwnership.json"] = (CapabilityClass.INTENTIONALLY_NOT_EXPOSED, "Research graph behind the attachment catalogs; authoring uses AttachmentOptionCapabilities and MagazineAttachmentCapabilities"),
        ["AttachmentPresetGraph.json"] = (CapabilityClass.INTENTIONALLY_NOT_EXPOSED, "Research graph of native attachment presets; not a write surface"),
        ["AttachmentSelectionCapabilities.json"] = (CapabilityClass.INTENTIONALLY_NOT_EXPOSED, "Research view of attachment selection; the writable attachment fields are in MagazineAttachmentCapabilities"),
        ["WeaponAttachmentCatalog.json"] = (CapabilityClass.INTENTIONALLY_NOT_EXPOSED, "Research catalog of attachments; the writable attachment fields are in MagazineAttachmentCapabilities"),
        ["OutputCompositionCapabilities.json"] = (CapabilityClass.RUNTIME_READ_ONLY, "Research verdict: a projectile weapon cannot fire another output family (beam) while keeping its weapon; no write surface"),
        ["WeaponFireRateCapabilities.json"] = (CapabilityClass.NOT_APPLICABLE, "Per-weapon summary of fire_rate.modes; the authored fields are the per-field metadata in the weapon catalogs"),
        ["WeaponMovementCapabilities.json"] = (CapabilityClass.NOT_APPLICABLE, "Per-weapon summary of weapon.stationary_while_firing; the authored field is in the weapon catalogs"),
    };

    /// <param name="sdkFiles">Every entry of the SDK archive (name → bytes); the audit classifies each file and reads the raw event actions.</param>
    public static CapabilityAuditReport Run(SdkMetadata sdk, IReadOnlyDictionary<string, byte[]>? sdkFiles = null)
    {
        var entries = new List<AuditEntry>();
        // ---- Fields: (area, normalized semantic field, value type), aggregated over instances.
        void Fields<T>(string area, IEnumerable<T> fields, Func<T, string> id, Func<T, string> type, Func<T, bool> editable, Func<T, string?> reason)
        {
            foreach (var g in fields.GroupBy(f => (Id: Normalize(id(f)), Type: type(f))).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.Type, StringComparer.Ordinal))
            {
                var all = g.ToArray(); var writable = all.Count(editable); var gated = all.Count(f => !editable(f) && reason(f) == AuthoredTypes.NotAuthoredReason);
                var readOnly = all.Length - writable - gated;
                var surface = CapabilitySurfaces.Fields.GetValueOrDefault((area, g.Key.Type));
                var exemption = CapabilityExemptions.Find(area, g.Key.Id, g.Key.Type);
                CapabilityClass c; string? why = null;
                if (exemption is { } x) { c = x.Class; why = x.Reason; }
                else if (writable > 0) { c = surface != null ? CapabilityClass.SUPPORTED_UI : CapabilityClass.MISSING_UI; why = surface == null ? "no editor lists this authored type" : null; }
                else if (gated > 0) { c = CapabilityClass.MISSING_UI; why = AuthoredTypes.NotAuthoredReason; }
                else { c = CapabilityClass.RUNTIME_READ_ONLY; why = all.Select(reason).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) ?? "read-only in the SDK"; }
                entries.Add(new(area, g.Key.Id, g.Key.Type, c, all.Length, writable, readOnly + gated, c == CapabilityClass.SUPPORTED_UI ? surface : null, why));
            }
        }
        if (sdk.PlayerWeapons is { } pw)
        {
            Fields("player_weapon", pw.Weapons.SelectMany(w => w.Fields), f => f.SemanticFieldId, f => f.Type, f => f.Editable, f => f.Reason);
            // Underbarrels: their own area; authored only when the subweapon section lists the type.
            foreach (var g in (pw.Subweapons ?? []).SelectMany(s => s.Fields.Select(f => (Sub: s, Field: f))).GroupBy(x => (Id: Normalize(x.Field.SemanticFieldId), x.Field.Type)).OrderBy(g => g.Key.Id, StringComparer.Ordinal))
            {
                var all = g.ToArray(); var writable = all.Count(x => x.Field.Editable);
                var surface = CapabilitySurfaces.Subweapons.GetValueOrDefault(g.Key.Type);
                var c = writable == 0 ? CapabilityClass.RUNTIME_READ_ONLY : surface != null ? CapabilityClass.SUPPORTED_UI : CapabilityClass.MISSING_UI;
                entries.Add(new("subweapon", g.Key.Id, g.Key.Type, c, all.Length, writable, all.Length - writable, surface,
                    c == CapabilityClass.MISSING_UI ? "underbarrel fields have no nested editor" : c == CapabilityClass.RUNTIME_READ_ONLY ? all.Select(x => x.Field.Reason).FirstOrDefault(r => r != null) ?? "read-only in the SDK" : null));
            }
        }
        if (sdk.SupportAuthoring is { } sa)
            Fields("support_weapon", sa.FieldInstances, f => f.SemanticFieldId, f => f.Value.Kind + "/" + f.Value.Type, f => f.Writable && !f.ReadOnly, f => f.BlockedReason);
        if (sdk.Stratagems is { } st)
            Fields("stratagem", st.FieldInstances, f => f.SemanticFieldId, f => f.Type, f => f.Editable, f => f.Reason);
        if (sdk.Entities is { } en)
            foreach (var resource in en.AllFields.GroupBy(f => f.Target.Resource).OrderBy(g => g.Key, StringComparer.Ordinal))
                Fields(resource.Key, resource, f => f.SemanticFieldId + (f.Target.Linked is { } l ? " [" + l + "]" : ""), f => f.Type, f => f.Editable, f => f.Reason);

        // ---- Attack outputs: projectile hosts, donors, builder slots and mode presentation.
        if (sdk.AttackOutputs is { } ao)
        {
            foreach (var g in ao.ProjectileSources.GroupBy(s => (s.HostKind, Writable: s.DirectWritable || s.Status == AttackProjectileSource.Indirect && ao.Ammunition(s.Weapon) != null)).OrderBy(g => g.Key.HostKind))
            {
                var surface = CapabilitySurfaces.ProjectileHosts.GetValueOrDefault(g.Key.HostKind);
                var c = !g.Key.Writable ? CapabilityClass.RUNTIME_READ_ONLY : surface != null ? CapabilityClass.SUPPORTED_UI : CapabilityClass.MISSING_UI;
                entries.Add(new("attack_output", "projectile host (" + g.Key.HostKind + ")", g.Key.Writable ? "writable" : "read-only", c, g.Count(), g.Key.Writable ? g.Count() : 0,
                    g.Key.Writable ? 0 : g.Count(), c == CapabilityClass.SUPPORTED_UI ? surface : null,
                    c == CapabilityClass.RUNTIME_READ_ONLY ? "shown read-only with Runtime's reason (" + string.Join("; ", g.Select(s => s.Status).Distinct().Order()) + ")"
                        : c == CapabilityClass.MISSING_UI ? "host kind not handled by the attack-output editor" : null));
            }
            foreach (var g in ao.Outputs.GroupBy(o => (o.Family, o.SelectableAsProjectileReference)).OrderBy(g => g.Key.Family, StringComparer.Ordinal).ThenBy(g => g.Key.SelectableAsProjectileReference))
            {
                var selectable = g.Key.SelectableAsProjectileReference;
                var c = selectable ? (CapabilitySurfaces.ProjectileHosts.Count > 0 ? CapabilityClass.SUPPORTED_UI : CapabilityClass.MISSING_UI) : CapabilityClass.RUNTIME_READ_ONLY;
                entries.Add(new("attack_output", "donor output (" + g.Key.Family + ")", selectable ? "selectable" : "not selectable", c, g.Count(), selectable ? g.Count() : 0, selectable ? 0 : g.Count(),
                    c == CapabilityClass.SUPPORTED_UI ? "Projectile output picker (one donor pool)" : null, selectable ? null : "shown with Runtime's blocked reason"));
            }
            var rows = ao.Outputs.Where(o => o.Slots != null).ToArray();
            if (rows.Length > 0)
            {
                foreach (var slot in AttackOutputSlots.Keys)
                    entries.Add(new("attack_output", "builder slot " + slot, "slot", CapabilitySurfaces.BuilderSlots != null ? CapabilityClass.SUPPORTED_UI : CapabilityClass.MISSING_UI,
                        rows.Length, rows.Length, 0, CapabilitySurfaces.BuilderSlots, CapabilitySurfaces.BuilderSlots == null ? "no row slot editor" : null));
                foreach (var field in new[] { "mode_label", "mode_icon" })
                    entries.Add(new("attack_output", "presentation." + field, "presentation", CapabilitySurfaces.ModePresentation != null ? CapabilityClass.SUPPORTED_UI : CapabilityClass.MISSING_UI,
                        rows.Length, rows.Count(r => r.Presentation!.Writable), rows.Count(r => !r.Presentation!.Writable), CapabilitySurfaces.ModePresentation,
                        CapabilitySurfaces.ModePresentation == null ? "no mode presentation editor" : null));
            }
            if (ao.Builder is { } b)
                foreach (var (name, cls) in b.Classes.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var key = "builder class " + name; var ex = CapabilityExemptions.Find("attack_output", key, "*");
                    var supported = cls.Supported.ValueKind != JsonValueKind.False;
                    entries.Add(new("attack_output", key, cls.Supported.ValueKind == JsonValueKind.String ? cls.Supported.GetString()! : supported ? "supported" : "unsupported",
                        ex?.Class ?? (supported ? CapabilityClass.SUPPORTED_UI : CapabilityClass.RUNTIME_READ_ONLY), 1, supported ? 1 : 0, supported ? 0 : 1,
                        supported ? "Projectile builder (row slots)" : null, ex?.Reason ?? (supported ? null : cls.Reason)));
                }
        }
        // ---- Events and actions: authored in custom Lua with the reference, pickers and snippets.
        if (sdk.Events is { } ev)
        {
            foreach (var e in ev.Events.OrderBy(e => e.Name, StringComparer.Ordinal))
                entries.Add(new("event", e.Name, "event", e.IsAvailable ? CapabilityClass.SUPPORTED_CUSTOM_LUA_ONLY : CapabilityClass.RUNTIME_READ_ONLY, 1, e.IsAvailable ? 1 : 0, e.IsAvailable ? 0 : 1,
                    e.IsAvailable ? CapabilitySurfaces.CustomLua : null, e.IsAvailable ? null : "blocked in Runtime 0.28.0 (shown in the event reference with its reason)"));
            // Every published action, from the catalog itself: its API, or its blocked status and reason.
            if (sdkFiles?.GetValueOrDefault(EventCatalogReader.FileName) is { } raw)
            {
                using var doc = JsonDocument.Parse(raw);
                foreach (var action in doc.RootElement.GetProperty("actions").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    var blocked = action.Value.TryGetProperty("status", out var status) && status.GetString() == "blocked";
                    var api = action.Value.TryGetProperty("api", out var a) ? a.GetString() : null;
                    entries.Add(new("event_action", api ?? action.Name, "action", blocked ? CapabilityClass.RUNTIME_READ_ONLY : CapabilityClass.SUPPORTED_CUSTOM_LUA_ONLY, 1, blocked ? 0 : 1, blocked ? 1 : 0,
                        blocked ? null : CapabilitySurfaces.CustomLua, blocked ? action.Value.GetProperty("reason").GetString() : null));
                }
            }
        }
        // ---- SDK files.
        var files = new List<AuditFile>();
        foreach (var name in (sdkFiles?.Keys ?? []).Where(n => !n.EndsWith('/')).OrderBy(n => n, StringComparer.Ordinal))
        {
            var top = name.Contains('/') ? name[..(name.IndexOf('/') + 1)] : name;
            if (top is "docs/" or "templates/" or "tools/") { files.Add(new(name, CapabilityClass.NOT_APPLICABLE, top == "docs/" ? "Runtime documentation (linked from the references)" : "Runtime SDK tooling / templates; never run by ModBuilder")); continue; }
            if (name == Scripting.LuaApiIndex.StubPath) { files.Add(new(name, CapabilityClass.SUPPORTED_UI, "LuaLS stub: custom Lua autocomplete")); continue; }
            if (FileUses.TryGetValue(name, out var use)) { files.Add(new(name, use.Class, use.Use)); continue; }
            files.Add(IsRead(name) ? new(name, CapabilityClass.SUPPORTED_UI, "read, validated and cross-checked by SdkCache")
                : new(name, CapabilityClass.MISSING_UI, "not read and not classified"));
        }
        return new(sdk.Version, sdk.ContentFingerprint, sdk.IsPinnedBuild, entries, files);
    }
    // Files SdkCache reads (the consumed set of the SDK pin).
    private static bool IsRead(string name) => name is "metadata.json" || name == PlayerWeaponCatalogReader.FileName || name == PlayerWeaponAmmoCatalogReader.FileName
        || SdkCache.ConsumedFiles.Contains(name);
    // Role, feed, phase and status-slot qualifiers name instances of one capability.
    private static string Normalize(string id) => Regex.Replace(Regex.Replace(id, @"\.(primary|alternate|feed_primary|feed_alternate|impact|expiry)(?=\.)", ""), @"status_\d+_", "status_");

    public static string Json(CapabilityAuditReport r) => JsonSerializer.Serialize(new
    {
        sdk = r.SdkVersion, contentFingerprint = r.ContentFingerprint, pinnedBuild = r.PinnedBuild,
        totals = r.Totals.ToDictionary(p => p.Key.ToString(), p => p.Value), instanceTotals = r.InstanceTotals.ToDictionary(p => p.Key.ToString(), p => p.Value),
        unexpectedMissing = r.UnexpectedMissing.Count,
        entries = r.Entries.Select(e => new { e.Area, e.Capability, e.Type, Class = e.Class.ToString(), e.Instances, e.WritableInstances, e.ReadOnlyInstances, e.Surface, e.Reason }),
        files = r.Files.Select(f => new { f.Name, Class = f.Class.ToString(), f.Use }),
    }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public static string Markdown(CapabilityAuditReport r, string title)
    {
        var s = new StringBuilder();
        string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
        s.Append("# ").Append(title).Append("\n\n");
        s.Append("Generated from the loaded SDK by `CapabilityAudit` (HD2RuntimeGUI.Core/Audit). SDK **").Append(r.SdkVersion).Append("**, content fingerprint `")
            .Append(r.ContentFingerprint).Append("`").Append(r.PinnedBuild == true ? " (the pinned build)" : r.PinnedBuild == false ? " (**not** the pinned build)" : "").Append(".\n\n");
        s.Append("## Totals\n\n| Class | Capabilities | Instances |\n| --- | ---: | ---: |\n");
        foreach (var c in Enum.GetValues<CapabilityClass>()) s.Append("| ").Append(c).Append(" | ").Append(N(r.Totals[c])).Append(" | ").Append(N(r.InstanceTotals[c])).Append(" |\n");
        s.Append("| **Total** | ").Append(N(r.Entries.Count)).Append(" | ").Append(N(r.Entries.Sum(e => e.Instances))).Append(" |\n\n");
        s.Append("**Unexpected missing capabilities: ").Append(r.UnexpectedMissing.Count).Append("**\n\n");
        foreach (var area in r.Entries.GroupBy(e => e.Area))
        {
            s.Append("## ").Append(area.Key).Append("\n\n| Capability | Type | Class | Instances (writable / read-only) | Surface or reason |\n| --- | --- | --- | ---: | --- |\n");
            foreach (var e in area)
                s.Append("| `").Append(e.Capability).Append("` | ").Append(e.Type).Append(" | ").Append(e.Class).Append(" | ").Append(N(e.Instances)).Append(" (").Append(N(e.WritableInstances)).Append(" / ")
                    .Append(N(e.ReadOnlyInstances)).Append(") | ").Append((e.Surface ?? e.Reason ?? "").Replace("|", "\\|").Replace("\n", " ")).Append(" |\n");
            s.Append('\n');
        }
        s.Append("## SDK files\n\n| File | Class | Use |\n| --- | --- | --- |\n");
        foreach (var f in r.Files) s.Append("| `").Append(f.Name).Append("` | ").Append(f.Class).Append(" | ").Append(f.Use.Replace("|", "\\|")).Append(" |\n");
        return s.ToString();
    }
}
