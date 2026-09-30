// ModBuilder UI string resources (HD2RuntimeGUI.Core/Resources/Strings/Strings*.resx), for maintainers and translators.
//
//   node tools/i18n/resx.mjs check                      key sets, empty values, placeholders and .Html markup of every locale
//   node tools/i18n/resx.mjs missing <culture>          neutral keys a locale lacks or leaves empty (what to translate next)
//   node tools/i18n/resx.mjs new <culture>              create Strings.<culture>.resx with every neutral key (English values to replace)
//   node tools/i18n/resx.mjs merge <fragment.json>...   add [{key, en, zh?, comment?}] entries (maintainer tooling for bulk conversions)
//   node tools/i18n/resx.mjs sort                       rewrite every resx sorted by key (stable diffs)
//   node tools/i18n/resx.mjs usage [fragment.json]...   keys the source uses but no resource has, and resources no source uses
//
// The unit tests (LocalizationTests) enforce the same rules; this script only gives faster feedback.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const folder = path.join(root, 'HD2RuntimeGUI.Core', 'Resources', 'Strings');
export const file = culture => path.join(folder, culture ? `Strings.${culture}.resx` : 'Strings.resx');
const decode = s => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16))).replace(/&amp;/g, '&');
const encode = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

export function read(culture) {
    const text = fs.readFileSync(file(culture), 'utf8');
    const entries = new Map(), duplicates = [];
    for (const m of text.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>(?:\s*<comment>([\s\S]*?)<\/comment>)?\s*<\/data>/g)) {
        if (entries.has(m[1])) duplicates.push(m[1]);
        entries.set(m[1], { value: decode(m[2]), comment: m[3] === undefined ? undefined : decode(m[3]) });
    }
    return { entries, duplicates };
}
const header = `<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
`;
export function write(culture, entries) {
    const keys = [...entries.keys()].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'base' }) || (a < b ? -1 : a > b ? 1 : 0));
    let out = header;
    for (const key of keys) {
        const e = entries.get(key);
        out += `  <data name="${key}" xml:space="preserve">\n    <value>${encode(e.value)}</value>\n`;
        if (e.comment) out += `    <comment>${encode(e.comment)}</comment>\n`;
        out += '  </data>\n';
    }
    fs.writeFileSync(file(culture), out + '</root>\n');
}
export const cultures = () => fs.readdirSync(folder).map(f => /^Strings\.(.+)\.resx$/.exec(f)?.[1]).filter(Boolean);
export const placeholders = s => [...s.replace(/\{\{|\}\}/g, '').matchAll(/\{(\d+)(?:[,:][^}]*)?\}/g)].map(m => Number(m[1])).sort((a, b) => a - b).filter((v, i, a) => a.indexOf(v) === i);
const allowedTags = /^<\/?(code|strong|em|br)\s*\/?>$/;

function check() {
    const neutral = read(); let problems = [];
    if (neutral.duplicates.length) problems.push(`Strings.resx: duplicate keys ${neutral.duplicates.join(', ')}`);
    for (const [key, e] of neutral.entries) {
        if (!/^[A-Z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$/.test(key)) problems.push(`Strings.resx: ${key}: key is not Area.Element`);
        if (!e.value.trim()) problems.push(`Strings.resx: ${key}: empty`);
        const tags = e.value.match(/<[^>]*>/g) ?? [];
        if (key.endsWith('.Html') ? tags.some(t => !allowedTags.test(t)) : tags.length) problems.push(`Strings.resx: ${key}: markup only in .Html keys (<code>, <strong>, <em>, <br>)`);
    }
    for (const culture of cultures()) {
        const locale = read(culture);
        if (locale.duplicates.length) problems.push(`${culture}: duplicate keys ${locale.duplicates.join(', ')}`);
        for (const [key, e] of neutral.entries) {
            const t = locale.entries.get(key);
            if (!t) { problems.push(`${culture}: missing ${key}`); continue; }
            if (!t.value.trim()) problems.push(`${culture}: ${key}: empty`);
            if (placeholders(t.value).join() !== placeholders(e.value).join()) problems.push(`${culture}: ${key}: placeholders {${placeholders(t.value)}} differ from English {${placeholders(e.value)}}`);
        }
        for (const key of locale.entries.keys()) if (!neutral.entries.has(key)) problems.push(`${culture}: ${key} is not a neutral key (obsolete?)`);
    }
    console.log(problems.length ? problems.join('\n') : `OK: ${neutral.entries.size} keys; locales ${cultures().join(', ') || '(none)'}`);
    process.exitCode = problems.length ? 1 : 0;
}
function merge(files) {
    const neutral = read().entries, zh = read('zh-Hans').entries; let added = 0, same = 0;
    for (const f of files) for (const x of JSON.parse(fs.readFileSync(f, 'utf8'))) {
        const old = neutral.get(x.key);
        if (old && old.value !== x.en) throw new Error(`${f}: ${x.key} is already "${old.value}", not "${x.en}"`);
        if (old) { same++; continue; }
        neutral.set(x.key, { value: x.en, comment: x.comment }); added++;
        if (x.zh) zh.set(x.key, { value: x.zh, comment: x.zhComment ?? 'new: needs native review' });
    }
    write(undefined, neutral); write('zh-Hans', zh);
    console.log(`merged: ${added} new, ${same} already present`);
}
// Keys referenced by source: explicit lookups (T["…"], T.Format("…", CoreText.Get("…") and friends) plus any other string literal shaped
// like a key whose area (first segment) the resources use, so conditional keys (T[x ? "A.B" : "A.C"]) count too.
export function sourceFiles() {
    const out = [];
    const walk = dir => { for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        if (e.isDirectory()) { if (!['bin', 'obj', 'Resources', 'Bundled'].includes(e.name)) walk(path.join(dir, e.name)); }
        else if (/\.(razor|cs)$/.test(e.name)) out.push(path.join(dir, e.name));
    } };
    walk(path.join(root, 'HD2RuntimeGUI', 'Components')); walk(path.join(root, 'HD2RuntimeGUI', 'Services')); walk(path.join(root, 'HD2RuntimeGUI.Core'));
    return out;
}
export function usedKeys(files, areas) {
    const used = new Map();
    const add = (key, f) => { if (!used.has(key)) used.set(key, new Set()); used.get(key).add(path.relative(root, f)); };
    for (const f of files) {
        const text = fs.readFileSync(f, 'utf8');
        for (const m of text.matchAll(/(?:\bT|\bt|\bText|CoreText)\s*(?:\[\s*|\.(?:Format|Plural|Or|Markup|Html|Get)\(\s*)"([^"]+)"/g)) add(m[1], f);
        for (const m of text.matchAll(/"([A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"/g)) if (areas.has(m[1].split('.')[0])) add(m[1], f);
    }
    return used;
}
function usage(fragmentFiles) {
    const neutral = read().entries;
    for (const f of fragmentFiles) for (const x of JSON.parse(fs.readFileSync(f, 'utf8'))) neutral.set(x.key, { value: x.en });
    const areas = new Set([...neutral.keys()].map(k => k.split('.')[0]));
    const used = usedKeys(sourceFiles(), areas);
    const has = k => neutral.has(k) || neutral.has(k + '.Other');
    const missing = [...used].filter(([k]) => !has(k));
    const base = k => k.replace(/\.(Zero|One|Two|Few|Many|Other)$/, '');
    const unused = [...neutral.keys()].filter(k => !used.has(k) && !used.has(base(k)));
    for (const [k, files] of missing) console.log(`missing ${k}  (${[...files].join(', ')})`);
    for (const k of unused) console.log(`unused  ${k}`);
    console.log(`${used.size} keys referenced; ${missing.length} missing; ${unused.length} unused`);
    process.exitCode = missing.length ? 1 : 0;
}
const [command, ...args] = process.argv.slice(2);
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    if (command === 'init') { for (const c of [undefined, ...args]) if (!fs.existsSync(file(c))) write(c, new Map()); }
    else if (command === 'check') check();
    else if (command === 'usage') usage(args);
    else if (command === 'missing') { const n = read().entries, l = read(args[0]).entries; for (const [k, e] of n) if (!l.get(k)?.value.trim()) console.log(`${k}\t${e.value}`); }
    else if (command === 'new') { if (fs.existsSync(file(args[0]))) throw new Error('exists'); write(args[0], new Map([...read().entries].map(([k, e]) => [k, { value: e.value, comment: e.comment }]))); console.log('created ' + file(args[0])); }
    else if (command === 'merge') merge(args);
    else if (command === 'sort') { write(undefined, read().entries); for (const c of cultures()) write(c, read(c).entries); }
    else console.log('usage: check | missing <culture> | new <culture> | merge <fragment.json>... | sort');
}
