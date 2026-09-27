local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    transaction={
        id='gui-3be120931696095c6fe0ac89',
        target=hd2.weapon('P-113 Verdict'),
        changes={
            {field=hd2.fields.projectile.drag,expect=1.2,value=0.1},
            {field=hd2.fields.projectile.gravity,expect=1,value=0.2},
        },
    }
})
