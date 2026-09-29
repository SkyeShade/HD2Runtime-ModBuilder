// Developer-only smoke test for projectile swaps with existing stat edits against a running isolated app (WebView2 CDP): the choice is
// made inline in the Projectile section (keep / discard / cancel), the Changes-page reset asks in its row, and edits left on a projectile
// the attack no longer fires (older projects) are resolved in the weapon, never by a trip to Overview / Changes.
// Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9239,
// then run: HD2GUI_CDP_PORT=9239 HD2GUI_DATA_ROOT=<same folder> node tools/projectile-swap-smoke.mjs
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const port = process.env.HD2GUI_CDP_PORT ?? 9236, dataRoot = process.env.HD2GUI_DATA_ROOT;
assert(dataRoot, 'HD2GUI_DATA_ROOT must name the app data root');
let pages;
for (let attempt = 0; attempt < 80; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
    catch { /* WebView2 is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 500));
}
const page = pages.find(p => p.url === 'https://0.0.0.1/');
assert(page, 'MAUI Blazor WebView page must be running');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0; const pending = new Map();
socket.onmessage = ({data}) => { const message = JSON.parse(data); if (message.id) { const p = pending.get(message.id); pending.delete(message.id); message.error ? p.reject(message.error) : p.resolve(message.result); } };
const cdp = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, {resolve, reject}); socket.send(JSON.stringify({id: n, method, params})); });
const evaluate = async expression => { const result = await cdp('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true}); if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails)); return result.result.value; };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const waitFor = async (expression, message) => { for (let i = 0; i < 160; i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(250); } throw new Error('Timed out: ' + message + '\n' + (await evaluate('document.body.innerText')).slice(0, 3000)); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive: true}); const image = await cdp('Page.captureScreenshot', {format: 'png'}); await fs.writeFile('docs/screenshots/' + name + '.png', Buffer.from(image.data, 'base64')); };
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(450); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(450); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(550); };
const scroll = async selector => { await evaluate(`(() => { ${q(selector)}.scrollIntoView({block: 'start'}); const c = document.querySelector('.workspace-content'); if (c) c.scrollTop -= 120; })()`); await sleep(300); };
const innerText = selector => evaluate(`${q(selector)}?.innerText ?? ''`);
const buildBlocked = async () => (await evaluate('document.body.innerText')).includes('Build requires review');
const VERDICT = 'P-113 Verdict', JAR = 'JAR-5 Dominator|primary';
const proj = '[data-projectile="primary"]', velocity = '[data-object-field="projectile.velocity"]', damage = '[data-object-field="damage.standard_damage"]';
const openVerdict = async () => { await go('player-weapons'); await click(`[data-weapon="${VERDICT}"]`); await waitFor(q(`[data-authoring-summary="${VERDICT}"]`), 'Verdict editor'); };
const onVerdictEditor = () => evaluate(`!!${q(`[data-authoring-summary="${VERDICT}"]`)} && !${q('[data-overview]')}`);
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor(q('#mod-name'), 'create dialog');
    await fill('#mod-name', 'Projectile Swap Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    // Two stat edits on the Verdict's own projectile.
    await openVerdict();
    await fill(velocity + ' input', '350'); await waitFor(`${q(velocity)}.classList.contains('modified')`, 'velocity edit');
    await fill(damage + ' input', '200'); await waitFor(`${q(damage)}.classList.contains('modified')`, 'damage edit');

    // Choosing another projectile asks right there; nothing is saved yet, and Cancel leaves everything as it was.
    await evaluate(`${q('[data-composition-attack="primary"]')}.open = true`);
    await fill(proj + ' select', JAR); await waitFor(q(proj + ' [data-swap-prompt]'), 'swap prompt');
    assert.equal(await count(proj + ' [data-swap-edit]'), 2);
    assert((await innerText(proj + ' [data-swap-prompt]')).includes('You changed 2 values on the P-113 Verdict'));
    assert.equal(await evaluate(`${q(proj + ' select')}.disabled`), true, 'selector is locked while deciding');
    assert(await onVerdictEditor(), 'still on the weapon'); assert(!(await buildBlocked()));
    await scroll(proj); await screenshot('projectile-swap-prompt');
    await click(proj + ' [data-swap-cancel]');
    assert.equal(await count(proj + ' [data-swap-prompt]'), 0); assert.equal(await evaluate(`${q(proj + ' select')}.value`), '');
    assert.equal(await evaluate(`${q('[data-projectile-object]')}.dataset.projectileObject`), VERDICT);

    // Keep: one save swaps the projectile and moves both values onto the JAR-5 projectile.
    await fill(proj + ' select', JAR); await waitFor(q(proj + ' [data-swap-prompt]'), 'swap prompt again');
    await click(proj + ' [data-swap-keep]'); await waitFor(q(proj + ' [data-swap-result]'), 'swap result');
    assert.equal(await innerText(proj + ' [data-swap-result] span'), 'Kept 2 values on the new projectile.');
    assert.equal(await evaluate(`${q('[data-projectile-object]')}.dataset.projectileObject`), 'JAR-5 Dominator');
    assert(await evaluate(`${q(velocity)}.classList.contains('modified') && ${q(velocity + ' input')}.value === '350'`), 'velocity kept on the new projectile');
    assert(await onVerdictEditor(), 'still on the weapon'); assert(!(await buildBlocked()), 'build not blocked');
    await scroll(proj); await screenshot('projectile-swap-kept');
    await go('lua'); const lua = await evaluate('document.querySelector("pre").innerText');
    assert(lua.includes("hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile()") && lua.includes('value=350'), 'Lua writes the kept values on the JAR-5 projectile');

    // An older project with edits left on a projectile the attack no longer fires: the banner leads to the weapon, which resolves it inline.
    const [projectId] = await fs.readdir(path.join(dataRoot, 'Projects'));
    const file = path.join(dataRoot, 'Projects', projectId, 'project.hd2mod.json');
    const project = JSON.parse(await fs.readFile(file, 'utf8')); project.projectileChanges = [];
    await fs.writeFile(file, JSON.stringify(project, null, 2));
    await go('library'); await click('.project-card .project-open');
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project reopened');
    assert(await buildBlocked(), 'orphaned edits block the build');
    assert.equal(await innerText('[data-fix-projectile]'), 'Open P-113 Verdict'); assert(!(await innerText('[role=alert]')).includes('Review changes'));
    await screenshot('projectile-orphaned-banner');
    await click('[data-fix-projectile]'); await waitFor(q(proj + ' [data-orphaned-edits]'), 'orphaned edits panel');
    assert(await onVerdictEditor(), 'banner opened the weapon');
    assert.equal(await count('[data-projectile-needs-attention]'), 1); assert.equal(await evaluate(`${q('[data-composition-attack="primary"]')}.open`), true);
    assert.equal(await count('[data-fix-projectile]'), 0, 'no redundant banner button on the weapon page');
    await scroll(proj); await screenshot('projectile-orphaned-inline');
    await click(proj + ' [data-orphaned-keep]'); await waitFor(q(proj + ' [data-swap-result]'), 'orphan result');
    assert.equal(await count('[data-orphaned-edits]'), 0); assert.equal(await count('[data-projectile-needs-attention]'), 0);
    assert(!(await buildBlocked()), 'resolved in place'); assert(await evaluate(`${q(velocity + ' input')}.value === '350'`));

    // The Changes-page reset asks in its row as well; discarding removes the stat edits with the swap.
    await fill(proj + ' select', JAR); await waitFor(q(proj + ' [data-swap-prompt]'), 'swap prompt (third)'); await click(proj + ' [data-swap-keep]');
    await go('changes');
    if (!(await evaluate(`${q(`[data-weapon-group="${VERDICT}"]`)}.open`))) await click(`[data-weapon-group="${VERDICT}"] > summary`);
    await click('[data-projectile-change="primary"] .reset-action'); await waitFor(q('[data-projectile-change="primary"] [data-swap-prompt]'), 'reset prompt');
    assert((await innerText('[data-projectile-change="primary"] [data-swap-prompt]')).includes('Resetting to P-113 Verdict'));
    await scroll('[data-projectile-change="primary"]'); await screenshot('projectile-reset-prompt');
    // With nothing left on the weapon its group leaves the Changes list, like any other reset there.
    await click('[data-projectile-change="primary"] [data-swap-discard]'); await waitFor(`!${q(`[data-weapon-group="${VERDICT}"]`)}`, 'weapon changes cleared');
    assert.equal(await count('[data-projectile-change]'), 0); assert(!(await buildBlocked()));
    const saved = JSON.parse(await fs.readFile(file, 'utf8')); assert.equal(saved.projectileChanges.length, 0); assert.equal(saved.compositionChanges.length, 0);
    console.log('PASS: projectile swaps with stat edits are decided inline (keep / discard / cancel, Changes reset, older-project repair)');
} finally { socket.close(); }
