local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    transaction={
        id='gui-da38f420400172752525addd',
        target=hd2.weapon('SG-8 Punisher'),
        changes={
            {field=hd2.fields.rounds.feed_capacity_1,expect=8,value=10},
            {field=hd2.fields.rounds.feed_capacity_2,expect=8,value=10},
            {field=hd2.fields.rounds.rounds_from_supply,expect=60,value=80},
            {field=hd2.fields.rounds.spare_rounds,expect=60,value=80},
            {field=hd2.fields.rounds.starting_rounds,expect=32,value=40},
        },
    }
})
