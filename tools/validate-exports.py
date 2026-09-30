"""Validate ModBuilder export ZIPs with HD2Runtime's own example validator, against a frozen Runtime tree.

The Runtime tree is used read-only: extract the release commit first, never point this at a working checkout with
uncommitted changes, e.g.

    git -C ..\\HD2Runtime archive v0.28.1 | tar -x -C %TEMP%\\hd2runtime-0.28.1-public
    py -3 -B tools\\validate-exports.py --runtime %TEMP%\\hd2runtime-0.28.1-public build\\export-fixtures\\*.zip

For every export, Runtime's validator runs src/addon.lua through the production write domains (every hd2.patch /
transaction / plan / ensure request is validated), resolves and prepares every operation against the retained snapshot
(unless --offline) and checks that the declared requires.hd2runtime.min_version covers every API the mod uses.

--isolation also re-runs each export with an extra operation injected FIRST whose request cannot even be built (an
unknown weapon): the export must still validate with exactly its own operations, proving that ModBuilder's pcall
wrapper skips only the broken operation.
"""
from __future__ import annotations

import argparse
import importlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import zipfile

BROKEN = ("add(function() return hd2.ensure({patch={id='isolation-probe',target=hd2.weapon('ModBuilder Isolation Probe'),"
          "field=hd2.fields.weapon.sway,expect=1,value=2}}) end)\n")
ADD_HEADER_END = "    else print('[ModBuilder] operation skipped: '..tostring(operation)) end\nend\n"


def load_validator(runtime: Path):
    sys.path.insert(0, str(runtime / 'scripts'))
    module = importlib.import_module('validate_examples')
    if module.ROOT.resolve() != runtime.resolve():
        raise SystemExit('validate_examples was not loaded from ' + str(runtime))
    return module


def extract(zips: list[Path], work: Path, isolation: bool) -> list[tuple[str, Path]]:
    found = []
    for archive in zips:
        with zipfile.ZipFile(archive) as z:
            names = set(z.namelist())
            if 'hd2runtime.json' not in names or 'src/addon.lua' not in names:
                raise SystemExit(f'{archive.name}: not a ModBuilder export (hd2runtime.json and src/addon.lua required)')
            manifest, addon = z.read('hd2runtime.json'), z.read('src/addon.lua').decode('utf-8')
        variants = [(archive.stem, addon)]
        if isolation:
            if ADD_HEADER_END not in addon:
                raise SystemExit(f'{archive.name}: generated operations are not isolated (no pcall add() wrapper)')
            variants.append((archive.stem + '+isolation', addon.replace(ADD_HEADER_END, ADD_HEADER_END + BROKEN, 1)))
        for name, body in variants:
            folder = work / name
            (folder / 'src').mkdir(parents=True)
            (folder / 'hd2runtime.json').write_bytes(manifest)
            (folder / 'src' / 'addon.lua').write_text(body, encoding='utf-8', newline='\n')
            found.append((name, folder))
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--runtime', type=Path, required=True, help='extracted HD2Runtime release tree (read-only)')
    parser.add_argument('--snapshot', type=Path, help='retained snapshot (default: the Runtime build profile reference snapshot)')
    parser.add_argument('--offline', action='store_true', help='API validation only; skip snapshot baselines')
    parser.add_argument('--isolation', action='store_true', help='also prove a broken operation skips only itself')
    parser.add_argument('--output', type=Path, help='write the JSON report here')
    parser.add_argument('exports', nargs='+', type=Path)
    args = parser.parse_args()
    validator = load_validator(args.runtime.resolve())
    zips = sorted({p.resolve() for pattern in args.exports for p in (pattern.parent.glob(pattern.name) if '*' in pattern.name else [pattern])})
    work = Path(tempfile.mkdtemp(prefix='modbuilder-exports-'))
    try:
        found = extract(zips, work, args.isolation)
        validator.examples = lambda: found
        result = validator.validate(args.snapshot or validator.SNAPSHOT, args.offline)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    problems = []
    for name, item in sorted(result['results'].items()):
        ops = len(item['operations'])
        if name.endswith('+isolation'):
            base = result['results'].get(name[:-len('+isolation')])
            if base and item['status'] == 'VALIDATED' and len(item['operations']) != len(base['operations']):
                item['problems'].append(f'isolation: {len(item["operations"])} operations registered, expected {len(base["operations"])}')
                item['status'] = 'FAILED'
        flags = sorted({a for op in item['operations'] for a in op['acknowledgements']})
        print(('ok   ' if item['status'] == 'VALIDATED' else 'FAIL ') + name + f" min={item['minVersion']} need={item['requiredMinVersion']}"
              + f" ops={ops} baselines={item.get('baselineChanges')} flags={','.join(flags) or '-'}"
              + ('' if not item['problems'] else ' :: ' + ' | '.join(str(p) for p in item['problems'])))
        if item['status'] != 'VALIDATED':
            problems.append(name)
    report = {'runtime': str(args.runtime), 'runtimeVersion': (args.runtime / 'VERSION').read_text().strip(), 'mode': result['mode'],
              'snapshot': result['snapshot'], 'exports': len(zips), 'failed': problems,
              'status': 'VALIDATED' if not problems else 'FAILED', 'results': result['results']}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + '\n', encoding='utf-8', newline='\n')
    print(report['status'], len(problems), 'failed of', len(result['results']))
    return 1 if problems else 0


if __name__ == '__main__':
    raise SystemExit(main())
