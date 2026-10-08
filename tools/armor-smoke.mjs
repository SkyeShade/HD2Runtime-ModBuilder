// Desktop smoke for the Armor page (HD2Runtime 0.30.0 armor stats and passives), against a running isolated app (WebView2 CDP) on the bundled SDK.
// Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9303, then:
//   HD2GUI_CDP_PORT=9303 HD2GUI_SCREENSHOTS=<folder> node tools/armor-smoke.mjs
// It edits a kit's piece weights (one piece, then every piece), a class value, a damage curve point, the player's armor bonus and stamina factor
// and the passive override, and checks the live preview, the warnings (never a checkbox), the Lua preview and the Changes page.
import assert from 'node:assert/strict';
import { connect } from './smoke-cdp.mjs';

const s = await connect(process.env.HD2GUI_CDP_PORT ?? 9303);
const { evaluate, sleep, q, waitFor, idle, click, fill, count, go, lua, screenshot, startup, createProject } = s;
const stat = key => evaluate(`${q(`[data-armor-preview] [data-armor-stat="${key}"]`)}?.dataset.value`);
const field = key => `[data-entity-field="${key}"]`;
try {
    await startup();
    await createProject('Armor Smoke');
    assert.equal(await count('[data-quick-armor]'), 1, 'Overview quick author button');
    await go('armor');
    await waitFor(q('[data-armor-browser]'), 'armor kit list');
    assert.equal(await count('[data-armor-kit]'), 135, 'every armor kit is listed');
    await screenshot('armor-01-kits');

    // RS-100 Sanctioner: torso heavy -> rating 70 (docs/armor-stats.md).
    await fill('#armor-search', 'Sanctioner');
    await click('[data-armor-kit="4DD749C6"]');
    await waitFor(q('[data-armor-kit-editor="4DD749C6"]'), 'kit editor');
    assert.equal(await stat('rating'), '50', 'vanilla rating');
    assert.equal(await count('[data-armor-pieces] select[data-enum-select]'), await count('[data-armor-pieces] [data-entity-field]'), 'one weight dropdown per piece');
    await fill(`${field('armor_kit:4DD749C6:torso')} select`, 'heavy'); await idle();
    await waitFor(`${q('[data-armor-preview] [data-armor-stat="rating"]')}?.dataset.value === '70'`, 'preview follows the torso edit');
    assert.equal(await count('[data-ack-panel="shared"]'), 1, 'kit warning: every player wearing the kit');
    assert.equal(await count('input[type=checkbox][data-ack]'), 0, 'no acknowledgement checkbox');
    await screenshot('armor-02-kit-torso-heavy');

    // FS-37 Ravager: every piece heavy at once -> rating 150.
    await fill('#armor-search', 'Ravager');
    await click('[data-armor-kit="1F9BFA78"]');
    await click('[data-armor-all="heavy"]'); await idle();
    await waitFor(`${q('[data-armor-preview] [data-armor-stat="rating"]')}?.dataset.value === '150'`, 'all heavy');
    assert.equal(await stat('damage'), '0.75');
    await screenshot('armor-03-kit-all-heavy');
    await evaluate(`${q('[data-armor-pieces]')}.scrollIntoView({block:'start'})`); await sleep(300);
    await screenshot('armor-03b-kit-pieces');

    // Classes and the damage curve.
    await click('[data-armor-tab="classes"]');
    await waitFor(q('[data-armor-class-table]'), 'class table');
    await fill(`${field('armor_class:heavy:rating')} input[type=number]`, '3'); await idle();
    await waitFor(`${q(field('armor_class:heavy:rating'))}.classList.contains('modified')`, 'class value saved');
    await fill(`${field('armor_damage_curve:at_2')} input[type=number]`, '0.6'); await idle();
    await waitFor(`${q(field('armor_damage_curve:at_2'))}.classList.contains('modified')`, 'curve point saved');
    assert.match(await evaluate(`${q('[data-armor-class-row="heavy"]')}.innerText`), /200/, 'heavy rating 200 in the class table');
    await screenshot('armor-04-classes-curve');

    // Player (solo): armor bonus, stamina factor, passive override.
    await click('[data-armor-tab="player"]');
    await waitFor(q('[data-armor-player]'), 'player values');
    await fill('[data-armor-player-field="armor_bonus"] input', '1'); await idle();
    await fill('[data-armor-player-field="stamina_factor"] input', '0.5'); await idle();
    await waitFor(`${q('[data-armor-player-field="stamina_factor"]')}.classList.contains('modified')`, 'stamina factor saved');
    await fill('[data-armor-player-field="armor_bonus"] input', '5'); await idle();
    await waitFor(q('[data-armor-player-error]'), 'out-of-range refused inline');
    await waitFor(`${q('[data-armor-player-field="armor_bonus"] input')}.value === '1'`, 'the refused value is not kept in the box');
    await fill('[data-armor-passive="armor"]', '8'); await idle();
    await waitFor(`${q('[data-armor-solo="passives"]')}?.dataset.armorProven === 'true'`, 'live-proven armor swap');
    assert.equal(await evaluate(`${q('[data-armor-passive-lua]')}.innerText`), "hd2.player_passives.set({armor='SERVO-ASSISTED'})");
    await screenshot('armor-05-player-proven');
    await fill('[data-armor-passive="second"]', '2'); await idle();
    await waitFor(`${q('[data-armor-solo="passives"]')}?.dataset.armorProven === 'false'`, 'a second passive is unverified');
    await fill('[data-armor-passive="armor"]', '17'); await idle();
    await waitFor(q('[data-armor-package="17"]'), 'effect package warning');
    assert.equal(await evaluate(`${q('[data-armor-passive-lua]')}.innerText`), "hd2.player_passives.set({armor='INTEGRATED EXPLOSIVES',second='SCOUT',allow_unverified_effect=true})");
    await screenshot('armor-06-player-package');
    await evaluate(`${q('[data-armor-passive-cards]')}?.scrollIntoView({block:'start'}) ?? ${q('[data-armor-passive-card]')}.scrollIntoView({block:'start'})`); await sleep(300);
    await screenshot('armor-06b-passive-cards');

    const code = await lua();
    for (const part of ["target=hd2.armor_stats.kit('4DD749C6')", "target=hd2.armor_stats.kit('1F9BFA78')", "target=hd2.armor_stats.class('heavy')",
        'target=hd2.armor_stats.damage_curve()', 'player:set({armor_bonus=1,stamina_factor=0.5,allow_unverified_effect=true})',
        "hd2.events.on('player_spawned'", "hd2.player_passives.set({armor='INTEGRATED EXPLOSIVES',second='SCOUT',allow_unverified_effect=true})"])
        assert.ok(code.includes(part), 'Lua preview has ' + part);
    await screenshot('armor-07-lua');
    await go('changes');
    await waitFor(q('[data-armor-player-change]'), 'player armor on the Changes page');
    assert.ok(await count('[data-entity-change-group^="armor_"]') >= 4, 'kit, class and curve groups');
    await screenshot('armor-08-changes');
    console.log(JSON.stringify({ smoke: 'armor', status: 'PASS' }));
} catch (e) {
    await screenshot('armor-failure').catch(() => {});
    console.log(JSON.stringify({ smoke: 'armor', status: 'FAIL', error: String(e?.stack ?? e) }));
    process.exitCode = 1;
} finally { s.close(); }
