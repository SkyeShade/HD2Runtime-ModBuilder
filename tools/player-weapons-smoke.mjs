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
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(100); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
const nav = async label => { await evaluate(`[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes(${JSON.stringify(label)})).click()`); await sleep(180); };
const choose = async weapon => {
    await evaluate(`(()=>{const e=document.querySelector('#weapon-search');e.value=${JSON.stringify(weapon)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    await sleep(150);
    await evaluate(`document.querySelector('[data-weapon='+CSS.escape(${JSON.stringify(weapon)})+']').click()`); await sleep(150);
};
const saveField = async (field,value) => {
    await fill(`[data-field="${field}"] input[type=number]`,value);
    await waitFor(`document.querySelector('[data-field="${field}"]').classList.contains('modified')`,'saved '+field);
    await waitFor("!document.querySelector('.stat-card [role=status]')",'autosave complete');
    assert(!await evaluate("!!document.querySelector('.dialog-error')"));
};
try {
    await waitFor("!!document.querySelector('.desktop-shell')",'startup');
    await waitFor("!document.querySelector('.activity')",'SDK check');
    if(process.argv.includes('--relaunch')) {
        const text=await evaluate('document.body.innerText');
        for(const name of ['Concussive1100','VerdictFlatTrajectory','ReprimandFlatTrajectory']) assert(text.includes(name));
        await screenshot('player-project-library'); console.log('PASS: three projects survive desktop relaunch');
    } else if(process.argv.includes('--safety')) {
        await nav('Project library');
        await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('Concussive1100')).click()"); await sleep(200);
        await nav('Player Weapons'); await choose('AR-23 Liberator');
        await saveField('projectile.drag','0.1');
        assert((await evaluate('document.body.innerText')).includes('Build requires review'));
        await nav('Lua Preview'); assert((await evaluate('document.querySelector("pre").innerText')).includes('Build blocked'));
        await nav('Player Weapons'); await choose('AR-23 Liberator');
        await evaluate("document.querySelector('[data-field=\"projectile.drag\"] .shared-warning input').click()");
        await saveField('projectile.drag','0.1'); await nav('Lua Preview');
        assert((await evaluate('document.querySelector("pre").innerText')).includes('allow_shared=true'));
        await nav('Player Weapons'); await choose('AR-23 Liberator'); await screenshot('shared-write-approved'); await click('Reset weapon');
        await choose('LAS-5 Scythe');
        assert((await evaluate('document.querySelector(".weapon-detail").innerText')).includes('Ordinary writes unavailable'));
        assert.equal(await evaluate("document.querySelectorAll('.weapon-detail input[type=number]').length"),0);
        await screenshot('duplicate-identity-blocked');
        for(const name of ['Concussive1100','VerdictFlatTrajectory','ReprimandFlatTrajectory']) {
            await nav('Project library');
            await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await sleep(150);
            await click('↗ Build / Export Mod'); await waitFor("document.body.innerText.includes('Export complete')",'final export');
            console.log('Final ZIP: '+await evaluate("document.querySelector('.export-success code').innerText"));
        }
        await nav('Snapshot Research'); assert((await evaluate('document.body.innerText')).includes('Load / relink .hd2snap'));
        await screenshot('snapshot-browser');
        console.log('PASS: shared approval gates build, duplicate writes blocked, reset restores sample, snapshot UI available');
    } else {
        if(!process.argv.includes('--families')) for(const [name,weapon] of [['Concussive1100','AR-23C Liberator Concussive'],['VerdictFlatTrajectory','P-113 Verdict'],['ReprimandFlatTrajectory','SMG-32 Reprimand']]) {
            await nav('Project library'); await click('+ Create New Mod');
            await waitFor("!!document.querySelector('#mod-name')",'new project');
            await fill('#mod-name',name); await fill('#author','SkyeShade'); await fill('#resource-id','mods/skyeshade/'+name.toLowerCase());
            await click('Create project →'); await waitFor("!!document.querySelector('[data-overview]')",'project saved');
            await nav('Player Weapons'); await choose(weapon);
            if(name==='Concussive1100') await saveField('weapon.fire_rate','1100');
            else { await saveField('projectile.drag','0.1'); await saveField('projectile.gravity','0.2'); }
            await screenshot(name+'-editor'); await nav('Changes');
            const lua=await evaluate('document.querySelector("pre").innerText');
            assert(lua.includes('hd2.ensure(')); assert(lua.includes(`hd2.weapon('${weapon}')`));
            assert(!lua.includes('allow_shared'));
            await screenshot(name+'-changes'); await click('↗ Build / Export Mod');
            await waitFor("document.body.innerText.includes('Export complete')",'export');
            const path=await evaluate("document.querySelector('.export-success code').innerText");
            assert((await fs.stat(path)).size>0); console.log('PASS: GUI authoring, preview and export: '+path);
            await click('Open Export Folder ↗'); assert(!await evaluate("!!document.querySelector('.notice.error')"));
        }
        await nav('Player Weapons');
        for(const [weapon,section] of [['ARC-12 Blitzer','Arc'],['LAS-13 Trident','Beam'],['FLAM-66 Torcher','Damage'],['CQC-19 Stun Lance','Damage'],['SG-8 Punisher','Ammo / Feed']]) {
            // Names are test cases; the editor and generation use only the catalog.
            await evaluate("document.querySelector('#weapon-search').value='';document.querySelector('#weapon-search').dispatchEvent(new Event('input',{bubbles:true}))"); await sleep(100);
            if(!await evaluate(`!!document.querySelector('[data-weapon='+CSS.escape(${JSON.stringify(weapon)})+']')`)) throw new Error('Required family test weapon unavailable: '+weapon);
            await choose(weapon); assert((await evaluate('document.querySelector(".weapon-detail").innerText')).includes(section));
            await screenshot('family-'+section.replaceAll(' / ','-').toLowerCase());
            if(section==='Arc'||section==='Beam') assert(!await evaluate("!!document.querySelector('[data-field=\"projectile.drag\"]')"));
            console.log('PASS: family rendering '+weapon);
        }
    }
} finally { socket.close(); }
