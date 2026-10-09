// Desktop smoke for the HD2Runtime 0.30.2 sentry and emplacement projectile swaps, against a running isolated app (WebView2 CDP) on the
// bundled SDK. Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9321,
// then:
//   HD2GUI_CDP_PORT=9321 HD2GUI_SCREENSHOTS=artifacts/look-sentry node tools/sentry-swaps-smoke.mjs
// It swaps the A/AC-8 Autocannon Sentry's round and the E/AT-12 emplacement's, checks the type-level and aiming notes, the own round's
// facts, the read-only A/MG-43, the Lua Preview and the Changes page, and reopens the project.
import assert from 'node:assert/strict';
import { connect } from './smoke-cdp.mjs';

const s = await connect(process.env.HD2GUI_CDP_PORT ?? 9321);
const { evaluate, q, waitFor, idle, click, fill, count, go, lua, screenshot, startup, createProject, openProject } = s;
const stratagem = async (category, name) => {
    await go('stratagems:' + category); await click(`[data-stratagem="${name}"]`);
    await waitFor(`document.querySelector('[data-stratagem-header="${name}"]')`, 'stratagem ' + name); await idle();
};
const section = key => `[data-sentry-projectile="${key}"]`;
const textOf = selector => evaluate(`${q(selector)}?.innerText ?? ''`);
const scrollTo = selector => evaluate(`${q(selector)}?.scrollIntoView({block:'start'})`);
const choose = async (key, search, donor) => {
    await fill(`${section(key)} [data-donor-search]`, search);
    await click(`${section(key)} [data-donor="${donor}"]`); await idle();
    await waitFor(q(`${section(key)} [data-host-reset]`), 'swap saved on ' + key);
};
const Ac8 = 'A/AC-8 Autocannon Sentry / weapon', At12 = 'E/AT-12 Anti-Tank Emplacement / weapon', Mg43 = 'A/MG-43 Machine Gun Sentry / weapon';
const Scorcher = 'output/v1/projectile/plas-1-scorcher', Recoilless = 'output/v1/projectile/gr-8-recoilless-rifle';
try {
    await startup();
    await createProject('Sentry Swap Look');

    // The Autocannon Sentry: type-level note, its own round, the aiming note, then a same-class donor.
    await stratagem('defensive', 'A/AC-8 Autocannon Sentry');
    await waitFor(q(section(Ac8)), 'sentry projectile section');
    assert.match(await textOf(`${section(Ac8)} [data-sentry-type-level]`), /teammates/, 'type-level note');
    assert.match(await textOf(`${section(Ac8)} [data-own-velocity]`), /1021/, 'own round speed');
    assert.equal(await count(`${section(Ac8)} [data-sentry-aim-note]`), 1, 'aiming note before a swap');
    assert.equal(await count(`${section(Ac8)} input[type=checkbox]:not([data-donor-cross])`), 0, 'no opt-in checkbox before a swap');
    await scrollTo(section(Ac8)); await screenshot('sentry-ac8-before');
    await choose(Ac8, 'Scorcher', Scorcher);
    assert.equal(await count(`${section(Ac8)} [data-sentry-aim]`), 1, 'aiming warning with a donor');
    assert.match(await textOf(`${section(Ac8)} [data-sentry-round-donor]`), /Scorcher/, 'donor column');
    assert.equal(await count(`${section(Ac8)} [data-opt-in="allow_unverified_effect"]`), 1, 'effect opt-in panel');
    await scrollTo(section(Ac8)); await screenshot('sentry-ac8-scorcher');

    // The read-only Machine Gun Sentry keeps Runtime's reason.
    await stratagem('defensive', 'A/MG-43 Machine Gun Sentry');
    assert.match(await textOf(`${section(Mg43)} [data-host-readonly]`), /magazine names each round/, 'read-only reason');
    assert.equal(await count(`${section(Mg43)} [data-donor]`), 0, 'no donors on a read-only host');
    await scrollTo(section(Mg43)); await screenshot('sentry-mg43-read-only');

    // The Anti-Tank Emplacement: aimed by the player.
    await stratagem('defensive', 'E/AT-12 Anti-Tank Emplacement');
    assert.equal(await count(`${section(At12)} [data-emplacement-aim]`), 1, 'emplacement aiming note');
    await choose(At12, 'Recoilless', Recoilless);
    await scrollTo(section(At12)); await screenshot('sentry-at12-recoilless');

    const generated = await lua();
    assert.match(generated, /hd2\.stratagem\('A\/AC-8 Autocannon Sentry'\):attack\('primary'\):projectile_source\(\)\.target/, 'Lua: AC-8 target');
    assert.match(generated, /hd2\.stratagem\('E\/AT-12 Anti-Tank Emplacement'\):attack\('primary'\):projectile_source\(\)\.expect/, 'Lua: AT-12 expect');
    assert.match(generated, /allow_unverified_effect=true/);
    await evaluate(`window.find("hd2.stratagem('A/AC-8 Autocannon Sentry')")`);
    await screenshot('sentry-lua-preview');

    await go('changes');
    await waitFor(q(`[data-host-change="${Ac8}"]`), 'Changes lists the AC-8 swap');
    assert.equal(await count(`[data-host-change="${At12}"]`), 1, 'Changes lists the AT-12 swap');
    await evaluate(`${q('[data-projectile-builder-changes]')}?.scrollIntoView({block:'start'})`);
    await screenshot('sentry-changes');

    await openProject('Sentry Swap Look');
    assert.equal(await lua(), generated, 'reopened project generates the same Lua');
    console.log('sentry swaps smoke: OK');
} finally { s.close(); }
