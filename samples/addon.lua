local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    patch={
        id='gui-011cce6c15684bcf88afc0235318d988',
        target=hd2.weapon('JAR-5 Dominator'):projectile():damage(),
        field=hd2.fields.damage.armor_penetration,
        expect=3,
        value=4,
    }
})
