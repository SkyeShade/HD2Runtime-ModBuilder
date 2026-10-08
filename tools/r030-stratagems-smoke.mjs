// Desktop smoke for ModBuilder 1.7.0's HD2Runtime 0.30 stratagem authoring (docs/runtime030-integration-plan.md, Phase 4), against a running
// isolated app (WebView2 CDP) on the bundled SDK. Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9347, then:
//   HD2GUI_CDP_PORT=9347 node tools/r030-stratagems-smoke.mjs
// It covers the call-in code editor (draft, apply, the equal-code warning, the clash asked where it is applied), presentation pickers and
// "look like", the barrage pattern summary, the Eagle landing-pattern dropdown, the sentry turret / targeting components with the
// "same as range" toggle, the timing badges, the generated Lua and reopening.
import assert from 'node:assert/strict';
import { connect } from './smoke-cdp.mjs';

const s = await connect(process.env.HD2GUI_CDP_PORT ?? 9347);
const { evaluate, sleep, q, waitFor, idle, click, fill, count, go, lua, screenshot, startup, createProject, openProject } = s;
const stratagem = async (category, name) => {
    await go('stratagems:' + category); await click(`[data-stratagem="${name}"]`);
    await waitFor(`document.querySelector('[data-stratagem-header="${name}"]')`, 'stratagem ' + name); await idle();
};
const code = (stratagemName) => `[data-stratagem-code="${stratagemName}"]`;
const build = async (scope, arrows) => {
    while (await count(`${scope} [data-code-remove]`) > 0) await click(`${scope} [data-code-remove="0"]`);
    for (const a of arrows) await click(`${scope} [data-code-add="${a}"]`);
};
const textOf = selector => evaluate(`${q(selector)}?.innerText ?? ''`);
try {
    await startup();
    await createProject('R030 Stratagem Smoke');

    // Call-in code: a draft, applied in one save; a code that only starts a mission stratagem's code needs no opt-in.
    const barrage = 'Orbital 120mm HE Barrage', precision = 'Orbital Precision Strike';
    await stratagem('offensive', barrage);
    await waitFor(q(code(barrage)), 'call-in code editor');
    assert.ok(await count('[data-stratagem-group="presentation"] [data-presentation-picker]') === 4, 'four presentation pickers');
    assert.match(await textOf('[data-stratagem-group="orbital"] [data-orbital-vanilla]'), /5.*3.*15/, 'vanilla barrage pattern in words');
    assert.ok(await count('[data-read-timing="beacon_creation"]') >= 1, 'call-in time has its timing badge');
    await build(code(barrage), ['up', 'up', 'down', 'down']);
    await click(`${code(barrage)} [data-code-apply]`); await idle();
    assert.equal(await count(`${code(barrage)} [data-code-equal]`), 0, 'no equal-code warning for Up Up Down Down');
    assert.equal(await count(`${code(barrage)} [data-code-overlap]`), 1, 'the overlap with a mission stratagem is noted');
    await screenshot('r030-call-in-code');

    // The Precision Strike's own code on the barrage: warned (no checkbox), written with the opt-in.
    await build(code(barrage), ['right', 'right', 'up']);
    await waitFor(q(`${code(barrage)} [data-code-equal]`), 'equal-code warning before applying');
    assert.equal(await count(`${code(barrage)} input[type=checkbox]`), 0, 'never a checkbox');
    await click(`${code(barrage)} [data-code-apply]`); await idle();
    assert.match(await lua(), /allow_unverified_effect=true/, 'opt-in for an equal native code');

    // Two edits giving two stratagems one code: asked where the second is applied.
    await stratagem('offensive', barrage);
    await build(code(barrage), ['up', 'up', 'down', 'down']); await click(`${code(barrage)} [data-code-apply]`); await idle();
    await stratagem('offensive', precision);
    await build(code(precision), ['up', 'up', 'down', 'down']); await click(`${code(precision)} [data-code-apply]`);
    await waitFor(q(`${code(precision)} [data-code-clash-prompt]`), 'clash asked where the code is applied');
    await screenshot('r030-code-clash');
    await click(`${code(precision)} [data-clash-restore-other]`); await idle();
    await waitFor(q(`${code(precision)} [data-code-report]`), 'restored report');

    // Presentation: look like another stratagem.
    await stratagem('offensive', barrage);
    await fill('[data-look-like]', 'Orbital Gas Strike'); await idle();
    const looks = await evaluate(`[...document.querySelectorAll('[data-stratagem-group="presentation"] [data-presentation-source]')].map(s => s.value)`);
    assert.deepEqual(looks, ['Orbital Gas Strike', 'Orbital Gas Strike', 'Orbital Gas Strike', 'Orbital Gas Strike'], 'all four presentation fields take the source');
    await screenshot('r030-presentation');

    // Barrage pattern: an edited pattern is described beside the vanilla one.
    await fill('[data-stratagem-field$=":orbital.salvos"] input[type=number]', '8'); await idle();
    assert.match(await textOf('[data-orbital-edited]'), /8.*3.*24/, 'edited pattern in words');
    await screenshot('r030-barrage-pattern');

    // Eagle: the landing pattern is a dropdown of Runtime's patterns.
    await stratagem('offensive', 'Eagle Gas Airstrike');
    assert.equal(await count('[data-stratagem-group="eagle"] select[data-allowed-values] option'), 8, 'eight landing patterns');
    await fill('[data-stratagem-group="eagle"] select[data-allowed-values]', '3'); await idle();
    assert.match(await textOf('[data-eagle-bombs]'), /8/, 'bombs per strike follow the pattern');
    await screenshot('r030-eagle');

    // Sentries: turret and targeting components, "same as range".
    await stratagem('defensive', 'A/G-16 Gatling Sentry');
    await waitFor(q('[data-entity-component="turret"]'), 'turret component');
    await waitFor(q('[data-entity-component="targeting"] [data-sentinel-toggle]'), 'same-as-range toggle');
    const side = '[data-stratagem-field$=":targeting.side_range"]';
    assert.equal(await evaluate(`${q(side + ' [data-sentinel-toggle]')}.checked`), true, 'side range uses targeting.range by default');
    await click(side + ' [data-sentinel-toggle]'); await idle();
    assert.equal(await evaluate(`${q(side + ' input[type=number]')}.value`), '75', 'unticking starts from the targeting range');
    await screenshot('r030-sentry');
    const generated = await lua();
    for (const field of ['calldown_code', 'presentation_name', 'orbital.salvos', 'eagle.airstrike_pattern', 'targeting.side_range'])
        assert.ok(generated.includes(field), 'Lua has ' + field);

    // Reopened exactly.
    await openProject('R030 Stratagem Smoke');
    assert.equal(await lua(), generated, 'reopened project generates the same Lua');
    console.log('r030 stratagems smoke: OK');
} finally { s.close(); }
