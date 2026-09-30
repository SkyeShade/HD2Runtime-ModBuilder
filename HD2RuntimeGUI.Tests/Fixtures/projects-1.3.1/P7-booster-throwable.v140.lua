local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    patch={
        id='entity-9c0101e1c1706c397433be91',
        target=hd2.booster('Vitality Enhancement'):tuning(),
        allow_unverified_effect=true,
        field=hd2.fields.booster.damage_taken_scale,
        expect=0.9,
        value=1.9,
    }
}) end)
add(function() return hd2.ensure({
    patch={
        id='entity-af7ee23900009ad8d38be115',
        target=hd2.throwable('G-12 High Explosive'),
        allow_unverified_effect=true,
        field=hd2.fields.throwable.starting_count,
        expect=4,
        value=5,
    }
}) end)
return operations
