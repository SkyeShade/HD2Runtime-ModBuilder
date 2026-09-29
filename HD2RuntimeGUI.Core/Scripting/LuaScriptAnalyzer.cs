using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Scripting;

// Checks hand-written mod Lua: the syntax (LuaParser), what the build adds itself (the discovery header), and names the SDK enumerates
// where a literal is passed straight to the API: event names (hd2.events.on/once, mod:on/once), explosions, projectiles, statuses and
// enemy semantic IDs. Anything computed at run time is not checked, and nothing is executed; a warning never blocks a build.
public static class LuaScriptAnalyzer
{
    public static IReadOnlyList<LuaDiagnostic> Analyze(string source, EventCatalog? events, EnemyCatalog? enemies = null)
    {
        var result = new List<LuaDiagnostic>();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("-- HD2-Addon:", StringComparison.Ordinal))
                result.Add(new(i + 1, lines[i].IndexOf("-- HD2-Addon:", StringComparison.Ordinal) + 1, LuaDiagnostic.Error, "Remove the '-- HD2-Addon:' header: ModBuilder adds the mod's discovery header itself.", "header"));
            if (lines[i].Contains("---@meta", StringComparison.Ordinal))
                result.Add(new(i + 1, lines[i].IndexOf("---@meta", StringComparison.Ordinal) + 1, LuaDiagnostic.Error, "Remove '---@meta': SDK stub declarations cannot be part of a mod.", "meta"));
        }
        List<LuaToken> tokens;
        try { tokens = LuaParser.Check(source); }
        catch (LuaSyntaxException e)
        {
            result.Add(new(e.Line, e.Column, LuaDiagnostic.Error, e.Message, "syntax"));
            try { tokens = LuaLexer.Tokenize(source); } catch (LuaSyntaxException) { return result; }
        }
        bool Seq(int i, params string[] parts)
        {
            for (var k = 0; k < parts.Length; k++)
            {
                if (i + k >= tokens.Count) return false;
                var t = tokens[i + k]; var part = parts[k];
                if (part == "$str") { if (t.Kind != LuaTokenKind.String) return false; }
                else if (part.Contains('|')) { if (!part.Split('|').Contains(t.Text) || t.Kind is LuaTokenKind.String or LuaTokenKind.Number) return false; }
                else if (t.Text != part || t.Kind is LuaTokenKind.String or LuaTokenKind.Number) return false;
            }
            return true;
        }
        void Warn(LuaToken at, string message, string code) => result.Add(new(at.Line, at.Column, LuaDiagnostic.Warning, message, code));
        var eventNames = events?.Events.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var explosions = events?.ExplosionNames.ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (events != null)
            {
                // hd2.events.on('name', ...) / hd2.events.once(...) / <context>:on('name', ...) / <context>:once(...)
                LuaToken? eventName = Seq(i, "hd2", ".", "events", ".", "on|once", "(", "$str") ? tokens[i + 6] : Seq(i, ":", "on|once", "(", "$str") ? tokens[i + 3] : null;
                if (eventName != null && (i == 0 || !tokens[i - 1].Is(".")) && eventNames!.TryGetValue(eventName.Value!, out var definition) is var known)
                {
                    if (!known) Warn(eventName, $"Unknown event '{eventName.Value}'. Catalogued events: {string.Join(", ", events.Events.Where(e => e.IsAvailable).Select(e => e.Name))}.", "event");
                    else if (!definition!.IsAvailable) Warn(eventName, $"Event '{eventName.Value}' is {definition.Status}: {definition.Reason}", "event");
                }
                if (Seq(i, "hd2", ".", "explosions", ".", "spawn|prepare", "(", "$str") && !explosions!.Contains(tokens[i + 6].Value!))
                    Warn(tokens[i + 6], $"'{tokens[i + 6].Value}' is not a catalogued explosion (hd2.explosions.list()); Runtime refuses it with UNKNOWN_EXPLOSION.", "explosion");
                if (Seq(i, "hd2", ".", "explosions", ".", "of", "(", "$str") && !events.Actions.Explosions.Weapons.Contains(tokens[i + 6].Value!))
                    Warn(tokens[i + 6], $"'{tokens[i + 6].Value}' has no catalogued explosion; hd2.explosions.of returns nil for it.", "explosion");
                if (Seq(i, "hd2", ".", "projectiles", ".", "spawn|prepare", "(", "$str") && !events.Actions.Projectiles.Weapons.Contains(tokens[i + 6].Value!))
                    Warn(tokens[i + 6], $"'{tokens[i + 6].Value}' is not a catalogued projectile weapon (hd2.projectiles.list()); Runtime refuses it with UNKNOWN_PROJECTILE.", "projectile");
                if (Seq(i, "hd2", ".", "status", ".", "apply", "(") && SecondArgument(tokens, i + 6) is { Kind: LuaTokenKind.String } status
                    && !events.Actions.StatusEffects.Statuses.Any(s => s.Id == status.Value))
                    Warn(status, $"'{status.Value}' is not a status hd2.status can apply ({string.Join(", ", events.Actions.StatusEffects.Statuses.Select(s => s.Id))}); Runtime refuses it with UNKNOWN_STATUS.", "status");
                if (Seq(i, "hd2", ".", "input", ".", "bind", "(", "$str") && !tokens[i + 6].Value!.Contains('.'))
                    Warn(tokens[i + 6], "Keybind ids are namespaced ('author_mod.action') so they never collide with another mod's.", "keybind");
            }
            if (enemies != null && tokens[i].Kind == LuaTokenKind.String && tokens[i].Value!.StartsWith("enemy/v1/", StringComparison.Ordinal) && enemies.Find(tokens[i].Value!) == null)
                Warn(tokens[i], $"'{tokens[i].Value}' is not a catalogued enemy or structure semantic id; comparisons with it never match.", "enemy");
        }
        return result.OrderBy(d => d.Line).ThenBy(d => d.Column).ToArray();
    }
    // The token that starts the second argument of a call whose '(' precedes `start`, or null when it is not a plain literal position.
    private static LuaToken? SecondArgument(List<LuaToken> tokens, int start)
    {
        var depth = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == LuaTokenKind.Symbol && t.Text is "(" or "{" or "[") depth++;
            else if (t.Kind == LuaTokenKind.Symbol && t.Text is ")" or "}" or "]") { if (depth == 0) return null; depth--; }
            else if (depth == 0 && t.Is(",")) return i + 1 < tokens.Count ? tokens[i + 1] : null;
        }
        return null;
    }
}
