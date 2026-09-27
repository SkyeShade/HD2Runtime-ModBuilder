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
const nav = async label => { await waitFor(`[...document.querySelectorAll('.nav-item')].some(b=>b.innerText.includes(${JSON.stringify(label)}))`,label+' navigation'); await evaluate(`[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes(${JSON.stringify(label)})).click()`); await sleep(150); };
const choose = async weapon => { await fill('#weapon-search',weapon); await evaluate(`document.querySelector('[data-weapon='+CSS.escape(${JSON.stringify(weapon)})+']').click()`); await sleep(150); };
const fieldClick = async (field,label) => { await evaluate(`[...document.querySelector('[data-field="${field}"]').querySelectorAll('button')].find(b=>b.innerText===${JSON.stringify(label)}).click()`); await sleep(200); };
const group = name => `[data-weapon-group="${name}"]`;
const expand = async name => { await evaluate(`document.querySelector(${JSON.stringify(group(name)+' > summary')}).click()`); await sleep(100); };
const state = async field => evaluate(`({modified:document.querySelector('[data-field="${field}"]').classList.contains('modified'),text:document.querySelector('[data-field="${field}"] .value-comparison').innerText})`);
const create = async name => {
    await nav('Project library'); await click('+ Create New Mod'); await waitFor("!!document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name',name); await fill('#author','SkyeShade'); await fill('#resource-id','mods/skyeshade/'+name.toLowerCase());
    await click('Create project →'); await waitFor("document.body.innerText.includes('Project overview')",'saved project'); await nav('Player Weapons');
};
const build = async () => { await click('↗ Build / Export Mod'); await waitFor("document.body.innerText.includes('Export complete')",'export'); console.log('GUI ZIP: '+await evaluate("document.querySelector('.export-success code').innerText")); };
const openProject = async name => { await nav('Project library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')",'open '+name); };
const approve = async (domain, value=true) => {
    const selector=`[data-scope-domain="${domain}"] input[type=checkbox]`;
    await evaluate(`(()=>{const input=document.querySelector(${JSON.stringify(selector)});if(!input)throw new Error('Missing shared approval scope');if(input.checked!==${value})input.click();})()`); await sleep(200);
};
const lua = async () => { await nav('Lua Preview'); return evaluate('document.querySelector("pre").innerText'); };
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    if(!process.argv.includes('--relaunch')) {
        const weapon='AR-23C Liberator Concussive'; await create('ConcussiveComposition019'); await choose(weapon);
        await fill('[data-field="weapon.fire_rate"] input[type=number]','1100');
        for(const [id,value] of [['damage.push_force','30'],...['direct','slight','large','extreme'].map(l=>['damage.ap_'+l,'3'])])
            await fill(`[data-object-field="${id}"] input[type=number]`,value);
        assert.equal(await evaluate("document.querySelectorAll('[data-scope-domain=damage] input[type=checkbox]').length"),1);
        assert.equal(await evaluate("document.querySelectorAll('[data-object-field] input[type=checkbox]').length"),0);
        await approve('damage');
        for(const phase of ['impact','expiry']) await fill(`[data-terminal=${phase}] select`,'R-36 Eruptor|primary|impact');
        assert((await evaluate('document.body.innerText')).includes('Build requires review'),'damage approval must not authorize terminal scope');
        assert.equal(await evaluate("document.querySelectorAll('[data-scope-domain=terminal] input[type=checkbox]').length"),1);
        await approve('terminal');
        await waitFor("!document.body.innerText.includes('Build requires review')",'approved plan buildable');
        await approve('damage',false); assert((await evaluate('document.body.innerText')).includes('Build requires review')); await approve('damage');
        await screenshot('runtime019-shared-scopes');
        await nav('Changes'); await expand(weapon);
        assert.equal(await evaluate("document.querySelectorAll('[data-composition-change]').length"),7);
        assert.equal(await evaluate("document.querySelectorAll('[data-change]').length"),1);
        await screenshot('runtime019-concussive-changes');
        const result=await lua(); assert.equal(result.split('plan={').length-1,1); assert.equal(result.split('hd2.ensure(').length-1,2);
        assert(result.includes("terminal_action('impact')") && result.includes("terminal_action('expiry')"));
        assert(!result.includes('Build blocked')); await screenshot('runtime019-plan-preview'); await build();

        await create('ProjectileSwapAndTune019'); await choose('P-113 Verdict');
        await fill('[data-projectile=primary] select','JAR-5 Dominator|primary');
        assert.equal(await evaluate("document.querySelector('[data-projectile-object]').dataset.projectileObject"),'JAR-5 Dominator');
        await fill('[data-object-field="projectile.velocity"] input[type=number]','350');
        await fill('[data-object-field="projectile.drag"] input[type=number]','0.2');
        await approve('projectile');
        const swap=await lua(); assert(swap.includes('phases={')); assert(swap.includes('target_from={')); assert(swap.includes("path='projectile'"));
        assert(swap.includes('expect=180,value=350')); assert(swap.includes('expect=0,value=0.2')); assert.equal(swap.split('hd2.ensure(').length-1,1);
        await screenshot('runtime019-swap-plan'); await build();
    }
    for(const name of ['ConcussiveComposition019','ProjectileSwapAndTune019']) {
        await openProject(name); const result=await lua(); assert(result.includes('plan={')); assert(!result.includes('Build blocked')); await build();
    }
    if(process.argv.includes('--catalog')) {
        const catalog=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/PlayerWeaponAuthoringCapabilities.json','utf8'));
        await nav('Player Weapons'); let modes=0, terminals=0, options=0, heat=0;
        for(const w of catalog.weapons) {
            await choose(w.name);
            modes+=await evaluate("document.querySelectorAll('[data-field=\"weapon.default_fire_mode\"] select').length");
            terminals+=await evaluate("document.querySelectorAll('[data-terminal] select').length");
            options+=await evaluate("document.querySelectorAll('[data-attachment]').length");
            heat+=await evaluate("document.querySelectorAll('[data-section=\"Heat / Heatsink\"] input[type=number]').length");
            assert.equal(await evaluate("document.querySelectorAll('[data-section=Attachments] input, [data-section=Attachments] select').length"),0);
            assert.equal(await evaluate("document.querySelectorAll('.dialog-error, #blazor-error-ui[style*=block]').length"),0);
        }
        assert.equal(modes,21); assert.equal(terminals,130); assert.equal(options,419); assert.equal(heat,30);
        await nav('Support Weapons'); assert((await evaluate('document.body.innerText')).includes('35 support weapons'));
        const support=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/SupportWeaponCapabilities.json','utf8'));
        for(const name of Object.keys(support.weapons)) {
            await fill('#support-search',name); await evaluate(`document.querySelector('[data-support='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(100);
            assert.equal(await evaluate("document.querySelectorAll('.weapon-detail input, .weapon-detail select').length"),0);
        }
        console.log('PASS: 80 player views, 21 fire-mode controls, 130 terminal selectors, 419 read-only attachments, 30 heat controls, 35 read-only support graphs');
    }
    console.log('PASS: Concussive coordinated plan; per-scope approvals; swap target_from phases; Changes, preview, ZIP and reload');
} finally { socket.close(); }
