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
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    if (process.argv.includes('--relaunch')) {
        await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('Jar5VerdictProjectile')).click()");
        await sleep(300); await nav('Player Weapons'); await choose('JAR-5 Dominator');
        assert((await evaluate("document.querySelector('[data-projectile=primary]').innerText")).includes('P-113 Verdict'));
        assert(await evaluate("document.querySelector('[data-projectile=primary]').classList.contains('modified')"));
        await nav('Changes'); await expand('JAR-5 Dominator'); assert.equal(await evaluate("document.querySelectorAll('[data-projectile-change]').length"),1);
        await nav('Player Weapons'); await choose('LAS-5 Scythe');
        await evaluate("document.querySelector('[data-inspection=magazine] summary').click()");
        assert((await evaluate("document.querySelector('[data-inspection=magazine]').innerText")).includes('Laser Heatsink. Medium Standard'));
        assert((await evaluate("document.querySelector('[data-inspection=magazine]').innerText")).includes('not proven'));
        assert.equal(await evaluate("document.querySelectorAll('[data-inspection=magazine] input, [data-inspection=magazine] select').length"),0);
        console.log('PASS: semantic projectile override survived desktop relaunch');
        if (process.argv.includes('--catalog')) {
            const catalog = JSON.parse(await fs.readFile('HD2RuntimeGUI.Core/Metadata/Bundled/PlayerWeaponAuthoringCapabilities.json','utf8'));
            let attacks=0,writable=0;
            for (const weapon of catalog.weapons) {
                await choose(weapon.name);
                attacks += await evaluate("document.querySelectorAll('[data-projectile]').length");
                writable += await evaluate("document.querySelectorAll('[data-projectile] select').length");
                assert.equal(await evaluate("document.querySelectorAll('[data-inspection=fire-mode]').length"),1);
                assert.equal(await evaluate("document.querySelectorAll('[data-inspection] input, [data-inspection] select').length"),0);
                assert.equal(await evaluate("document.querySelectorAll('.dialog-error, #blazor-error-ui[style*=block]').length"),0);
            }
            assert.equal(attacks,catalog.summary.composition.projectile.projectileAttacks);
            assert.equal(writable,catalog.summary.composition.projectile.writableTargetAttacks);
            console.log(`PASS: rendered all ${catalog.weapons.length} weapons, ${attacks} projectile selectors, ${writable} writable; all inspection controls read-only`);
        }
    } else {
        await click('+ Create New Mod'); await waitFor("!!document.querySelector('#mod-name')", 'create dialog');
        await fill('#mod-name','Jar5VerdictProjectile'); await fill('#author','SkyeShade'); await fill('#resource-id','mods/skyeshade/jar5_verdict_projectile');
        await fill('#description','Guarded JAR-5 primary projectile replacement with the P-113 Verdict conventional projectile.');
        await click('Create project →'); await waitFor("document.body.innerText.includes('Project overview')", 'project saved');
        await nav('Player Weapons'); await choose('JAR-5 Dominator');
        assert.equal(await evaluate("document.querySelectorAll('[data-field*=\"attack.\"]').length"),0);
        assert.equal(await evaluate("document.querySelector('[data-projectile=primary] select').options.length"),45);
        await fill('[data-projectile=primary] select','P-113 Verdict|primary');
        await waitFor("document.querySelector('[data-projectile=primary]').classList.contains('modified')", 'reference autosave');
        await nav('Changes'); await expand('JAR-5 Dominator');
        assert.equal(await evaluate("document.querySelectorAll('[data-projectile-change]').length"),1);
        assert((await evaluate("document.querySelector('[data-projectile-change]').innerText")).includes('P-113 Verdict'));
        await screenshot('composition-projectile-changes');
        await nav('Player Weapons'); await choose('JAR-5 Dominator'); await fill('[data-projectile=primary] select','');
        await waitFor("!document.querySelector('[data-projectile=primary]').classList.contains('modified')", 'return to original');
        await fill('[data-projectile=primary] select','P-113 Verdict|primary'); await sleep(300);
        await screenshot('composition-projectile-editor');
        await nav('Lua Preview'); const lua=await evaluate('document.querySelector("pre").innerText');
        assert(lua.includes("expect=hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile()"));
        assert(lua.includes("value=hd2.weapon('P-113 Verdict'):attack('primary'):projectile()"));
        assert(!lua.includes('177')); assert(!lua.includes('offset'));
        await click('↗ Build / Export Mod'); await waitFor("document.body.innerText.includes('Export complete')", 'projectile export');
        console.log('GUI ZIP: '+await evaluate("document.querySelector('.export-success code').innerText"));
        await nav('Player Weapons'); await choose('SG-20 Halt');
        assert.equal(await evaluate("document.querySelectorAll('[data-projectile=feed_primary] select').length"),1);
        assert.equal(await evaluate("document.querySelectorAll('[data-projectile=feed_alternate] select').length"),0);
        assert((await evaluate("document.querySelector('[data-projectile=feed_alternate]').innerText")).includes('only conventional_plain'));
        await screenshot('composition-halt-guarded-feeds');
        await choose('CB-9 Exploding Crossbow'); assert.equal(await evaluate("document.querySelectorAll('[data-projectile] select').length"),0);
        await evaluate("document.querySelector('[data-inspection=terminal] summary').click()");
        assert((await evaluate("document.querySelector('[data-inspection=terminal]').innerText")).includes('ExplosionSettings · 59'));
        assert.equal(await evaluate("document.querySelectorAll('[data-inspection=terminal] input, [data-inspection=terminal] select').length"),0);
        await screenshot('composition-crossbow-terminal');
        await choose('AR-23 Liberator'); await evaluate("document.querySelector('[data-inspection=magazine] summary').click()");
        assert((await evaluate("document.querySelector('[data-inspection=magazine]').innerText")).includes('Rifle 5,5x50mm. Extended'));
        assert.equal(await evaluate("document.querySelectorAll('[data-inspection=magazine] input, [data-inspection=magazine] select').length"),0);
        await choose('JAR-5 Dominator'); await evaluate("document.querySelector('[data-inspection=fire-mode] summary').click()");
        assert((await evaluate("document.querySelector('[data-inspection=fire-mode]').innerText")).includes('Native mode: 2'));
        assert.equal(await evaluate("document.querySelectorAll('[data-inspection=fire-mode] input, [data-inspection=fire-mode] select').length"),0);
        await nav('Support Weapons'); assert((await evaluate('document.body.innerText')).includes('Awaiting a published SDK catalog'));
        assert.equal(await evaluate("document.querySelectorAll('.workspace input, .workspace select').length"),0);
        await screenshot('composition-support-contract');
        await nav('Project library'); console.log('PASS: guarded selectors, source filtering, autosave/reset, Changes, Lua, ZIP, read-only composition and missing support contract');
    }
} finally { socket.close(); }
