// Export smoke (packaged app): a new project on the bundled SDK 0.28.0 with a weapon edit and custom Lua, exported in English and again
// after switching to 简体中文. The two ZIPs must be byte-identical (exports never depend on the UI language). The runner then validates
// the export with HD2Runtime 0.28.0 (tools/validate-exports.py).
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import { run } from './smoke-cdp.mjs';

const hash = async file => crypto.createHash('sha256').update(await fs.readFile(file)).digest('hex');
await run('export', async (ui, report) => {
    await ui.startup();
    assert((await ui.evaluate("document.querySelector('.sdk-mini')?.innerText ?? ''")).includes('SDK 0.28.0'), 'the bundled SDK 0.28.0 is current');
    await ui.createProject('Export Smoke');
    await ui.go('player-weapons'); await ui.chooseWeapon('AR-23 Liberator'); await ui.setField('weapon.fire_rate', '720');
    await ui.go('scripting'); await ui.click('[data-custom-lua-add]'); await ui.waitFor(ui.q('[data-lua-input]'), 'custom Lua editor');
    const exportOnce = async () => {
        await ui.go('export'); await ui.click('section.panel button.primary');
        await ui.waitFor("!!document.querySelector('.export-success')", 'export'); await ui.idle();
        return ui.evaluate("document.querySelector('.export-success code.path').innerText");
    };
    const english = await exportOnce(); const englishHash = await hash(english);
    const lua = await ui.lua();
    assert(lua.includes("hd2.weapon('AR-23 Liberator')") && lua.includes('pcall(build)'), 'generated Lua');
    await ui.setLanguage('zh-Hans');
    const chinese = await exportOnce();
    assert.equal(chinese, english, 'same export path');
    assert.equal(await hash(chinese), englishHash, 'the export ZIP is byte-identical in both UI languages');
    await ui.setLanguage('en');
    report.export = english; report.sha256 = englishHash;
    await ui.screenshot('export-smoke');
});
