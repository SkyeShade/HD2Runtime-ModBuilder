// Changes-page UI benchmark (packaged app). The app runs with a data folder holding one large project (LargeProjectBenchmark
// .Write_ui_benchmark_data writes them) and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>. Prints one JSON line:
// wall-clock time from the click until the DOM stops changing, plus the browser's own layout/style/script time for each step, so the
// .NET-side render time is the difference.
import { connect } from './smoke-cdp.mjs';

const ui = await connect();
const result = { steps: [] };
try {
    await ui.startup();
    await ui.cdp('Performance.enable');
    const metrics = async () => Object.fromEntries((await ui.cdp('Performance.getMetrics')).metrics.map(m => [m.name, m.value]));
    await ui.evaluate(`window.__last = performance.now(); new MutationObserver(() => { window.__last = performance.now(); })
        .observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true }); true`);
    const measure = async (label, action, ready, quiet = 600) => {
        const before = await metrics(); const t0 = await ui.evaluate('performance.now()');
        await ui.evaluate(action);
        // Done when the step's own post-condition holds, the DOM changed after the click, and it has been quiet since.
        for (let i = 0; ; i++) {
            const s = await ui.evaluate(`({ ready: Boolean(${ready}) && !document.querySelector('.activity'), last: window.__last, now: performance.now() })`);
            if (s.ready && s.last > t0 && s.now - s.last > quiet) break;
            if (i > 3000) throw new Error('timed out: ' + label);
            await ui.sleep(50);
        }
        const last = await ui.evaluate('window.__last'); const after = await metrics();
        const d = k => Math.round((after[k] - before[k]) * 1000);
        const step = { label, ms: Math.round(last - t0), layoutMs: d('LayoutDuration'), styleMs: d('RecalcStyleDuration'), scriptMs: d('ScriptDuration'),
            nodes: after.Nodes, domNodes: await ui.evaluate("document.getElementsByTagName('*').length") };
        result.steps.push(step); return step;
    };
    const click = selector => `(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('missing ' + ${JSON.stringify(selector)}); el.click(); })()`;
    await ui.go('library');
    await measure('open project', `[...document.querySelectorAll('.project-open')].find(e => e.innerText.includes('Large Project')).click()`, "document.querySelector('[data-overview]')");
    await measure('show Changes', click('[data-nav="changes"]'), "document.querySelector('.weapon-summary')");
    result.weaponGroups = await ui.count('[data-weapon-group]'); result.entityGroups = await ui.count('[data-entity-change-group]');
    // What collapsed groups build: their rows and editor inputs (none once groups build their content only while open).
    result.collapsed = { weaponRows: await ui.count('[data-weapon-group]:not([open]) .weapon-change'),
        editorInputs: await ui.count('[data-entity-change-group]:not([open]) input, [data-entity-change-group]:not([open]) select, [data-stratagem-change-group]:not([open]) input, [data-stratagem-change-group]:not([open]) select') };
    // Open one weapon group with edits (native <details> before 1.4.2; its rows are built on open since).
    const groupName = await ui.evaluate(`[...document.querySelectorAll('[data-weapon-group]')].find(g => g.querySelector('summary .modified-badge')).dataset.weaponGroup`);
    const group = `document.querySelector('[data-weapon-group="' + CSS.escape(${JSON.stringify(groupName)}) + '"]')`;
    await measure('open one weapon group', `${group}.querySelector('summary').click()`, `${group}.open && ${group}.querySelector('.weapon-change.modified')`, 400);
    // A save on the Changes page (disable, then re-enable one weapon edit): the path every edit takes, plus the page re-render.
    const target = await ui.evaluate(`(() => { const row = ${group}.querySelector('.weapon-change.modified input[type=checkbox][aria-label]').closest('.weapon-change');
        return { group: row.closest('[data-weapon-group]').dataset.weaponGroup, field: row.dataset.change }; })()`);
    const row = `document.querySelector('[data-weapon-group="' + CSS.escape(${JSON.stringify(target.group)}) + '"] .weapon-change[data-change="' + ${JSON.stringify(target.field)} + '"]')`;
    const toggle = `${row}.querySelector('input[type=checkbox][aria-label]').click()`;
    await measure('toggle one edit off (save + re-render)', toggle, `${row}?.querySelector('input[type=checkbox][aria-label]').checked === false`);
    await measure('toggle it back on', toggle, `${row}?.querySelector('input[type=checkbox][aria-label]').checked === true`);
    // Local UI state only (no save): expand one group's "Show all fields".
    const rowsBefore = await ui.evaluate(`${group}.querySelectorAll('.weapon-change').length`);
    await measure('show all fields in one weapon group', `${group}.querySelector('.switch-label input[type=checkbox]').click()`, `${group}.querySelectorAll('.weapon-change').length > ${rowsBefore}`);
    await measure('leave Changes (Overview)', click('[data-nav="overview"]'), "document.querySelector('[data-overview]')");
    await measure('return to Changes', click('[data-nav="changes"]'), "document.querySelector('.weapon-summary')");
    result.status = 'PASS';
} catch (e) { result.status = 'FAIL'; result.error = String(e?.stack ?? e); }
finally { ui.close(); }
console.log(JSON.stringify(result));
if (result.status !== 'PASS') process.exit(1);
