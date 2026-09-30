local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    plan={
        id='support-plan-3d25332a72be70be9a448a90',
        operations={
            {
                id='support-320e21e6f9200c19cade60e0',
                target=hd2.support_weapon('GR-8 Recoilless Rifle'):attack('primary_impact'):explosion(),
                allow_shared=true,
                field=hd2.fields.explosion.outer_radius,
                expect=3,
                value=10,
            },
            {
                id='support-a39ac76d95b7ce223518fe89',
                target=hd2.support_weapon('GR-8 Recoilless Rifle'):attack('primary'):projectile(),
                allow_shared=true,
                field=hd2.fields.projectile.velocity,
                expect=250,
                value=350,
            }
        },
    }
}) end)
return operations
