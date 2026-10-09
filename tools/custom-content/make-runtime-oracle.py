"""Write the conformance oracle of ModBuilder's custom stratagem engine: what HD2Runtime's own SDK tools say about a corpus of projects.

    py -3 -B tools/custom-content/make-runtime-oracle.py --sdk <extracted HD2Runtime-0.30.3-sdk.zip>

The SDK is used read-only (Python writes no bytecode). For every Runtime example project (sdk/fixtures/custom_stratagems) and for a corpus of
mutations of them that break one rule each, it records sdk/tools/custom_stratagem_project.py's validate() problems, and for every valid one
its compile_lua() output. It does the same for ModBuilder's templates (HD2RuntimeGUI.Core/CustomContent/Templates), all of which must be
valid, alone and together. HD2RuntimeGUI.Tests/CustomStratagemConformanceTests holds RuntimeProjectValidator and RuntimeRegistrationLua (and
ModBuilder's project form round trip) to exactly this. Regenerate it when the bundled schema (CustomContent/Targets) is updated from the same
SDK, and whenever a template changes.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'HD2RuntimeGUI.Tests/Fixtures/custom-content/oracle/runtime-oracle.json'
TEMPLATES = ROOT / 'HD2RuntimeGUI.Core/CustomContent/Templates'


def mutations(examples: dict) -> list:
    """(name, project) pairs: each starts from a Runtime example and breaks one rule (or several of one kind)."""
    ex = lambda name: copy.deepcopy(examples[name])
    S = lambda p: p['stratagems'][0]
    cases = []

    def case(name, base, edit):
        p = ex(base)
        edit(p)
        cases.append((name, p))

    def payload(name, base, **kv):
        def edit(p):
            for k, v in kv.items():
                if v is None:
                    S(p)['payload'].pop(k, None)
                else:
                    S(p)['payload'][k] = v
        case(name, base, edit)

    # project
    case('project-format', 'GasEatExample', lambda p: p.__setitem__('format', 'other/1'))
    case('project-log-long', 'GasEatExample', lambda p: p.__setitem__('log', 'x' * 161))
    case('project-empty', 'GasEatExample', lambda p: p.__setitem__('stratagems', []))
    case('project-unknown-key', 'GasEatExample', lambda p: p.__setitem__('extra', 1))
    def many(p):
        base = S(p)
        p['stratagems'] = []
        for k in range(17):
            s = copy.deepcopy(base)
            s['id'] = 'many_%d' % k
            p['stratagems'].append(s)
    case('project-too-many', 'GasEatExample', many)
    # metadata
    case('id-invalid', 'GasEatExample', lambda p: S(p).__setitem__('id', '9bad-id'))
    case('id-long', 'GasEatExample', lambda p: S(p).__setitem__('id', 'a' * 49))
    case('name-long', 'GasEatExample', lambda p: S(p).__setitem__('name', 'N' * 65))
    case('name-control', 'GasEatExample', lambda p: S(p).__setitem__('name', 'BAD\tNAME'))
    case('name-cased-empty', 'GasEatExample', lambda p: S(p).__setitem__('name_cased', ''))
    case('description-long', 'GasEatExample', lambda p: S(p).__setitem__('description', 'd' * 401))
    case('description-missing', 'GasEatExample', lambda p: S(p).pop('description'))
    case('icon-bad', 'GasEatExample', lambda p: S(p).__setitem__('icon', {'image': 'Bad Icon'}))
    case('icon-source-other', 'GasEatExample', lambda p: S(p).__setitem__('icon', {'image': 'ok', 'source': 'other.png'}))
    case('icon-string', 'GasEatExample', lambda p: S(p).__setitem__('icon', 'eat17_gas'))
    case('code-long', 'GasEatExample', lambda p: S(p).__setitem__('code', ['up'] * 9))
    case('code-bad-direction', 'GasEatExample', lambda p: S(p).__setitem__('code', ['up', 'sideways']))
    case('code-native-equal', 'GasEatExample', lambda p: S(p).__setitem__('code', ['down', 'left', 'up', 'right']))
    case('code-native-prefix', 'GasEatExample', lambda p: S(p).__setitem__('code', ['down', 'left', 'up']))
    case('code-native-extends', 'GasEatExample', lambda p: S(p).__setitem__('code', ['down', 'left', 'up', 'right', 'up']))
    def collide(p):
        a = S(p)
        b = copy.deepcopy(a)
        b['id'] = 'second'
        b['code'] = a['code'][:3]
        c = copy.deepcopy(a)
        c['id'] = 'third'
        p['stratagems'] += [b, c]
    case('code-collisions-and-duplicate', 'GasEatExample', collide)
    case('cooldown-zero', 'GasEatExample', lambda p: S(p).__setitem__('cooldown', 0))
    case('cooldown-high', 'GasEatExample', lambda p: S(p).__setitem__('cooldown', 600.5))
    case('cooldown-text', 'GasEatExample', lambda p: S(p).__setitem__('cooldown', '60'))
    case('cooldown-bool', 'GasEatExample', lambda p: S(p).__setitem__('cooldown', True))
    case('uses-zero', 'GasEatExample', lambda p: S(p).__setitem__('uses', 0))
    case('uses-fraction', 'GasEatExample', lambda p: S(p).__setitem__('uses', 2.5))
    case('uses-high', 'GasEatExample', lambda p: S(p).__setitem__('uses', 101))
    # 0.30.3: 0 or -1 (or none) means unlimited; below -1 is refused.
    case('uses-minus-one', 'GasEatExample', lambda p: S(p).__setitem__('uses', -1))
    case('uses-minus-two', 'GasEatExample', lambda p: S(p).__setitem__('uses', -2))
    case('uses-hundred-valid', 'GasEatExample', lambda p: S(p).__setitem__('uses', 100))
    case('max-per-player-zero', 'GasEatExample', lambda p: S(p).__setitem__('max_per_player', 0))
    case('max-per-player-high', 'GasEatExample', lambda p: S(p).__setitem__('max_per_player', 5))
    case('max-per-player-fraction', 'GasEatExample', lambda p: S(p).__setitem__('max_per_player', 1.5))
    case('max-per-player-eagle', 'EagleStunRocketPodsExample', lambda p: S(p).__setitem__('max_per_player', 2))
    case('traits-many', 'GasEatExample', lambda p: S(p).__setitem__('traits', ['A', 'B', 'C', 'D', 'E']))
    case('traits-long', 'GasEatExample', lambda p: S(p).__setitem__('traits', ['T' * 33]))
    case('traits-unicode', 'GasEatExample', lambda p: S(p).__setitem__('traits', ['Ünicode']))
    case('assets-unknown', 'GasEatExample', lambda p: S(p).__setitem__('assets', ['Orbital Gas Strike', 'No Such Stratagem']))
    case('stratagem-unknown-key', 'GasEatExample', lambda p: S(p).__setitem__('on_activate', 'x'))
    # carrier
    case('carrier-unknown-key', 'GasEatExample', lambda p: S(p)['carrier'].__setitem__('mode', 'all'))
    case('carrier-beacon-bad', 'GasEatExample', lambda p: S(p)['carrier'].__setitem__('beacon', 'green'))
    case('carrier-beacon-missing', 'GasEatExample', lambda p: S(p)['carrier'].pop('beacon'))
    case('carrier-family-unknown', 'GasEatExample', lambda p: S(p)['carrier'].__setitem__('prefer_families', ['support', 'boat']))
    case('carrier-exclude-unknown', 'GasEatExample', lambda p: S(p)['carrier'].__setitem__('exclude', ['Nope']))
    case('carrier-slots-without-group', 'GasEatExample', lambda p: S(p)['carrier'].__setitem__('slots', 2))
    case('carrier-group-unknown', 'EAT23Example', lambda p: S(p)['carrier'].__setitem__('group', 'boats'))
    case('carrier-group-wrong-payload', 'EAT23Example', lambda p: S(p)['carrier'].__setitem__('group', 'orbital'))
    case('carrier-group-beacon-mismatch', 'EAT23Example', lambda p: S(p)['carrier'].__setitem__('beacon', 'offensive'))
    case('carrier-group-family-outside', 'EAT23Example', lambda p: S(p)['carrier'].__setitem__('prefer_families', ['orbital']))
    case('carrier-slots-wrong-group', 'ShredderSiloExample', lambda p: S(p)['carrier'].__setitem__('slots', 2))
    case('carrier-slots-range', 'EAT23Example', lambda p: S(p)['carrier'].__setitem__('slots', 9))
    case('carrier-sentry-allow', 'HmgSentryExample', lambda p: S(p)['carrier'].__setitem__('allow_families', ['support']))
    case('carrier-sentry-none', 'HmgSentryExample', lambda p: (S(p)['carrier'].pop('allow_families'), S(p)['carrier'].pop('prefer_families')))
    case('carrier-eagle-beacon', 'EagleStunRocketPodsExample', lambda p: S(p)['carrier'].__setitem__('beacon', 'support'))
    case('carrier-group-any-red-pelican', 'PelicanCasExplosive', lambda p: S(p).__setitem__('carrier', {'group': 'any_red'}))
    case('carrier-group-sentry-ok', 'HmgSentryExample', lambda p: S(p).__setitem__('carrier', {'group': 'sentry', 'allow_families': ['sentry']}))
    case('carrier-not-object', 'GasEatExample', lambda p: S(p).__setitem__('carrier', 'support'))
    # payload family
    payload('payload-family-unknown', 'GasEatExample', family='boat')
    payload('payload-family-script', 'GasEatExample', family='script')
    case('payload-missing', 'GasEatExample', lambda p: S(p).pop('payload'))
    payload('payload-unknown-key', 'GasEatExample', extra=1)
    # support
    case('support-two-items', 'GasEatExample', lambda p: S(p)['payload']['items'].append(copy.deepcopy(S(p)['payload']['items'][0])))
    case('support-donor-unknown', 'GasEatExample', lambda p: S(p)['payload']['items'][0].__setitem__('donor', 'Nope'))
    case('support-count', 'GasEatExample', lambda p: S(p)['payload']['items'][0].__setitem__('count', 5))
    case('support-backpack-modify', 'GasEatExample', lambda p: S(p)['payload']['items'][0].update({'donor': 'B-1 Supply Pack', 'modify': {'ammo': 10}}))
    case('support-modify-ranges', 'GasEatExample', lambda p: S(p)['payload']['items'][0].__setitem__('modify',
        {'rpm': 10, 'spread': 0, 'ammo': 5000, 'recoil': 'low', 'rounds': 100, 'impact_explosion': 'Hellbomb'}))
    case('support-modify-projectile', 'GasEatExample', lambda p: S(p)['payload']['items'][0].__setitem__('modify', {'projectile': 'Not A Weapon', 'sound': 'x'}))
    case('support-rate-selector', 'GasEatExample', lambda p: S(p)['payload']['items'][0].update({'donor': 'MG-43 Machine Gun', 'modify': {'rpm': 900}}))
    case('support-modify-not-object', 'GasEatExample', lambda p: S(p)['payload']['items'][0].__setitem__('modify', 3))
    # pod
    def pod(items):
        def edit(p):
            S(p)['carrier'] = {'group': 'support_pod'}
            S(p)['payload'] = {'family': 'pod', 'items': items}
        return edit
    case('pod-valid', 'GasEatExample', pod([{'item': 'support_weapon/MG-43 Machine Gun', 'modify': {'ammo': 300}}, {'item': 'backpack/B-1 Supply Pack'}]))
    case('pod-empty', 'GasEatExample', pod([]))
    case('pod-unknown', 'GasEatExample', pod([{'item': 'support_weapon/Nope'}]))
    case('pod-clone', 'GasEatExample', pod([{'item': 'clone', 'count': 2}]))
    case('pod-unverified', 'GasEatExample', pod([{'item': 'player_weapon/AR-23 Liberator'}]))
    case('pod-unverified-ok', 'GasEatExample', pod([{'item': 'player_weapon/AR-23 Liberator', 'allow_unverified_effect': True}]))
    case('pod-verified-flag', 'GasEatExample', pod([{'item': 'backpack/B-1 Supply Pack', 'allow_unverified_effect': True}]))
    case('pod-backpack-modify', 'GasEatExample', pod([{'item': 'backpack/B-1 Supply Pack', 'modify': {'ammo': 3}}]))
    case('pod-twice', 'GasEatExample', pod([{'item': 'backpack/B-1 Supply Pack'}, {'item': 'backpack/B-1 Supply Pack'}]))
    case('pod-count-total', 'GasEatExample', pod([{'item': 'backpack/B-1 Supply Pack', 'count': 6}, {'item': 'support_weapon/MG-43 Machine Gun', 'count': 5}]))
    case('pod-rate-selector', 'GasEatExample', pod([{'item': 'support_weapon/MG-43 Machine Gun', 'modify': {'rpm': 1000}}]))
    case('pod-wrong-group', 'GasEatExample', lambda p: (pod([{'item': 'backpack/B-1 Supply Pack'}])(p), S(p)['carrier'].__setitem__('group', 'support')))
    case('pod-item-key', 'GasEatExample', pod([{'item': 'backpack/B-1 Supply Pack', 'colour': 'red'}]))
    # expendable
    payload('expendable-weapon-unknown', 'EAT23Example', weapon='AC-8 Autocannon')
    payload('expendable-level-bad', 'EAT23Example', level='half')
    payload('expendable-no-clone', 'EAT23Example', pod=[{'item': 'backpack/B-1 Supply Pack'}])
    payload('expendable-clone-modify', 'EAT23Example', pod=[{'item': 'clone', 'count': 2, 'modify': {'ammo': 1}}])
    payload('expendable-presentation', 'EAT23Example', presentation={'name': 'N' * 65, 'icon': 'Bad Icon', 'colour': 'red'})
    payload('expendable-presentation-donor', 'EAT23Example', presentation={'name': 'donor', 'icon': 'donor'})
    payload('expendable-projectile', 'EAT23Example', modify={'projectile': 'AC-8 Autocannon'})
    payload('expendable-round-bad', 'EAT17CExample', round='AC-8 Autocannon')
    payload('expendable-round-impact', 'EAT17CExample', modify={'impact_explosion': 'Orbital Gas Strike'})
    payload('expendable-pod-count', 'EAT23Example', pod=[{'item': 'clone', 'count': 9}])
    # expendable direct damage (r52: modify.direct_damage, reviewed per clone donor's round, written with impact_explosion)
    payload('expendable-direct-damage-ok', 'EAT17GExample', modify={'impact_explosion': 'Orbital Gas Strike', 'direct_damage': 500})
    payload('expendable-direct-damage-bad', 'EAT17GExample', modify={'impact_explosion': 'Orbital Gas Strike', 'direct_damage': 250})
    payload('expendable-direct-damage-bool', 'EAT17GExample', modify={'impact_explosion': 'Orbital Gas Strike', 'direct_damage': True})
    payload('expendable-direct-damage-alone', 'EAT17GExample', modify={'direct_damage': 500})
    payload('expendable-direct-damage-round', 'EAT17CExample', modify={'direct_damage': 500})
    # weapon variant
    def variant(**kv):
        def edit(p):
            S(p)['carrier'] = {'group': 'weapon'}
            S(p)['payload'] = dict({'family': 'weapon', 'weapon': 'M-1000 Maxigun', 'round': 'output/v1/projectile/las-58-talon',
                                    'presentation': {'name': 'LASER MAXIGUN', 'icon': 'laser_maxigun'}, 'model': 'laser_maxigun', 'model_use': 'check'}, **kv)
            for k in [k for k, v in S(p)['payload'].items() if v is None]:
                S(p)['payload'].pop(k)
        return edit
    case('variant-valid', 'GasEatExample', variant())
    case('variant-weapon-unknown', 'GasEatExample', variant(weapon='AR-23 Liberator'))
    case('variant-round-bad', 'GasEatExample', variant(round='output/v1/projectile/eagle-500kg'))
    case('variant-model-bad', 'GasEatExample', variant(model='Bad Model'))
    case('variant-model-use-alone', 'GasEatExample', variant(model=None))
    case('variant-model-use-bad', 'GasEatExample', variant(model_use='maybe'))
    case('variant-presentation-icon', 'GasEatExample', variant(presentation={'icon': 'Bad'}))
    case('variant-group-wrong', 'GasEatExample', lambda p: (variant()(p), S(p)['carrier'].__setitem__('group', 'support')))
    # every support weapon a host (ab5941a): deliverable, shared types (allow_shared), hosts that fire no projectile
    bare = dict(round=None, model=None, model_use=None)
    case('variant-not-deliverable', 'GasEatExample', variant(weapon='TX-41 Sterilizer', **bare))
    case('variant-shared-without-consent', 'GasEatExample', variant(weapon='LAS-98 Laser Cannon', **bare))
    case('variant-shared-ok', 'GasEatExample', variant(weapon='LAS-98 Laser Cannon', allow_shared=True, **bare))
    case('variant-shared-false', 'GasEatExample', variant(weapon='LAS-98 Laser Cannon', allow_shared=False, **bare))
    case('variant-allow-shared-text', 'GasEatExample', variant(allow_shared='yes'))
    case('variant-no-round-host', 'GasEatExample', variant(weapon='LAS-98 Laser Cannon', allow_shared=True, model=None, model_use=None))
    case('variant-plain-host-ok', 'GasEatExample', variant(weapon='GR-8 Recoilless Rifle', **bare))
    # sentry
    payload('sentry-donor-unknown', 'HmgSentryExample', donor='A/XX Sentry')
    payload('sentry-impact', 'HmgSentryExample', weapon={'impact_explosion': 'Orbital Gas Strike', 'rounds': 2})
    payload('sentry-sound-bad', 'HmgSentryExample', weapon={'sound': 'kazoo'})
    payload('sentry-weapon-ranges', 'HmgSentryExample', weapon={'rpm': 4000, 'spread': -1, 'ammo': 0.5})
    # silo
    payload('silo-donor-bad', 'ShredderSiloExample', donor='MS-12 Silo')
    payload('silo-blast-missing', 'ShredderSiloExample', blast=None)
    payload('silo-blast-unknown', 'ShredderSiloExample', blast='Big Boom', fallback='Bigger Boom')
    payload('silo-fallback-same', 'ShredderSiloExample', fallback='Cyborg Production Unit')
    # eagle
    payload('eagle-donor-bad', 'EagleStunRocketPodsExample', donor='Eagle 9000')
    payload('eagle-ranges', 'EagleStunRocketPodsExample', uses=21, rearm_seconds=0.5)
    payload('eagle-impact-unknown', 'EagleStunRocketPodsExample', payload={'impact_explosion': 'Nope'})
    payload('eagle-impact-not-replaceable', 'EagleStunRocketPodsExample', donor='Eagle 500kg Bomb')
    payload('eagle-payload-projectile', 'EagleStunRocketPodsExample', payload={'projectile': 'x'})
    case('eagle-cooldown-uses', 'EagleStunRocketPodsExample', lambda p: S(p).update({'cooldown': 30, 'uses': 3}))
    # pelican
    payload('pelican-hover-missing', 'PelicanCasExplosive', hover=None)
    payload('pelican-hover-zero', 'PelicanCasExplosive', hover=0)
    payload('pelican-orbit', 'PelicanCasExplosive', orbit={'radius': 1, 'altitude': 200, 'spin': 3})
    payload('pelican-orbit-entry', 'PelicanCasExplosive', orbit={'entry': 55})
    payload('pelican-orbit-entry-ok', 'PelicanCasExplosive', orbit={'entry': 50, 'duration': 60})
    payload('pelican-approach', 'PelicanCasExplosive', approach={'distance': 2000, 'height': -1})
    payload('pelican-gun-values', 'PelicanCasExplosive', gun={'round': 'laser', 'behave_as': 'eagle', 'rate_multiplier': 3, 'casing': 'none',
                                                              'recoil': True, 'unlimited_ammo': False, 'face_target': False, 'sound': 'kazoo'})
    payload('pelican-gun-needs-gatling', 'PelicanCasExplosive', gun={'rpm': 600, 'face_target': True, 'aim_height': 1, 'rate_multiplier': 2, 'casing': 'gatling'})
    payload('pelican-gun-aim-zero', 'PelicanCasExplosive', gun={'aim_height': 0})
    payload('pelican-gun-rpm-multiplier', 'PelicanCasExplosive', gun={'behave_as': 'gatling_sentry', 'rpm': 600, 'rate_multiplier': 1.5})
    payload('pelican-gun-multiplier-one', 'PelicanCasExplosive', gun={'behave_as': 'gatling_sentry', 'rpm': 600, 'rate_multiplier': 1})
    payload('pelican-gun-slow-donor', 'PelicanCasExplosive', gun={'behave_as': 'gatling_sentry', 'impact_explosion': 'EMS mortar field'})
    payload('pelican-gun-slow-fast', 'PelicanCasExplosive', gun={'behave_as': 'gatling_sentry', 'rpm': 600, 'impact_explosion': 'EMS mortar field'})
    payload('pelican-gun-donor-unknown', 'PelicanCasExplosive', gun={'impact_explosion': 'Nope'})
    payload('pelican-gun-unknown-key', 'PelicanCasExplosive', gun={'colour': 'red'})
    payload('pelican-gun-ranges', 'PelicanCasExplosive', gun={'behave_as': 'gatling_sentry', 'rpm': 10, 'spread': 0, 'aim_height': 4})
    # orbital
    payload('orbital-native-false', 'GasBarrageExample', native=False)
    payload('orbital-native-extra', 'GasBarrageExample', salvos=3, shell='Orbital Gas Strike')
    payload('orbital-native-pattern', 'GasBarrageExample', pattern='Orbital Gas Strike')
    payload('orbital-native-impact', 'GasBarrageExample', impact_explosion=None)
    def runtime_barrage(**kv):
        def edit(p):
            S(p)['payload'] = dict({'family': 'orbital', 'shell': 'Orbital EMS Strike', 'pattern': 'Orbital 120mm HE Barrage'}, **kv)
            for k in [k for k, v in S(p)['payload'].items() if v is None]:
                S(p)['payload'].pop(k)
        return edit
    case('orbital-runtime-valid', 'GasBarrageExample', runtime_barrage(salvos=3, shells_per_salvo=4, shell_interval=0.3, salvo_interval=2,
                                                                      scatter=20, impact_explosion='Orbital Gas Strike'))
    case('orbital-runtime-shell', 'GasBarrageExample', runtime_barrage(shell=None))
    case('orbital-runtime-pattern', 'GasBarrageExample', runtime_barrage(pattern='Nope'))
    case('orbital-runtime-ranges', 'GasBarrageExample', runtime_barrage(salvos=20, shells_per_salvo=0, shell_interval=11, salvo_interval=-1,
                                                                       scatter=200, salvo_scatter=101))
    case('orbital-runtime-total', 'GasBarrageExample', runtime_barrage(salvos=16, shells_per_salvo=16))
    case('orbital-runtime-total-default', 'GasBarrageExample', runtime_barrage(shells_per_salvo=16))
    case('orbital-runtime-impact', 'GasBarrageExample', runtime_barrage(impact_explosion='Nope'))
    case('orbital-runtime-native-null', 'GasBarrageExample', runtime_barrage(native=None))
    return cases


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--sdk', type=Path, required=True, help='the HD2Runtime SDK folder the bundled schema comes from (read-only)')
    args = parser.parse_args()
    sdk = args.sdk.resolve()
    sys.path.insert(0, str(sdk))
    from tools import custom_stratagem_project as P  # noqa: E402
    schema_bytes = (sdk / 'CustomStratagemSchema.json').read_bytes()
    # The target's schema file keeps the target's version in its name (CustomContentTargets.BundledSchemaResource): there is exactly one.
    (schema_file,) = (ROOT / 'HD2RuntimeGUI.Core/CustomContent/Targets').glob('CustomStratagemSchema-*.json')
    bundled = schema_file.read_bytes()
    if schema_bytes != bundled:
        print('the bundled schema differs from this SDK\'s: copy it first', file=sys.stderr)
        return 1
    schema = json.loads(schema_bytes)
    examples = {p.stem: json.loads(p.read_text(encoding='utf-8')) for p in sorted((sdk / 'fixtures/custom_stratagems').glob('*.json'))}
    out = {'sdk_schema_sha256': hashlib.sha256(schema_bytes).hexdigest(), 'examples': [], 'templates': [], 'cases': []}
    for name, project in examples.items():
        problems = P.validate(project, schema)
        out['examples'].append({'name': name, 'project': project, 'problems': problems,
                                'addon': P.compile_lua(project) if not problems else None})
    templates = {p.stem: json.loads(p.read_text(encoding='utf-8')) for p in sorted(TEMPLATES.glob('*.json'))}
    for name, project in templates.items():
        problems = P.validate(project, schema)
        out['templates'].append({'name': name, 'project': project, 'problems': problems,
                                 'addon': P.compile_lua(project) if not problems else None})
    together = {'format': 'hd2runtime-custom-stratagems/1', 'stratagems': [s for p in templates.values() for s in p['stratagems']]}
    out['templates_together'] = P.validate(together, schema)
    for name, project in mutations(examples):
        try:
            problems = P.validate(project, schema)
        except Exception as e:  # an SDK validator defect: recorded, ModBuilder's port must report instead of failing
            out['cases'].append({'name': name, 'project': project, 'problems': None, 'crash': '%s: %s' % (type(e).__name__, e), 'addon': None})
            continue
        out['cases'].append({'name': name, 'project': project, 'problems': problems,
                             'addon': P.compile_lua(project) if not problems else None})
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, indent=1, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    for c in out['cases']:
        if c.get('crash'):
            print('SDK validator crash on %s: %s' % (c['name'], c['crash']))
    bad = [c['name'] for c in out['cases'] if c['problems'] == [] and not c['name'].endswith(('-valid', '-ok', '-one', '-zero', '-null'))]
    print('%d examples, %d cases (%d valid) -> %s' % (len(out['examples']), len(out['cases']),
          sum(1 for c in out['cases'] if not c['problems']), OUT.relative_to(ROOT)))
    if bad:
        print('cases meant to break a rule that the SDK accepts: ' + ', '.join(bad))
    invalid = [t['name'] for t in out['templates'] if t['problems']]
    if invalid or out['templates_together']:
        print('templates the SDK refuses: %s %s' % (', '.join(invalid), out['templates_together']), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
