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
