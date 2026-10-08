"""Validate ModBuilder custom content exports with HD2Runtime's own custom stratagem registration (HD2Runtime 0.30.0).

    py -3 -B tools/custom-content/validate-runtime.py --runtime ../HD2Runtime HD2RuntimeGUI.Tests/bin/Debug/net10.0/custom-content-exports/*.zip

The Runtime tree is used read-only (nothing is written into it; Python writes no bytecode). For every export:
  1. the addon is read from the mod's archive, exactly as the game loads it (ModBuilder's dependency wrapper included);
  2. it runs in the Runtime's own offline Lua harness (tests/support.py with the stratagem call-down world fixture), so the real
     hd2.custom_stratagem.register validates every custom stratagem the export registers;
  3. each registered definition is compared with the definition the Runtime's SDK compiler gives for its own data fixture of the same
     example (sdk/fixtures/custom_stratagems/*.json compiled by sdk/tools/custom_stratagem_project.py and wrapped by the SDK build), when the
     Runtime has one for that id;
  4. the icons' build record (<resource>/hd2runtime_images, from the same archive) is available to the Runtime as in game, and the
     Runtime's own image log names every icon a custom stratagem uses as built (never "no build record" or "NOT in this mod's build").
Exit code 0 when every export loads, every custom stratagem registers, every definition with an SDK fixture matches it, and every icon
has its build record.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import zipfile

sys.dont_write_bytecode = True


def fixture_ids(root: Path) -> dict:
    # Every Runtime example's custom stratagem ids, to the example (sdk/fixtures/custom_stratagems/<name>.json) that defines them.
    out = {}
    for path in sorted((root / 'sdk/fixtures/custom_stratagems').glob('*.json')):
        for s in json.loads(path.read_text(encoding='utf-8'))['stratagems']:
            out[s['id']] = path.stem
    return out


def addon_from_export(zip_path: Path, read_archive, resource_hash) -> tuple[str, str, list[str], str | None]:
    with zipfile.ZipFile(zip_path) as z:
        manifest = json.loads(z.read('hd2runtime.json'))
        archive = z.read('mod/9ba626afa44a3aa3.patch_0')
        gpu = z.read('mod/9ba626afa44a3aa3.patch_0.gpu_resources')
        report = json.loads(z.read('build-report.json'))
    resource = manifest['resource']
    resources = read_archive(archive, gpu)
    lua_type = 0xA14E8DFA2CD117E2
    main, _ = resources[(lua_type, resource_hash(resource))]
    record = resources.get((lua_type, resource_hash(resource + '/hd2runtime_images')))
    ids = (report.get('custom_content') or {}).get('stratagems') or []
    return resource, main[8:].decode('utf-8'), ids, record[0][8:].decode('utf-8') if record else None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--runtime', type=Path, required=True, help='HD2Runtime 0.30.0 tree (read-only)')
    parser.add_argument('exports', nargs='+', type=Path)
    args = parser.parse_args()
    root = args.runtime.resolve()
    sys.path.insert(0, str(root / 'tests'))
    sys.path.insert(0, str(root / 'sdk'))
    sys.path.insert(0, str(root / 'scripts'))
    from support import run, lua  # noqa: E402  (the Runtime's own offline Lua harness)
    from test_stratagem_calldown_code import WORLD  # noqa: E402
    from test_event_scripting import SDK  # noqa: E402  (the SDK build module: wrap_addon)
    from tools import custom_stratagem_project as P  # noqa: E402
    from tools.hd2_archive import read_archive, resource_hash  # noqa: E402

    zips = sorted({p.resolve() for pattern in args.exports for p in (pattern.parent.glob(pattern.name) if '*' in pattern.name else [pattern])})
    if not zips:
        raise SystemExit('no export ZIPs given')
    FIXTURE_IDS = fixture_ids(root)
    loads, expected_ids = [], []
    for zip_path in zips:
        resource, addon, ids, record = addon_from_export(zip_path, read_archive, resource_hash)
        references = []
        for sid in ids:
            fixture = FIXTURE_IDS.get(sid)
            if fixture:
                spec = json.loads((root / 'proof' / fixture / 'hd2runtime.json').read_text(encoding='utf-8'))
                data = json.loads((root / 'sdk/fixtures/custom_stratagems' / (fixture + '.json')).read_text(encoding='utf-8'))
                wrapped = SDK.wrap_addon(spec['resource'], spec['requires']['hd2runtime']['min_version'], P.compile_lua(data))
                references.append('{id=%s,resource=%s,text=%s}' % (lua(sid), lua(spec['resource']), lua(wrapped)))
        loads.append('{name=%s,resource=%s,addon=%s,record=%s,ids={%s},references={%s}}' % (lua(zip_path.name), lua(resource), lua(addon),
            lua(record) if record else 'nil', ','.join(lua(i) for i in ids), ','.join(references)))
        expected_ids.append((zip_path.name, ids))
    result = run(WORLD + 'local LOADS={' + ','.join(loads) + '}\n' + r'''
local custom=require('hd2runtime/runtime/custom_stratagems')
-- A definition as data: every field but callbacks, sorted (as the Runtime's own SDK test compares them).
local function dump(v,seen)
    seen=seen or{}
    local t=type(v)
    if t=='function'then return'fn'end
    if t~='table'then return tostring(v)end
    if seen[v]then return'<cycle>'end
    seen[v]=true
    local keys={}
    for k,x in pairs(v)do if type(x)~='function'then keys[#keys+1]=k end end
    table.sort(keys,function(p,q)return tostring(p)<tostring(q)end)
    local out={}
    for _,k in ipairs(keys)do out[#out+1]=tostring(k)..'='..dump(v[k],seen)end
    seen[v]=nil
    return '{'..table.concat(out,',')..'}'
end
-- The definition, its texts and its icon name the mod that owns them (owner=, mod=): ModBuilder's export, or the SDK example. The
-- comparison ignores which mod.
local function normalised(definition)
    local text=dump(definition)
    return (text:gsub('mod=[^,}]+',''):gsub('owner=[^,}]+',''))
end
-- The Runtime's log, kept: its image lines say whether each icon's build record was found.
local log=require('hd2runtime/runtime/log')
local emit,logged=log.emit,{}
log.emit=function(line)logged[#logged+1]=line;return emit(line)end
local images=require('hd2runtime/runtime/image_resources')
local function load(text,resource,record)
    custom.reset_for_tests()
    require('hd2runtime/runtime/virtual_stratagems').reset_for_tests()
    images.reset_for_tests()
    rawset(_G,'HD2RuntimeMod:'..resource,nil)
    -- The archive's other Lua resource, as the game makes it available to require.
    package.loaded[resource..'/hd2runtime_images']=nil
    package.preload[resource..'/hd2runtime_images']=record and assert(loadstring(record,'@'..resource..'/hd2runtime_images'))or nil
    local chunk,problem=loadstring(text,'@'..resource)
    assert(chunk,problem)
    local ok,err=pcall(chunk)
    assert(ok,'the addon raised: '..tostring(err))
    return custom.get
end
local lines={}
for _,l in ipairs(LOADS)do
    logged={}
    local get=load(l.addon,l.resource,l.record)
    local registered={}
    for _,id in ipairs(l.ids)do
        local d=get(id)
        assert(d,l.name..': custom stratagem '..id..' did not register (see the [ModBuilder] lines above for Runtime\'s refusal)')
        registered[id]=normalised(d)
    end
    lines[#lines+1]=l.name..': '..#l.ids..' custom stratagems registered'
    local built=0
    for _,line in ipairs(logged)do
        if line:find('custom image '..l.resource..'/images/',1,true)then
            assert(line:find(' built from ',1,true),l.name..': the Runtime does not find this icon in the build record: '..line)
            built=built+1
        end
    end
    lines[#lines+1]=l.name..': '..built..' icons named by their build record'
    for _,r in ipairs(l.references)do
        local reference=normalised(load(r.text,r.resource)(r.id))
        assert(registered[r.id]==reference,l.name..': '..r.id..' differs from the SDK fixture\n'..registered[r.id]..'\n'..reference)
        lines[#lines+1]=l.name..': '..r.id..' registers exactly as the HD2Runtime SDK fixture does'
    end
end
return table.concat(lines,'\n')
''')
    print(result.decode('utf-8') if isinstance(result, bytes) else result)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
