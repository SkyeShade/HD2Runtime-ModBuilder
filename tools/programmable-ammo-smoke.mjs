// Programmable ammunition as base and alternate mode cards (packaged app, bundled SDK 0.28.1): a player weapon with no alternate, adding
// one, choosing a donor and its label, reopening the project, removing it (and the label edit it leaves on its row), a support weapon with
// a live-proven and an unverified donor, a native programmable weapon (replace and restore, never remove), the cards stacked at a narrow
// width, and the editor in 简体中文 and back. The generated Lua follows every step and is the same in both languages.
import assert from 'node:assert/strict';
import { run } from './smoke-cdp.mjs';

const out = name => 'output/v1/projectile/' + name;
const cjk = s => (s.match(/[一-鿿]/g) ?? []).length;
await run('programmable-ammo', async (ui, report) => {
    const { q, click, fill, waitFor, count, evaluate, screenshot, cdp } = ui;
    await ui.startup();
    await ui.createProject('Programmable Ammo Smoke');
    const liberator = '[data-weapon-functions="AR-23 Liberator"]', alt = `${liberator} [data-ammo-mode="alternate"]`, base = `${liberator} [data-ammo-mode="base"]`;
    const openLiberator = async () => { await ui.go('player-weapons'); await ui.chooseWeapon('AR-23 Liberator'); await waitFor(q(`${liberator} [data-programmable-ammo]`), 'Liberator programmable ammunition'); };
    const select = async (selector, value) => { await fill(selector, value); await ui.idle(); };
    // Screenshots frame the editor (below the sticky weapon header), not the top of the page.
    const shot = async (selector, name) => { await evaluate(`(() => { const e = ${q(selector)}; e.style.scrollMarginTop = '150px'; e.scrollIntoView({block: 'start'}); })()`); await ui.sleep(300); await screenshot(name); };

    // 1. A player weapon with only its base mode: vanilla base card, empty alternate card with one primary action.
    await openLiberator();
    assert.equal(await evaluate(`${q(base)}.dataset.baseVanilla`), 'true', 'base mode is the vanilla projectile');
    assert(await evaluate(`${q(base)}.innerText.includes('AR-23 Liberator')`), 'base card names the weapon projectile');
    await waitFor(q(`${alt} [data-alternate-empty]`), 'no alternate mode');
    assert.equal(await count(`${liberator} [data-ammo-remove]`), 0, 'nothing to remove in a base-only weapon');
    assert.equal(await count(`${base} [data-ammo-remove], ${base} [data-function-donor]`), 0, 'the base card has no remove action or donors');
    assert.equal(await count(`${alt} [data-ammo-input="pending"][data-input="left"]`), 1, 'adding binds the left input');
    await shot(`${liberator} [data-programmable-ammo]`, 'programmable-ammo-1-base-only');

    // 2. Add alternate mode: the card opens with its donor picker; nothing is saved until a donor is chosen.
    await click(`${alt} [data-ammo-add]`);
    await waitFor(q(`${liberator} [data-alternate-mode="adding"] ${'[data-ammo-donor-picker]'}`), 'donor picker inside the alternate card');
    assert.equal(await evaluate(`${q(alt)}.contains(${q(`${liberator} [data-function-ammo-donors]`)})`), true, 'donor list belongs to the alternate card');
    assert(!(await ui.lua()).includes('function_ammo'), 'adding alone saves nothing');
    await openLiberator(); await click(`${alt} [data-ammo-add]`); await waitFor(q(`${alt} [data-ammo-donor-picker]`), 'picker again');
    await shot(`${liberator} [data-programmable-ammo]`, 'programmable-ammo-2-adding');

    // 3. Choose a donor (a shared row: its label warning sits on its own editor), then give each mode its own label and icon.
    const concussive = out('ar-23c-liberator-concussive');
    await fill(`${alt} [data-function-ammo-search]`, 'Concussive'); await click(`${alt} [data-function-donor="${concussive}"]`);
    await waitFor(q(`${liberator} [data-alternate-mode="donor"] [data-feed-alternate="${concussive}"]`), 'Concussive as the alternate mode');
    assert(await evaluate(`${q(`${alt} [data-mode-summary]`)}.innerText.includes('AR-23C Liberator Concussive')`), 'alternate summary names the donor');
    await waitFor(q(`${alt} [data-ammo-input="bound"][data-input="left"]`), 'left input bound by this mod');
    await waitFor(q(`${alt} [data-alternate-unverified]`), 'unverified donor badge on the alternate card');
    assert.equal(await count(`${alt} [data-opt-in="allow_unverified_effect"]`), 1, 'effect warning in the alternate card');
    assert.equal(await count(`${alt} [data-opt-in="allow_unverified_reference"]`), 1, 'reference warning in the alternate card');
    assert.equal(await count(`${liberator} > [data-opt-in]`), 0, 'no group warning far from the mode');
    assert.equal(await count(`${alt} [data-mode-presentation="${concussive}"] [data-mode-shared]`), 1, 'shared-row note on the donor label editor');
    await select(`${alt} [data-mode-presentation="${concussive}"] [data-mode-label]`, 'flak');
    await waitFor(`${q(`${alt} [data-mode-presentation="${concussive}"]`)}.classList.contains('modified')`, 'alternate label saved');
    await select(`${base} [data-mode-icon]`, 'auto');
    await waitFor(`${q(`${base} [data-mode-presentation]`)}.classList.contains('modified')`, 'base icon saved');
    let lua = await ui.lua();
    for (const expected of ['hd2.fields.function_ammo.projectile', `hd2.attack_output('${concussive}')`, "value='programmable_ammo'", 'hd2.fields.presentation.mode_label', 'hd2.fields.presentation.mode_icon',
        'allow_unverified_effect=true', 'allow_unverified_reference=true', 'allow_shared=true'])
        assert(lua.includes(expected), 'Lua contains ' + expected);
    await openLiberator(); await shot(`${liberator} [data-programmable-ammo]`, 'programmable-ammo-3-donor');

    // Save / reopen: the alternate mode, its binding and both labels come back.
    await ui.openProject('Programmable Ammo Smoke'); await openLiberator();
    await waitFor(q(`${liberator} [data-alternate-mode="donor"] [data-feed-alternate="${concussive}"]`), 'alternate after reopening');
    assert.equal(await evaluate(`${q(`${alt} [data-mode-presentation="${concussive}"] [data-mode-label]`)}.value`), 'flak', 'alternate label after reopening');
    assert.equal(await ui.lua(), lua, 'reopening generates the same Lua');

    // 4. Remove alternate mode: its row's label edit is resolved inline, the weapon returns to its base mode only.
    await openLiberator(); await click(`${alt} [data-ammo-remove]`);
    await waitFor(q(`${alt} [data-selector-conflict]`), 'label edits on the donor row asked about in the card');
    await click(`${alt} [data-selector-resolve]`);
    await waitFor(q(`${alt} [data-alternate-empty]`), 'back to base only');
    lua = await ui.lua();
    for (const gone of ['function_ammo', 'weapon_function', concussive]) assert(!lua.includes(gone), 'Lua no longer contains ' + gone);
    assert(lua.includes('hd2.fields.presentation.mode_icon'), 'the base icon edit stays');
    await openLiberator(); await select(`${base} [data-mode-icon]`, await evaluate(`${q(`${base} [data-mode-icon] option`)}.value`));
    assert(!(await ui.lua()).includes('presentation.mode_icon'), 'vanilla icon removes the base edit');
    await openLiberator(); await shot(`${liberator} [data-programmable-ammo]`, 'programmable-ammo-4-removed');

    // 5. Support weapon: MG-206 with its live-proven Hyena (no opt-in), then an unverified donor (warnings in its card).
    const hmg = '[data-weapon-functions="MG-206 Heavy Machine Gun"]', hmgAlt = `${hmg} [data-ammo-mode="alternate"]`;
    const openHmg = async () => { await ui.go('stratagems:support'); await fill('#stratagem-search', 'MG-206'); await click('[data-stratagem="MG-206 Heavy Machine Gun"]'); await waitFor(q(`${hmg} [data-programmable-ammo]`), 'MG-206 programmable ammunition'); };
    await openHmg();
    assert.equal(await count(`${hmg} [data-ammo-mode="base"] [data-mode-shared]`), 1, 'the MG-206 row is shared: noted on the base label editor');
    await click(`${hmgAlt} [data-ammo-add]`); await fill(`${hmgAlt} [data-function-ammo-search]`, 'Hyena');
    assert.equal(await count(`${hmgAlt} [data-function-donor="${out('r-4-hyena')}"] [data-donor-proven]`), 1, 'live-proven donor badge');
    await click(`${hmgAlt} [data-function-donor="${out('r-4-hyena')}"]`);
    await waitFor(q(`${hmgAlt} [data-live-proven]`), 'live-proven alternate');
    assert.equal(await count(`${hmgAlt} [data-opt-in]`), 0, 'a live-proven pair carries no opt-in');
    await shot(`${hmg} [data-programmable-ammo]`, 'programmable-ammo-5-support-proven');
    await click(`${hmgAlt} [data-ammo-change]`); await fill(`${hmgAlt} [data-function-ammo-search]`, 'De-Escalator');
    assert.equal(await count(`${hmgAlt} [data-function-donor="${out('gl-52-de-escalator')}"] [data-flags="allow_unverified_effect allow_unverified_reference"]`), 1, 'compact unverified badge in the list');
    await click(`${hmgAlt} [data-function-donor="${out('gl-52-de-escalator')}"]`);
    await waitFor(q(`${hmgAlt} [data-opt-in="allow_unverified_reference"]`), 'unverified donor warnings in the alternate card');
    report.hmgCardOptIns = await count(`${hmgAlt} [data-opt-in]`);
    await shot(hmgAlt, 'programmable-ammo-5-support-unverified');

    // Responsive: at a narrow window the two cards stack; wide, they sit side by side.
    const side = () => evaluate(`(() => { const [b, a] = [...document.querySelectorAll(${JSON.stringify(`${hmg} [data-ammo-mode]`)})].map(e => e.getBoundingClientRect()); return Math.abs(b.top - a.top) < 2 && a.left > b.right - 1; })()`);
    await cdp('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false }); await ui.sleep(500);
    report.wideSideBySide = await side();
    assert(report.wideSideBySide, 'cards sit side by side on a wide window');
    await shot(`${hmg} [data-programmable-ammo]`, 'programmable-ammo-5-wide');
    await cdp('Emulation.setDeviceMetricsOverride', { width: 900, height: 900, deviceScaleFactor: 1, mobile: false }); await ui.sleep(500);
    report.narrowStacked = !(await side());
    assert(report.narrowStacked, 'cards stack at a narrow width');
    await shot(`${hmg} [data-programmable-ammo]`, 'programmable-ammo-5-narrow');
    await cdp('Emulation.clearDeviceMetricsOverride'); await ui.sleep(500);

    // 6. Native programmable weapon: the AC-8's FLAK mode is replaced and restored, never removed.
    const ac = '[data-weapon-functions="AC-8 Autocannon"]', acAlt = `${ac} [data-ammo-mode="alternate"]`;
    const openAc = async () => { await ui.go('stratagems:support'); await fill('#stratagem-search', 'AC-8'); await click('[data-stratagem="AC-8 Autocannon"]'); await waitFor(q(`${ac} [data-programmable-ammo]`), 'AC-8 programmable ammunition'); };
    await openAc();
    await waitFor(q(`${ac} [data-alternate-mode="native"] [data-alternate-native]`), 'native alternate mode');
    assert(await evaluate(`${q(`${acAlt} [data-mode-summary]`)}.innerText.includes('FLAK')`), 'native summary from the SDK presentation');
    assert.equal(await count(`${acAlt} [data-ammo-input="native"][data-input="left"]`), 1, 'native binding on the left input');
    assert.equal(await count(`${ac} [data-ammo-remove], ${ac} [data-ammo-add]`), 0, 'a native alternate is never removed');
    await shot(`${ac} [data-programmable-ammo]`, 'programmable-ammo-6-native');
    await click(`${acAlt} [data-ammo-change]`); await fill(`${acAlt} [data-function-ammo-search]`, 'Scorcher'); await click(`${acAlt} [data-function-donor="${out('plas-1-scorcher')}"]`);
    await waitFor(q(`${acAlt} [data-function-ammo-native]`), 'restore native');
    assert((await ui.lua()).includes("hd2.support_weapon('AC-8 Autocannon'):feed('programmable'):projectile()"), 'native projectile is the expected value');
    await openAc(); await click(`${acAlt} [data-function-ammo-native]`);
    await waitFor(q(`${ac} [data-alternate-mode="native"]`), 'native restored');

    // 7. The editor in 简体中文 (switched from Settings, then reopened: the app has no in-page switch) and back; the Lua is unchanged.
    const luaEn = await ui.lua();
    await ui.setLanguage('zh-Hans'); await openHmg();
    report.cardsCjk = cjk(await evaluate(`${q(`${hmg} [data-programmable-ammo]`)}.innerText`)); assert(report.cardsCjk > 30, 'mode cards in Chinese');
    await click(`${hmgAlt} [data-ammo-change]`); await waitFor(q(`${hmgAlt} [data-ammo-donor-picker]`), 'picker (zh)');
    await shot(`${hmg} [data-programmable-ammo]`, 'programmable-ammo-7-zh');
    assert.equal(await ui.lua(), luaEn, 'generated Lua is independent of the UI language');
    await ui.setLanguage('en'); await openHmg();
    assert(await evaluate(`${q(hmgAlt)}.innerText.toLowerCase().includes('alternate mode')`), 'back in English');
    report.operations = (luaEn.match(/^add\(function\(\) return /gm) ?? []).length;
    await ui.go('export'); await click('section.panel button.primary'); await waitFor("!!document.querySelector('.export-success')", 'export');
    report.export = await evaluate("document.querySelector('.export-success code.path').innerText");
});
