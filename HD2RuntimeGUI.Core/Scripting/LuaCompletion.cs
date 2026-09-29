using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Scripting;

// The editor's autocomplete data (serialized to lua-editor.js): the stub's classes and aliases (member name, kind, signature, type, doc)
// and the literal lists the event catalog publishes for event names, explosions, projectiles and statuses. Without a stub, the event
// catalog's API classes are used; without either, only the literal lists are offered.
public sealed record LuaCompletionItem(string N, string K, string? S, string? T, string? D);
public sealed record LuaCompletionData(string Root, Dictionary<string, LuaCompletionItem[]> Classes, Dictionary<string, string[]> Aliases, Dictionary<string, string[]> Strings);

public static class LuaCompletion
{
    public static LuaCompletionData Build(SdkMetadata? sdk)
    {
        var classes = new Dictionary<string, LuaCompletionItem[]>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var root = "";
        if (sdk?.LuaApi is { } api)
        {
            foreach (var (name, members) in api.Classes) classes[name] = members.Select(m => new LuaCompletionItem(m.Name, m.Kind, m.Signature, m.Type, Trim(m.Doc))).ToArray();
            foreach (var (name, values) in api.Aliases) aliases[name] = values;
            root = api.RootClass;
        }
        else if (sdk?.Events is { } events)
        {
            // No stub: the event catalog's own API classes and the hd2 table's scripting members.
            foreach (var (name, c) in events.Api.Classes)
                classes[name] = (c.Fields ?? []).Select(f => new LuaCompletionItem(f[0].TrimEnd('?'), "field", null, f.Length > 1 ? f[1] : null, f.Length > 2 ? Trim(f[2]) : null))
                    .Concat((c.Methods ?? []).Select(m => new LuaCompletionItem(m.Name, "method", m.Signature, m.Returns, Trim(m.Doc)))).ToArray();
            root = "hd2";
            classes[root] = events.Api.Fields.Select(f => new LuaCompletionItem(f[0], "field", null, f[1], null))
                .Concat(events.Api.Functions.Select(f => new LuaCompletionItem(f.Name, "function", f.Signature, f.Returns, Trim(f.Doc)))).ToArray();
        }
        var strings = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (sdk?.Events is { } catalog)
        {
            strings["events"] = catalog.Events.Where(e => e.IsAvailable).Select(e => e.Name).ToArray();
            strings["explosions"] = catalog.ExplosionNames.Distinct().ToArray();
            strings["explosionWeapons"] = catalog.Actions.Explosions.Weapons;
            strings["projectiles"] = catalog.Actions.Projectiles.Weapons;
            strings["statuses"] = catalog.Actions.StatusEffects.Statuses.Select(s => s.Id).ToArray();
        }
        return new(root, classes, aliases, strings);
    }
    private static string? Trim(string? doc) => doc is { Length: > 240 } ? doc[..240] + "…" : doc;
}
