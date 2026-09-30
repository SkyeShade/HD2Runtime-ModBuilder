// Old-project compatibility smoke (packaged app). scripts/run-rc-smokes.ps1 seeds the data folder like a ModBuilder 1.3.1 user's:
// SDK 0.27.0 cached and current, and the seven projects 1.3.1 saved (HD2RuntimeGUI.Tests/Fixtures/projects-1.3.1). The 1.4.0 app
// must adopt its bundled SDK 0.28.0 for new projects, open every old project on 0.27.0 with its Lua isolated per operation, rebind one to
// 0.28.0 explicitly and export it.
import assert from 'node:assert/strict';
import { run } from './smoke-cdp.mjs';

const projects = ['Weapon Rebalance', 'Projectile Swap', 'Support Weapon', 'Stratagems', 'Vehicle Backpack', 'Halt Edits', 'Booster Throwable'];
await run('old-project', async (ui, report) => {
    await ui.startup();
    report.sdk = await ui.evaluate("document.querySelector('.sdk-mini')?.innerText ?? ''");
    assert(report.sdk.includes('SDK 0.28.0'), 'the bundled 0.28.0 SDK is current after the upgrade');
    report.opened = [];
    for (const name of projects) {
        await ui.openProject(name);
        const facts = await ui.text();
        assert(facts.includes('0.27.0'), name + ' stays on SDK 0.27.0');
        assert.equal(await ui.count('[data-rebind-panel]'), 1, name + ' offers the rebind to 0.28.0');
        assert.equal(await ui.evaluate(`[...document.querySelectorAll('[role=alert]')].filter(e => e.offsetParent).length`), 0, name + ' builds without review items');
        const lua = await ui.lua();
        assert(lua.includes('local function add(build)') && lua.includes('pcall(build)'), name + ': operations are isolated');
        report.opened.push({ name, operations: (lua.match(/^add\(function\(\) return /gm) ?? []).length });
    }
    // An explicit rebind to 0.28.0 keeps the edits and exports a mod requiring 0.28.0.
    await ui.openProject('Halt Edits');
    await ui.click('[data-rebind]'); await ui.idle(); await ui.sleep(600);
    assert.equal(await ui.count('[data-rebind-panel]'), 0, 'rebound');
    const lua = await ui.lua();
    assert(lua.includes("hd2.weapon('SG-20 Halt')") && lua.includes("hd2.weapon('AR-23 Liberator')") && lua.includes("hd2.weapon('SMG-32 Reprimand')"), 'Halt and unrelated edits kept');
    await ui.go('export'); await ui.clickText('button.primary', 'Build'); await ui.waitFor("!!document.querySelector('.export-success')", 'export');
    report.export = await ui.evaluate("document.querySelector('.export-success code.path').innerText");
    await ui.screenshot('old-project-export');
});
