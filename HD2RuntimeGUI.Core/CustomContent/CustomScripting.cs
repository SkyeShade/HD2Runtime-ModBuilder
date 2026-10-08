using System.Reflection;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Scripting;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>One Lua escape hatch of a custom stratagem: a callback of hd2.custom_stratagem.register (its body, given ctx), the setup that runs
/// before the registration, or extra registration fields. Key is the diagnostic field; Lua is Runtime's name; Label and Help are resource keys.</summary>
public sealed record CustomLuaSlot(string Key, string Lua, string Label, string Help, Func<CustomCallbacks, string?> Get, Action<CustomCallbacks, string?> Set, bool Callback = true);

/// <summary>A ready piece of Lua for a callback (inserted at the caret), from the 0.30.0 API's real signatures.</summary>
public sealed record CustomLuaSnippet(string Key, string Label, string Code, IReadOnlyList<string> Slots);

/// <summary>The custom stratagem scripting surface of the target Runtime: the callbacks in the order the Runtime runs them, autocomplete from that
/// Runtime's own LuaLS stub (the bound SDK may be older and know nothing of hd2.custom_stratagem), and snippets.</summary>
public static class CustomScripting
{
    public const string StubResource = "HD2RuntimeGUI.Core.CustomContent.Targets.hd2runtime-stubs-0.30.0.lua";
    public const string CallClass = "HD2CustomStratagemCall";

    public static readonly IReadOnlyList<CustomLuaSlot> Slots =
    [
        new("callbacks.setup", "setup", "CustomStratagem.Lua.Setup", "CustomStratagem.Lua.Setup.Help", c => c.Setup, (c, v) => c.Setup = v, Callback: false),
        new("callbacks.on_called", "on_called", "CustomStratagem.Lua.OnCalled", "CustomStratagem.Lua.OnCalled.Help", c => c.OnCalled, (c, v) => c.OnCalled = v),
        new("callbacks.on_beacon_created", "on_beacon_created", "CustomStratagem.Lua.OnBeaconCreated", "CustomStratagem.Lua.OnBeaconCreated.Help", c => c.OnBeaconCreated, (c, v) => c.OnBeaconCreated = v),
        new("callbacks.on_beacon_landed", "on_beacon_landed", "CustomStratagem.Lua.OnBeaconLanded", "CustomStratagem.Lua.OnBeaconLanded.Help", c => c.OnBeaconLanded, (c, v) => c.OnBeaconLanded = v),
        new("callbacks.on_activate", "on_activate", "CustomStratagem.Lua.OnActivate", "CustomStratagem.Lua.OnActivate.Help", c => c.OnActivate, (c, v) => c.OnActivate = v),
        new("callbacks.on_delivered", "on_delivered", "CustomStratagem.Lua.OnDelivered", "CustomStratagem.Lua.OnDelivered.Help", c => c.OnDelivered, (c, v) => c.OnDelivered = v),
        new("callbacks.extra", "fields", "CustomStratagem.Lua.Extra", "CustomStratagem.Lua.Extra.Help", c => c.ExtraFields, (c, v) => c.ExtraFields = v, Callback: false),
    ];

    private static readonly Lazy<LuaApiIndex?> api = new(() =>
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(StubResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return LuaStubReader.Read(reader.ReadToEnd());
        }
        catch (Exception e) when (e is InvalidDataException or IOException or FormatException) { return null; }
    });
    /// <summary>The target Runtime's API index (null if its stub cannot be read: autocomplete is then off, nothing else changes).</summary>
    public static LuaApiIndex? Api => api.Value;

    /// <summary>Completion for a callback body: the target Runtime's classes and aliases, the bound SDK's catalog strings, and ctx typed as one
    /// custom stratagem call (or no ctx, for the setup and the extra fields).</summary>
    public static LuaCompletionData Completion(SdkMetadata? sdk, bool callback)
    {
        var data = LuaCompletion.Build(sdk);
        if (Api is { } index)
        {
            foreach (var (name, members) in index.Classes)
                data.Classes[name] = members.Select(m => new LuaCompletionItem(m.Name, m.Kind, m.Signature, m.Type, m.Doc is { Length: > 240 } d ? d[..240] + "…" : m.Doc, m.Param)).ToArray();
            foreach (var (name, values) in index.Aliases) data.Aliases[name] = values;
            data = data with { Root = index.RootClass };
        }
        return data with { Locals = callback ? new Dictionary<string, string>(StringComparer.Ordinal) { ["ctx"] = CallClass } : null };
    }

    /// <summary>Snippets for a stratagem: each names the slots it makes sense in. Names come from the target's catalogues (the stratagem's own
    /// shell or explosion when it has one), never invented.</summary>
    public static IReadOnlyList<CustomLuaSnippet> Snippets(CustomStratagem s, CustomSchema schema)
    {
        var explosion = s.Delivery.Orbital?.ImpactPayload?.Id ?? schema.Catalogs.Explosions.FirstOrDefault()?.Name ?? "Orbital Gas Strike";
        var shell = s.Delivery.Orbital?.Shell ?? schema.Catalogs.Orbitals.FirstOrDefault(o => o.Pattern != null)?.Name ?? "Orbital 120mm HE Barrage";
        var pattern = s.Delivery.Orbital?.Pattern ?? "Orbital 120mm HE Barrage";
        string Q(string text) => RuntimeRegistrationLua.LuaString(text);
        string[] callbacks = ["on_called", "on_beacon_created", "on_beacon_landed", "on_activate", "on_delivered"];
        return
        [
            new("log", "CustomStratagem.Snippet.Log", "ctx:log('call ' .. ctx.call_id .. ' at ' .. tostring(ctx.position))", callbacks),
            new("barrage", "CustomStratagem.Snippet.Barrage", "ctx:barrage({shell = " + Q(shell) + ", pattern = " + Q(pattern) + "})", ["on_activate", "on_beacon_landed"]),
            new("explosion", "CustomStratagem.Snippet.Explosion", "hd2.explosions.spawn(" + Q(explosion) + ", {position = ctx.position})", ["on_activate", "on_beacon_landed"]),
            new("pelican", "CustomStratagem.Snippet.Pelican",
                "hd2.pelican.spawn({\n    position = ctx.position, hover = 60, call = ctx, credit_to = ctx.player,\n    orbit = {radius = 40, altitude = 60, duration = 55},\n"
                + "    gun = {behave_as = 'gatling_sentry', round = 'ap4', unlimited_ammo = true, face_target = true},\n})", ["on_activate"]),
            new("delivered", "CustomStratagem.Snippet.Delivered",
                "for _, weapon in ipairs(ctx.weapons or {}) do\n    weapon:set_impact_explosion(" + Q(explosion) + ")\nend", ["on_delivered"]),
            new("counter", "CustomStratagem.Snippet.Counter", "local calls = 0", ["setup"]),
            new("token", "CustomStratagem.Snippet.Token", "selection = 'token',", ["fields"]),
        ];
    }
}
