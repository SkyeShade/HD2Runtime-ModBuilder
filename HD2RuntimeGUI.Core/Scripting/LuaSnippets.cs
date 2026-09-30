using System.Globalization;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Scripting;

// Insertable patterns for hand-written event scripting, written against the current Runtime API (docs/event-scripting.md and the SDK stub).
// Names used in them (events, explosions, projectiles, statuses, weapon values) come from the bound SDK where it publishes them; a snippet
// that needs something the SDK does not publish is not offered. Titles and descriptions are UI text (Snippets.*); the code never changes.
public sealed record LuaSnippet(string Id, string Title, string Description, string Code, string[] Uses);

public static class LuaSnippets
{
    // A keybind id for this mod: 'author_mod.action' from mods/<author>/<mod>.
    public static string BindingPrefix(string resourceId)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[1] + "_" + parts[2] : "my_mod";
    }
    public static IReadOnlyList<LuaSnippet> For(SdkMetadata sdk, string resourceId)
    {
        if (sdk.Events is not { } events) return [];
        var result = new List<LuaSnippet>();
        bool Event(string name) => events.Event(name)?.IsAvailable == true;
        var hellbomb = events.Actions.Explosions.Named.FirstOrDefault(n => n.Aliases.Contains("Hellbomb"))?.Aliases.First(a => a == "Hellbomb");
        var weaponExplosion = events.Actions.Explosions.Weapons.Contains("R-36 Eruptor") ? "R-36 Eruptor" : events.Actions.Explosions.Weapons.FirstOrDefault();
        var projectile = events.Actions.Projectiles.LiveProven.FirstOrDefault() ?? events.Actions.Projectiles.Weapons.FirstOrDefault();
        var status = events.Actions.StatusEffects.Statuses.FirstOrDefault(s => s.LiveProven) ?? events.Actions.StatusEffects.Statuses.FirstOrDefault();
        var enemy = sdk.Entities?.Enemies?.Find("enemy/v1/automatons/soldier_mg")?.SemanticId ?? sdk.Entities?.Enemies?.Of(EnemyAuthoringReader.Enemy).FirstOrDefault()?.SemanticId;
        var prefix = BindingPrefix(resourceId);

        if (Event("player_died") && hellbomb != null)
            result.Add(new("player-death-explosion", CoreText.Get("Snippets.PlayerDeathExplosion.Title"), CoreText.Get("Snippets.PlayerDeathExplosion.Description"),
                $$"""
                hd2.events.on('player_died', function(event)
                    if event.local_player and event.position and event.cause.source ~= 'mod' then
                        hd2.explosions.spawn('{{hellbomb}}', {position = event.position})
                    end
                end)

                """, ["player_died", "hd2.explosions.spawn"]));
        if (Event("entity_died") && Event("mission_started") && enemy != null && weaponExplosion != null)
            result.Add(new("enemy-death-callback", CoreText.Get("Snippets.EnemyDeathCallback.Title"), CoreText.Get("Snippets.EnemyDeathCallback.Description"),
                $$"""
                local TARGET = '{{enemy}}'
                assert(hd2.entities.describe(TARGET), 'unknown enemy id ' .. TARGET)

                hd2.events.on('mission_started', function() hd2.explosions.prepare('{{weaponExplosion}}') end)

                hd2.events.on('entity_died', function(event)
                    if event.semantic_id ~= TARGET or not event.position then return end
                    local position = event.position -- a read-only snapshot: still valid after the enemy is gone
                    hd2.after(math.random(1, 10), function()
                        local action = hd2.explosions.spawn('{{weaponExplosion}}', {position = position})
                        if action.status == 'refused' then hd2.mod():log('explosion refused: ' .. action.code) end
                    end, {scope = 'mission'})
                end)

                """, ["entity_died", "hd2.after", "hd2.explosions.spawn"]));
        // Kill stacking needs a weapon damage value to guard; the Liberator's published standard damage is the baseline.
        if (Event("player_kill_credited") && sdk.PlayerWeapons?.FindCanonicalField("AR-23 Liberator", "damage.standard_damage") is { Editable: true, ApiFieldConstant: { } api } damage
            && damage.CurrentDefault.TryGetInt32(out var baseDamage))
        {
            var step = Math.Max(1, baseDamage / 10);
            result.Add(new("kill-stacking", CoreText.Get("Snippets.KillStacking.Title"), CoreText.Get("Snippets.KillStacking.Description"),
                $$"""
                local stack_mod = hd2.mod()
                local BASE, STEP, CAP = {{baseDamage.ToString(CultureInfo.InvariantCulture)}}, {{step.ToString(CultureInfo.InvariantCulture)}}, 10 -- published standard damage; +10% per kill; at most +100%
                local damage = stack_mod:value({id = 'liberator_damage', min = BASE, max = BASE + STEP * CAP, step = STEP, default = BASE})

                local function stacks(count)
                    stack_mod.mission.stacks = count
                    damage:set(BASE + STEP * count)
                end
                hd2.events.on('mission_started', function() stacks(0) end, {id = 'stack_reset_on_start'})
                hd2.events.on('mission_ended', function() damage:set(BASE) end, {id = 'stack_reset_on_end'})
                hd2.events.on('player_kill_credited', function(event)
                    local gained = 0
                    for _, source in ipairs(event.sources) do
                        if source.name == 'AR-23 Liberator' then gained = gained + source.kills end
                    end
                    if gained > 0 then stacks(math.min(CAP, (stack_mod.mission.stacks or 0) + gained)) end
                end, {id = 'stack_on_liberator_kill'})

                -- The guarded definition write, re-applied whenever the script value changes.
                hd2.ensure({
                    patch = {id = 'kill-stack-liberator-damage', target = hd2.weapon('AR-23 Liberator'):attack('primary'):projectile(),
                        allow_shared = true, field = {{api}}, expect = BASE, value = damage},
                })

                """, ["player_kill_credited", "mod:value", "hd2.ensure"]));
        }
        result.Add(new("timer", CoreText.Get("Snippets.Timer.Title"), CoreText.Get("Snippets.Timer.Description"),
            """
            local delayed = hd2.after(2.5, function(timer)
                hd2.mod():log('2.5 s of game time later')
            end, {scope = 'mission'})

            local repeating = hd2.every(1, function(timer)
                -- runs every second of game time until cancelled or the mission ends
            end, {scope = 'mission'})
            -- delayed:cancel(); repeating:cancel()

            """, ["hd2.after", "hd2.every"]));
        result.Add(new("keybind", CoreText.Get("Snippets.Keybind.Title"), CoreText.Get("Snippets.Keybind.Description"),
            $$"""
            hd2.input.bind('{{prefix}}.action', {key = 'F6', on_press = function(binding)
                local player = hd2.local_player()
                local here = player and player:position()
                if here then hd2.mod():log('F6 pressed at ' .. tostring(here)) end
            end})

            """, ["hd2.input.bind"]));
        if (projectile != null)
            result.Add(new("projectile-spawn", CoreText.Get("Snippets.ProjectileSpawn.Title"), CoreText.Format("Snippets.ProjectileSpawn.Description", string.Join(", ", events.Actions.Projectiles.LiveProven)),
                $$"""
                local player = hd2.local_player()
                local p = player and player:position()
                if p then
                    local action = hd2.projectiles.spawn('{{projectile}}', {
                        position = {x = p.x, y = p.y, z = p.z + 2.5},
                        direction = {x = 1, y = 0, z = 0},
                    })
                    if action.status == 'refused' then hd2.mod():log(action.code .. ': ' .. action.reason) end
                end

                """, ["hd2.projectiles.spawn"]));
        if (status != null && Event("entity_damaged"))
            result.Add(new("status-application", CoreText.Get("Snippets.StatusApplication.Title"), CoreText.Format("Snippets.StatusApplication.Description", status.Id),
                $$"""
                hd2.events.on('entity_damaged', function(event)
                    if event.local_attacker and event.enemy then
                        hd2.status.apply(event.entity, '{{status.Id}}', {buildup = 100})
                    end
                end)

                """, ["entity_damaged", "hd2.status.apply"]));
        if (Event("weapon_changed"))
            result.Add(new("weapon-changed", CoreText.Get("Snippets.WeaponChanged.Title"), CoreText.Get("Snippets.WeaponChanged.Description"),
                """
                hd2.events.on('weapon_changed', function(event)
                    local previous = event.previous and event.previous.name or 'nothing'
                    local current = event.current and event.current.name or 'nothing'
                    hd2.mod():log(previous .. ' -> ' .. current)
                end)

                """, ["weapon_changed"]));
        if (Event("entity_killed"))
            result.Add(new("heal-on-kill", CoreText.Get("Snippets.HealOnKill.Title"), CoreText.Get("Snippets.HealOnKill.Description"),
                """
                hd2.events.on('entity_killed', function(event)
                    if not (event.local_killer and event.enemy) then return end
                    local amount, why = hd2.actions.heal(25)
                    if not amount then hd2.mod():log('heal refused: ' .. tostring(why)) end
                end)

                """, ["entity_killed", "hd2.actions.heal"]));
        if (Event("entity_killed") && Event("mission_ended"))
            result.Add(new("mission-counter", CoreText.Get("Snippets.MissionCounter.Title"), CoreText.Get("Snippets.MissionCounter.Description"),
                """
                local counter_mod = hd2.mod()
                hd2.events.on('entity_killed', function(event)
                    if not event.local_killer then return end
                    counter_mod.mission.kills = (counter_mod.mission.kills or 0) + 1
                end)
                hd2.events.on('mission_ended', function()
                    counter_mod:log('mission over: ' .. (counter_mod.mission.kills or 0) .. ' kills')
                end)

                """, ["entity_killed", "mission_ended"]));
        // player_hit (0.28.0): projectile hits per source next to player_fired's shots, logged when the mission ends (mod.mission is still
        // readable in mission_ended).
        if (Event("player_fired") && Event("player_hit") && Event("mission_ended"))
            result.Add(new("player-hit-accuracy", CoreText.Get("Snippets.PlayerHitAccuracy.Title"), CoreText.Get("Snippets.PlayerHitAccuracy.Description"),
                """
                local accuracy_mod = hd2.mod()
                local function tally(key, event, count)
                    local totals = accuracy_mod.mission[key] or {}
                    accuracy_mod.mission[key] = totals
                    for _, source in ipairs(event.sources) do
                        local name = source.name or ('unnamed ' .. source.type)
                        totals[name] = (totals[name] or 0) + (source[count] or 0)
                    end
                end
                hd2.events.on('player_fired', function(event) tally('shots', event, 'shots') end)
                hd2.events.on('player_hit', function(event) tally('hits', event, 'hits') end)
                hd2.events.on('mission_ended', function()
                    local shots, hits = accuracy_mod.mission.shots or {}, accuracy_mod.mission.hits or {}
                    for name, fired in pairs(shots) do
                        accuracy_mod:log(('%s: %d hits from %d shots'):format(name, hits[name] or 0, fired))
                    end
                end)

                """, ["player_fired", "player_hit", "mission_ended"]));
        // player_damage_dealt (0.28.0): heal on the damage a throwable deals. The K-2 Throwing Knife is keyed by itself in the game's damage
        // stats and never appears in player_hit (it is not a projectile-system projectile); the knife attribution and heal(25) from this
        // handler are what the SDK's live evidence records (event_damage_source_attribution, event_action_heal).
        if (Event("player_damage_dealt") && sdk.Entities?.Throwables?.Find("K-2 Throwing Knife")?.Name is { } knife)
            result.Add(new("damage-dealt-heal", CoreText.Get("Snippets.DamageDealtHeal.Title"), CoreText.Format("Snippets.DamageDealtHeal.Description", knife),
                $$"""
                local KNIFE = {{Generation.LuaGenerator.Quote(knife)}}
                hd2.events.on('player_damage_dealt', function(event)
                    for _, source in ipairs(event.sources) do
                        if source.name == KNIFE and (source.damage or 0) > 0 then
                            local amount, why = hd2.actions.heal(25)
                            if not amount then hd2.mod():log('heal refused: ' .. tostring(why)) end
                            return
                        end
                    end
                end)

                """, ["player_damage_dealt", "hd2.actions.heal"]));
        // Diagnostics (hd2.diagnostics, docs/diagnostics.md): read the always-on write-conflict report. Telemetry is never part of a snippet.
        if (Event("mission_ended") && sdk.LuaApi?.Resolve(RuntimeDiagnostics.WriteConflicts) != null)
            result.Add(new(RuntimeDiagnostics.WriteConflictsSnippet, CoreText.Get("Snippets.WriteConflicts.Title"), CoreText.Get("Snippets.WriteConflicts.Description"),
                """
                hd2.events.on('mission_ended', function()
                    for _, conflict in ipairs(hd2.diagnostics.write_conflicts()) do
                        hd2.mod():log(('write conflict: %s (%s) re-applied %d times'):format(conflict.operation, tostring(conflict.target), conflict.externalChanges))
                    end
                end)

                """, ["mission_ended", RuntimeDiagnostics.WriteConflicts]));
        return result;
    }

    /// <summary>What the action pickers insert: one call each, with the catalogued name verbatim. Positions and entities come from the
    /// event the user is handling (event.position, event.entity).</summary>
    public static class Calls
    {
        public static string Explosion(string name) => "hd2.explosions.spawn(" + Generation.LuaGenerator.Quote(name) + ", {position = event.position})\n";
        public static string Projectile(string weapon) => "hd2.projectiles.spawn(" + Generation.LuaGenerator.Quote(weapon) + ", {position = position, direction = {x = 1, y = 0, z = 0}})\n";
        public static string Status(string id) => "hd2.status.apply(event.entity, " + Generation.LuaGenerator.Quote(id) + ", {buildup = 100})\n";
        public const string Heal = "local amount, why = hd2.actions.heal(25)\n";
        public const string PlayerHeal = """
            local player = hd2.local_player()
            if player then
                local amount, why = player:heal(25)
                if not amount then hd2.mod():log('heal refused: ' .. tostring(why)) end
            end

            """;
        public const string EquippedWeapon = """
            local player = hd2.local_player()
            if player then
                local weapon, why = player:equipped_weapon()
                hd2.mod():log(weapon and (tostring(weapon.name) .. ' in the ' .. tostring(weapon.slot) .. ' slot') or ('nothing in hand: ' .. tostring(why)))
            end

            """;
    }

    /// <summary>A subscription to one catalogued event, listing its payload fields (and, for an array of plain records such as
    /// event.sources, the record fields this event fills). Blocked events have none.</summary>
    public static string? Handler(EventCatalog catalog, EventDefinition e)
    {
        if (!e.IsAvailable) return null;
        var code = new System.Text.StringBuilder("hd2.events.on(" + Generation.LuaGenerator.Quote(e.Name) + ", function(event)\n");
        Comment(code, "    ", (e.Payload ?? []).Select(f => "event." + f.Name));
        foreach (var f in (e.Payload ?? []).Where(f => f.Type.EndsWith("[]", StringComparison.Ordinal)))
        {
            var detail = ScriptingReference.Detail(catalog, e, f);
            if (detail.Length == 0) continue;
            var item = f.Name.EndsWith('s') && f.Name.Length > 1 ? f.Name[..^1] : "item";
            code.Append("    for _, ").Append(item).Append(" in ipairs(event.").Append(f.Name).Append(") do\n");
            Comment(code, "        ", detail.Select(d => item + "." + d[0].TrimEnd('?')));
            code.Append("    end\n");
        }
        return code.Append("end)\n").ToString();
    }
    // "-- a, b, c" comment lines of at most about 100 characters.
    private static void Comment(System.Text.StringBuilder code, string indent, IEnumerable<string> names)
    {
        var line = "";
        foreach (var name in names)
        {
            if (line.Length > 0 && indent.Length + line.Length + name.Length > 100) { code.Append(indent).Append("-- ").Append(line).Append(",\n"); line = ""; }
            line = line.Length == 0 ? name : line + ", " + name;
        }
        if (line.Length > 0) code.Append(indent).Append("-- ").Append(line).Append('\n');
    }
}
