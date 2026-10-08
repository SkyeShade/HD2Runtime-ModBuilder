// Desktop smoke for the Player page (hd2.helldiver(), ModBuilder 1.7.0), against a running isolated app (WebView2 CDP) on the bundled SDK.
// Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9301, then:
//   HD2GUI_CDP_PORT=9301 HD2GUI_SCREENSHOTS=artifacts/look-helldiver node tools/helldiver-smoke.mjs
// It edits a speed, a zone damage multiplier and a zone's health, checks the warnings (never a checkbox), the zone table, the Lua Preview
// (one request per target, both opt-ins, enum names) and the Changes page.
import assert from 'node:assert/strict';
import { run } from './smoke-cdp.mjs';

const field = key => `[data-entity-field="${key}"]`;
await run('helldiver', async ({ evaluate, waitFor, click, fill, count, go, lua, screenshot, startup, createProject, idle, q }, report) => {
    await startup();
    await createProject('Helldiver Smoke');
    assert.equal(await count('[data-quick-helldiver]'), 1, 'overview quick-author button');
    await go('helldiver');
    await waitFor(q('[data-helldiver-warnings]'), 'Player page');
    assert.equal(await count('[data-helldiver-warnings] input[type=checkbox]'), 0, 'opt-ins are warnings, never checkboxes');
    assert.equal(await count('[data-helldiver-warnings] [data-opt-in]'), 2, 'allow_shared and allow_unverified_effect warnings');
    assert.equal(await count('[data-helldiver-fact]'), 3, 'mission only, next spawn, speed cap');
    assert.equal(await count('[data-zone-table] tbody tr'), 6, 'six body zones');
    await screenshot('helldiver-page');

    // A speed, a zone enum (head is selected first) and a zone's health.
    await fill(`${field('helldiver:helldiver.speed.jog')} input[type=number]`, '4.5'); await idle();
    await waitFor(`${q(field('helldiver:helldiver.speed.jog'))}.classList.contains('modified')`, 'jog saved');
    await evaluate(`${q('[data-helldiver-section="movement"]')}.scrollIntoView({block:'start'})`);
    await screenshot('helldiver-movement');
    await fill(`${field('helldiver:zone.damage_multiplier:head')} select`, 'reduced'); await idle();
    await waitFor(`${q(field('helldiver:zone.damage_multiplier:head'))}.classList.contains('modified')`, 'head multiplier saved');
    await click('[data-zone="leg_left"]');
    await waitFor(q(field('helldiver:zone.health:leg_left')), 'left leg selected');
    await fill(`${field('helldiver:zone.health:leg_left')} input[type=number]`, '90'); await idle();
    await waitFor(`${q(field('helldiver:zone.health:leg_left'))}.classList.contains('modified')`, 'leg health saved');
    assert.equal(await evaluate(`${q('[data-zone="leg_left"] [data-zone-value="zone.health"]')}.innerText.trim()`), '90', 'the table shows the edited value');
    assert.equal(await evaluate(`${q('[data-zone="head"] [data-zone-value="zone.damage_multiplier"]')}.innerText.trim()`), 'reduced', 'the table shows the edited name');
    // A value past the speed cap is refused inline.
    await fill(`${field('helldiver:helldiver.speed.sprint')} input[type=number]`, '12'); await idle();
    await waitFor(`${q(field('helldiver:helldiver.speed.sprint') + ' [role=alert]')}`, 'out-of-range refusal');
    await evaluate(`${q('[data-helldiver-section="zones"]')}.scrollIntoView({block:'start'})`);
    await screenshot('helldiver-zones');

    const code = await lua();
    for (const s of ["target=hd2.helldiver(),", "field=hd2.fields.helldiver.speed_jog,", "target=hd2.helldiver():zone('head'),", "field=hd2.fields.zone.damage_multiplier,",
        "expect='normal',", "value='reduced',", "target=hd2.helldiver():zone('leg_left'),", "field=hd2.fields.zone.health,", "expect=45,", "value=90,", "allow_shared=true,", "allow_unverified_effect=true,"])
        assert.ok(code.includes(s), 'Lua Preview has ' + s);
    report.lua = code.split('\n').filter(l => l.includes('helldiver') || l.includes('allow_')).length;
    await screenshot('helldiver-lua');

    await go('changes');
    await click('[data-entity-change-group="helldiver:Helldiver"] summary'); await idle();
    await waitFor(q('[data-entity-change-group="helldiver:Helldiver"] [data-helldiver-section="zones"]'), 'Changes lists the Helldiver edits');
    assert.equal(await count('[data-entity-change-group="helldiver:Helldiver"] [data-entity-field]'), 3, 'three edited fields');
    await screenshot('helldiver-changes');
});
