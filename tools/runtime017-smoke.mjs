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
const projectileScalarRegression = async () => {
    const catalog=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/PlayerWeaponAuthoringCapabilities.json','utf8'));
    const weapon='P-113 Verdict', source='JAR-5 Dominator';
    const root='[data-attack=primary] [data-projectile-object]';
    const card=id=>root+' [data-object-field="'+id+'"]';
    const scalar=(name,id)=>catalog.weapons.find(w=>w.name===name).fields.find(f=>f.semanticFieldId===id);
    const assertFields=async name=> {
        assert.equal(await evaluate(`document.querySelector(${JSON.stringify(root)}).dataset.projectileObject`),name);
        const fields=catalog.weapons.find(w=>w.name===name).fields.filter(f=>f.preferred && f.editable && ['projectile','damage'].includes(f.semanticFieldId.split('.')[0]) && f.backing?.branch==='primary');
        for(const field of fields) {
            const selector=card(field.semanticFieldId);
            const state=await evaluate(`(()=>{const el=document.querySelector(${JSON.stringify(selector)});const input=el?.querySelector('input[type=number]');return {visible:!!input?.getClientRects().length,editable:!!input&&!input.disabled,value:input?.value,section:el?.closest('[data-object-section]')?.dataset.objectSection};})()`);
            assert(state.visible && state.editable,field.semanticFieldId+' must be visibly editable without expanding a disclosure');
            assert.equal(Math.fround(Number(state.value)),Math.fround(field.currentDefault));
            assert.equal(state.section,field.semanticFieldId.split('.')[0]);
            assert.equal(await evaluate(`document.querySelectorAll('[data-field="${field.semanticFieldId}"]').length`),0,'no weapon-local duplicate');
        }
    };
    await create('ProjectileScalarRegression'); await choose(weapon); await assertFields(weapon);
    // An ordinary baseline projectile remains authorable without any reference swap.
    await fill(card('projectile.velocity')+' input[type=number]','300');
    await waitFor("document.body.innerText.includes('Build requires review')",'shared projectile gate');
    await evaluate(`document.querySelector(${JSON.stringify(card('projectile.velocity')+' input[type=checkbox]')}).click()`);
    await waitFor("!document.body.innerText.includes('Build requires review')",'shared projectile approval');
    await nav('Changes'); await expand(weapon);
    assert.equal(await evaluate("document.querySelectorAll('[data-composition-changes] [data-composition-change=\"projectile.velocity\"]').length"),1);
    await nav('Player Weapons'); await choose(weapon);
    await evaluate(`document.querySelector(${JSON.stringify(card('projectile.velocity')+' .reset-action')}).click()`); await sleep(200);
    await assertFields(weapon);
    await fill('[data-projectile=primary] select',source+'|primary'); await assertFields(source);
    for(const id of ['projectile.velocity','projectile.drag','damage.standard_damage','damage.ap_direct']) {
        await fill(card(id)+' input[type=number]',String(scalar(source,id).currentDefault+1));
        await evaluate(`(()=>{const input=document.querySelector(${JSON.stringify(card(id)+' input[type=checkbox]')});if(!input.checked)input.click();})()`);
        await waitFor("document.body.innerText.includes('Composition dependency')",id+' dependent operation blocked');
    }
    await nav('Changes'); await expand(weapon);
    assert.equal(await evaluate("document.querySelectorAll('[data-composition-changes] [data-composition-change]').length"),4);
    assert((await evaluate("document.querySelector('[data-composition-changes]').innerText")).includes('Damage'));
    await nav('Lua Preview'); assert((await evaluate('document.querySelector("pre").innerText')).includes('Build blocked'));
    await nav('Player Weapons'); await choose(weapon);
    for(const id of ['projectile.velocity','projectile.drag','damage.standard_damage','damage.ap_direct'])
        await fill(card(id)+' input[type=number]',String(scalar(source,id).currentDefault));
    assert.equal(await evaluate("document.querySelectorAll('[data-object-field].modified').length"),0);
    await openProject('ProjectileScalarRegression'); await nav('Player Weapons'); await choose(weapon); await assertFields(source);
    await nav('Changes'); await expand(weapon);
    assert.equal(await evaluate("document.querySelectorAll('[data-composition-change]').length"),0);
    assert.equal(await evaluate("document.querySelectorAll('[data-projectile-change]').length"),1);
    await nav('Player Weapons'); await choose(weapon);
    await evaluate("document.querySelector('[data-projectile-object]').scrollIntoView({block:'start'})");
    await screenshot('runtime017-projectile-scalars-visible');
    console.log('PASS: visible baseline/replacement physics, damage and AP; shared gating; Composition summary; dependency blocking; reset/reload');
};
const groupingRegression = async () => {
    const weapon='AR-23C Liberator Concussive'; await create('ConcussiveGrouping'); await choose(weapon);
    await fill('[data-field="weapon.fire_rate"] input[type=number]','1100');
    await fill('[data-object-field="damage.push_force"] input[type=number]','30');
    await waitFor("document.body.innerText.includes('Build requires review')",'DamageInfo approval');
    await evaluate("document.querySelector('[data-object-field=\"damage.push_force\"] input[type=checkbox]').click()");
    for(const lane of ['direct','slight','large','extreme']) {
        await fill(`[data-object-field="damage.ap_${lane}"] input[type=number]`,'3');
        assert(await evaluate(`document.querySelector('[data-object-field="damage.ap_${lane}"] input[type=checkbox]').checked`),'one object approval covers sibling '+lane);
    }
    await waitFor("!document.body.innerText.includes('Build requires review')",'grouped approval');
    await nav('Lua Preview'); let lua=await evaluate('document.querySelector("pre").innerText');
    assert.equal(lua.split('hd2.ensure(').length-1,2); assert.equal(lua.split('transaction={').length-1,1); assert(lua.includes('changes={')); await build();
    await nav('Player Weapons'); await choose(weapon);
    const source=await evaluate("[...document.querySelector('[data-terminal=impact] select').options].find(o=>o.text.includes('R-36 Eruptor')).value");
    await fill('[data-terminal=impact] select',source);
    await evaluate("document.querySelector('[data-terminal=impact] input[type=checkbox]').click()");
    await waitFor("!document.body.innerText.includes('Build requires review')",'terminal object approval');
    await fill('[data-terminal=expiry] select',source);
    await waitFor("document.body.innerText.includes('Runtime 0.17 transactions have one target')",'unsupported cross-phase transaction blocked');
    assert(await evaluate("document.querySelector('[data-terminal=expiry] input[type=checkbox]').checked"));
    await evaluate("document.querySelector('[data-terminal=expiry] .reset-action').click()");
    await waitFor("!document.body.innerText.includes('Build requires review')",'single terminal permitted');
    await nav('Lua Preview'); lua=await evaluate('document.querySelector("pre").innerText'); assert.equal(lua.split('hd2.ensure(').length-1,3);
    console.log('PASS: exact Concussive grouping; one DamageInfo approval; independent terminal scope; impact+expiry fails closed');
};
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    if (process.argv.includes('--grouping')) { await groupingRegression(); }
    else if (process.argv.includes('--projectile-scalars')) { await projectileScalarRegression(); }
    else {
    if (!process.argv.includes('--relaunch')) {
        await create('FireModeSample'); await choose('AR-23C Liberator Concussive');
        await fill('[data-field="weapon.default_fire_mode"] select','2'); await nav('Changes'); await expand('AR-23C Liberator Concussive');
        assert((await evaluate("document.querySelector('[data-change=\"weapon.default_fire_mode\"]').innerText")).includes('Full Auto'));
        assert((await evaluate("document.querySelector('[data-change=\"weapon.default_fire_mode\"]').innerText")).includes('Semi Auto'));
        await nav('Lua Preview'); assert((await evaluate('document.querySelector("pre").innerText')).includes('hd2.enums.fire_mode.semi_auto')); await build();

        await create('TerminalExplosionSample'); await choose('R-36 Eruptor');
        await fill('[data-terminal=expiry] select','none'); await nav('Changes'); await expand('R-36 Eruptor');
        assert((await evaluate("document.querySelector('[data-composition-change=\"terminal.expiry\"]').innerText")).includes('None'));
        await nav('Lua Preview'); assert((await evaluate('document.querySelector("pre").innerText')).includes(':no_explosion()')); await build();

        await create('ExplosionTuningSample'); await choose('R-36 Eruptor');
        await evaluate("document.querySelector('[data-explosion=impact] > summary').click()");
        await fill('[data-explosion=impact] [data-object-field="explosion.primary.impact.outer_radius"] input[type=number]','10');
        await fill('[data-explosion=impact] [data-object-field="explosion.primary.impact.damage.standard_damage"] input[type=number]','500');
        await nav('Changes'); await expand('R-36 Eruptor'); assert.equal(await evaluate("document.querySelectorAll('[data-composition-change]').length"),2); await build();

        await create('ProjectileCompositionSample'); await choose('P-113 Verdict');
        assert(!(await evaluate("[...document.querySelector('[data-projectile=primary] select').options].map(o=>o.text).join('|')")).includes('LAS-58 Talon'));
        await fill('[data-projectile=primary] select','JAR-5 Dominator|primary');
        assert((await evaluate("document.querySelector('[data-projectile=primary]').innerText")).includes('SELF CONTAINED'));
        await fill('[data-object-field="projectile.velocity"] input[type=number]','350');
        assert((await evaluate('document.body.innerText')).includes('Build requires review'));
        await evaluate("document.querySelector('[data-object-field=\"projectile.velocity\"] input[type=checkbox]').click()");
        await waitFor("document.body.innerText.includes('Composition dependency')",'dependency cannot be scheduled');
        await screenshot('runtime017-projectile-object');
        await evaluate("document.querySelector('[data-object-field=\"projectile.velocity\"] .reset-action').click()");
        await waitFor("!document.body.innerText.includes('Build requires review')",'replacement-only mod'); await build();
    } else {
        for(const name of ['FireModeSample','TerminalExplosionSample','ExplosionTuningSample','ProjectileCompositionSample']) {
            await openProject(name);
            if(name==='ProjectileCompositionSample' && (await evaluate('document.body.innerText')).includes('Composition dependency')) { console.log('PASS: earlier unsafe composition sample remains saved and is blocked pending review'); continue; }
            assert(!(await evaluate('document.body.innerText')).includes('Build requires review')); await nav('Lua Preview');
            assert(!(await evaluate('document.querySelector("pre").innerText')).includes('No enabled modifications'));
            await build();
        }
        console.log('PASS: all four new change types survived application restart');
    }
    if (process.argv.includes('--catalog')) {
        const catalog=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/PlayerWeaponAuthoringCapabilities.json','utf8'));
        await nav('Player Weapons'); let modes=0,terminals=0,options=0;
        for(const w of catalog.weapons) {
            await choose(w.name);
            modes+=await evaluate("document.querySelectorAll('[data-field=\"weapon.default_fire_mode\"] select').length");
            terminals+=await evaluate("document.querySelectorAll('[data-terminal] select').length");
            options+=await evaluate("document.querySelectorAll('[data-attachment]').length");
            assert.equal(await evaluate("document.querySelectorAll('[data-section=Attachments] input, [data-section=Attachments] select').length"),0);
            assert.equal(await evaluate("document.querySelectorAll('.dialog-error, #blazor-error-ui[style*=block]').length"),0);
        }
        assert.equal(modes,21); assert.equal(terminals,130); assert.equal(options,419);
        console.log(`PASS: all 80 player views render ${modes} writable fire-mode controls, ${terminals} terminal controls, ${options} read-only attachments`);
    }
    await nav('Player Weapons'); await choose('JAR-5 Dominator');
    assert.equal(await evaluate("document.querySelectorAll('[data-field=\"weapon.default_fire_mode\"] select').length"),0);
    await choose('AR-23C Liberator Concussive');
    await evaluate("document.querySelector('[data-attachment-category=Magazine] > summary').click()");
    for (const [name,value] of [['Drum Magazine','60'],['Short Magazine','30'],['Extended Magazine','45']]) {
        await evaluate(`document.querySelector('[data-attachment='+CSS.escape(${JSON.stringify(name)})+'] > summary').click()`);
        assert((await evaluate(`document.querySelector('[data-attachment='+CSS.escape(${JSON.stringify(name)})+']').innerText`)).includes(value));
    }
    assert.equal(await evaluate("document.querySelectorAll('[data-section=Attachments] input, [data-section=Attachments] select').length"),0);
    await screenshot('runtime017-attachments');
    await nav('Support Weapons'); assert((await evaluate('document.body.innerText')).includes('35 support weapons'));
    if(process.argv.includes('--catalog')) {
        const catalog=JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/SupportWeaponCapabilities.json','utf8'));
        for(const name of Object.keys(catalog.weapons)) {
            await fill('#support-search',name); await evaluate(`document.querySelector('[data-support='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(100);
            assert.equal(await evaluate("document.querySelectorAll('.weapon-detail input, .weapon-detail select').length"),0);
            assert.equal(await evaluate("document.querySelectorAll('#blazor-error-ui[style*=block]').length"),0);
        }
        console.log('PASS: all 35 support weapon graphs render without authoring controls');
    }
    for (const name of ['GR-8 Recoilless Rifle','ARC-3 Arc Thrower','B/MD C4 Pack','MS-11 Solo Silo','RS-422 Railgun']) {
        await fill('#support-search',name); await evaluate(`document.querySelector('[data-support='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(150);
        assert.equal(await evaluate("document.querySelectorAll('.weapon-detail input, .weapon-detail select').length"),0);
        if(name==='MS-11 Solo Silo') assert((await evaluate('document.body.innerText')).includes('HellpodRackComponentData'));
        if(name==='RS-422 Railgun') assert((await evaluate('document.body.innerText')).includes('UNRESOLVED'));
        if(name==='ARC-3 Arc Thrower') await screenshot('runtime017-support-arc');
    }
    await screenshot('runtime017-support-railgun');
    await nav('Project library'); console.log('PASS: 0.17 authoring samples, shared gating, semantic Lua, read-only attachment/support graphs');
    }
} finally { socket.close(); }
