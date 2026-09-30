local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    patch={
        id='stratagem-558f449fba24680f9ad02aa5',
        target=hd2.stratagem('Orbital Precision Strike'),
        field=hd2.fields.stratagem.definition_cooldown,
        expect=80,
        value=40,
    }
}) end)
return operations
