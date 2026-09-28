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
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    if(process.argv.includes('--relaunch')) {
        await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('UX polish validation')).click()"); await sleep(200);
        assert.equal(await evaluate("document.querySelectorAll('.weapon-change-group').length"),2);
        assert.equal(await evaluate("document.querySelectorAll('.weapon-change-group[open]').length"),0);
        await expand('AR-23C Liberator Concussive');
        assert((await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive'))}).innerText`)).includes('1100'));
        assert((await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive')+' [data-change="weapon.suppressed"]')}).innerText`)).includes('On'));
        assert.equal(await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive')+' .weapon-group-content .panel-heading input')}).checked`),false);
        await screenshot('ux-overview-reloaded');
        console.log('PASS: project reload preserves edits; groups collapsed and Show all fields off');
    } else if(process.argv.includes('--safety')) {
        await nav('Project library');
        await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('UX polish validation')).click()"); await sleep(200);
        await nav('Player Weapons'); await choose('AR-23 Liberator');
        await fill('[data-field="projectile.drag"] input[type=number]','0.1');
        await waitFor("document.body.innerText.includes('Build requires review')",'shared edit blocked');
        await evaluate("document.querySelector('[data-field=\"projectile.drag\"] .shared-warning input').click()");
        await waitFor("!document.body.innerText.includes('Build requires review')",'explicit acknowledgement autosaved');
        await nav('Lua Preview');
        assert((await evaluate('document.querySelector("pre").innerText')).includes('allow_shared=true'));
        await nav('Player Weapons'); await choose('AR-23 Liberator'); await fieldClick('projectile.drag','Reset field');
        assert.equal((await state('projectile.drag')).modified,false);
        await choose('LAS-5 Scythe');
        assert.equal(await evaluate("document.querySelectorAll('.weapon-detail input[type=number]').length"),0);
        console.log('PASS: shared acknowledgement autosaves explicitly; reset removes override; duplicate writes remain blocked');
    } else {
        await click('+ Create New Mod'); await waitFor("!!document.querySelector('#mod-name')",'create');
        await fill('#mod-name','UX polish validation'); await fill('#author','SkyeShade'); await fill('#resource-id','mods/skyeshade/ux_polish');
        await click('Create project →'); await waitFor("!!document.querySelector('[data-overview]')",'saved');
        await nav('Player Weapons'); await choose('AR-23C Liberator Concussive');
        assert.equal(await evaluate("[...document.querySelectorAll('button')].some(b=>b.innerText.trim()==='Save change')"),false);
        assert.equal((await state('weapon.suppressed')).modified,false);
        assert((await state('weapon.suppressed')).text.includes('Off'));
        assert.equal(await evaluate("document.querySelector('[data-field=\"weapon.suppressed\"]').innerText.includes('Reset field')"),false);
        await fill('[data-field="weapon.fire_rate"] input[type=number]','1100');
        await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        await fill('[data-field="weapon.fire_rate"] input[type=number]','1200');
        await waitFor("!document.querySelector('.stat-card [role=status]')",'updated override');
        await fill('[data-field="weapon.fire_rate"] input[type=number]','');
        await waitFor("!!document.querySelector('.dialog-error')",'invalid number rejected');
        await nav('Changes');
        await expand('AR-23C Liberator Concussive');
        assert((await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive'))}).innerText`)).includes('1200'));
        await nav('Player Weapons'); await choose('AR-23C Liberator Concussive');
        await fill('[data-field="weapon.fire_rate"] input[type=number]','1100');
        assert((await state('weapon.fire_rate')).text.replace(/\s+/g,' ').includes('400 → 1100 RPM'));
        await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        assert(await evaluate("document.querySelector('.weapon-heading').innerText.includes('Reset weapon')"));
        await evaluate("document.querySelector('[data-field=\"weapon.suppressed\"] input[role=switch]').click()"); await sleep(100);
        assert((await state('weapon.suppressed')).text.replace(/\s+/g,' ').includes('Off → On'));
        await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        await evaluate("document.querySelector('[data-field=\"weapon.suppressed\"] input[role=switch]').click()");
        await waitFor("!document.querySelector('[data-field=\"weapon.suppressed\"]').classList.contains('modified')",'boolean baseline');
        assert.equal((await state('weapon.suppressed')).modified,false);
        assert.equal(await evaluate("document.querySelector('[data-field=\"weapon.suppressed\"]').innerText.includes('Reset field')"),false);
        await evaluate("document.querySelector('[data-field=\"weapon.suppressed\"] input[role=switch]').click()");
        await waitFor("document.querySelector('[data-field=\"weapon.suppressed\"]').classList.contains('modified') && !document.querySelector('.stat-card [role=status]')",'boolean autosave');
        await screenshot('ux-modified-numeric-boolean');
        await choose('P-113 Verdict');
        await fill('[data-field="projectile.gravity"] input[type=number]','1.0000000');
        await waitFor("!document.querySelector('.stat-card [role=status]')",'equivalent float');
        assert.equal((await state('projectile.gravity')).modified,false);
        await fill('[data-field="projectile.drag"] input[type=number]','0.1'); await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        for(const page of ['Overview','Changes']) {
            await nav(page);
            assert.equal(await evaluate("document.querySelectorAll('.weapon-change-group').length"),2);
            assert.equal(await evaluate("document.querySelectorAll('.weapon-change-group[open]').length"),0);
            await expand('AR-23C Liberator Concussive');
            assert.equal(await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive'))}).querySelectorAll('[data-change]').length`),2);
            await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive')+' .weapon-group-content .panel-heading input')}).click()`); await sleep(100);
            assert((await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive'))}).querySelectorAll('[data-change]').length`))>20);
            assert.equal(await evaluate(`document.querySelector(${JSON.stringify(group('AR-23C Liberator Concussive')+' [data-change="weapon.ergonomics"]')}).classList.contains('modified')`),false);
            await screenshot('ux-'+page.toLowerCase());
        }
        await nav('Player Weapons'); await choose('AR-23C Liberator Concussive'); await fieldClick('weapon.suppressed','Reset field');
        assert.equal((await state('weapon.suppressed')).modified,false); assert((await state('weapon.suppressed')).text.includes('Off'));
        await choose('P-113 Verdict'); await click('Reset weapon');
        assert.equal((await state('projectile.drag')).modified,false);
        // Committing the baseline removes the override without a Save action.
        await choose('AR-23C Liberator Concussive'); await fill('[data-field="weapon.fire_rate"] input[type=number]','400'); await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        assert.equal((await state('weapon.fire_rate')).modified,false);
        assert.equal(await evaluate("document.querySelector('.weapon-heading').innerText.includes('Reset weapon')"),false);
        await fill('[data-field="weapon.fire_rate"] input[type=number]','1100'); await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        await evaluate("document.querySelector('[data-field=\"weapon.suppressed\"] input[role=switch]').click()"); await sleep(100); await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        await choose('P-113 Verdict'); await fill('[data-field="projectile.drag"] input[type=number]','0.1'); await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave');
        await nav('Changes'); await screenshot('ux-collapsed-groups');
        assert(!await evaluate("!!document.querySelector('.notice.error,.dialog-error')"));
        console.log('PASS: numeric/boolean/neutral states, multi-field/multi-weapon grouping, Show all fields, reset field/weapon/baseline');
    }
} finally { socket.close(); }
