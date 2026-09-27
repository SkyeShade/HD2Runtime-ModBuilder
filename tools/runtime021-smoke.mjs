// Developer-only smoke test against a running isolated MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9234}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
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
const nav = async label => { await waitFor(`[...document.querySelectorAll('.nav-item')].some(b=>b.innerText.includes(${JSON.stringify(label)}))`,label+' navigation'); await evaluate(`[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes(${JSON.stringify(label)})).click()`); await sleep(150); };

const open = async name => { await nav('Project library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')",'open '+name); };
const select = async name => { await nav('Stratagems'); await fill('#stratagem-search',name); await evaluate(`document.querySelector('[data-stratagem='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(200); };
const field = id => '[data-stratagem-field][data-semantic-field="'+id+'"]';
const build = async () => { await nav('Build / Export'); await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')",'export'); console.log(await evaluate("document.querySelector('.export-success code').innerText")); };
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    await open('OrbitalLaser021'); await select('Orbital Laser');
    assert.equal(await evaluate("document.querySelectorAll('[data-stratagem-branch]').length"),3);
    assert.equal(await evaluate("document.querySelectorAll('[data-semantic-field=\"stratagem.max_uses\"] input[type=number]').length"),0);
    assert((await evaluate('document.body.textContent')).includes('Call-In Time'));
    await fill(field('damage.standard_damage')+' input[type=number]','410');
    await waitFor("document.body.innerText.includes('410')",'damage autosave');
    await nav('Lua Preview'); assert((await evaluate('document.querySelector("pre").innerText')).includes('value=410'));
    await select('Orbital Laser'); await fill(field('damage.standard_damage')+' input[type=number]','400');
    await fill(field('stratagem.cooldown')+' input[type=number]','300.0');
    await waitFor("!document.querySelector('[data-semantic-field=\"stratagem.cooldown\"]').classList.contains('modified')",'no-op removal');
    await fill(field('stratagem.cooldown')+' input[type=number]','180');
    await screenshot('runtime021-orbital-laser'); await build();
    await open('Eagle021'); await select('Eagle Airstrike');
    assert.equal(await evaluate("document.querySelectorAll('[data-stratagem-approval=\"eagle_rearm\"] input').length"),1);
    assert.equal(await evaluate("document.querySelector('[data-semantic-field=\"eagle.rearm_time\"] input[type=number]').value"),'90');
    await select('Eagle Strafing Run');
    assert.equal(await evaluate("document.querySelector('[data-semantic-field=\"eagle.rearm_time\"] input[type=number]').value"),'90');
    await nav('Changes');
    assert.equal(await evaluate("document.querySelectorAll('[data-stratagem-change-group=\"Eagle Shared System\"]').length"),1);
    await evaluate("document.querySelector('[data-stratagem-change-group=\"Eagle Shared System\"] summary').click()"); await screenshot('runtime021-eagle-shared');
    await build();
    await open('OrbitalPrecision021'); await select('Orbital Precision Strike'); assert((await evaluate('document.body.innerText')).includes('delivery:1'));
    assert.equal(await evaluate("document.querySelectorAll('[data-stratagem-field].modified').length"),2);
    await build();
    await open('SupportCallIn021'); await select('GR-8 Recoilless Rifle');
    assert.equal(await evaluate("document.querySelectorAll('[data-stratagem-field] input[type=number]').length"),1);
    await select('SG-88 Break-Action Shotgun'); assert((await evaluate('document.body.innerText')).includes('No uniquely correlated'));
    assert.equal(await evaluate("document.querySelectorAll('[data-stratagem-field] input[type=number]').length"),0);
    await build();
    await open('GasOrNapalm021'); await select('Orbital Gas Strike'); assert((await evaluate('document.body.innerText')).includes('Status duration'));
    await build(); await nav('Project library');
    console.log('PASS: five projects, branch rendering, autosave/reset, shared Eagle scope, read-only call-ins, Lua and exports');
} finally { socket.close(); }
