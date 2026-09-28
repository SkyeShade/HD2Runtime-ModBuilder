# Support stratagem / support weapon linkage

## Current state (HD2Runtime 0.22.1)

**Resolved for 32 of 35 support weapons.** HD2Runtime 0.22.1 (tag `v0.22.1` → commit `488c6e2`, SDK SHA-256 `a109798a…a469a`) publishes `hd2runtime.support_callin_linkage.v1`:

- Stable `semanticId` on every support weapon and stratagem root.
- Forward `weapons[].linkedStratagem` (`known`, `state`, `semanticId`, `relationshipId`, `blocker`) and reverse `stratagems[].delivers`.
- An identical `supportCallInLinks` collection in both capability files, with a join contract (`displayNameMatchingRequired: false`, `writeTargetsRemainSeparate: true`), a per-pair `deliveryGraph` and an audit (32 known, 3 unresolved, 0 bidirectional mismatches, 0 name-only links).

`SupportCallInLinker` joins only through these IDs and cross-validates the forward link, reverse link, relationship, delivery graph and audit. Any inconsistency or unknown contract version rejects the SDK. The merged editor is presentation only: saved changes and generated `hd2.stratagem(...)` / `hd2.support_weapon(...)` operations are unchanged, and support-weapon write guards still come solely from the support catalog. Consequently, the 7 linked weapons with `DUPLICATE` identities stay read-only.

Unresolved and kept separate, with Runtime's blocker text: **B/MD C4 Pack** (delivered object not proven to be the placed charge), **SG-88 Break-Action Shotgun** and **CQC-72 Entrenchment Tool** (no uniquely correlated call-in).

Verification: `SupportLinkageTests` (join, guards, Solo Silo graph, persistence/Lua, name independence, fail-closed mutations, 0.22.0 → 0.22.1 rebind) and `tools/support-link-smoke.mjs`.

## History: blocker report for 0.22.0


Status in 0.22.0: **not implemented**. The published HD2Runtime SDK (0.22.0, tag `b39abcc`) does not publish a safe semantic link between a support-weapon authoring identity and its support call-in stratagem. The GUI therefore keeps **Stratagems → Support** (call-in definition: cooldown, max uses) and **Support Weapons** (equipment: ammo, projectile, damage, explosion, …) as separate editors with separate, non-duplicated change state. Nothing is paired by display name.

### What the SDK publishes

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

### Why name matching is unsafe

- It contradicts the published contract: the SDK says `known: false` for 34 weapons. Pairing them would present a relationship Runtime declines to assert.
- Identity models differ: 7 support weapons are `DUPLICATE` (blocked) identities whose same-named call-in roots are `UNIQUE`. CQC-72 is `DUPLICATE` with an `UNRESOLVED` root, and SG-88 is a `UNIQUE` weapon with an `UNRESOLVED` root. A name pair cannot say which physical weapon identity a call-in delivers.
- Solo Silo is the only weapon whose delivery link is marked known, and even there the linked identity is absent. Its payload graph (missile, explosions) is published only in the support-weapon catalog; the stratagem catalog has no attack branches for it. A combined "cooldown + payload" view would have to attach one catalog's graph to the other by name.
- A merged editor would imply shared ownership, persistence grouping and Overview/Changes grouping that no published backing, operation-group or scope metadata supports.

### Affected entries

All 35 support stratagems / support weapons: 40-K Meltagun, AC-8 Autocannon, APW-1 Anti-Materiel Rifle, ARC-3 Arc Thrower, B/FLAM-80 Cremator, B/MD C4 Pack, CQC-1 One True Flag, CQC-20 Breaching Hammer, CQC-72 Entrenchment Tool, CQC-9 Defoliation Tool, EAT-17 Expendable Anti-Tank, EAT-411 Leveller, EAT-700 Expendable Napalm, FAF-14 Spear, FLAM-40 Flamethrower, GL-21 Grenade Launcher, GL-28 Belt-Fed Grenade Launcher, GL-52 De-Escalator, GR-8 Recoilless Rifle, LAS-98 Laser Cannon, LAS-99 Quasar Cannon, M-1000 Maxigun, M-105 Stalwart, MG-206 Heavy Machine Gun, MG-43 Machine Gun, MGX-42 Bullet Storm, MLS-4X Commando, MS-11 Solo Silo, PLAS-45 Epoch, RL-77 Airburst Rocket Launcher, RS-422 Railgun, S-11 Speargun, SG-88 Break-Action Shotgun, StA-X3 W.A.S.P. Launcher, TX-41 Sterilizer.

### What Runtime should publish

In either catalog (ideally both, cross-checked):

1. **Support-weapon side:** for each weapon, `linkedStratagem` with `known`, `kind` and the linked stratagem's semantic identity, for example `{"known": true, "kind": "support_weapon_call_in", "stratagem": "GR-8 Recoilless Rifle", "rootResolution": "UNIQUE"}`, or an explicit reason when unknown.
2. **Stratagem side:** for each `support` root, the delivered payload identity, for example `"deliveredSupportWeapon": {"identity": "<support weapon name>", "identityStatus": "UNIQUE|DUPLICATE", "kind": "support_weapon_call_in|support_weapon_delivery"}`, plus Solo Silo's delivery graph relationship.
3. **Consistency:** a pair relationship Runtime has validated against native ownership (the payload the call-in root delivers), with duplicate/unresolved identities stated explicitly, and an audit count (for example `summary.linkedStratagemIdentities`).

With that metadata, the GUI can present one logical editor per pair while still emitting `hd2.stratagem(...)` and `hd2.support_weapon(...)` targets through their own operation groups and plans. `NavigationTests.Runtime_does_not_publish_support_weapon_call_in_identity` documents the current published state and fails when it changes.
