# Blocked: merged support stratagem / support weapon editor

Status: **not implemented**. The published HD2Runtime SDK (0.22.0, tag `b39abcc`) does not publish a safe semantic link between a support-weapon authoring identity and its support call-in stratagem. The GUI therefore keeps **Stratagems → Support** (call-in definition: cooldown, max uses) and **Support Weapons** (equipment: ammo, projectile, damage, explosion, …) as separate editors with separate, non-duplicated change state. Nothing is paired by display name.

## What the SDK publishes

`SupportWeaponAuthoringCapabilities.json` (`hd2runtime.support_weapon.guarded_authoring.v2`), per weapon:

```json
"linkedStratagem": { "known": false }
```

- 34 of 35 weapons: `{"known": false}`. Runtime explicitly states the link is unknown.
- `MS-11 Solo Silo`: `{"known": true, "kind": "support_weapon_delivery"}`. The link is known to exist, but **no identity of the linked stratagem is published** (no name, root key, instance key or backing identity).

`StratagemAuthoringCapabilities.json` (`hd2runtime.stratagem.guarded_authoring.v2`), for the 35 `family: "support"` roots:

- Published properties are only `name`, `family`, `rootResolution`, `attackRoles` (always empty), `cooldown`, `cooldownCapability`, `maxUses`, `callInTime` and `blockedReason`.
- No support-weapon identity, no semantic branches, no attack graph. The 66 support field instances all use target path `stratagem` (cooldown and max uses only).

The only available relationship is that the 35 root names are textually identical to the 35 support-weapon names. Internally, Runtime derives its support roots from its support-weapon list and a hand-maintained debug-name table (`SUPPORT_DEBUG_NAMES` in `scripts/research_stratagem_authoring.py`). That correlation is not part of the public contract.

## Why name matching is unsafe

- It contradicts the published contract: the SDK says `known: false` for 34 weapons. Pairing them would present a relationship Runtime declines to assert.
- Identity models differ: 7 support weapons are `DUPLICATE` (blocked) identities whose same-named call-in roots are `UNIQUE`. CQC-72 is `DUPLICATE` with an `UNRESOLVED` root, and SG-88 is a `UNIQUE` weapon with an `UNRESOLVED` root. A name pair cannot say which physical weapon identity a call-in delivers.
- Solo Silo is the only weapon whose delivery link is marked known, and even there the linked identity is absent. Its payload graph (missile, explosions) is published only in the support-weapon catalog; the stratagem catalog has no attack branches for it. A combined "cooldown + payload" view would have to attach one catalog's graph to the other by name.
- A merged editor would imply shared ownership, persistence grouping and Overview/Changes grouping that no published backing, operation-group or scope metadata supports.

## Affected entries

All 35 support stratagems / support weapons: 40-K Meltagun, AC-8 Autocannon, APW-1 Anti-Materiel Rifle, ARC-3 Arc Thrower, B/FLAM-80 Cremator, B/MD C4 Pack, CQC-1 One True Flag, CQC-20 Breaching Hammer, CQC-72 Entrenchment Tool, CQC-9 Defoliation Tool, EAT-17 Expendable Anti-Tank, EAT-411 Leveller, EAT-700 Expendable Napalm, FAF-14 Spear, FLAM-40 Flamethrower, GL-21 Grenade Launcher, GL-28 Belt-Fed Grenade Launcher, GL-52 De-Escalator, GR-8 Recoilless Rifle, LAS-98 Laser Cannon, LAS-99 Quasar Cannon, M-1000 Maxigun, M-105 Stalwart, MG-206 Heavy Machine Gun, MG-43 Machine Gun, MGX-42 Bullet Storm, MLS-4X Commando, MS-11 Solo Silo, PLAS-45 Epoch, RL-77 Airburst Rocket Launcher, RS-422 Railgun, S-11 Speargun, SG-88 Break-Action Shotgun, StA-X3 W.A.S.P. Launcher, TX-41 Sterilizer.

## What Runtime should publish

In either catalog (ideally both, cross-checked):

1. **Support-weapon side:** for each weapon, `linkedStratagem` with `known`, `kind` and the linked stratagem's semantic identity, for example `{"known": true, "kind": "support_weapon_call_in", "stratagem": "GR-8 Recoilless Rifle", "rootResolution": "UNIQUE"}`, or an explicit reason when unknown.
2. **Stratagem side:** for each `support` root, the delivered payload identity, for example `"deliveredSupportWeapon": {"identity": "<support weapon name>", "identityStatus": "UNIQUE|DUPLICATE", "kind": "support_weapon_call_in|support_weapon_delivery"}`, plus Solo Silo's delivery graph relationship.
3. **Consistency:** a pair relationship Runtime has validated against native ownership (the payload the call-in root delivers), with duplicate/unresolved identities stated explicitly, and an audit count (for example `summary.linkedStratagemIdentities`).

With that metadata, the GUI can present one logical editor per pair while still emitting `hd2.stratagem(...)` and `hd2.support_weapon(...)` targets through their own operation groups and plans. `NavigationTests.Runtime_does_not_publish_support_weapon_call_in_identity` documents the current published state and fails when it changes.
