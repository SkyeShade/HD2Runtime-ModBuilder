// Desktop smoke for ModBuilder 1.7.0's experimental custom content, against a running isolated app (WebView2 CDP) on the bundled SDK.
// Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9271, then:
//   HD2GUI_CDP_PORT=9271 node tools/custom-content-smoke.mjs
// scripts/run-rc-smokes.ps1 runs it as "custom-content". It covers the custom-content styling (its tabs in the brand yellow; references elsewhere violet; ordinary editing keeps its
// own), creating a custom stratagem (name, placeholder icon, call-in arrows, cooldown, family), switching delivery family and carrier group,
// refused fields with Runtime's reasons, validation error rendering (the Runtime's own validator), the live preview and section jumps, the
// example gallery, a Pelican gunship with a Lua callback from a snippet, a weapon variant with a recoloured model checked against
// the installed game, a custom projectile, cloning any mapped vanilla projectile, the
// projectile selector's vanilla and custom groups
// (choosing one stores a typed custom reference), Create New Custom Projectile… from a weapon (draft, cancel, complete and return),
// rename, duplicate, clear, inline validation, the Projectile Builder's hosts (return to them; a support host says why not), delete
// protection, the Changes panel, the generated Lua, Simplified Chinese, reopening and the export.
import assert from 'node:assert/strict';
import { connect } from './smoke-cdp.mjs';

const s = await connect(process.env.HD2GUI_CDP_PORT ?? 9271);
const { evaluate, sleep, q, waitFor, idle, click, fill, count, go, lua, screenshot, startup, createProject, openProject, chooseWeapon, setLanguage } = s;
const colour = (selector, property) => evaluate(`getComputedStyle(${q(selector)}).${property}`);
const editor = id => `[data-custom-stratagem-editor="${id}"]`;
const select = async (selector, value) => { await fill(selector, value); await idle(); };
try {
    await startup();
    await createProject('Custom Smoke');

    // Navigation: the custom content group, apart from the workspace and research groups.
    assert.equal(await count('[data-nav-group="custom"] [data-nav="custom-stratagems"]'), 1, 'Custom Stratagems in the custom group');
    assert.equal(await count('[data-nav-group="custom"] [data-nav="custom-projectiles"]'), 1, 'Custom Projectiles in the custom group');
    assert.equal(await colour('[data-nav-group="custom"] .badge.experimental', 'color'), 'rgb(253, 208, 14)', 'the custom content tabs use the brand yellow');
    // Ordinary editing keeps the brand styling: the Build button is still brand yellow.
    assert.equal(await colour('.page-heading .primary', 'backgroundColor'), 'rgb(253, 208, 14)', 'ordinary primary action stays brand yellow');

    // A new custom stratagem: name, family, editor.
    await go('custom-stratagems');
    await waitFor(q('[data-custom-page="stratagems"] [data-custom-experimental]'), 'experimental banner');
    await waitFor(q('[data-custom-empty]'), 'empty state');
    assert.equal(await colour('[data-new-custom-stratagem]', 'backgroundColor'), 'rgb(253, 208, 14)', 'Create Custom is brand yellow, like every primary action');
    await click('[data-new-custom-stratagem]');
    await fill('[data-custom-new-name]', 'Gas Rain Barrage');
    assert.match(await evaluate("document.querySelector('[data-custom-create] .hint').innerText"), /gas_rain_barrage/, 'internal id suggested from the name');
    assert.ok(await count('[data-custom-create] [data-family-evidence]') >= 8, 'family cards say what HD2Runtime has live-tested');
    await click('[data-custom-create] [data-family="orbital"]');
    await click('[data-custom-create-submit]'); await idle();
    await waitFor(q(editor('gas_rain_barrage')), 'editor opens');
    assert.equal(await count(`${editor('gas_rain_barrage')} [data-custom-family="orbital"]`), 1, 'family badge');
    // A new custom stratagem starts with a placeholder icon, its family's default carrier group and the live preview.
    await waitFor(`${q(editor('gas_rain_barrage') + ' .custom-editor-header [data-custom-icon]')}?.dataset.customIcon === 'gas_rain_barrage'`, 'placeholder icon');
    assert.equal(await count('[data-section="icon"].needs-attention'), 0, 'the icon is never missing');
    assert.equal(await count('[data-carrier-group="any_red"].active'), 1, "the family's default carrier group");
    await waitFor(q('[data-loadout-preview] [data-preview-name]'), 'live preview');

    // Icon (placeholder again), call-in arrows and cooldown.
    await click('[data-icon-placeholder]'); await idle();
    while (await count('[data-code-remove]') > 0) { await click('[data-code-remove="0"]'); await idle(); }
    for (const d of ['up', 'up', 'down', 'down']) { await click(`[data-code-add="${d}"]`); await idle(); }
    assert.equal(await evaluate(`${q('[data-code-editor] .arrow-seq')}.innerText.replace(/[^↑↓←→]/g, '')`), '↑↑↓↓', 'arrow sequence');
    await fill('#custom-cooldown', '60'); await idle();

    // Orbital: the vanilla barrage with a gas impact; refused fields stay visible with Runtime's reason.
    await click('[data-orbital-mode="native"]'); await idle();
    await select('#custom-field-payload-pattern', 'Orbital 120mm HE Barrage');
    await select('#custom-field-payload-impact_explosion', 'Orbital Gas Strike');
    assert.match(await evaluate(`${q('[data-unused-fields*="payload.salvos"]')}.innerText`), /vanilla barrage/, 'timings unused with the vanilla barrage, said once');
    assert.equal(await count('[data-custom-field="payload.salvos"]'), 0, 'no disabled row for an unused field');
    assert.equal(await evaluate(`${q('#custom-cooldown')}.disabled`), false, "a vanilla barrage keeps the stratagem's own cooldown");
    assert.match(await evaluate(`${q('[data-refused-field="orbital.explosion_scale"]')}.innerText`), /shared row/, 'explosion size refused with the reason');
    await screenshot('custom-stratagem-orbital');

    // Switching family asks first, then shows only the Eagle's fields and its refusals.
    await click('[data-family-change]'); await idle();
    await click('[data-family="eagle"]');
    await waitFor(q('[data-family-confirm]'), 'family switch confirmation');
    await click('[data-family-switch]'); await idle();
    await waitFor(`${q(editor('gas_rain_barrage') + ' [data-custom-family="eagle"]')}`, 'eagle family');
    assert.equal(await count('[data-custom-field="payload.shell"]'), 0, 'no orbital fields with the Eagle family');
    assert.equal(await count('[data-carrier-group="eagle"].active'), 1, 'an Eagle takes the Eagle carrier group');
    assert.match(await evaluate(`${q('[data-custom-field="cooldown"] [data-field-reason]')}.innerText`), /native cooldown/i, 'Eagle cooldown refused with the reason');
    assert.equal(await count('[data-refused-field="eagle.pattern"]') + await count('[data-refused-field="eagle.payload.projectile"]'), 2, 'Eagle pattern and projectile refused');
    await select('#custom-field-payload-donor', 'Eagle 500kg Bomb');
    assert.equal(await evaluate(`${q('[data-custom-field="payload.payload.impact_explosion"]')}.dataset.fieldStatus`), 'refused', '500 kg impact cannot be replaced');
    // Back to the orbital barrage. The Eagle switch cleared the cooldown (an Eagle keeps its native one); it is set again.
    await click('[data-family-change]'); await idle();
    await click('[data-family="orbital"]'); await waitFor(q('[data-family-confirm]'), 'confirm'); await click('[data-family-switch]'); await idle();
    assert.equal(await evaluate(`${q('#custom-cooldown')}.value`), '', 'cooldown cleared by the Eagle family');
    await fill('#custom-cooldown', '60'); await idle();
    await click('[data-orbital-mode="native"]'); await idle();
    await select('#custom-field-payload-pattern', 'Orbital 120mm HE Barrage');
    await select('#custom-field-payload-impact_explosion', 'Orbital Gas Strike');

    // Validation: an error (the Runtime's own validator's) is shown under its field, in the checklist and in the preview, whose link
    // jumps to its section; fixed, the stratagem is ready.
    await fill('#custom-description', ''); await idle();
    await waitFor(q('[data-field-issue="description"]'), 'description error rendered');
    await waitFor(q('[data-preview-readiness].error [data-preview-issue="description"]'), 'the preview lists it');
    assert.equal(await count('[data-jump="identity"].attention'), 1, 'the section bar marks Identity');
    assert.equal(await count('[data-runtime-words]'), 1, "the Runtime's own words are said to be its");
    assert.ok(await count('[data-section="checks"] [data-check="error"]') > 0, 'checklist shows the error');
    assert.match(await evaluate("document.querySelector('.desktop-shell').innerText"), /Custom content/, 'build-blocked text names custom content');
    await fill('#custom-description', 'Calls down a barrage of gas shells.'); await idle();
    await waitFor(`${q(editor('gas_rain_barrage') + ' [data-custom-state]')}.dataset.customState === 'ready'`, 'stratagem ready');
    assert.equal(await count('[data-section="checks"] [data-check="error"]'), 0, 'no errors');
    assert.ok(await count('[data-section="checks"] [data-check="pass"]') >= 3, 'passing checks listed');
    await evaluate("document.querySelector('[data-section=\"generated\"]').open = true"); await sleep(200);
    const registration = await evaluate(`${q('[data-custom-registration]')}.innerText`);
    assert.match(registration, /register_custom_stratagem\('gas_rain_barrage', function\(\)\n {4}return \{\n {8}id='gas_rain_barrage',/, "the SDK compiler's registration");
    assert.match(registration, /orbital=\{native=true,pattern='Orbital 120mm HE Barrage',impact_explosion='Orbital Gas Strike'\},/);
    await click('[data-generated-tab="json"]'); await idle();
    assert.match(await evaluate(`${q('[data-custom-project-form]')}.innerText`), /"family": "orbital"/, 'the Runtime project form');
    await screenshot('custom-stratagem-editor');

    // The example gallery: a Pelican gunship, its Lua callback from a snippet (completion knows ctx), then removed again.
    await click('[data-custom-back]'); await idle();
    await click('[data-custom-templates]'); await idle();
    await waitFor(q('[data-template-gallery]'), 'example gallery');
    assert.equal(await count('[data-template-gallery] [data-custom-template]'), 7, "ModBuilder's five simple examples and two of HD2Runtime's");
    await screenshot('custom-stratagems-gallery');
    await click('[data-custom-template="SimplePelican"]'); await idle();
    await waitFor(q(editor('pelican_flyover')), 'Pelican opens');
    await waitFor(`${q(editor('pelican_flyover') + ' [data-custom-state]')}.dataset.customState === 'ready'`, 'Pelican ready');
    // The simple example fires the chin gun as it is (its other gun fields say why they are unused); another round opens its firing sound.
    assert.match(await evaluate(`${q('[data-unused-fields~="payload.gun.sound"]')}.innerText`), /native round/, 'why the sound is unused');
    await select('#custom-field-payload-gun-round', 'ap4');
    await waitFor(q('[data-custom-field="payload.gun.sound"]'), 'the chin gun firing sound');
    await click('[data-jump="lua"]'); await idle();
    await click('[data-callback-tab="on_activate"]'); await idle();
    await waitFor(q('[data-callback-host="on_activate"] textarea'), 'Lua editor for on_activate');
    const completion = await evaluate("JSON.stringify(window.hd2LuaEditor.completions('ctx:', 4, { classes: { HD2CustomStratagemCall: [{ n: 'barrage', k: 'method' }] }, aliases: {}, strings: {}, root: '', locals: { ctx: 'HD2CustomStratagemCall' } }))");
    assert.match(completion, /barrage/, 'ctx: completes from the typed local');
    await click('[data-callback-snippet="log"]'); await idle();
    await waitFor(`${q('[data-callback-state]')}?.innerText === 'Saved'`, 'callback saved after typing stops', 40);
    await evaluate("document.querySelector('[data-section=\"generated\"]').open = true"); await sleep(200);
    await click('[data-generated-tab="lua"]'); await idle();
    assert.match(await evaluate(`${q('[data-custom-registration]')}.innerText`), /on_activate=function\(ctx\)\n\s+ctx:log\(/, 'the callback in the registration');
    await screenshot('custom-stratagem-pelican-lua');
    await click('[data-custom-delete]'); await click('[data-custom-delete-confirm] .danger'); await idle();

    // A weapon variant with a recoloured model, checked against the installed game (when there is one), then removed again.
    await click('[data-new-custom-stratagem]');
    await fill('[data-custom-new-name]', 'Laser Maxigun');
    await click('[data-custom-create] [data-family="weapon"]');
    await click('[data-custom-create-submit]'); await idle();
    await waitFor(q(editor('laser_maxigun')), 'variant opens');
    await select('#custom-field-payload-weapon', 'M-1000 Maxigun');
    await select('#custom-field-payload-round', 'output/v1/projectile/las-58-talon');
    await click('[data-model-create]'); await idle();
    await waitFor(q('[data-model-zones] [data-model-zone="7"]'), 'eight colour zones');
    await click('[data-model-custom]'); await idle();
    await click('[data-model-check-game]');
    await waitFor(q('[data-model-game-status]'), 'installed game checked', 160);
    console.log('model check: ' + await evaluate(`${q('[data-model-game-status]')}.innerText`));
    await screenshot('custom-stratagem-variant-model');
    await click('[data-custom-delete]'); await click('[data-custom-delete-confirm] .danger'); await idle();

    // Templates and the card grid.
    await click('[data-custom-templates]'); await idle();
    await click('[data-custom-template="SimpleSentry"]'); await idle();
    assert.equal(await count('[data-template-gallery]'), 0, 'the gallery closes');
    await waitFor(q(editor('quick_mg_sentry')), 'template opens');
    await waitFor(`${q(editor('quick_mg_sentry') + ' [data-custom-state]')}.dataset.customState === 'ready'`, 'template ready');
    await click('[data-custom-back]'); await idle();
    assert.equal(await count('[data-custom-card]'), 2, 'two custom stratagem cards');
    assert.match(await colour('[data-custom-card="quick_mg_sentry"]', 'boxShadow'), /98, 213, 150/, "a sentry's card has the defensive category accent");
    await click('[data-custom-card="quick_mg_sentry"] [data-card-duplicate]'); await idle();
    await waitFor(q(editor('quick_mg_sentry_copy')), 'duplicate opens');
    await waitFor(`${q(editor('quick_mg_sentry_copy') + ' [data-custom-state]')}.dataset.customState === 'ready'`, 'duplicate ready (own call-in code)');
    // The strip above an open editor lists every custom stratagem of the mod: switch between them without leaving the editor.
    assert.equal(await count('[data-custom-switcher] [data-custom-switch]'), 3, "the strip lists the mod's custom stratagems");
    assert.equal(await count('[data-custom-switch="quick_mg_sentry_copy"].active'), 1, 'the open one is marked');
    await click('[data-custom-switch="quick_mg_sentry"]'); await idle();
    await waitFor(q(editor('quick_mg_sentry')), 'switched from the strip');
    await screenshot('custom-stratagems-switcher');
    await click('[data-custom-back]'); await idle();
    assert.equal(await count('[data-custom-add-card]'), 1, 'the grid ends with an add card');
    await click('[data-custom-card="quick_mg_sentry_copy"] [data-card-delete]');
    await click('[data-card-delete-confirm] .danger'); await idle();
    assert.equal(await count('[data-custom-card]'), 2, 'duplicate deleted');
    await screenshot('custom-stratagems-grid');
    // Overview lists the mod's custom stratagems and opens one.
    await go('overview'); await idle();
    assert.equal(await count('[data-overview-custom-item]'), 2, "Overview lists the mod's custom stratagems");
    await click('[data-overview-custom-item="quick_mg_sentry"]'); await idle();
    await waitFor(q(editor('quick_mg_sentry')), 'opened from Overview');
    await click('[data-custom-back]'); await idle();

    // A custom projectile from the template.
    await go('custom-projectiles');
    await waitFor(q('[data-custom-projectile-distinction]'), 'custom vs vanilla projectile notice');
    await click('[data-custom-templates] .template-toggle');
    await click('[data-custom-template="talon_hybrid"]'); await idle();
    await waitFor(q('[data-custom-projectile-editor="talon_hybrid_bolt"]'), 'projectile editor');
    assert.equal(await count('[data-component-evidence]'), 4, 'Runtime evidence per component');
    await screenshot('custom-projectile-editor');

    // Clone a vanilla projectile…: every mapped projectile, as an exact copy to start from; those Runtime cannot use as a base say why.
    await click('[data-custom-back]'); await idle();
    await click('[data-custom-templates] .template-toggle');
    await click('[data-custom-clone-open]'); await idle();
    await waitFor(q('[data-custom-clone-list]'), 'clone list');
    assert.match(await evaluate(`${q('[data-custom-clone-count]')}.innerText`), /88 of the 107/, 'every mapped projectile listed');
    assert.equal(await count('[data-custom-clone-source]'), 107, 'one entry per mapped projectile');
    assert.equal(await count('[data-clone-refusal]'), 19, 'the ones that cannot be a base say why');
    await fill('[data-custom-clone-search]', 'R-36 Eruptor');
    await screenshot('custom-projectile-clone');
    await click('[data-custom-clone-source="output/v1/projectile/r-36-eruptor"]'); await idle();
    await waitFor(q('[data-custom-projectile-editor] #projectile-base'), 'clone opens in the editor');
    assert.equal(await evaluate(`${q('#projectile-base')}.value`), 'output/v1/projectile/r-36-eruptor', "the clone's base");
    assert.equal(await evaluate(`${q('#projectile-name')}.value`), 'R-36 Eruptor clone', 'named after what it copies');
    await waitFor(`${q('[data-custom-projectile-editor] [data-custom-state]')}.dataset.customState === 'ready'`, 'clone ready');

    // A player weapon's projectile selector: a vanilla group and a violet custom group in one selector.
    await go('player-weapons');
    await chooseWeapon('SMG-32 Reprimand');
    await evaluate("document.querySelector('[data-composition-attack=\"primary\"]').open = true"); await sleep(300);
    const picker = '[data-composition-attack="primary"] [data-projectile-picker]';
    await waitFor(q(`${picker} [data-custom-projectile-group]`), 'custom group in the projectile selector');
    assert.equal(await count(`${picker} [data-vanilla-group] [data-donor-list]`), 1, 'vanilla group');
    assert.match(await evaluate(`${q(picker + ' .picker-group-label.custom')}.innerText`), /custom — experimental/i, 'custom group label');
    assert.equal(await colour(`${picker} [data-custom-projectile-group]`, 'borderLeftColor'), 'rgb(167, 139, 250)', 'custom group is violet');
    assert.equal(await count(`${picker} [data-custom-option="talon_hybrid_bolt"]`), 1, 'existing custom projectile offered');
    const reprimand = async () => { await waitFor(q(picker), 'back on the SMG-32 Reprimand'); };

    // Create New Custom Projectile…: a draft in the builder; cancelled, the weapon is unchanged and the draft is gone.
    await click(`${picker} [data-custom-create-new]`); await idle();
    await waitFor(q('[data-custom-draft]'), 'draft banner in the Custom Projectile Builder');
    assert.match(await evaluate(`${q('[data-custom-draft]')}.innerText`), /SMG-32 Reprimand/, 'draft names its weapon');
    await screenshot('custom-projectile-draft');
    await click('[data-draft-cancel]'); await idle();
    await reprimand();
    assert.equal(await count('[data-custom-reference]'), 0, 'cancelled: the weapon fires what it fired');
    assert.equal(await count(`${picker} [data-custom-option]`), 2, 'cancelled draft deleted');

    // Again, completed: its id follows its name; once ready it is assigned to the weapon, which the builder returns to.
    await click(`${picker} [data-custom-create-new]`); await idle();
    await waitFor(q('[data-custom-draft]'), 'draft again');
    await fill('#projectile-name', 'Gas Bolt'); await idle();
    await waitFor(q('[data-custom-projectile-editor="gas_bolt"]'), 'draft id follows its name');
    await select('#projectile-base', '');
    await waitFor(q('[data-draft-not-ready]'), 'not ready without a base');
    assert.equal(await evaluate(`${q('[data-draft-complete]')}.disabled`), true, 'cannot complete while invalid');
    await select('#projectile-base', 'output/v1/projectile/las-58-talon');
    await select('#projectile-impactExplosion', 'output/v1/projectile/r-36-eruptor');
    await waitFor(q('[data-draft-ready]'), 'draft ready');
    await click('[data-draft-complete]'); await idle();
    if (await count('[data-draft-conflict]')) { await click('[data-draft-replace]'); await idle(); }
    await reprimand();
    await waitFor(`${q('[data-custom-reference]')}?.innerText.includes('gas_bolt')`, 'assigned where it was created');
    assert.equal(await evaluate(`${q(picker + ' [data-custom-option="gas_bolt"]')}.getAttribute('aria-selected')`), 'true', 'custom option selected');
    assert.equal(await count(`${picker} [data-donor].active`), 0, 'no vanilla donor selected');
    await screenshot('custom-projectile-weapon');

    // An existing custom projectile; renamed from the weapon, its id stays.
    await click(`${picker} [data-custom-option="talon_hybrid_bolt"]`); await idle();
    if (await count(`${picker} [data-custom-assign-conflict]`)) { await click(`${picker} [data-custom-assign-replace]`); await idle(); }
    await waitFor(`${q('[data-custom-reference]')}?.innerText.includes('talon_hybrid_bolt')`, 'existing custom projectile chosen');
    await click(`${picker} [data-custom-rename]`); await idle();
    await fill(`${picker} [data-custom-rename-input]`, 'Hybrid Bolt');
    await click(`${picker} [data-custom-rename-save]`); await idle();
    await waitFor(`${q('[data-projectile-custom="talon_hybrid_bolt"]')}?.innerText === 'Hybrid Bolt'`, 'renamed, id kept');

    // Opened from the weapon and broken there: the error shows inline in the weapon's selector; fixed from there.
    await click(`${picker} [data-custom-open]`); await idle();
    await waitFor(q('[data-custom-projectile-editor="talon_hybrid_bolt"] [data-custom-return]'), 'way back to the weapon');
    await select('#projectile-base', '');
    await click('[data-custom-return]'); await idle();
    await reprimand();
    await waitFor(q(`${picker} [data-custom-issue]`), 'invalid custom projectile shown inline');
    assert.equal(await count('[data-custom-reference-state="error"]'), 1, 'projectile summary marks the invalid reference');
    await click(`${picker} [data-custom-fix]`); await idle();
    await select('#projectile-base', 'output/v1/projectile/las-58-talon');
    await click('[data-custom-return]'); await idle();
    await reprimand();
    assert.equal(await count(`${picker} [data-custom-issue]`), 0, 'fixed');

    // Duplicate and edit…: the copy replaces it only when completed.
    await click(`${picker} [data-custom-duplicate-for]`); await idle();
    await waitFor(q('[data-custom-draft]'), 'copy draft');
    assert.match(await evaluate(`${q('[data-custom-draft]')}.innerText`), /talon_hybrid_bolt/, 'copy of the assigned projectile');
    await click('[data-draft-complete]'); await idle();
    await reprimand();
    await waitFor(`${q('[data-custom-reference]')}?.innerText.includes('talon_hybrid_bolt_copy')`, 'copy assigned');

    // Clear: the attack fires its own projectile again; then the template bolt again for the rest of the smoke.
    await click(`${picker} [data-custom-clear]`); await idle();
    assert.equal(await count('[data-custom-reference]'), 0, 'cleared to the vanilla projectile');
    await click(`${picker} [data-custom-option="talon_hybrid_bolt"]`); await idle();
    await waitFor(q('[data-custom-reference]'), 'custom chip in the projectile summary');

    // The Projectile Builder's hosts use the same selector: a player host returns there; a support host says why not.
    await go('projectiles');
    await fill('[data-builder-search]', 'SMG-32 Reprimand');
    await click('[data-builder-host="SMG-32 Reprimand"]'); await idle();
    await waitFor(q('[data-builder-host-detail] [data-custom-option="talon_hybrid_bolt"][aria-selected="true"]'), 'assigned custom projectile in the builder host');
    await click('[data-builder-host-detail] [data-custom-open]'); await idle();
    await click('[data-custom-return]'); await idle();
    await waitFor(q('[data-builder-host-detail="SMG-32 Reprimand"]'), 'returned to the Projectile Builder host');
    await fill('[data-builder-search]', 'APW-1 Anti-Materiel Rifle');
    await waitFor(q('[data-builder-host="APW-1 Anti-Materiel Rifle"]'), 'a writable support host');
    await click('[data-builder-host="APW-1 Anti-Materiel Rifle"]'); await idle();
    await waitFor(q('[data-builder-host-detail] [data-custom-unsupported]'), 'support host explains why not');
    assert.match(await evaluate(`${q('[data-builder-host-detail] [data-custom-unsupported]')}.innerText`), /player weapons only/);
    assert.equal(await count('[data-builder-host-detail] [data-custom-create-new]'), 0, 'no Create New where it cannot be assigned');
    await screenshot('custom-projectile-support-host');

    // Deleting a used custom projectile is refused with its uses.
    await go('custom-projectiles');
    // The page returns to the projectile that was open.
    if (!await count('[data-custom-projectile-editor="talon_hybrid_bolt"]')) { await click('[data-custom-card="talon_hybrid_bolt"] .custom-card-open'); await idle(); }
    await click('[data-custom-delete]'); await idle();
    await waitFor(q('[data-custom-delete-references]'), 'references listed');
    assert.match(await evaluate(`${q('[data-custom-delete-references]')}.innerText`), /SMG-32 Reprimand/);
    await evaluate(`[...document.querySelectorAll('[data-custom-delete-references] button')].at(-1).click()`); await sleep(300);
    assert.equal(await count('[data-custom-projectile-editor="talon_hybrid_bolt"]'), 1, 'still there after cancel');

    // Changes, Lua, Chinese, reopen, export.
    await go('changes');
    await waitFor(q('[data-custom-changes]'), 'custom content panel on Changes');
    assert.equal(await count('[data-custom-change]'), 7, 'two stratagems, four projectiles, one weapon');
    assert.match(await evaluate("document.querySelector('[data-nav=\"changes\"]').innerText"), /\b7\b/, 'the Changes count includes custom content');
    const source = await lua();
    assert.match(source, /register_custom_stratagem\('/); assert.match(source, /td-110-maelstrom-slot-2/); assert.match(source, /custom_projectiles\.define/);
    await setLanguage('zh-Hans');
    await go('custom-stratagems');
    await waitFor("document.querySelector('[data-custom-page=\"stratagems\"] h1')?.innerText === '自定义战略配备'", 'Chinese title');
    await screenshot('custom-stratagems-zh');
    await setLanguage('en');
    await openProject('Custom Smoke');
    await go('custom-stratagems');
    assert.equal(await count('[data-custom-card]'), 2, 'reopened: custom stratagems');
    await click('[data-custom-card="gas_rain_barrage"] .custom-card-open'); await idle();
    assert.equal(await evaluate(`${q('[data-code-editor] .arrow-seq')}.innerText.replace(/[^↑↓←→]/g, '')`), '↑↑↓↓', 'reopened: call-in code');
    await go('export');
    await waitFor(q('[data-export-custom]'), 'export notice');
    assert.match(await evaluate(`${q('[data-export-custom]')}.innerText`), /HD2Runtime 0\.30\.0 or newer/);
    await evaluate("document.querySelector('.page-heading .primary').click()"); await idle();
    await waitFor(q('.export-success'), 'export succeeded');
    const exported = await evaluate("document.querySelector('.export-success code.path').innerText");
    console.log(JSON.stringify({ ok: true, export: exported }));
} finally { s.close(); }
