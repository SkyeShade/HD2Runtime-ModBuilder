local hd2=require('mods/skyeshade/hd2runtime')

return {
    hd2.ensure({
        patch={
            id='gui-object-4a42c68fd00a9aa283b991d5',
            target=hd2.weapon('SG-20 Halt'):attack('feed_alternate'):projectile(),
            allow_shared=true,
            field=hd2.fields.projectile.velocity,
            expect=800,
            value=300,
        }
    }),
    hd2.ensure({
        patch={
            id='gui-object-42939c3689ac68ff7c6b3e31',
            target=hd2.weapon('SG-20 Halt'):attack('feed_primary'):projectile(),
            allow_shared=true,
            field=hd2.fields.damage.player_standard_damage,
            expect=35,
            value=40,
        }
    }),
    hd2.ensure({
        patch={
            id='gui-object-37bf1d122efbfe78784c88b9',
            target=hd2.weapon('SMG-32 Reprimand'),
            field=hd2.fields.weapon.fire_rate,
            expect=490,
            value=900,
        }
    }),
    hd2.ensure({
        patch={
            id='gui-object-b033ed8591bfd7a731413f1e',
            target=hd2.weapon('SG-20 Halt'),
            field=hd2.fields.weapon.sway,
            expect=1,
            value=0.7,
        }
    }),
    hd2.ensure({
        patch={
            id='gui-object-c25b47bd89e96dc10552892e',
            target=hd2.weapon('AR-23 Liberator'),
            field=hd2.fields.weapon.sway,
            expect=1,
            value=0.8,
        }
    })
}
