local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    patch={
        id='support-15a82e255c6c99508cc7dc33',
        target=hd2.support_weapon('ARC-3 Arc Thrower'):attack('primary'),
        allow_shared=true,
        field=hd2.fields.arc.range,
        expect=55,
        value=75,
    }
}) end)
return operations
