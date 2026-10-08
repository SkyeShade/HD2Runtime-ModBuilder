"""Make an HD2Runtime SDK release ZIP ModBuilder's bundled pin (docs/runtime030-integration-plan.md, "Swapping the bundled SDK").

    py -3 -B tools/swap-bundled-sdk.py --zip <HD2Runtime-x.y.z-sdk.zip> --commit <full Runtime commit> [--runtime-tree <Runtime checkout at it>]

Byte for byte, never through text mode:
  - the ZIP becomes the test fixture HD2RuntimeGUI.Tests/Fixtures/sdk-<version>.zip (an older fixture stays: tests load it by name);
  - every file in HD2RuntimeGUI.Core/Metadata/Bundled is replaced by the archive's file of the same name (hd2runtime-stubs.lua is
    stubs/mods/skyeshade/hd2runtime.lua); a bundled file the archive lacks refuses the swap;
  - the HUD icon sprite names (GameAssets/Bundled/HudIconSprites.json, the game icons ModBuilder reads from the installed game's texture
    atlases) are replaced by the archive's HudIconSprites.json; an archive without it refuses the swap;
  - custom content: CustomContent/Targets (the custom stratagem schema, the model bases, the stub) and the two templates that are the
    Runtime's own example projects (CustomTemplates.RuntimeExampleNames, sdk/fixtures/custom_stratagems) are replaced in place (the
    targets' file names keep the target's version: rename them by hand for a new one); ModBuilder's own templates are left alone, and the
    oracle checks them against the SDK;
  - SdkPin is set (Version, both commits, ArchiveName, ArchiveSha256, ContentFingerprint; SdkCompatibility follows SdkPin.Version);
  - the custom stratagem conformance oracle is regenerated from the archive's own tools (make-runtime-oracle.py).
It prints what a NEW version string still needs by hand (test constants, scripts, smokes). Then run the full test suite.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CORE = ROOT / 'HD2RuntimeGUI.Core'
BUNDLED = CORE / 'Metadata/Bundled'
TARGETS = CORE / 'CustomContent/Targets'
TEMPLATES = CORE / 'CustomContent/Templates'
STUB = 'stubs/mods/skyeshade/hd2runtime.lua'


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def set_const(text: str, name: str, value: str) -> str:
    new, n = re.subn(r'(public const string ' + name + r' = ")[^"]*(")', lambda m: m.group(1) + value + m.group(2), text)
    if n != 1:
        sys.exit(f'refused: {name} not found exactly once')
    return new


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--zip', type=Path, required=True)
    parser.add_argument('--commit', required=True, help='the full Runtime commit the SDK was generated at')
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9a-f]{40}', args.commit):
        sys.exit('refused: --commit must be a full 40-character commit hash')
    data = args.zip.read_bytes()
    archive = zipfile.ZipFile(io.BytesIO(data))
    names = {n.replace('\\', '/'): n for n in archive.namelist()}

    def member(name: str) -> bytes:
        if name not in names:
            sys.exit(f'refused: the archive has no {name}')
        return archive.read(names[name])

    version = json.loads(member('metadata.json'))['runtime_version']
    print(f'SDK {version} ({args.zip.name}, sha256 {sha(data)})')

    # The bundle: every file ModBuilder reads, by its flat name.
    bundled = {}
    for path in sorted(BUNDLED.iterdir()):
        new = member(STUB if path.name == 'hd2runtime-stubs.lua' else path.name)
        if path.read_bytes() != new:
            print(f'  Bundled/{path.name} changed')
        path.write_bytes(new)
        bundled[path.name] = new
    fingerprint = sha(''.join(f'{n}:{sha(bundled[n])}\n' for n in sorted(bundled)).encode('utf-8'))

    # Custom content: the target's files keep their names (CustomContentTarget.BundledSchemaResource etc.).
    # The Custom Lua sound picker's catalogue (embedded beside the scripting code, not an SDK cache file).
    for path in sorted((CORE / 'Scripting/Bundled').glob('WeaponSoundCatalogue-*.json')):
        new = member('WeaponSoundCatalogue.json')
        if path.read_bytes() != new:
            print(f'  Scripting/Bundled/{path.name} changed')
        path.write_bytes(new)
    # The HUD icon sprite names (GameAssets/Bundled, embedded beside the icon reader; HudIconTests pins its SHA-256 and counts).
    sprites = CORE / 'GameAssets/Bundled/HudIconSprites.json'
    new = member('HudIconSprites.json')
    if sprites.read_bytes() != new:
        print(f'  GameAssets/Bundled/HudIconSprites.json changed (build {json.loads(new)["build"]}, sha256 {sha(new).upper()}): '
              'update HudIconTests (SHA-256, counts) and review the icon coverage on an installed game')
    sprites.write_bytes(new)
    for path in sorted(TARGETS.iterdir()):
        source = STUB if path.suffix == '.lua' else re.sub(r'-\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?\.json$', '.json', path.name)
        new = member(source)
        if path.read_bytes() != new:
            print(f'  CustomContent/Targets/{path.name} changed (from {source})')
        path.write_bytes(new)
    # Only the Runtime examples ModBuilder keeps as templates follow the SDK; the others are ModBuilder's own.
    kept = re.search(r'RuntimeExampleNames = new HashSet<string>\(StringComparer\.Ordinal\) \{([^}]*)\}',
                     (CORE / 'CustomContent/CustomTemplates.cs').read_text(encoding='utf-8')).group(1)
    for stem in re.findall(r'"([^"]+)"', kept):
        name = f'fixtures/custom_stratagems/{stem}.json'
        if name not in names:
            print(f'  Template {stem}.json is no longer a Runtime example: review it by hand')
            continue
        path, new = TEMPLATES / f'{stem}.json', member(name)
        if path.read_bytes() != new:
            print(f'  CustomContent/Templates/{path.name} changed')
        path.write_bytes(new)

    fixture = ROOT / f'HD2RuntimeGUI.Tests/Fixtures/sdk-{version}.zip'
    fixture.write_bytes(data)

    pin = CORE / 'Metadata/SdkPin.cs'
    text = pin.read_bytes().decode('utf-8')
    old_version = re.search(r'public const string Version = "([^"]*)"', text).group(1)
    for name, value in [('Version', version), ('SdkCommit', args.commit), ('RuntimeCommit', args.commit), ('ArchiveName', args.zip.name),
                        ('ArchiveSha256', sha(data)), ('ContentFingerprint', fingerprint)]:
        text = set_const(text, name, value)
    pin.write_bytes(text.encode('utf-8'))
    print(f'SdkPin: {version}, commit {args.commit}, fingerprint {fingerprint}')

    with tempfile.TemporaryDirectory() as tmp:
        archive.extractall(tmp)
        subprocess.run([sys.executable, '-B', str(ROOT / 'tools/custom-content/make-runtime-oracle.py'), '--sdk', tmp], check=True)

    if version != old_version:
        print(f'''
A new version string ({old_version} -> {version}) still needs, by hand:
  - PublishedArtifactVersion: check the eight 0.19.0-labelled graphs are still byte-identical;
  - Runtime030Tests.Version / Fixture, the 0.30 version literals in OldProjectCompatibilityTests and the other Runtime030*Tests, and
    WeaponSounds.Resource (the sound catalogue's file name keeps the version);
  - RuntimeRequirement.Release030, if the floors should name the release;
  - scripts/build-rc.ps1 ($RuntimeCommit, $RuntimeVersion, $RuntimeTree), scripts/run-rc-smokes.ps1 ($RuntimeTree, $SdkVersion) and the
    expected SDK in tools/*-smoke.mjs;
  - the custom content target's files (CustomContent/Targets/*-<version>.*, their resource names in CustomContentTargets,
    CustomModelCompiler and CustomScripting) and its wording (CustomContent.Banner), if the Runtime's API status changes.''')
    print('\nNext: dotnet test HD2RuntimeGUI.Tests')


if __name__ == '__main__':
    main()
