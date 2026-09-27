# Runtime 0.19 composition plans

Published authority: [HD2Runtime v0.19.0](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.19.0), commit `183b205d1f007827e80c1d8cbaa4375dd045d796`. The downloaded SDK build report matches that commit. SDK SHA-256: `88e1c9468af23d738eb94e2fa891e35d60dac1dbd91098475d3fb3691e502ebe`.

All thirteen bundled JSON files match the published archive byte for byte. Production release discovery, digest/size/path validation and cache installation succeeded in `artifacts/019-online`. The new typed `CompositionPlanCapabilities.json` reader validates schema, API, operation forms, dependency paths, limits and per-operation authorization. Missing or unsupported plan contracts reject installation before the current cache pointer changes.

## Generation

The existing planner continues grouping fields by SDK semantic backing identity. Related operations are connected by weapon/attack context, shared backing identity and projectile replacement dependencies. Each connected composition becomes one `hd2.ensure({plan=...})`, or `hd2.plan(...)` when persistence is off. Unrelated components retain independent patch/transaction output. One native owner is allowed per operation; field offsets never determine grouping.

The released typed terminal API requires **separate impact and expiry operations**, even though they share one ProjectileSettings record. They participate in the same coordinated plan and complete phase write set. ProjectileSettings scalar siblings and DamageInfo siblings each remain grouped. ExplosionSettings and explosion DamageInfo similarly produce separate operations in one plan.

Swaps execute in phase 1. Dependent projectile and terminal operations execute in phase 2 using `target_from` paths `projectile`, `terminal.impact` or `terminal.expiry`, referencing the prior swap operation. The replacement object's baseline and permissions come from its SDK capability descriptors. There are no concurrent dependent ensures, stale scalar targets, raw IDs, native offsets or addresses in generated Lua.

The GUI does not handle partially applied runtime state. Runtime owns expected/already-desired/mixed-state handling, validation, rollback and protection restoration.

Simple edits still produce ordinary patch/transaction requests. A separate weapon-local field can join a plan if it shares the selector's native component; unrelated fire rate remains separate. The released `HD2AuthoringTarget` type includes weapon handles. Operation/field ordering and IDs are deterministic. Per-operation transaction limits and published plan limits are checked; unsupported or mixed-persistence groups are never split into racing jobs.

## Shared approval and persistence

Composition shows one acknowledgement control per modified object and exact consumer/write scope, with affected names and advanced unnamed-resource evidence. Approving DamageInfo never authorizes ProjectileSettings, terminal or explosion scopes. Revoking a scope revokes all its sibling approvals. Consumer changes after SDK rebind require review.

Project format remains 4. No plan request is stored: projects retain semantic weapon/attack/field/reference intent, original and desired values, baseline SDK version, enabled/persistence/group/notes and approval evidence. Existing 0.17/0.18 projects remain pinned and retain their safe composition blocks. Explicit rebind to 0.19 enables plans without rewriting saved baselines. Historical SDK fixtures preserve regression coverage.

## Generated samples

Both were created and exported through the Windows editor, then reloaded and rebuilt after a real process restart:

- `artifacts/019-ui-verified/Exports/ConcussiveComposition019-0.1.0.zip`
- `artifacts/019-ui-verified/Exports/ProjectileSwapAndTune019-0.1.0.zip`

Concussive: fire rate 400 → 1100; push 60 → 30; direct/slight/large/extreme AP 2 → 3; impact and expiry None → Eruptor explosion. **Two ensures:** one composition plan with three operations (grouped five-field DamageInfo, impact, expiry), plus a fire-rate patch. DamageInfo and terminal permissions are independently approved.

Swap-and-tune: P-113 Verdict selects the self-contained JAR-5 projectile, then its velocity changes **180 → 350** and drag **0 → 0.2**. **One ensure**, two phases, two operations. The physics operation contains:

```lua
target_from={operation='op-00a3d460ad81aef626ae75e2',path='projectile'},
allow_shared=true,
changes={
    {field=hd2.fields.projectile.drag,expect=0,value=0.2},
    {field=hd2.fields.projectile.velocity,expect=180,value=350},
},
```

Complete reviewed output is in `HD2RuntimeGUI.Tests/Golden/ConcussiveComposition019.lua` and `ProjectileSwapAndTune019.lua`. Each ZIP contains only the established eight gameplay-mod entries, with Bingus and HD2Runtime 0.19.0+/API 1 requirements. No Runtime implementation or SDK metadata is bundled.

Reproduce using the ordinary project/change/export services:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/019-sample --plans
```

Add `--online` to validate public release discovery/download/cache installation too.

## Validation

**286 tests passed**, zero failures/skips. New tests cover SDK plan ingestion/rejection, failed-update preservation, exact Concussive golden output, physics+damage, impact+expiry+physics, explosion settings+damage, swap+dependent physics/damage/terminals, isolated shared scopes/revocation, changed scope after rebind, same-owner grouping, deterministic Lua/ZIP/reload/reset, nonpersistent plans, simple scalar fallback, same-component weapon+selector plans, limits and explicit 0.18 → 0.19 rebind.

Windows Debug build passed with zero warnings/errors. Self-contained Release publish passed at `artifacts/publish-0.19/win-x64/HD2RuntimeGUI.exe`. Distribute the whole folder; WebView2 Evergreen is required, developer tools are not.

```powershell
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj
dotnet publish HD2RuntimeGUI/HD2RuntimeGUI.csproj -c Release `
  -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
  -o artifacts/publish-0.19/win-x64
```

Desktop verification: `tools/runtime019-smoke.mjs --catalog`, then `--relaunch` after restarting the isolated Debug app. Both samples passed editing, scope gating/revocation, Changes, actual plan preview, export and persistence. All 80 player views retained 21 fire-mode controls, 130 terminal selectors, 30 writable heat fields and 419 read-only attachments. All 35 support graphs remained read-only. Local screenshots remain ignored by Git:

- `docs/screenshots/runtime019-shared-scopes.png`
- `docs/screenshots/runtime019-concussive-changes.png`
- `docs/screenshots/runtime019-plan-preview.png`
- `docs/screenshots/runtime019-swap-plan.png`

## Remaining limits

The published plan API supports up to eight phases, 64 operations and 128 physical changes per phase. Each grouped transaction supports up to 32 changes. Dependency-derived targets must reference a prior projectile replacement; there is no published `target_from` path for an explosion or a terminal-reference replacement. Changing a source handle chain or creating source cycles still requires a stable supported source rather than an invented dependency path. Conflicting values/baselines, stale targets, mixed persistence, read-only/ambiguous capabilities, unsafe residency and missing shared approval remain blocked.

No released-SDK defect blocked this integration. Runtime was not modified. No mod was deployed and the game was not launched.
