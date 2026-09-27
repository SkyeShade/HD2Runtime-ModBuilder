# Runtime 0.21 stratagem integration

## Published source and audit

- Release: https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.21.0
- Tag commit: `0250e678af87d35f5c5dce576921732f8d87cd52` (verified through GitHub's tag API).
- SDK asset: `HD2Runtime-0.21.0-sdk.zip`, 696,154 bytes.
- Verified SHA-256: `13c6f0cf930b06679450be51cf6d5ae70afa10940af9806ed64c695d19c9aab5`.
- Canonical contract: `hd2runtime.stratagem.guarded_authoring.v1`, schema 1, `fieldInstances`.
- Stratagem catalog SHA-256: `786126cb35dece0a31485ccf02a69fa514f7a58d7b087465f4570d10f4403ce5`.

All consumed bundled metadata is copied byte-for-byte from that SDK. Some player composition artifacts still declare 0.19; only the exact previously reviewed SHA-256 values are accepted under the published 0.21 release. The new stratagem artifact does not contain its own runtime-version property: release/cache identity is validated through root metadata and the release digest. No internal research file is an authoring input.

| Catalog property | Loaded |
| --- | ---: |
| Visible stratagems | 55 |
| Offensive / Orbital / Eagle | 20 / 12 / 8 |
| Support call-ins / resolved | 35 / 33 |
| Canonical instances / writable | 989 / 936 |
| Backing objects / operation groups | 135 / 135 |
| Reviewed shared scopes | 81 |
| Descriptive attack branches / native backing branches | 55 / 143 |
| Writable cooldowns | 53 |
| Writable Eagle uses / rearm handles | 8 / 8 |
| Writable max-uses fields | 0 |

`SG-88 Break-Action Shotgun` and `CQC-72 Entrenchment Tool` remain visible with the released unresolved-root reason. Support call-in metadata contains definition settings only, including Solo Silo; its weapon missile/explosion graph remains in Support Weapons. The GUI does not invent stratagem graph capabilities absent from the published artifact.

## Implementation

Typed C# records preserve roots, native attack roles/paths, descriptive branches, exact baselines, units, provenance, read-only reasons, API constants, backing/operation/plan identities and phase/dependency metadata. The reader validates canonical counts, identity uniqueness, target paths, API constant syntax, shared-scope consistency, scalar types and the published phase-1/no-dependency contract. Unknown sequencing fails closed.

The separate Stratagems browser offers family, system, writable/blocked/shared filters. Branch sections retain distinct instances. The legacy mapped-resource editor remains available under an explicit legacy label. Overview/Changes groups modified fields by stratagem and branch; the shared Eagle rearm override has one `Eagle Shared System` group. Normal controls show no native IDs or offsets.

Project format 4 gains `stratagemChanges` and `stratagemApprovals`; older project files default to empty collections. Changes save semantic target kind/name/path/attack/instance/field, baseline and desired values, baseline SDK version, enabled/persistence/group/notes and an ownership-evidence digest. The SDK's opaque backing identity is its published shared-scope key; no addresses, record indices or raw resource hashes are generated or persisted. Approval evidence includes sorted affected consumers and completeness/dynamic-consumer flags. Scope changes invalidate approval without silently updating the user's baseline.

All eight Eagle rearm handles explicitly share one backing object and API field. Editing any handle updates one existing semantic change. Other branch instances are never deduplicated by name or offsets. Resetting to the normalized baseline removes the override. Float comparisons and Lua values use Float32 round-trip semantics.

Generation uses the catalog's operation group and plan group directly. One object emits patch/transaction; related objects emit a single ensured plan. Connected plan groups sharing a backing object are joined to avoid racing ensures. Per-object approvals are checked before generation, and `allow_shared` is emitted only for approved operations. Current published phases are all 1 with empty dependencies; no phase or native structure is inferred.

## Remaining released-contract limitation

The SDK assigns status-strength fields to their parent DamageInfo object while giving each status slot its own semantic attack target. For example, Orbital Napalm Barrage publishes:

- `delivery_1_projectile_impact_damage_status_1`, `status.strength`, baseline **100**;
- `delivery_1_projectile_impact_damage_status_2`, `status.strength`, baseline **50**;
- both use `hd2.fields.status.strength`, backing and operation group `damageinfo:9f3ed8ffbf63f538`.

These are distinct instances and cannot be collapsed. A transaction has one target, so choosing either target for both fields would direct both writes at one slot. Splitting the SDK operation group into concurrent ensures would violate its ownership grouping. The GUI preserves both edits and blocks their combined export with a composition-conflict message. Other differing targets in one published operation group are treated conservatively the same way, except the explicitly shared Eagle rearm handles. A future SDK needs compatible target-aware operation groups or a documented operation form for multiple slot-specific targets on one object. Single-slot status edits and all five requested samples are supported. HD2Runtime itself was not modified.

## Validation

- Full GUI suite: **348 passed, 0 failed** (325 existing cases plus 23 new cases).
- Every one of the 936 writable descriptors creates and validates a typed scalar override and generates a semantic patch in regression coverage.
- Tests cover Orbital Laser cooldown/damage/ability plans; Eagle scope coalescing; read-only/unresolved roots; per-object approval isolation; exact reset/no-op handling; reload/duplicate; changed baseline/consumer review; explicit 0.20.1 to 0.21 rebind; malformed/missing/unsupported catalog rejection; deterministic Lua/ZIP; and status-branch conflict rejection.
- Windows Debug build and self-contained Release publish succeeded. A pre-existing GUI locked the normal Debug output; the validation build used an isolated output directory.
- Desktop automation: `node tools/runtime021-smoke.mjs`, CDP port 9234, isolated data root. All five sample projects opened and exported through the actual MAUI UI. Verified editable Orbital Laser damage, immediate preview update, cooldown reset-to-vanilla, branch sections, max-uses read-only presentation, Eagle value/approval reuse across consumers, one shared Changes group, and unresolved call-in controls disabled.
- Screenshots are local ignored captures in `docs/screenshots/runtime021-orbital-laser.png` and `runtime021-eagle-shared.png`.
- The same five-project desktop smoke passed again after closing Debug and opening the self-contained Release executable, using CDP port 9235 and the retained isolated project library. This verified project persistence across application restart and Release Lua/build initialization. Logs: `artifacts/runtime021/tests.log`, `build-isolated.log`, `publish.log`, `ui.log`, `release-ui.log`.

## Samples and application

Generate samples with `dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/runtime021/samples --stratagems`. This uses the same project/change/persistence/generator/export services as the UI and explicitly acknowledges scopes for these requested proof samples.

ZIPs under `artifacts/runtime021/samples/Exports/`:

- `OrbitalLaser021-0.1.0.zip`: cooldown 300 → 180; damage 60 → 400.
- `OrbitalPrecision021-0.1.0.zip`: cooldown 80 → 40; outer explosion radius 12 → 20.
- `Eagle021-0.1.0.zip`: cooldown 15 → 10; uses 2 → 4; acknowledged shared rearm 150 → 90.
- `SupportCallIn021-0.1.0.zip`: Recoilless call-in cooldown → 120.
- `GasOrNapalm021-0.1.0.zip`: Orbital Gas Strike first status branch duration → 30.

Project JSON files are retained under the sample workspace's `Projects/`. Lua previews are also saved at its root. ZIP validation uses the existing package inventory/dependency checks; HD2Runtime remains a separate install. Nothing was deployed and HD2 was not launched.

Published app: `artifacts/publish-0.21/win-x64/HD2RuntimeGUI.exe`. Distribute the entire self-contained folder. WebView2 Evergreen remains an external Windows prerequisite.
