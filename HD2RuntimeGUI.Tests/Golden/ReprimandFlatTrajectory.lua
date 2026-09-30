local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    transaction={
        id='gui-487fbbd4bf4269691cbf9674',
        target=hd2.weapon('SMG-32 Reprimand'),
        changes={
            {field=hd2.fields.projectile.drag,expect=1.2,value=0.1},
            {field=hd2.fields.projectile.gravity,expect=1,value=0.2},
        },
    }
}) end)
return operations
