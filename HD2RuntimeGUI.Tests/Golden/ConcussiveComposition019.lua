local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    plan={
        id='plan-7edd05ad0d661c4b53a0e5bc',
        operations={
            {
                id='op-d9c66a37857de135f8e26297',
                target=hd2.weapon('AR-23C Liberator Concussive'):attack('primary'):projectile():terminal_action('expiry'),
                allow_shared=true,
                field=hd2.fields.terminal.explosion,
                expect=hd2.weapon('AR-23C Liberator Concussive'):attack('primary'):projectile():terminal_action('expiry'):no_explosion(),
                value=hd2.weapon('R-36 Eruptor'):attack('primary'):projectile():terminal_action('impact'):explosion(),
            },
            {
                id='op-e30fb643e6f39424a12f5e15',
                target=hd2.weapon('AR-23C Liberator Concussive'):attack('primary'):projectile():terminal_action('impact'),
                allow_shared=true,
                field=hd2.fields.terminal.explosion,
                expect=hd2.weapon('AR-23C Liberator Concussive'):attack('primary'):projectile():terminal_action('impact'):no_explosion(),
                value=hd2.weapon('R-36 Eruptor'):attack('primary'):projectile():terminal_action('impact'):explosion(),
            },
            {
                id='op-b99aa08efdd7182deec92a03',
                target=hd2.weapon('AR-23C Liberator Concussive'):attack('primary'):projectile(),
                allow_shared=true,
                changes={
                    {field=hd2.fields.damage.ap_direct,expect=2,value=3},
                    {field=hd2.fields.damage.ap_extreme,expect=2,value=3},
                    {field=hd2.fields.damage.ap_large,expect=2,value=3},
                    {field=hd2.fields.damage.ap_slight,expect=2,value=3},
                    {field=hd2.fields.damage.push_force,expect=60,value=30},
                },
            }
        },
    }
}) end)
add(function() return hd2.ensure({
    patch={
        id='gui-object-ef0c749dcbc150be9a262ae5',
        target=hd2.weapon('AR-23C Liberator Concussive'),
        field=hd2.fields.weapon.fire_rate,
        expect=400,
        value=1100,
    }
}) end)
return operations
