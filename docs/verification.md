# 0.13 player-weapon authoring verification

## UX polish validation

The follow-up UI pass uses actual SDK-baseline differences for highlighting and counts, shows numeric/boolean before-and-after values, and shares collapsed weapon summaries between Overview and Changes. Show all fields defaults off; unchanged fields remain neutral. Suppressed retains its SDK label with an explanation of the Runtime flag. Generation, SDK/cache behavior, metadata/project formats, packaging and snapshot logic were not changed.

`tools/ux-smoke.mjs` exercised numeric and boolean edits, unchanged fields, multiple edits on one weapon, multiple weapons, section grouping, Show all fields, field/weapon resets and saving a baseline value. Its `--relaunch` mode verified persisted numeric/boolean edits, collapsed groups and the default-off toggle after restarting the actual desktop app. All 84 automated tests passed, and the final Windows build completed with zero warnings/errors. The build used `artifacts/ux-build/` because the user's running application locked the normal output; that application was left running. UI test libraries and screenshots remain ignored local artifacts.

Upstream authority: [HD2Runtime v0.13.0](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.13.0), commit `2d1be38a1830bb973140b0b431e58d3c2fa9e081`. The SDK and ModTemplate assets were downloaded from the public release and checked against its published SHA-256 digests:

| Asset | SHA-256 |
| --- | --- |
| HD2Runtime-0.13.0-sdk.zip | `22078a02bb3a573da1d35f4f3692e17e18c085c02a26d16e3dd7d3f9f7cbdf56` |
| HD2Runtime-ModTemplate-0.13.0.zip | `5459aef0e882ba8630f5c79b3be45a792406f69019f4c1b0685fd367439494aa` |
| HD2Runtime-0.13.0-example-projects.zip | `6803d88ae4099560138abcc674a3469f5f327995c67123abf91ac6b0bfef629a` |

The bundled catalog is the unmodified SDK asset: 80 identities, 73 uniquely writable identities, seven fail-closed duplicate identities, 2,667 entries, 1,772 editable entries, and 48 definitions (38 writable, 10 read-only, three derived). Fifteen distinct backing groups contain editable shared fields; the catalog also describes one group only in blocked/read-only entries. Some shared damage consumers are unnamed; the UI still requires approval.

## Automated tests

84 tests pass with no game dependency. Coverage includes:

- Existing release parsing, semver comparison, current/newer SDK decisions, Ignore This Time, safe install, failed-update preservation, offline fallback, unsupported/malformed schemas, archive traversal and cache-path rejection.
- Actual published capability parsing/counts, field lookup, derived/read-only/duplicate handling, shared acknowledgement and changed scope, conflicting shared changes, duplicate overrides and SDK baseline/missing-field review.
- Every one of the 1,772 writable capability entries generates a semantic constant checked against the published SDK API stub. The stub is a test fixture only.
- Project creation, identity/GUID validation, format persistence/reload, folder-opening abstraction, Lua patch/transaction/ensure behavior and deterministic exports.
- Golden Lua and deterministic ZIP tests for all three requested mods, correct expected baselines and Runtime dependency metadata, package inventory and absence of Runtime implementation.
- HD2SNAP header/index validation, bounded read-only raw reads, malformed headers, persisted/relinked paths, and mapper report fingerprint/read-only checks.

## Desktop validation

The actual Debug MAUI app was launched with an isolated library at `artifacts/player-weapons-ui`. WebView2 UI actions exercised project creation, SDK status, weapon search/selection, field controls, saving, Changes, Lua Preview, Build/Export and Open Export Folder for:

- `Concussive1100`: AR-23C fire rate 400 → 1100.
- `VerdictFlatTrajectory`: drag 1.2 → 0.1 and gravity 1 → 0.2.
- `ReprimandFlatTrajectory`: drag 1.2 → 0.1 and gravity 1 → 0.2.

Both trajectory projects leave velocity/mass untouched. The exported source from each actual GUI ZIP was compared exactly with its golden Lua. Closing/relaunching restored all three project cards.

The gameplay archive in each GUI ZIP was rebuilt using the archive codec from the downloaded, digest-verified 0.13 ModTemplate; all three were byte-identical to that reference codec's output.

Additional desktop checks covered ARC-12 Blitzer, LAS-13 Trident, FLAM-66 Torcher, CQC-19 Stun Lance and SG-8 Punisher. Arc and Beam did not show conventional projectile-drag controls. Shared AR-23 Liberator drag blocked generation until explicitly approved; approved output contained `allow_shared=true`. Reset removed that test change. LAS-5 Scythe showed its ambiguity explanation and no editable numeric controls.

The Snapshot Research page is integrated. File-format and relink/report behavior are tested with synthetic files; no game capture was taken. A full native snapshot-picker/report inspection with a real capture was not performed. The report lacks addresses, so automatic semantic-to-byte navigation remains unavailable.

Reproducible developer UI helpers are `tools/player-weapons-smoke.mjs` with normal, `--families`, `--relaunch` and `--safety` modes. They require a running Debug app with an isolated WebView2 CDP profile. Node is developer tooling only. Local screenshots are in ignored `docs/screenshots/`, including the three editors/Changes views, family views, shared approval, duplicate blocking and restored library.

## Outputs and builds

GUI-generated test ZIPs:

```text
artifacts/player-weapons-ui/Exports/Concussive1100-0.1.0.zip
artifacts/player-weapons-ui/Exports/VerdictFlatTrajectory-0.1.0.zip
artifacts/player-weapons-ui/Exports/ReprimandFlatTrajectory-0.1.0.zip
```

The final public-release integration run (`tools/HD2RuntimeGUI.Sample --online`) successfully discovered, downloaded, digest-validated, installed and parsed SDK 0.13.0, then generated all three mods in `artifacts/live-release-0.13/Exports/`.

The Windows Debug build passed without compiler warnings/errors. Self-contained Windows x64 publish succeeded at `artifacts/publish/win-x64`; NuGet reported NU1900 because its vulnerability-data endpoint was unavailable during restore. This did not prevent package restore or publishing.

No game was launched, no mods were installed/deployed, and the HD2Runtime working tree was left unchanged. Runtime behavior during gameplay remains for the user's manual validation.

## Field autosave cleanup

Weapon editor controls now commit through the existing workspace/change service on change (numeric inputs on blur, switches on toggle). Save change buttons are removed. Typed comparison is shared by persistence and Modified highlighting: exact booleans/integers and Float32 round-trip values. Committing the SDK baseline or using Reset field removes the override. Older redundant overrides are removed when a project opens; missing/type-changed capabilities remain available for migration review.

The automated suite passes 101 tests, including create/update/remove, equivalent float spellings, boolean toggles, invalid/incomplete input, concurrent field commits, reload cleanup and shared-write gating. Existing Lua/ZIP golden tests pass unchanged. The Windows Debug build passes with no warnings or errors.

Desktop WebView2 checks used the isolated `artifacts/autosave-validation` library and `tools/ux-smoke.mjs` (normal, `--safety`, `--relaunch`). They verified automatic persistence, immediate Modified/count/group updates, invalid input preserving the last saved value, neutral equivalent floats, boolean round trips, field/weapon reset, multiple weapons, explicit shared acknowledgement, duplicate write blocking and application relaunch. Local screenshots remain ignored under `docs/screenshots/`.

## SDK 0.14 ammo authoring

Consumed the public `HD2Runtime-0.14.0-sdk.zip`, SHA-256 `ec6cb938dc86c77cff7f431ee3bf5b10e98128d1bd66813b2445134ec1908749`. Its build report identifies commit `742138ff7b2d7c9924abc9cba538471e645d2e31`. The bundled metadata files are exact published bytes. Live discovery/download/digest validation/cache installation passed through `SdkUpdateService` and `SdkCache` using the sample helper's `--online --ammo` mode.

The release's GitHub tag reference currently resolves to the older `2d1be38a1830bb973140b0b431e58d3c2fa9e081`, and the supplied 0.14 commit is not available through the public commit endpoint. This upstream publication discrepancy does not block consumption of the consistent, digest-verified 0.14 release assets. No upstream files or safety rules were changed.

Loaded counts:

- 80 player weapons, 3,027 authoring entries, 1,966 writable entries.
- 60 semantic definitions: 47 writable, 13 read-only, including 6 derived.
- 360 semantic ammo entries, 194 writable, across 45 weapons with writable ammo fields. The new surface adds 9 writable and 3 derived/read-only definitions.
- Companion ammo catalog: 80 identities; 33 direct-magazine and 15 rounds-feed weapons; 19 customization presets; 13 not-applicable weapons; one shared default-preset group. Some direct/rounds identities remain blocked.

Both catalogs are parsed into typed models and cross-validated for versions, fingerprints, identities, scalar baselines, permissions and summary counts before atomic cache installation. Controls and Lua still come exclusively from published semantic authoring descriptors. Companion-only read-only evidence for a blocked identity does not manufacture extra controls. Existing 0.13 tests retain the original release fixtures; projects remain pinned until explicitly rebound. No project-format or generator/package changes were necessary.

The final automated suite passes **130 tests**. New coverage includes all detachable-magazine writes, dual feeds, derived capacity/ammo-box values, all customization presets, not-applicable cases, shared gating, GP-31, both catalog discrepancies, autosave/reload/reset-to-baseline, explicit 0.13→0.14 rebind, failed/malformed/missing/unsafe SDK updates, offline cache, published constant validation, golden Lua and deterministic ZIPs. Existing 0.13 goldens remain unchanged.

Desktop validation used `tools/ammo-smoke.mjs`, including `--relaunch` and `--review`, with the isolated `artifacts/ammo-ui` library. Both sample projects were created, edited, reset, autosaved, reviewed, built and reopened after application restart. Their previews match the golden source exactly. Preset explanations, the shared read-only group, GP-31 diagnostics, absent Blitzer ammo controls, derived badges and Arbitrator/One-Two warnings passed UI assertions. Existing player-weapon smoke tests also passed for the three earlier sample projects, Arc/Beam/Flame/Melee/rounds-feed rendering, shared approval, duplicate blocking and snapshot-page availability.

GUI-generated samples, ready for manual installation/gameplay testing:

```text
artifacts/ammo-ui/Exports/VerdictMagazine-0.1.0.zip
artifacts/ammo-ui/Exports/PunisherDualFeed-0.1.0.zip
```

The 0.14 ModTemplate's archive/dependency conventions remain compatible with the existing exporter. Packages declare HD2Runtime 0.14.0/API 1 and Bingus dependencies and contain no Runtime implementation or GUI metadata. Sample source and values are documented in `samples/README.md`.

Windows Debug build succeeded without warnings/errors at `artifacts/ammo-build/Debug/net10.0-windows10.0.19041.0/win-x64/`. Screenshots remain in ignored `docs/screenshots/`, including `VerdictMagazine-ammo-editor.png`, `PunisherDualFeed-ammo-editor.png`, `ammo-gp31-blocked.png` and `ammo-catalog-disagreement.png`. No support-weapon work, snapshot changes, game launch or mod deployment was performed.

## SDK 0.14.1 / capability schema v2 aliases

Consumed the public `HD2Runtime-0.14.1-sdk.zip`, SHA-256 `b032dee479d5328539140770116821dcf40cc6d8d7e3ef66a2553428e5d9b106`. Live discovery, digest verification and installation passed through the app's release/cache services. Bundled authoring, ammo and base metadata preserve the published bytes. The authoring catalog uses schema 2; base metadata and the companion ammo file remain schema 1.

The typed reader retains the six field alias attributes, three published alias rules and collision-audit evidence. It validates canonical targets, flags, semantic targets, accepted writes and counts before installation. Grouping uses explicit per-weapon `aliasOf` declarations, never inferred backing offsets or semantic-target equality. The catalog has 3,027 entries, including 62 alias instances; 2,965 preferred entries and 1,907 editable entries remain. Read-only `weapon.base_capacity` remains visible independently of `magazine.capacity` even when their backing offsets match.

Existing 0.13/0.14 projects stay pinned until explicit rebind. Saved source records and baselines remain intact on opening/rebinding. A canonical project view coalesces equal values for controls, counts, Changes and deterministic Lua; generation validates every enabled source before coalescing. An edit saves the canonical identifier and preserves the retained source's expected value and baseline SDK version. Different desired values, duplicate records or conflicting saved baselines produce migration conflicts; build/export remains blocked until the user chooses a source or resets the field. Choosing a source preserves its original baseline, which still requires explicit acceptance if it differs from the current SDK. Baseline-valued or disabled sources cannot conceal a conflict through no-op cleanup or enable toggles.

**152 automated tests pass**, including each of:

- `weapon.capacity` → `magazine.capacity`.
- `weapon.feed_capacity_1` → `rounds.feed_capacity_1`.
- `weapon.feed_capacity_2` → `rounds.feed_capacity_2`.

Tests cover pinning/rebind, value/baseline retention, editing/reload/reset, equal-value coalescing, differing-value conflicts/resolution, deterministic Lua/ZIPs, non-alias fields sharing an offset, rejected writes, schema downgrade attempts, malformed targets/flags/counts and existing 0.13/0.14 regressions. `Fixtures/sdk-0.14.0.zip` contains only the three published metadata files for compatibility tests; it contains no runtime or executable SDK scripts.

Desktop checks used `tools/alias-smoke.mjs` with an isolated `artifacts/alias-ui` library: Verdict capacity was saved under its old alias; Punisher had conflicting feed-1 and equal feed-2 alias/canonical records. The UI showed only canonical controls, retained the distinct base-capacity row, displayed one Changes row per canonical field, blocked conflicting generation, saved a source choice and successfully exported canonical Lua. All choices/edits survived application relaunch (`--relaunch`). The conflict panel refresh was verified after resolution. Screenshot: ignored `docs/screenshots/alias-migration-conflict.png`.

Windows Debug build passes without warnings/errors at `artifacts/alias-build/Debug/net10.0-windows10.0.19041.0/win-x64/`. No Runtime files, packaging format, snapshot behavior, game processes or deployed mods were changed.

## SDK 0.15.0 / guarded player composition

Consumed the public `HD2Runtime-0.15.0-sdk.zip`, SHA-256 `e3f6b7787544b06a440cb719c1f4d1f9eda99138431c7d157778dfafc814c79b`, whose build report identifies Runtime commit `c1b9960cd2b5b679a9acd370ab7cd27fb5582e45`. Bundled metadata is copied byte-for-byte from that release. Production release discovery, digest verification, schema validation and installation succeeded with `tools/HD2RuntimeGUI.Sample --online --ammo` using an isolated library. No Runtime implementation was downloaded by the app or bundled into exports.

Loaded 80 player weapons, 3,094 capability entries, 62 definitions (48 writable, 14 read-only, six derived), 1,952 editable entries and all schema-v2 alias rules. Four additional schema-1 graphs are required for 0.15 SDK installation. They are bounded, parsed into typed models and checked against catalog versions, build fingerprints, weapon identities, permissions, baselines and counts. Installation still stages all validated metadata atomically and preserves older pinned versions. The 0.14.1 regression fixture contains only the three original metadata files.

Composition renders 67 projectile selectors across 66 weapons, including 45 writable conventional selectors. Only approved compatible semantic sources appear. SG-20's primary feed is writable; its alternate status feed and CB-9 explosive selector remain blocked. Raw projectile IDs cannot enter scalar authoring or generated Lua. Semantic source/target attack handles and deterministic patch IDs use the published API; persistence uses the standard ensure behavior.

Project format 3 adds `projectileChanges`; formats 1/2 remain readable. References store weapon/attack identities, baseline SDK, compatibility class and opaque evidence hashes, plus enabled/persistence/group/notes. No runtime addresses, raw projectile IDs or backing offsets enter projects. Autosave, baseline removal, reset, duplicate and reload operate through the workspace/change service. Rebind preserves existing baselines and flags missing attacks, read-only selectors, changed target/source identities, changed classes and invalid sources; explicit acceptance or reset is required. Existing scalar and alias migration behavior is covered by the retained regressions.

Read-only composition inspection includes 52 native magazine option identities, 19 proven defaults and zero writable option-owned records. Six weapons have observed option identities without a proven default: these are labelled as unproven relationships. All 80 native fire modes are displayed numerically, without invented enum meanings. The 134 impact/expiry descriptors show 13 impact and five expiry ExplosionSettings links, or None/Unresolved according to metadata. Inspection describes SDK baselines, not simulated project results.

**Support contract limitation:** the published SDK contains `tools/support_weapon.py` and documentation for snapshot scanning, but no stable support-weapon GUI catalog/capability artifact. Therefore Support Weapons displays a contract-status shell, zero loaded entries and no authoring controls. The research mapping's 35 identities were not hardcoded or imported from internal research outputs. Snapshot browsing and Runtime code were unchanged. This is the only remaining integration limitation in this pass.

The automated suite passes **180 tests**, including 0.15 loading, missing/malformed graphs, rejected updates preserving the old cache, source filtering, explosive/status/shared/ambiguous blocking, semantic Lua golden output, deterministic ZIP/dependencies, persistence/duplicate/no-op cleanup, rebind validation and old alias-project compatibility. Windows Debug build passes with zero warnings/errors at `artifacts/composition-build/Debug/net10.0-windows10.0.19041.0/win-x64/`.

Desktop validation uses tracked `tools/composition-smoke.mjs` against the isolated `artifacts/composition-ui` library. It creates the project, selects JAR-5 and Verdict, checks autosave/highlighting/Changes/preview, returns to baseline, restores the swap, builds the ZIP, and verifies persistence after desktop restart. The catalog sweep renders all 80 weapons and asserts exactly 67 selector cards / 45 writable controls, with no controls in read-only inspection panels. Magazine presets/observed options, numeric JAR-5 fire mode, Crossbow terminal explosion, Halt's two feeds and the support contract notice are asserted. Screenshots are ignored under `docs/screenshots/composition-*.png`.

GUI-generated gameplay sample (not installed or deployed):

```text
artifacts/composition-ui/Exports/Jar5VerdictProjectile-0.1.0.zip
```

Sample ZIP SHA-256: `faaae0a4fc54ecc37a525dcb423f5314d66c3a0ace54a9e7d936d6829a087ffd`. It declares HD2Runtime 0.15.0/API 1 and Bingus dependencies, contains eight validated entries, and includes no Runtime implementation, SDK catalogs, research files or snapshots. Additional production-download regression exports are under `artifacts/composition-online/Exports/` (VerdictMagazine and PunisherDualFeed). No game was launched and no mods were deployed.
