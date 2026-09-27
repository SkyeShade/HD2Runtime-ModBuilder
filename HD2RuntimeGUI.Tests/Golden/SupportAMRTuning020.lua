local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    plan={
        id='support-plan-ae646236f0ceb43b70e7977e',
        operations={
            {
                id='support-a5d73b4bd66f805f4d0808bd',
                target=hd2.support_weapon('APW-1 Anti-Materiel Rifle'),
                changes={
                    {field=hd2.fields.weapon.ergonomics,expect=35,value=80},
                    {field=hd2.fields.weapon.sway,expect=1,value=0.5},
                },
            },
            {
                id='support-27223861712cf7ed1efff4bc',
                target=hd2.support_weapon('APW-1 Anti-Materiel Rifle'):attack('primary'):projectile(),
                allow_shared=true,
                field=hd2.fields.projectile.velocity,
                expect=880,
                value=1100,
            }
        },
    }
})
