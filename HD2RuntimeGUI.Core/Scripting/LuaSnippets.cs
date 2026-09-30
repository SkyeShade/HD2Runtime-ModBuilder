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
        return result;
    }
}
