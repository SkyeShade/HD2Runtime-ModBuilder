local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    patch={
        id='stratagem-558f449fba24680f9ad02aa5',
        target=hd2.stratagem('Orbital Precision Strike'),
        field=hd2.fields.stratagem.definition_cooldown,
        expect=80,
        value=40,
    }
})
