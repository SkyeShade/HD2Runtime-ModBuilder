using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Scripting;

/// <summary>A live-evidence family (sdk/LiveEvidenceCatalog.json) that covers an event, an action or a handle method.</summary>
public sealed record ScriptEvidence(string Family, LiveEvidenceFamily Evidence)
{
    public const string LiveProvenStatus = "live_proven";
    public bool LiveProven => Evidence.Status == LiveProvenStatus;
}

// What the event reference and the action pickers show next to the catalog's own data: the live evidence Runtime recorded for event
// scripting (domains events and event_actions: the weapon in hand, damage source attribution, heal, the explosion, projectile and status
// actions, the death position), and the plain records a payload carries (HD2StatSource, HD2EquippedWeapon, HD2EventCause).
public static partial class ScriptingReference
{
    /// <summary>The families that name a subject in their fields: an event ("player_damage_dealt", also through
    /// "player_damage_dealt.sources"), an action ("hd2.actions.heal") or a handle method ("player:equipped_weapon").</summary>
    public static IReadOnlyList<ScriptEvidence> Evidence(LiveEvidenceCatalog? catalog, string subject) => catalog == null ? [] : catalog.Families
        .Where(f => f.Value.Domain is "events" or "event_actions" && (f.Value.Fields ?? []).Any(x => x == subject || x.StartsWith(subject + ".", StringComparison.Ordinal)))
        .Select(f => new ScriptEvidence(f.Key, f.Value)).OrderBy(e => e.Family, StringComparer.Ordinal).ToArray();
    /// <summary>The subject of a handle method in the evidence catalog: HD2PlayerHandle + equipped_weapon → "player:equipped_weapon".</summary>
    public static string HandleSubject(string handle, string method) =>
        (handle.StartsWith("HD2", StringComparison.Ordinal) ? handle[3..] : handle).Replace("Handle", "", StringComparison.Ordinal).ToLowerInvariant() + ":" + method;

    // "HD2StatSource[]" / "HD2EquippedWeapon|nil" → the class name.
    public static string BaseType(string type) => (LuaApiIndex.FirstType(type) ?? type).Replace("[]", "", StringComparison.Ordinal);
    [GeneratedRegex(@"\(([a-z][a-z0-9_]*)\)", RegexOptions.CultureInvariant)] private static partial Regex Parenthesized();
    /// <summary>The fields of a payload value whose class the catalog documents as plain data (fields, no methods). A record shared by
    /// several events (HD2StatSource) keeps only the fields this event fills: a field documented for another event ("Kills
    /// (player_kill_credited).") is left out.</summary>
    public static string[][] Detail(EventCatalog catalog, EventDefinition e, EventPayloadField field)
    {
        if (catalog.Api.Classes.GetValueOrDefault(BaseType(field.Type)) is not { Fields: { Length: > 0 } fields } c || c.Methods is { Length: > 0 }) return [];
        var names = catalog.Events.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        return fields.Where(f => !(f.Length > 2 && Parenthesized().Matches(f[2]).Select(m => m.Groups[1].Value).Where(names.Contains).ToArray() is { Length: > 0 } named
            && !named.Contains(e.Name))).ToArray();
    }
}

/// <summary>One diagnostics call as the SDK stub declares it: the prefix a script writes it with and the member.</summary>
public sealed record DiagnosticsCall(string Path, string Prefix, LuaMember Member)
{
    public string Signature => Prefix + Member.Signature;
}
/// <summary>A section of the diagnostics reference: the stub calls it documents, and what Runtime logs (docs/diagnostics.md, docs/events.md).
/// Example is shown as documentation only; Snippet names the insertable snippet, if any.</summary>
public sealed record DiagnosticsSection(string Id, DiagnosticsCall[] Calls, string? Example, string[] LogLines, string? Snippet);

// The diagnostics reference (hd2.diagnostics, sdk docs/diagnostics.md): write conflicts (always on), telemetry (off by default), the logs
// and the status queries a mod can use. Calls come from the SDK stub and are listed only when it publishes them; the log lines are
// Runtime's documented output (never translated). ModBuilder never turns telemetry on: no generated operation, snippet or insert
// contains the opt-in, which is shown only as the documented call a modder may add to custom Lua by hand.
public static class RuntimeDiagnostics
{
    public const string WriteConflicts = "hd2.diagnostics.write_conflicts", Telemetry = "hd2.diagnostics.telemetry", WriteConflictsSnippet = "write-conflicts-report";
    public const string WriteConflictsId = "write-conflicts", TelemetryId = "telemetry", LogsId = "logs", StatusId = "status";
    // The documented opt-in (docs/diagnostics.md "Telemetry (off by default)").
    public const string TelemetryOptIn = "hd2.diagnostics.telemetry({enabled=true, report_seconds=60})";
    public const string WriteConflictLog = "[HD2Runtime] possible write conflict: operation gui-object-3d9ff4833c24584a039f823f (player_weapon SG-20 Halt: weapon.fire_rate) externally changed and re-applied 3 times in 120.0 s; another mod may be writing the same memory";
    public const string TelemetryLog = "[HD2Runtime] telemetry 60 s: update avg 0.071 ms p95 0.12 p99 0.31 max 1.20 (3600); ensure byte-check avg 0.02 ms p95 0.03 p99 0.05 max 0.08 (40); event polling avg 0.05 ms ...; active ensures 12; re-applications 0";
    public const string CallbackFailureLog = "[HD2Runtime] event entity_died callback failed (mod mods/author/my_mod, subscription 12): <error> [<frame>]";
    public const string ModLog = "[HD2Runtime] [mods/author/my_mod] hello";
    // Read-only status a mod can log: event sources, actions, Runtime counters, mods that need a newer Runtime, one subscription.
    private static readonly (string Path, string Prefix)[] StatusCalls =
        [("hd2.events.status", "hd2.events."), ("hd2.actions.status", "hd2.actions."), ("hd2.metrics", "hd2."), ("hd2.compatibility.status", "hd2.compatibility."),
         ("hd2.compatibility.incompatible", "hd2.compatibility."), ("HD2Subscription:describe", "sub:")];

    public static bool Available(LuaApiIndex? api) => api?.Resolve(WriteConflicts) != null || api?.Resolve(Telemetry) != null;
    public static IReadOnlyList<DiagnosticsSection> Sections(LuaApiIndex? api)
    {
        if (api == null) return [];
        DiagnosticsCall[] Calls(params (string Path, string Prefix)[] paths) =>
            paths.Select(p => api.Resolve(p.Path) is { } m ? new DiagnosticsCall(p.Path, p.Prefix, m) : null).OfType<DiagnosticsCall>().ToArray();
        var sections = new List<DiagnosticsSection>();
        if (Calls((WriteConflicts, "hd2.diagnostics.")) is { Length: > 0 } conflicts)
            sections.Add(new(WriteConflictsId, conflicts, null, [WriteConflictLog], WriteConflictsSnippet));
        if (Calls((Telemetry, "hd2.diagnostics.")) is { Length: > 0 } telemetry)
            sections.Add(new(TelemetryId, telemetry, TelemetryOptIn, [TelemetryLog], null));
        sections.Add(new(LogsId, Calls(("HD2ModContext:log", "mod:")), null, [ModLog, CallbackFailureLog, Generation.LuaGenerator.SkippedPrefix + "<error>"], null));
        if (Calls(StatusCalls) is { Length: > 0 } status) sections.Add(new(StatusId, status, null, [], null));
        return sections;
    }
}
