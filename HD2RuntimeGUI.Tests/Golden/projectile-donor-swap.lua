local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    patch={
        id='output-d5696ad533cd04bd8a083421',
        target=hd2.weapon('SMG-32 Reprimand'):attack('primary'),
        allow_unverified_effect=true,
        allow_unverified_reference=true,
        field=hd2.fields.attack.projectile,
        expect=hd2.weapon('SMG-32 Reprimand'):attack('primary'):projectile(),
        value=hd2.attack_output('output/v1/projectile/eagle-500kg-bomb-projectile-239'),
    }
}) end)
return operations
