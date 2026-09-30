local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    transaction={
        id='gui-3be120931696095c6fe0ac89',
        target=hd2.weapon('P-113 Verdict'),
        changes={
            {field=hd2.fields.projectile.drag,expect=1.2,value=0.1},
            {field=hd2.fields.projectile.gravity,expect=1,value=0.2},
        },
    }
}) end)
return operations
