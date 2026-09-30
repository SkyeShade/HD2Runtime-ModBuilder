// Custom Lua editor: a plain textarea (the text is never changed by the editor except by the user) over a syntax-highlighted layer, with a
// line gutter, diagnostic markers, snippet insertion and autocomplete from the SDK's LuaLS stub index and event catalog.
window.hd2LuaEditor = (() => {
    const editors = new Map();
    const KEYWORDS = new Set(['and', 'break', 'do', 'else', 'elseif', 'end', 'false', 'for', 'function', 'goto', 'if', 'in', 'local', 'nil', 'not', 'or',
        'repeat', 'return', 'then', 'true', 'until', 'while']);
    const CONSTANTS = new Set(['true', 'false', 'nil']);
    const escape = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const span = (cls, text) => '<span class="' + cls + '">' + escape(text) + '</span>';

    function longBracket(text, i) {
        if (text[i] !== '[') return -1;
        let j = i + 1; while (text[j] === '=') j++;
        return text[j] === '[' ? j - i - 1 : -1;
    }
    function closeLong(text, i, level) {
        const close = ']' + '='.repeat(level) + ']'; const at = text.indexOf(close, i);
        return at < 0 ? text.length : at + close.length;
    }
    function highlight(text) {
        let out = '', i = 0;
        while (i < text.length) {
            const c = text[i];
            if (c === '-' && text[i + 1] === '-') {
                let end; const level = longBracket(text, i + 2);
                if (level >= 0) end = closeLong(text, i + 2 + level + 2, level);
                else { end = text.indexOf('\n', i); if (end < 0) end = text.length; }
                out += span('lua-comment', text.slice(i, end)); i = end; continue;
            }
            if (c === '"' || c === "'") {
                let j = i + 1;
                while (j < text.length && text[j] !== c && text[j] !== '\n') j += text[j] === '\\' ? 2 : 1;
                if (text[j] === c) j++;
                out += span('lua-string', text.slice(i, j)); i = j; continue;
            }
            const level = longBracket(text, i);
            if (level >= 0) { const end = closeLong(text, i + level + 2, level); out += span('lua-string', text.slice(i, end)); i = end; continue; }
            if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(text[i + 1] || ''))) {
                const m = /^(0[xX][0-9a-fA-F.]+([pP][+-]?\d+)?|\d*\.?\d+([eE][+-]?\d+)?)[uUlLiI]*/.exec(text.slice(i));
                const n = m ? m[0] : c; out += span('lua-number', n); i += n.length; continue;
            }
            if (/[A-Za-z_]/.test(c)) {
                const m = /^[A-Za-z_]\w*/.exec(text.slice(i))[0];
                const rest = text.slice(i + m.length);
                if (CONSTANTS.has(m)) out += span('lua-constant', m);
                else if (KEYWORDS.has(m)) out += span('lua-keyword', m);
                else if (m === 'hd2') out += span('lua-hd2', m);
                else if (/^\s*[({'"]/.test(rest) || /^\s*\[\[/.test(rest)) out += span('lua-call', m);
                else out += escape(m);
                i += m.length; continue;
            }
            out += escape(c); i++;
        }
        return out + '\n';
    }

    // ---- completion -------------------------------------------------------------------------------------------------------------------
    const firstType = t => (t || '').split('|').map(x => x.trim().replace(/\?$/, '')).find(x => x && x !== 'nil') || '';
    // A call chain: hd2.weapon('AR-23 Liberator'):programmable_ammo(), event.player, sub
    const CHAIN = String.raw`[A-Za-z_]\w*(?:\s*(?:\.\s*[A-Za-z_]\w*|:\s*[A-Za-z_]\w*\s*\([^()\n]*\)|\([^()\n]*\)))*`;
    // The strings a parameter type accepts: every alias it names ("HD2ExplosionName|HD2WeaponName") and its inline literals ("primary"|...).
    function paramValues(type, data) {
        if (!type) return [];
        const values = [];
        for (const part of type.split('|').map(x => x.trim())) {
            if (data.aliases[part]) values.push(...data.aliases[part]);
            else { const lit = /^["'](.*)["']$/.exec(part); if (lit) values.push(lit[1]); }
        }
        return [...new Set(values)];
    }
    // The raw type of a chain's value ('HD2Weapon', 'HD2StatSource[]', 'HD2Explosion|nil'), or null.
    function resolve(chain, vars, data) {
        const parts = chain.match(/[A-Za-z_]\w*|\([^()]*\)|[.:]/g) || [];
        let type = null;
        for (let i = 0; i < parts.length; i++) {
            const p = parts[i];
            if (i === 0) { type = p === 'hd2' ? data.root : vars[p] || null; if (!type) return null; continue; }
            if (p === '.' || p === ':' || p.startsWith('(')) continue;
            const member = (data.classes[firstType(type)] || []).find(m => m.n === p); if (!member) return null;
            type = member.t || '';
        }
        return type;
    }
    function chainType(chain, vars, data) { const t = firstType(resolve(chain, vars, data)); return t && data.classes[t] ? t : null; }
    // Typed locals in the text before the caret: event handler parameters (the nearest handler wins), `local x = <chain>` and
    // `for _, x in ipairs(<chain>)` over an array type (event.sources, hd2.players()).
    function variables(text, data) {
        const vars = {};
        const found = [];
        for (const m of text.matchAll(/(?:\.|:)\s*(?:on|once)\s*\(\s*['"](\w+)['"]\s*,\s*function\s*\(\s*([A-Za-z_]\w*)/g)) {
            const cls = 'HD2Event_' + m[1]; found.push([m.index, () => { vars[m[2]] = data.classes[cls] ? cls : 'HD2Event'; }]);
        }
        for (const m of text.matchAll(new RegExp(String.raw`local\s+([A-Za-z_]\w*)(?:\s*,\s*[A-Za-z_]\w*)*\s*=\s*(` + CHAIN + String.raw`)(?=[ \t]*(?:\r?\n|;|--|$))`, 'g')))
            found.push([m.index, () => { const t = chainType(m[2].replace(/\s+/g, ''), vars, data); if (t) vars[m[1]] = t; }]);
        for (const m of text.matchAll(new RegExp(String.raw`for\s+[A-Za-z_]\w*\s*,\s*([A-Za-z_]\w*)\s+in\s+ipairs\s*\(\s*(` + CHAIN + String.raw`)\s*\)`, 'g')))
            found.push([m.index, () => {
                const raw = firstType(resolve(m[2].replace(/\s+/g, ''), vars, data));
                const element = raw.endsWith('[]') ? raw.slice(0, -2) : null;
                if (element && data.classes[element]) vars[m[1]] = element;
            }]);
        found.sort((a, b) => a[0] - b[0]).forEach(([, apply]) => apply());
        return vars;
    }
    function context(text, caret, data) {
        const before = text.slice(0, caret);
        const strings = [
            [/(?:events\s*\.\s*(?:on|once)|:\s*(?:on|once))\s*\(\s*['"](\w*)$/, data.strings.events],
            [/explosions\s*\.\s*(?:spawn|prepare)\s*\(\s*['"]([^'"\n]*)$/, data.strings.explosions],
            [/explosions\s*\.\s*of\s*\(\s*['"]([^'"\n]*)$/, data.strings.explosionWeapons],
            [/projectiles\s*\.\s*(?:spawn|prepare)\s*\(\s*['"]([^'"\n]*)$/, data.strings.projectiles],
            [/status\s*\.\s*apply\s*\([^,()]*,\s*['"](\w*)$/, data.strings.statuses],
        ];
        for (const [re, list] of strings) { const m = re.exec(before); if (m) return { prefix: m[1], items: (list || []).map(v => ({ n: v, k: 'value' })) }; }
        const vars = variables(before, data);
        // The first string argument of any stub function or method: its parameter's alias or literals (weapon names, attack outputs, feeds ...).
        const call = new RegExp('(' + CHAIN + String.raw`)\s*([.:])\s*([A-Za-z_]\w*)\s*\(\s*['"]([^'"\n]*)$`).exec(before);
        if (call) {
            const type = chainType(call[1].replace(/\s+/g, ''), vars, data);
            const member = type && (data.classes[type] || []).find(m => m.n === call[3]);
            const values = member ? paramValues(member.p, data) : [];
            if (values.length) return { prefix: call[4], items: values.map(v => ({ n: v, k: 'value' })) };
        }
        const m = new RegExp('(' + CHAIN + String.raw`)\s*([.:])\s*([A-Za-z_]\w*)?$`).exec(before);
        if (!m) return null;
        const type = chainType(m[1].replace(/\s+/g, ''), vars, data); if (!type) return null;
        // ':' calls methods; '.' reaches fields and functions (and methods, which Lua also allows through '.').
        const members = (data.classes[type] || []).filter(x => m[2] !== ':' || x.k === 'method');
        return { prefix: m[3] || '', items: members };
    }

    // ---- editor -----------------------------------------------------------------------------------------------------------------------
    function caretPoint(ed) {
        const ta = ed.textarea, mirror = ed.mirror;
        mirror.textContent = ta.value.slice(0, ta.selectionStart);
        const marker = document.createElement('span'); marker.textContent = '​'; mirror.appendChild(marker);
        return { x: marker.offsetLeft - ta.scrollLeft, y: marker.offsetTop - ta.scrollTop + marker.offsetHeight };
    }
    // Typed-in edits (indent, completion, snippets) go through the browser's text insertion so Ctrl+Z / Ctrl+Y undo them like typing.
    function type(ed, text, start, end) {
        const ta = ed.textarea; ta.focus(); ta.setSelectionRange(start, end);
        ed.quiet = true;
        let done = false; try { done = document.execCommand('insertText', false, text); } catch { done = false; }
        ed.quiet = false;
        if (!done) ta.setRangeText(text, start, end, 'end');
        changed(ed);
    }
    function closePopup(ed) { ed.popup.hidden = true; ed.items = []; }
    function openPopup(ed, explicit) {
        const ctx = context(ed.textarea.value, ed.textarea.selectionStart, ed.data);
        if (!ctx) { closePopup(ed); return; }
        const prefix = ctx.prefix.toLowerCase();
        const items = ctx.items.filter(i => i.n.toLowerCase().startsWith(prefix)).slice(0, 60);
        if (items.length === 0 || (!explicit && items.length === 1 && items[0].n === ctx.prefix)) { closePopup(ed); return; }
        ed.items = items; ed.prefix = ctx.prefix; ed.selected = 0;
        ed.popup.innerHTML = items.map((i, n) => '<div class="lua-completion' + (n === 0 ? ' active' : '') + '" data-index="' + n + '" title="' + escape(i.d || '') + '"><span class="lua-completion-name">'
            + escape(i.n) + '</span><small>' + escape(i.s ? i.s.slice(i.n.length) : i.t || i.k || '') + '</small></div>').join('');
        const p = caretPoint(ed);
        ed.popup.style.left = Math.max(0, p.x + ed.gutter.offsetWidth) + 'px'; ed.popup.style.top = p.y + 'px'; ed.popup.hidden = false;
    }
    function accept(ed) {
        const item = ed.items[ed.selected]; if (!item) return;
        const ta = ed.textarea, at = ta.selectionStart;
        closePopup(ed); type(ed, item.n, at - ed.prefix.length, at);
    }
    function move(ed, delta) {
        if (!ed.items.length) return;
        ed.selected = (ed.selected + delta + ed.items.length) % ed.items.length;
        ed.popup.querySelectorAll('.lua-completion').forEach((el, n) => el.classList.toggle('active', n === ed.selected));
        ed.popup.querySelector('.lua-completion.active')?.scrollIntoView({ block: 'nearest' });
    }
    function render(ed) {
        ed.code.innerHTML = highlight(ed.textarea.value);
        const lines = ed.textarea.value.split('\n').length;
        if (ed.lineCount !== lines) {
            ed.lineCount = lines;
            ed.gutter.innerHTML = Array.from({ length: lines }, (_, n) => '<div data-line="' + (n + 1) + '">' + (n + 1) + '</div>').join('');
        }
        markers(ed);
        sync(ed);
    }
    function markers(ed) {
        ed.gutter.querySelectorAll('[data-line]').forEach(el => { el.className = ''; el.removeAttribute('title'); });
        for (const d of ed.diagnostics) {
            const el = ed.gutter.querySelector('[data-line="' + d.line + '"]');
            if (el) { el.className = d.severity === 'error' ? 'lua-line-error' : 'lua-line-warning'; el.title = d.message; }
        }
    }
    function sync(ed) {
        ed.highlight.scrollTop = ed.textarea.scrollTop; ed.highlight.scrollLeft = ed.textarea.scrollLeft;
        ed.gutter.scrollTop = ed.textarea.scrollTop;
    }
    function changed(ed) {
        render(ed);
        clearTimeout(ed.timer);
        ed.timer = setTimeout(() => ed.dotnet.invokeMethodAsync('OnEditorInput', ed.textarea.value), 180);
    }
    function mount(host, dotnet, text, data) {
        host.innerHTML = '<div class="lua-gutter" aria-hidden="true"></div><div class="lua-surface"><pre class="lua-highlight" aria-hidden="true"><code></code></pre>'
            + '<textarea class="lua-input" spellcheck="false" autocapitalize="off" autocomplete="off" wrap="off" data-lua-input></textarea>'
            + '<div class="lua-mirror" aria-hidden="true"></div></div><div class="lua-popup" role="listbox" hidden></div>';
        const ed = { host, dotnet, data: data || { classes: {}, aliases: {}, strings: {}, root: '' }, diagnostics: [], items: [], selected: 0, prefix: '', lineCount: 0,
            gutter: host.querySelector('.lua-gutter'), highlight: host.querySelector('.lua-highlight'), code: host.querySelector('.lua-highlight code'),
            textarea: host.querySelector('.lua-input'), mirror: host.querySelector('.lua-mirror'), popup: host.querySelector('.lua-popup') };
        // The textarea's accessible name is UI text: the host's data-editor-label at mount, then setLabel on a language change.
        ed.textarea.setAttribute('aria-label', host.dataset.editorLabel || 'Custom Lua (src/addon.lua)');
        ed.textarea.value = text;
        ed.textarea.addEventListener('input', e => { if (ed.quiet) return; changed(ed); if (e.inputType !== 'deleteContentBackward' && /[.:'"\w]$/.test(ed.textarea.value.slice(0, ed.textarea.selectionStart))) openPopup(ed, false); else closePopup(ed); });
        ed.textarea.addEventListener('scroll', () => { sync(ed); closePopup(ed); });
        ed.textarea.addEventListener('blur', () => setTimeout(() => closePopup(ed), 150));
        ed.popup.addEventListener('mousedown', e => { const el = e.target.closest('.lua-completion'); if (el) { e.preventDefault(); ed.selected = +el.dataset.index; accept(ed); } });
        ed.textarea.addEventListener('keydown', e => {
            if (!ed.popup.hidden) {
                if (e.key === 'ArrowDown') { e.preventDefault(); move(ed, 1); return; }
                if (e.key === 'ArrowUp') { e.preventDefault(); move(ed, -1); return; }
                if (e.key === 'Enter' || e.key === 'Tab') { e.preventDefault(); accept(ed); return; }
                if (e.key === 'Escape') { e.preventDefault(); closePopup(ed); return; }
            }
            if (e.key === ' ' && e.ctrlKey) { e.preventDefault(); openPopup(ed, true); return; }
            if ((e.key === 's' || e.key === 'S') && e.ctrlKey) { e.preventDefault(); clearTimeout(ed.timer); ed.dotnet.invokeMethodAsync('OnEditorSave', ed.textarea.value); return; }
            if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey) { e.preventDefault(); type(ed, '    ', ed.textarea.selectionStart, ed.textarea.selectionEnd); return; }
            if (e.key === 'Enter') {
                const ta = ed.textarea, start = ta.value.lastIndexOf('\n', ta.selectionStart - 1) + 1;
                const indent = /^[ \t]*/.exec(ta.value.slice(start, ta.selectionStart))[0];
                e.preventDefault(); type(ed, '\n' + indent, ta.selectionStart, ta.selectionEnd);
            }
        });
        editors.set(host, ed); render(ed);
    }
    return {
        mount,
        // What the popup would offer at a caret (prefix and items), without an editor: for tools/scripting-smoke.mjs and checks.
        completions(text, caret, data) { return context(text, caret, data || { classes: {}, aliases: {}, strings: {}, root: '' }); },
        setText(host, text) { const ed = editors.get(host); if (!ed) return; const top = ed.textarea.scrollTop; ed.textarea.value = text; render(ed); ed.textarea.scrollTop = top; sync(ed); },
        getText(host) { return editors.get(host)?.textarea.value ?? ''; },
        setData(host, data) { const ed = editors.get(host); if (ed) ed.data = data; },
        setLabel(host, label) { const ed = editors.get(host); if (ed && label) ed.textarea.setAttribute('aria-label', label); },
        setDiagnostics(host, diagnostics) { const ed = editors.get(host); if (ed) { ed.diagnostics = diagnostics || []; markers(ed); } },
        insert(host, snippet) {
            const ed = editors.get(host); if (!ed) return;
            const ta = ed.textarea, start = ta.value.lastIndexOf('\n', ta.selectionStart - 1) + 1;
            const indent = /^[ \t]*/.exec(ta.value.slice(start, ta.selectionStart))[0];
            const text = snippet.replace(/\n(?=.)/g, '\n' + indent);
            type(ed, text, ta.selectionStart, ta.selectionEnd);
        },
        goto(host, line, column) {
            const ed = editors.get(host); if (!ed) return;
            const lines = ed.textarea.value.split('\n'); let at = 0;
            for (let i = 0; i < Math.min(line - 1, lines.length); i++) at += lines[i].length + 1;
            at += Math.max(0, (column || 1) - 1);
            ed.textarea.focus(); ed.textarea.setSelectionRange(at, at);
            const lineHeight = parseFloat(getComputedStyle(ed.textarea).lineHeight) || 18;
            ed.textarea.scrollTop = Math.max(0, (line - 4) * lineHeight); sync(ed);
        },
        dispose(host) { const ed = editors.get(host); if (ed) clearTimeout(ed.timer); editors.delete(host); },
        copy(text) { return navigator.clipboard.writeText(text); },
    };
})();
