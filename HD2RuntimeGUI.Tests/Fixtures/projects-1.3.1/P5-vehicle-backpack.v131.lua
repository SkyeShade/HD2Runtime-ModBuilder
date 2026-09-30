local hd2=require('mods/skyeshade/hd2runtime')

return {
    hd2.ensure({
        patch={
            id='entity-1f3ea36626130b13b8cbdf3d',
            target=hd2.backpack('SH-20 Ballistic Shield Backpack'),
            field=hd2.fields.entity.health,
            expect=1000,
            value=1001,
        }
    }),
    hd2.ensure({
        patch={
            id='entity-58061d88c9026cbaf43c3f8a',
            target=hd2.vehicle('EXO-45 Patriot Exosuit'),
            field=hd2.fields.entity.health,
            expect=1800,
            value=1500,
        }
    })
}
