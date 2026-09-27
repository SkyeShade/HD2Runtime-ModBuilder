local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    transaction={
        id='gui-9466194b13e9678c6931e9c0',
        target=hd2.weapon('P-113 Verdict'),
        changes={
            {field=hd2.fields.magazine.capacity,expect=10,value=15},
            {field=hd2.fields.magazine.magazines_from_supply,expect=10,value=8},
            {field=hd2.fields.magazine.spare_magazines,expect=10,value=8},
            {field=hd2.fields.magazine.starting_magazines,expect=6,value=8},
        },
    }
})
