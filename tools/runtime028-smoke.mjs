// HD2Runtime 0.28.0 integration smoke (packaged app, bundled SDK). One project exercises a representative of each new area:
// an underbarrel (AR/GL-21 One-Two), programmable ammo and rate-of-fire slots (MG-206), a mounted projectile host (EXO-45 Patriot) and a
// projectile-builder row, an event handler from the event reference, then the same screens in 简体中文 and back. The generated Lua must
// carry every one of them, isolated per operation.
import assert from 'node:assert/strict';
import { run } from './smoke-cdp.mjs';

const out = name => 'output/v1/projectile/' + name;
const cjk = s => (s.match(/[一-鿿]/g) ?? []).length;
await run('runtime028', async (ui, report) => {
    const { q, click, fill, waitFor, count, evaluate, screenshot, sleep } = ui;
    await ui.startup();
    assert((await evaluate("document.querySelector('.sdk-mini')?.innerText ?? ''")).includes('SDK 0.28.0'), 'bundled SDK 0.28.0');
    await ui.createProject('Runtime 0.28 Smoke');

    // Underbarrel: a nested section of the One-Two, never flattened into the rifle.
    await ui.go('player-weapons'); await ui.chooseWeapon('AR/GL-21 One-Two');
    const ub = '[data-subweapon="AR/GL-21 One-Two / underbarrel"]';
    await waitFor(q(ub), 'One-Two underbarrel section');
    await ui.setField('weapon.horizontal_spread', '15', ub);
    await ui.setField('rounds.spare_rounds', '10', ub);
    await screenshot('runtime028-underbarrel');

    // Programmable ammo and rate-of-fire slots on the MG-206 (Incendiary: the live-proven R-4 Hyena).
    await ui.go('stratagems:support'); await fill('#stratagem-search', 'MG-206'); await click('[data-stratagem="MG-206 Heavy Machine Gun"]');
    const fx = '[data-weapon-functions="MG-206 Heavy Machine Gun"]';
    await waitFor(q(fx), 'MG-206 rate of fire & functions');
    await fill(`${fx} [data-rate-slot="z"] input`, '900');
    await waitFor(q(`${fx} [data-rate-slot="z"][data-rate="900"]`), 'Z rate saved');
    await fill(`${fx} [data-function-ammo-search]`, 'Hyena'); await click(`${fx} [data-function-donor="${out('r-4-hyena')}"]`);
    await waitFor(q(`${fx} [data-feed-alternate="${out('r-4-hyena')}"]`), 'Hyena as the function projectile');
    await waitFor(q(`${fx} [data-mode-labels-slot] [data-mode-presentation="${out('r-4-hyena')}"]`), 'alternate mode label editor');
    report.hmgOptIns = await count(`${fx} [data-flag]`);
    await screenshot('runtime028-programmable-ammo');

    // Mounted projectile host and a projectile-builder row (the Patriot's own bullet row).
    const patriot = 'EXO-45 Patriot Exosuit / right_gun';
    await ui.go('projectiles'); await waitFor(q('[data-projectile-builder]'), 'projectile builder');
    await fill('[data-builder-search]', 'Patriot'); await click(`[data-builder-host="${patriot}"]`);
    await waitFor(q(`[data-projectile-host="${patriot}"] [data-donor-list]`), 'Patriot donors');
    await click(`[data-projectile-host="${patriot}"] [data-donor="${out('eat-17-expendable-anti-tank')}"]`);
    await waitFor(`${q(`[data-projectile-host="${patriot}"]`)}.classList.contains('modified')`, 'Patriot swapped');
    assert.equal(await count(`[data-projectile-host="${patriot}"] [data-opt-in]`), 0, 'live-proven Patriot donor carries no opt-in');
    await screenshot('runtime028-mounted-host');

    // Event / action: insert a player_hit handler from the event reference and save.
    await ui.go('scripting'); await click('[data-custom-lua-add]'); await waitFor(q('[data-lua-editor]'), 'custom Lua editor');
    await click('[data-custom-lua-tab="reference"]'); await waitFor(q('[data-reference-event="player_hit"]'), 'player_hit in the reference');
    await click('[data-reference-insert="player_hit"]');
    await click('[data-custom-lua-save]'); await waitFor(`${q('[data-custom-lua-state]')}.innerText === 'Saved'`, 'custom Lua saved');

    // Localization: the new panels re-render in 简体中文 with the project intact, then back to English.
    await ui.setLanguage('zh-Hans');
    await ui.go('player-weapons'); await ui.chooseWeapon('AR/GL-21 One-Two'); await waitFor(q(ub), 'underbarrel (zh)');
    report.underbarrelCjk = cjk(await evaluate(`${q(ub)}.innerText`)); assert(report.underbarrelCjk > 4, 'underbarrel section in Chinese');
    await ui.go('stratagems:support'); await fill('#stratagem-search', 'MG-206'); await click('[data-stratagem="MG-206 Heavy Machine Gun"]'); await waitFor(q(fx), 'functions (zh)');
    report.functionsCjk = cjk(await evaluate(`${q(fx)}.innerText`)); assert(report.functionsCjk > 10, 'functions panel in Chinese');
    await screenshot('runtime028-functions-zh');
    const luaZh = await ui.lua();
    await ui.setLanguage('en');
    const lua = await ui.lua();
    assert.equal(luaZh, lua, 'generated Lua is independent of the UI language');

    for (const expected of ["hd2.weapon('AR/GL-21 One-Two'):underbarrel()", 'hd2.fields.function_ammo.projectile', 'hd2.fields.fire_rate.modes',
        `hd2.attack_output('${out('r-4-hyena')}')`, "hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun')", "hd2.events.on('player_hit'", 'pcall(build)'])
        assert(lua.includes(expected), 'Lua contains ' + expected);
    report.operations = (lua.match(/^add\(function\(\) return /gm) ?? []).length;
    await ui.go('export'); await click('section.panel button.primary'); await waitFor("!!document.querySelector('.export-success')", 'export');
    report.export = await evaluate("document.querySelector('.export-success code.path').innerText");
});
