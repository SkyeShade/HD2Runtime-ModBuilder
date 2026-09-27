local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    plan={
        id='support-plan-1098704da6c88c281866c622',
        operations={
            {
                id='support-c131aab9c9157102837c0047',
                target=hd2.support_weapon('B/MD C4 Pack'):attack('detonation'):explosion(),
                allow_shared=true,
                field=hd2.fields.explosion.damage_standard_damage,
                expect=2000,
                value=1500,
            },
            {
                id='support-96c1311f8ebaa468f736d34a',
                target=hd2.support_weapon('B/MD C4 Pack'):attack('detonation'):explosion(),
                allow_shared=true,
                field=hd2.fields.explosion.outer_radius,
                expect=7,
                value=15,
            }
        },
    }
})
