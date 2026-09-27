// Developer-only smoke test against a running Debug MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9223}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
    catch { /* WebView2 is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 500));
}
assert(pages?.length, 'WebView2 CDP port must be enabled on the running Debug app');
const page = pages.find(p => p.url === 'https://0.0.0.1/');
assert(page, 'MAUI Blazor WebView page must be running');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map();
socket.onmessage = ({data}) => { const message = JSON.parse(data); if (message.id) { const p = pending.get(message.id); pending.delete(message.id); message.error ? p.reject(message.error) : p.resolve(message.result); } };
const cdp = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, {resolve, reject}); socket.send(JSON.stringify({id: n, method, params})); });
const evaluate = async expression => { const result = await cdp('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true}); if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails)); return result.result.value; };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const waitFor = async (expression, message) => { for (let i=0;i<120;i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(250); } throw new Error('Timed out: ' + message + '\n' + await evaluate('document.body.innerText')); };
const click = async text => { await evaluate(`(() => { const el = [...document.querySelectorAll('button')].find(b => b.innerText.trim() === ${JSON.stringify(text)}); if (!el) throw new Error('Missing button: '+${JSON.stringify(text)}); el.click(); })()`); await sleep(200); };
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(100); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
const nav = async label => { await evaluate(`[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes(${JSON.stringify(label)})).click()`); await sleep(150); };
const choose = async weapon => { await fill('#weapon-search',weapon); await evaluate(`document.querySelector('[data-weapon='+CSS.escape(${JSON.stringify(weapon)})+']').click()`); await sleep(150); };
const fieldClick = async (field,label) => { await evaluate(`[...document.querySelector('[data-field="${field}"]').querySelectorAll('button')].find(b=>b.innerText===${JSON.stringify(label)}).click()`); await sleep(200); };
const group = name => `[data-weapon-group="${name}"]`;
const expand = async name => { await evaluate(`document.querySelector(${JSON.stringify(group(name)+' > summary')}).click()`); await sleep(100); };
const state = async field => evaluate(`({modified:document.querySelector('[data-field="${field}"]').classList.contains('modified'),text:document.querySelector('[data-field="${field}"] .value-comparison').innerText})`);
try {
    const samples = JSON.parse(await fs.readFile('samples/player-weapon-ammo.json','utf8'));
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    if(process.argv.includes('--relaunch')) {
        for(const sample of samples) {
            await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(sample.name)})).click()`); await sleep(200);
            await expand(sample.weapon);
            assert.equal(await evaluate("document.querySelectorAll('[data-change].modified').length"),sample.changes.length);
            await nav('Project library');
        }
        console.log('PASS: both ammo projects and all semantic overrides survived desktop relaunch');
    } else {
        if (process.argv.includes('--review')) {
            await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('VerdictMagazine')).click()"); await sleep(200);
        }
        for(const sample of process.argv.includes('--review') ? [] : samples) {
            await nav('Project library'); await click('+ Create New Mod');
            await waitFor("!!document.querySelector('#mod-name')",'new project');
            await fill('#mod-name',sample.name); await fill('#author','SkyeShade');
            await fill('#resource-id','mods/skyeshade/'+sample.name.toLowerCase()); await fill('#description',sample.description);
            await click('Create project →'); await waitFor("document.body.innerText.includes('Project overview')",'project saved');
            await nav('Player Weapons'); await choose(sample.weapon);
            assert(await evaluate("!!document.querySelector('[data-section=\"Ammo / Magazine\"]')"));
            for(const c of sample.changes) {
                await fill(`[data-field="${c.field}"] input[type=number]`,String(c.value));
                await waitFor(`document.querySelector('[data-field="${c.field}"]').classList.contains('modified') && !document.querySelector('.stat-card [role=status]')`,'ammo autosaved');
            }
            const first=sample.changes[0];
            await fieldClick(first.field,'Reset field'); assert.equal((await state(first.field)).modified,false);
            await fill(`[data-field="${first.field}"] input[type=number]`,String(first.value)); await sleep(150);
            const derived=sample.weapon==='SG-8 Punisher'?'rounds.capacity':'magazine.magazines_from_ammo_box';
            assert.equal(await evaluate(`document.querySelectorAll('[data-field="${derived}"] input').length`),0);
            assert((await evaluate(`document.querySelector('[data-field="${derived}"]').innerText`)).includes('Calculated SDK baseline'));
            await evaluate("document.querySelector('[data-section=\"Ammo / Magazine\"]').scrollIntoView()");
            await screenshot(sample.name+'-ammo-editor');
            await nav('Changes'); await expand(sample.weapon);
            assert.equal(await evaluate("document.querySelectorAll('[data-change].modified').length"),sample.changes.length);
            assert((await evaluate("document.querySelector('.weapon-change-group').innerText")).includes('Ammo / Magazine'));
            await nav('Lua Preview');
            const lua=await evaluate('document.querySelector("pre").innerText');
            assert.equal(lua,(await fs.readFile(`HD2RuntimeGUI.Tests/Golden/${sample.name}.lua`,'utf8')).replaceAll('\r\n','\n'));
            await click('↗ Build / Export Mod'); await waitFor("document.body.innerText.includes('Export complete')",'ammo export');
            console.log('GUI ZIP: '+await evaluate("document.querySelector('.export-success code').innerText"));
        }
        await nav('Player Weapons');
        for(const name of ['AR-23C Liberator Concussive','AR-23 Liberator','AR-23P Liberator Penetrator','AR-59 Suppressor']) {
            await choose(name);
            assert(await evaluate("!!document.querySelector('[data-ammo-preset]')"));
            assert.equal(await evaluate("document.querySelectorAll('[data-section=\"Ammo / Magazine\"] input').length"),0);
        }
        await choose('GP-31 Grenade Pistol');
        assert.equal(await evaluate("document.querySelectorAll('[data-section=\"Ammo / Magazine\"] input').length"),0);
        assert((await evaluate('document.body.innerText')).includes('duplicate runtime resources have different rounds-feed values'));
        await screenshot('ammo-gp31-blocked');
        await choose('ARC-12 Blitzer'); assert.equal(await evaluate("document.querySelectorAll('[data-section=\"Ammo / Magazine\"]').length"),0);
        for(const [name,baseline] of [['AR-11 Arbitrator','45'],['AR/GL-21 One-Two','40']]) {
            await choose(name); assert.equal(await evaluate("document.querySelector('[data-field=\"magazine.capacity\"] input').value"),baseline);
            assert(await evaluate("!!document.querySelector('[data-ammo-discrepancy]')"));
        }
        await screenshot('ammo-catalog-disagreement');
        await nav('Project library');
        console.log('PASS: ammo autosave, reset, grouped changes, Lua goldens, build/export, derived/preset/shared read-only, GP-31, not-applicable, SDK disagreements');
    }
} finally { socket.close(); }
