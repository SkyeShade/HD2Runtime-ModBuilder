# Runtime 0.20.1 support authoring

The [0.20 SDK blocker](runtime020-sdk-blocker.md) is resolved by the published canonical schema-v2 contract. HD2Runtime itself was not modified.

## Release and metadata

Authority: [HD2Runtime v0.20.1](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.20.1), build report commit `8c38cffa522480711e582995ac0c16e1b4802b0f`.
The downloaded `HD2Runtime-0.20.1-sdk.zip` is 645324 bytes, SHA-256 `6056e8492597a0672b3091c9421c7e0e8534e5f9a9d03d465e1a23403210ef16`.

`SupportWeaponAuthoringCapabilities.json` is consumed as typed `hd2runtime.support_weapon.guarded_authoring.v2` metadata:

- 35 support weapons, 27 writable unique identities.
- 828 canonical instances consumed without deduplication; all are available as individual editor controls.
- 506 branch-specific instances; 16 repeated semantic-field groups retain all 32 instances.
- 146 backing semantic objects, 155 Runtime-safe operation groups, 81 shared object/scopes.
- The legacy 812 flattened names are ignored for authoring. Nothing is reconstructed from research files or native offsets.

Covered fields: weapon 232, projectile 80, damage 198, explosion/linked damage 204, magazine 48, rounds 15, charge 20, heat 4, heatsink 3, arc 6, beam 2 and status 16.

The eight blocked identities remain visible with exact reasons: B/FLAM-80, CQC-20, CQC-72, EAT-17, LAS-98, M-105, MG-206 and MG-43. There are no edit controls for them. Backpack storage, unresolved branches, Railgun Max Charge and linked stratagem scalars remain inspection-only where the SDK says ownership is unproven.

The SDK cache validates canonical instance/weapon/object/group joins, counts, version, types, safe API/accessor syntax, shared scope consistency and the supported phase contract. ZIP path, repository, digest and size protections remain in place. Only the new canonical artifact has an 8 MiB JSON limit; it is currently about 4.3 MB. Invalid updates preserve the old cache. Projects remain pinned until explicitly rebound.

The release reuses eight player graph/evidence artifacts byte-for-byte from 0.19, including their older version labels. `PublishedArtifactVersion` permits only the exact reviewed SHA-256 digests in the 0.20.1 archive; fingerprints, capability relationships, baseline and safety validations still run. Arbitrary version drift or modified old artifacts are rejected. The updated plan contract also explicitly declares the support capability source.

GitHub's public release API was rate-limited during this session. The public release asset was downloaded directly over HTTPS and its build report checked against the requested commit. Public API discovery, artifact naming and offline/cache behavior remain covered by tests; no token requirement or network bypass was added to the application.

## Editor and persistence

Support Weapons now has search, family/system, writable-only and shared filters. It retains the graph/ownership-chain inspector while adding applicable semantic controls grouped by branch and domain. This preserves conventional, explosion, charge, arc, beam, spray, melee, status, heat/heatsinks, magazine/rounds, placed-explosive C4 and Solo Silo missile/explosion presentations.

Valid committed edits autosave; Float32 round-trip/exact integer comparisons remove overrides returned to baseline. Modified cards show original to desired values, with reset actions and immediate Changes/Lua updates. Overview/Changes groups support modifications by weapon, collapsed initially, with only modified fields shown unless Show all fields is enabled. Enabled state and reset field/weapon/all are supported. Read-only graph information never becomes a change.

One checkbox authorizes an SDK acknowledgement scope and lists its semantic consumers. Approval applies to all fields of that exact object, including subsequent edits; it does not authorize other scopes. Approval evidence incorporates the object and consumer scope, so rebind changes invalidate it.

Project JSON format 4 adds optional `supportChanges` and `supportApprovals`. Records contain semantic instance/weapon/branch/field identity, expected and desired scalars, baseline SDK version, capability review evidence, enabled/persistence/group/notes. They contain no Runtime plan request, native ID, hash, address or offset. Opaque SDK semantic keys and review digests are retained. Earlier project files load with empty support collections. Duplicate/import/reload paths preserve support intent. Missing capabilities, changed baselines/types/targets or ownership remain reviewable and block affected builds until explicitly accepted or reset.

## Generation

`SupportLua` uses SDK `transactionGroupingKey`, `planGroupingKey`, backing object, accessor and `apiFieldConstant` directly. Single fields use patch; siblings in a Runtime operation group use transaction; multiple groups in a related composition use one plan. Shared object identities also connect edits spanning support weapons into one coordinated plan. Contradictory values for the same published object/field are rejected before export. IDs and ordering are deterministic.

The current public support contract specifies phase 1, empty dependencies and no `target_from` requirements. The generated operation list implements that contract. Unknown future sequencing is rejected rather than guessed; existing player replacement plans still use their ordered phases and `target_from` implementation unchanged.

Each operation validates its own current shared approval before emitting `allow_shared=true`. No raw resource hashes, process addresses, native offsets, projectile IDs or explosion IDs appear in generated Lua. Existing Runtime safety checks remain untouched. Persistence defaults to Runtime's standard ensure behavior.

## Samples

All four were created/edited, approved, exported and reopened through the actual desktop GUI. The ordinary service-based sample tool independently produced byte-identical ZIPs.

| Project | Changes | GUI export |
| --- | --- | --- |
| RecoillessTuning020 | Projectile velocity 250 to 350; outer radius 3 to 10 | `artifacts/0201-ui/Exports/RecoillessTuning020-0.1.0.zip` |
| ArcThrowerTuning020 | Arc range 55 to 75 | `artifacts/0201-ui/Exports/ArcThrowerTuning020-0.1.0.zip` |
| C4Explosion020 | Outer radius to 15; standard explosion damage to 1500 | `artifacts/0201-ui/Exports/C4Explosion020-0.1.0.zip` |
| SupportAMRTuning020 | Ergonomics to 80; sway to 0.5; projectile velocity 880 to 1100 | `artifacts/0201-ui/Exports/SupportAMRTuning020-0.1.0.zip` |

Exact baselines and generated Lua are checked into `HD2RuntimeGUI.Tests/Golden/*020.lua`. Recoilless has one ensure/plan with two backing-object operations; ARC-3 has one ensured patch; C4 has one plan separating explosion settings and linked DamageInfo; AMR has one plan containing a weapon handling transaction and a projectile operation. Each ZIP contains the established eight gameplay-mod entries and declares HD2Runtime 0.20.1/API 1 and Bingus dependencies. No Runtime implementation or GUI metadata is included.

No game was launched and no mods were installed or deployed.

## Verification and reproduction

- Full suite: **325 passed**, zero failures/skips. Includes all 828 canonical instances individually generating semantic Lua, all 27 identities, blocked duplicates, special families, repeated branch fields, scope isolation, grouping/plans, malformed metadata, exact reused-artifact validation, pin/rebind, save/reload/no-op/reset/duplicate, four golden samples and deterministic ZIPs.
- Windows Debug build: succeeded, zero warnings/errors.
- Self-contained Windows Release publish: succeeded at `artifacts/publish-0.20.1/win-x64/HD2RuntimeGUI.exe`. Distribute the entire folder; WebView2 Evergreen is required, developer tooling is not.
- Desktop smoke: all 35 support views, 828 numeric controls and eight blocked identities verified; all samples exported through the GUI. Screenshots remain local/ignored under `docs/screenshots/runtime0201-*`.
- After a real process restart, all four persisted projects reopened and rebuilt successfully. All 80 player views also retained 21 fire-mode controls, 130 terminal selectors, 419 read-only attachments and 30 writable heat controls.

```powershell
node tools/inspect-runtime020-sdk.mjs artifacts/sdk-0.20.1
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/0201-samples --support
dotnet publish HD2RuntimeGUI/HD2RuntimeGUI.csproj -c Release `
  -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
  -o artifacts/publish-0.20.1/win-x64
```

For desktop verification, launch Debug with an isolated `HD2RUNTIMEGUI_DATA_ROOT` and `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9225`, then run `node tools/runtime0201-smoke.mjs`. After restarting that process, use `--relaunch --players` to verify persisted projects and the existing player catalog.

No remaining 0.20.1 SDK blocker was found for the published scalar support-authoring surface. Attachment writes, unresolved ownership and duplicate identity bypasses remain unavailable by Runtime policy.
