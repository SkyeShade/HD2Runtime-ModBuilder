local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    patch={
        id='gui-ref-9d867ae13a50d965a28fe5a7',
        target=hd2.weapon('JAR-5 Dominator'):attack('primary'),
        field=hd2.fields.attack.projectile,
        expect=hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile(),
        value=hd2.weapon('P-113 Verdict'):attack('primary'):projectile(),
    }
})
