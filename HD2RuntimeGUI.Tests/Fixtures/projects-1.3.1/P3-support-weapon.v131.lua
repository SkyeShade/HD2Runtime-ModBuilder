local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    plan={
        id='support-plan-ffa42f50ffc6245bbf091d98',
        operations={
            {
                id='support-8b5d6ac209fe1fa0e5ba59c4',
                target=hd2.support_weapon('APW-1 Anti-Materiel Rifle'),
                field=hd2.fields.weapon.ergonomics,
                expect=35,
                value=80,
            },
            {
                id='support-68af6abd528d2b47a56e6699',
                target=hd2.support_weapon('APW-1 Anti-Materiel Rifle'):attack('primary'):projectile(),
                allow_shared=true,
                field=hd2.fields.projectile.velocity,
                expect=880,
                value=1100,
            }
        },
    }
})
