local hd2=require('mods/skyeshade/hd2runtime')

local generated={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then generated[#generated+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    patch={
        id='gui-object-99f641c7647b621ccc5e0e3b',
        target=hd2.weapon('AR/GL-21 One-Two'):underbarrel(),
        allow_unverified_effect=true,
        field=hd2.fields.weapon.horizontal_spread,
        expect=30,
        value=15,
    }
}) end)
add(function() return hd2.ensure({
    patch={
        id='gui-object-f36946e0a254dcd20274626d',
        target=hd2.weapon('AR/GL-21 One-Two'):underbarrel(),
        allow_unverified_effect=true,
        field=hd2.fields.rounds.spare_rounds,
        expect=5,
        value=10,
    }
}) end)
add(function() return hd2.ensure({
    patch={
        id='output-e8740ddf690486e27ee93701',
        target=hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun'):attack('primary'):projectile_source().target,
        field=hd2.fields.attack.projectile,
        expect=hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun'):attack('primary'),
        value=hd2.attack_output('output/v1/projectile/eat-17-expendable-anti-tank'),
    }
}) end)
add(function() return hd2.ensure({
    transaction={
        id='support-cd850847d805a6a63a82affe',
        target=hd2.support_weapon('MG-206 Heavy Machine Gun'),
        changes={
            {field=hd2.fields.fire_rate.modes,expect={450,600,750},value={450,600,900}},
            {field=hd2.fields.function_ammo.projectile,expect='none',value=hd2.attack_output('output/v1/projectile/r-4-hyena')},
            {field=hd2.fields.weapon_function.left,expect='none',value='programmable_ammo'},
        },
    }
}) end)

-- Custom Lua: src/addon.lua (hand-written; ModBuilder copies it unchanged)
local function addon(...)
-- Custom HD2Runtime Lua for this mod. ModBuilder's generated modifications are registered before this runs.
-- `hd2` is already required; the event API reference and snippets are in ModBuilder's Custom Lua page.
local mod = hd2.mod()
hd2.events.on('player_hit', function(event)
    -- event.player, event.local_player, event.hits, event.total, event.sources, event.unattributed
    for _, source in ipairs(event.sources) do
        -- source.type, source.name, source.hits
    end
end)
end
addon()
return generated
