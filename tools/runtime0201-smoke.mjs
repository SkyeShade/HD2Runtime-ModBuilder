// Developer-only smoke test against a running Debug MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9225}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
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
const choose = async weapon => { await fill('#weapon-search',weapon); await evaluate(`document.querySelector('[data-weapon='+CSS.escape(${JSON.stringify(weapon)})+']').click()`); await sleep(150); };
const fieldClick = async (field,label) => { await evaluate(`[...document.querySelector('[data-field="${field}"]').querySelectorAll('button')].find(b=>b.innerText===${JSON.stringify(label)}).click()`); await sleep(200); };
const group = name => `[data-weapon-group="${name}"]`;
const expand = async name => { await evaluate(`document.querySelector(${JSON.stringify(group(name)+' > summary')}).click()`); await sleep(100); };
const state = async field => evaluate(`({modified:document.querySelector('[data-field="${field}"]').classList.contains('modified'),text:document.querySelector('[data-field="${field}"] .value-comparison').innerText})`);
const create = async name => {
    await nav('Project library'); await click('+ Create New Mod'); await waitFor("!!document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name',name); await fill('#author','SkyeShade'); await fill('#resource-id','mods/skyeshade/'+name.toLowerCase());
    await click('Create project →'); await waitFor("!!document.querySelector('[data-overview]')",'saved project'); await nav('Player Weapons');
};
const build = async () => { await click('↗ Build / Export Mod'); await waitFor("document.body.innerText.includes('Export complete')",'export'); console.log('GUI ZIP: '+await evaluate("document.querySelector('.export-success code').innerText")); };
const openProject = async name => { await nav('Project library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')",'open '+name); };
const approve = async (domain, value=true) => {
    const selector=`[data-scope-domain="${domain}"] input[type=checkbox]`;
    await evaluate(`(()=>{const input=document.querySelector(${JSON.stringify(selector)});if(!input)throw new Error('Missing shared approval scope');if(input.checked!==${value})input.click();})()`); await sleep(200);
};
const lua = async () => { await nav('Lua Preview'); return evaluate('document.querySelector("pre").innerText'); };
const support = async name => {
    await nav('Support Weapons'); await fill('#support-search', name);
    await evaluate(`document.querySelector('[data-support='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(200);
};
const supportField = id => `[data-semantic-field="${id}"]`;
const supportEdit = async (id, value) => fill(supportField(id) + ' input[type=number]', value);
const approveSupport = async () => {
    for (const scope of await evaluate("[...document.querySelectorAll('[data-support-scope]')].map(e=>e.dataset.supportScope)")) {
        await evaluate(`(()=>{const input=document.querySelector('[data-support-scope='+CSS.escape(${JSON.stringify(scope)})+'] input');if(!input.checked)input.click()})()`); await sleep(200);
    }
};
const samples = [
    ['RecoillessTuning020','GR-8 Recoilless Rifle',[['projectile.velocity','350'],['explosion.outer_radius','10']]],
    ['ArcThrowerTuning020','ARC-3 Arc Thrower',[['arc.range','75']]],
    ['C4Explosion020','B/MD C4 Pack',[['explosion.outer_radius','15'],['explosion.damage.standard_damage','1500']]],
    ['SupportAMRTuning020','APW-1 Anti-Materiel Rifle',[['weapon.ergonomics','80'],['weapon.sway','0.5'],['projectile.velocity','1100']]]
];
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    if (!process.argv.includes('--relaunch')) {
        for (const [name,weapon,edits] of samples) {
            await nav('Project library');
            if (await evaluate(`[...document.querySelectorAll('.project-open')].some(b=>b.innerText.includes(${JSON.stringify(name)}))`)) await openProject(name);
            else await create(name);
            await support(weapon);
            for (const scope of await evaluate("[...document.querySelectorAll('[data-support-scope]')].map(e=>e.dataset.supportScope)")) {
                await evaluate(`(()=>{const input=document.querySelector('[data-support-scope='+CSS.escape(${JSON.stringify(scope)})+'] input');if(input.checked)input.click()})()`); await sleep(200);
            }
            for (const [field,value] of edits) await supportEdit(field,value);
            assert((await evaluate('document.body.innerText')).includes('Build requires review'));
            assert.equal(await evaluate("document.querySelectorAll('[data-support-field].modified').length"),edits.length);
            await approveSupport(); assert(!(await evaluate('document.body.innerText')).includes('Build requires review'));
            await screenshot('runtime0201-'+name);
            await nav('Changes');
            const summary = `[...document.querySelectorAll('details > summary')].find(e=>e.innerText.includes(${JSON.stringify(weapon)}))`;
            await evaluate(`${summary}.click()`); await sleep(100);
            assert.equal(await evaluate("document.querySelectorAll('[data-support-field]').length"),edits.length);
            const result = await lua(); assert(result.includes('hd2.support_weapon(')); assert(!result.includes('0x')); assert(!result.includes('Build blocked'));
            if(edits.length>1) assert(result.includes('plan={'));
            await build();
        }
        // One acknowledgement covers scalar siblings on one exact object, and reset removes intent.
        await support('APW-1 Anti-Materiel Rifle'); await supportEdit('projectile.drag','0.1');
        assert.equal(await evaluate("document.querySelectorAll('[data-support-scope]').length"),1);
        assert(!(await evaluate('document.body.innerText')).includes('Build requires review'));
        await evaluate(`[...document.querySelector(${JSON.stringify(supportField('projectile.drag'))}).querySelectorAll('button')].find(b=>b.innerText==='Reset field').click()`); await sleep(200);
        assert.equal(await evaluate(`${JSON.stringify('')} + document.querySelector(${JSON.stringify(supportField('projectile.drag'))}).classList.contains('modified')`),'false');

        const catalog=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/SupportWeaponAuthoringCapabilities.json','utf8'));
        let total=0,writable=0;
        for(const w of catalog.weapons) {
            await support(w.name);
            const count=await evaluate("document.querySelectorAll('[data-support-field] input[type=number]').length");
            assert.equal(count,w.fieldInstanceKeys.length,w.name); total+=count; if(count)writable++;
            assert.equal(await evaluate("document.querySelectorAll('#blazor-error-ui[style*=block]').length"),0);
            if(!w.writable) assert((await evaluate('document.body.innerText')).includes('Duplicate'));
        }
        assert.equal(total,828); assert.equal(writable,27);
        await support('MS-11 Solo Silo');
        assert((await evaluate('document.body.innerText')).toLowerCase().includes('hellpod'));
        await screenshot('runtime0201-solo-silo');
        console.log('PASS: 35 support views, 27 writable identities, all 828 controls, eight blocked identities, shared scopes, reset and Changes');
    }
    for(const [name,,edits] of samples) {
        await openProject(name); const result=await lua(); assert(result.includes('hd2.support_weapon(')); assert(!result.includes('Build blocked'));
        if(edits.length>1)assert(result.includes('plan={')); await build();
    }
    if(process.argv.includes('--players')) {
        const catalog=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/PlayerWeaponAuthoringCapabilities.json','utf8'));
        await nav('Player Weapons'); let modes=0,terminals=0,options=0,heat=0;
        for(const w of catalog.weapons) {
            await choose(w.name);
            modes+=await evaluate("document.querySelectorAll('[data-field=\"weapon.default_fire_mode\"] select').length");
            terminals+=await evaluate("document.querySelectorAll('[data-terminal] select').length");
            options+=await evaluate("document.querySelectorAll('[data-attachment]').length");
            heat+=await evaluate("document.querySelectorAll('[data-section=\"Heat / Heatsink\"] input[type=number]').length");
            assert.equal(await evaluate("document.querySelectorAll('[data-section=Attachments] input, [data-section=Attachments] select').length"),0);
            assert.equal(await evaluate("document.querySelectorAll('#blazor-error-ui[style*=block]').length"),0);
        }
        assert.equal(modes,21);assert.equal(terminals,130);assert.equal(options,419);assert.equal(heat,30);
        console.log('PASS: 80 player views, 21 fire modes, 130 terminal selectors, 419 read-only attachments, 30 heat controls');
    }
    console.log('PASS: four support samples edited, built and reloaded through the desktop GUI');
} finally { socket.close(); }
