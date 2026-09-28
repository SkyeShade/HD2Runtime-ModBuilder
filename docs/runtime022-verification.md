# Runtime 0.22 defensive stratagem integration

## Published source

- Release: https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.22.0
- Tag commit: `b39abcccb5e03fe8978b36bb252085b0d453be9f` (verified through GitHub's tag API).
- SDK asset: `HD2Runtime-0.22.0-sdk.zip`, 770,415 bytes, SHA-256 `8f2aec687f7b04788fd9555a0d6dbfce86da57da3ad6dea1f00c76b4218336d3`.
- Canonical contract: `hd2runtime.stratagem.guarded_authoring.v2`, schema 2, `fieldInstances`.

Bundled metadata is copied byte-for-byte from that SDK. The unchanged 0.19 composition artifacts are accepted only at their reviewed SHA-256 values. HD2Runtime was not modified.

## Loaded catalog

| Property | Loaded |
| --- | ---: |
| Stratagem roots (orbital / eagle / support / sentry / emplacement / mine) | 73 (12 / 8 / 35 / 10 / 4 / 4) |
| Canonical instances / writable | 1,382 / 1,311 |
| Defensive instances / writable | 393 / 375 |
| Backing semantic objects / operation groups / shared scopes | 226 / 327 / 115 |
| Defensive backing objects / operation groups / shared scopes | 97 / 110 / 40 |
| Deployed entities (health / armor writable) | 18 (18 / 18) |
| Mounted weapons / ammo instances / fire-rate instances / heat instances | 12 / 44 on 11 weapons / 9 / 4 |
| Defensive Projectile / DamageInfo / Explosion / Beam / Arc / Status / Spray branches | 9 / 19 / 7 / 1 / 1 / 8 / 0 |
| Writable cooldowns | 71 (18 defensive) |

The reader validates schema 2 strictly: the instance audit must be an exact match; each backing object and each operation group is cross-checked against its field instances (kind, shared scope key, consumers, target, plan groups, phase, recommended API); deployed-entity records must match their roots, field lists, attack roles and weapon branches; mines with unresolved instances must have no attack or weapon fields; summary counts are recomputed; and the public document must contain no native layout properties (offsets, record indices, native identities) or hexadecimal addresses. Unknown schemas fail closed. Schema 1 (0.21) catalogs continue to load unchanged.

## GUI behaviour

- Stratagems browser tabs: All, Orbital, Eagle, Support Call-Ins, Sentries, Emplacements, Mines / Deployables.
- Defensive graph: Stratagem → Deployed Entity (reusable Entity Stats: Health, Armor) → Mounted Weapon · Primary (Ammo, Weapon, Heat, Heatsinks) → Attack branches (Projectile, Damage, Explosion (impact), Explosion Damage, Beam, Arc, Status · slot N). Only sections with published instances render; every instance appears exactly once.
- Mines show the root cooldown and deployment-entity health/armor only, with Runtime's reasons for mine entities, triggers, distribution and mine explosion/status. Grenadier Battlement shows its blocked mounted weapon. Targeting, deployed/projectile lifetime, penetration slowdown and max uses show "Not currently writable" with the SDK reason.
- Acknowledgement is keyed by the published `sharedScopeKey` and records the exact reviewed consumer list; the consumer list comes from the backing object. One acknowledgement covers every consumer of that scope (for example the StatusEffectSettings shared by the Laser Sentry, Flame Sentry and three Napalm deliveries). `allow_shared=true` is emitted only for acknowledged shared operations.
- Planning uses the published operation group (one transaction target), backing object (plans sharing a backing object are joined) and phase/dependency metadata. Backing objects and operation groups are never assumed equal. Distinct status slots stored on one DamageInfo object are separate operations in one `hd2.plan`, which lifts the 0.21 export block. A non-blocking notice appears when enabled edits with the same API field on one backing object disagree.
- Lua targets follow the public API: `hd2.stratagem(name):deployed_entity()`, `…:weapon('primary')`, `…:weapon('primary'):attack(role)`. Runtime 0.22 exposes only the `main` entity, and its attack handles resolve under the `primary` weapon; any other identity fails closed (no published instance is affected).

## Persistence and rebind

Project format 5 adds `entity`, `weapon` and a target kind (`stratagem`, `deployed_entity`, `mounted_weapon`) to stratagem changes. Formats 1–4 still load. No addresses, offsets, record indices, native resource IDs, component IDs, backing IDs or operation-group IDs are written to projects or Lua.

Runtime 0.22 renamed every 0.21 canonical instance key while keeping each semantic target (stratagem, path, entity, weapon, attack, field) and baseline. Explicit rebind moves each saved change to the unique instance with the same semantic target. Evidence and acknowledgements carry over only when the previously valid capability and reviewed scope content are unchanged. 28 of the 989 0.21 instances changed shared-scope content (for example Napalm status objects now also serve two sentries); those require **Accept current capability** and a fresh acknowledgement. Baselines and desired values are never changed silently.

## Validation

- Full GUI suite: **402 passed, 0 failed** (348 existing + 54 new). The 0.21 regressions now run against the published 0.21.0 SDK archive fixture; the published 0.22.0 archive is also installed and reloaded from cache in tests.
- A GUI defect was fixed: the SDK cache previously capped the stratagem catalog at the 2 MiB composition-graph limit, so the 3.18 MB 0.22 catalog could not be installed or reloaded.
- Runtime acceptance: `dotnet run --project tools/HD2RuntimeGUI.Sample -- <samples> --runtime-validate --runtime-src=<HD2Runtime b39abcc source>` runs GUI-generated Lua through Runtime's own `patches`, `transactions` and `composition_plans` validators in HD2's standalone `lua51.dll` (the same offline harness as Runtime's release tests; no game process). Result: **3,829 passed, 0 failed**: all 1,311 writable instances accepted, 2,443 unsafe variants (missing `allow_shared` or stale baseline) rejected, 71 whole-stratagem grouped transactions/plans accepted and the four samples accepted.
- Desktop automation: `node tools/runtime022-smoke.mjs` (CDP port 9236, isolated data root) passed against Debug and the self-contained Release executable: tabs and counts, E/AT-12 graph and modified values, health/armor autosave and Lua preview, Changes grouping, mines, Grenadier Battlement, Laser Sentry heat/beam/status sections and shared consumers, Tesla Arc section, and exports.
- Screenshots (local, ignored): `docs/screenshots/runtime022-*.png`.

## Samples (generated, not deployed)

`dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/runtime022/samples --defensive`:

| Project | Changes |
| --- | --- |
| AntiTankEmplacement022 | cooldown 180 → 90; health 300 → 600; armor 2 → 3; projectile mass 6500 → 7000; impact outer radius 6 → 8 (one plan) |
| ConventionalSentry022 | A/MG-43: cooldown 90 → 60; health 400 → 800; armor 2 → 3; capacity 175 → 350; fire rate 630 → 900 |
| ExplosiveSentry022 | A/MLS-4X: impact outer radius 4 → 8; explosion standard damage 150 → 220 |
| UnusualSentry022 | A/LAS-98: beam length 200 → 240; heat capacity 250 → 400 |

No mine attack sample is generated because individual mine attacks are not resolved. Exports are byte-identical across independent workspaces.

## Runtime SDK observations (not blocking)

- `stratagem_attack` in Runtime 0.22 always tags deployed attack handles with weapon `primary`; a future multi-weapon entity would need the attack handle to carry its weapon identity. No 0.22 instance is affected, and the GUI fails closed for non-primary attack targets.
- Remaining blockers are Runtime-declared: individual mines, triggers and distribution, Grenadier Battlement weapon ownership, targeting, deployed/projectile lifetime, penetration slowdown, max uses and spray settings.
