local hd2=require('mods/skyeshade/hd2runtime')

local operations={}
local function add(build)
    local ok,operation=pcall(build)
    if ok then operations[#operations+1]=operation
    else print('[ModBuilder] operation skipped: '..tostring(operation)) end
end
add(function() return hd2.ensure({
    plan={
        id='plan-b10ef46e34f8bb87ba223777',
        phases={
            {id='phase-1',operations={
                {
                    id='op-00a3d460ad81aef626ae75e2',
                    target=hd2.weapon('P-113 Verdict'):attack('primary'),
                    field=hd2.fields.attack.projectile,
                    expect=hd2.weapon('P-113 Verdict'):attack('primary'):projectile(),
                    value=hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile(),
                }
            }},
            {id='phase-2',operations={
                {
                    id='op-9717ae9da0ec90d1e1fd21bf',
                    target_from={operation='op-00a3d460ad81aef626ae75e2',path='projectile'},
                    allow_shared=true,
                    changes={
                        {field=hd2.fields.projectile.drag,expect=0,value=0.2},
                        {field=hd2.fields.projectile.velocity,expect=180,value=350},
                    },
                }
            }},
        },
    }
}) end)
return operations
