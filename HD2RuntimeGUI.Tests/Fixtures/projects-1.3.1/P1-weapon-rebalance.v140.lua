local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    patch={
        id='gui-object-82cf8853a8a8036d3f092dab',
        target=hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile(),
        allow_shared=true,
        field=hd2.fields.projectile.velocity,
        expect=180,
        value=600,
    }
}) end)
add(function() return hd2.ensure({
    patch={
        id='gui-object-9eeedb33e5bad9057111a051',
        target=hd2.weapon('AR-23 Liberator'),
        field=hd2.fields.weapon.fire_rate,
        expect=640,
        value=720,
    }
}) end)
add(function() return hd2.ensure({
    patch={
        id='gui-object-da768a5752861c9f9c0589b6',
        target=hd2.weapon('AR-23 Liberator'),
        field=hd2.fields.weapon.ergonomics,
        expect=65,
        value=60,
    }
}) end)
return operations
