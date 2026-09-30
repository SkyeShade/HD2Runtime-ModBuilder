local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    patch={
        id='gui-object-40d9c58193dc0b03f10c8b11',
        target=hd2.weapon('AR-11 Arbitrator'):attack('primary'),
        field=hd2.fields.attack.projectile,
        expect=hd2.weapon('AR-11 Arbitrator'):attack('primary'):projectile(),
        value=hd2.weapon('AR-23 Liberator'):attack('primary'):projectile(),
    }
})
