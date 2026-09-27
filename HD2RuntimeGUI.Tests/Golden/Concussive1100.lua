local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    patch={
        id='gui-06a2a76bb7e2baeed7aad5cb',
        target=hd2.weapon('AR-23C Liberator Concussive'),
        field=hd2.fields.weapon.fire_rate,
        expect=400,
        value=1100,
    }
})
