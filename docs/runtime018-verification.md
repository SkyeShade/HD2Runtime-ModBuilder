# HD2Runtime 0.18 integration

Authority: [published v0.18.0 SDK](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.18.0), build-report commit `75a0781be3f4e8a3efd6c0295d10059ab2e70ce6`. SDK archive SHA-256: `747071ce4c4d8346c7cf4e3a0da6c7bdf91b81e2a9b972f66038358ba6846dfe`.

The production GitHub client discovered, inspected and installed the release into `artifacts/018-online`. All twelve bundled JSON files match the published SDK byte for byte. Only public authoring metadata is consumed; Runtime code, internal research and downloaded scripts are not installed or executed.

## Metadata and behavior

- 80 player weapons; 3,574 capability entries; 89 semantic definitions (69 writable, 20 read-only, including nine derived).
- Heat companion: seven heat/heatsink weapons; 49 direct field instances; 30 writable instances across five weapons. This is 15 heat and 15 heatsink instances; there are no shared heat records.
- Sai, Trident, Sickle, Double-Edge Sickle and Talon expose their supported direct heat/heatsink fields. Scythe and Dagger remain blocked by duplicate identities. Dagger keeps its native 2000 capacity and explicit native/catalog disagreement notices.
- Heat / Heatsink uses the existing scalar editor, autosave, typed baseline comparison, reset, project format and Changes/Lua Preview paths. Advanced/read-only cards retain warmup, post-overheat cooldown, derived values and SDK explanations. Attachment presets are a separate read-only system.
- The typed heat reader cross-checks version, schema, fingerprints, source snapshot, identities, safety, applicability, ownership, baselines, permissions and summary counts. Missing or inconsistent metadata rejects installation before replacing the cache pointer. Future unsupported shared-group contracts fail closed.
- The twelve-file cache is immutable per version. Existing 0.17 projects stay pinned until explicit rebind; saved expected/desired values remain unchanged. A preserved 0.17 fixture exercises historical behavior independently of the new bundled SDK.
- Existing 21 fire-mode controls, 130 writable terminal slots, 144 explosion scalar entries, 35 read-only support weapons and 419 read-only attachment options remain available. JAR-5 Full Auto stays blocked. Talon's projectile-source residency restriction is unchanged.

## Sickle proof mod

Created through both the ordinary desktop editor and generic sample-project pipeline:

`artifacts/018-ui/Exports/SickleHeatTuning-0.1.0.zip`

Capacity **100 → 140**, cooling **8 → 12**, spare heatsinks **3 → 5**. The cooling baseline is the published SDK's 8, not an illustrative catalog value. One component owner produces one ensure/transaction:

```lua
local hd2=require('mods/skyeshade/hd2runtime')

return hd2.ensure({
    transaction={
        id='gui-object-4c18b383a7e160f899d26694',
        target=hd2.weapon('LAS-16 Sickle'),
        changes={
            {field=hd2.fields.heat.capacity,expect=100,value=140},
            {field=hd2.fields.heat.cool_per_second,expect=8,value=12},
            {field=hd2.fields.heatsink.spare,expect=3,value=5},
        },
    }
})
```

The eight-entry ZIP has README, manifest, HD2Runtime dependency metadata, build report, source and three mod payload files. It requires separately installed Bingus v15+/API 1 and HD2Runtime 0.18.0+/API 1. No Runtime implementation, SDK catalogs or snapshots are bundled. Exports are byte-identical for unchanged projects.

Reproduce the generic sample with:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/018-sample --heat
```

Add `--online` to exercise production release discovery/download validation.

## Validation

Full suite: **264 passed**, zero failures/skips. Coverage includes heat parsing/counts, malformed/unsupported metadata, failed update preservation, all six writable field kinds on Sickle, float baseline equivalence, autosave/reload/reset, same-component transactions, deterministic Lua/ZIP, blocked duplicates, derived/unproven fields, read-only attachments, 0.17 → 0.18 pin/rebind and changed-baseline rejection. Existing composition grouping/dependency/conflict tests run against 0.18.

Desktop WebView2 verification uses `tools/runtime017-smoke.mjs --heat`, followed by `--heat --relaunch` after a real process restart. It checks all seven heat views, 30 writable controls, absent heat on an ordinary rifle, read-only timing/attachment/duplicate states, visible baseline differences, reset field/weapon, Changes, one transaction, build/export and persistence. Screenshots are local ignored artifacts:

- `docs/screenshots/runtime018-sickle-heat.png`
- `docs/screenshots/runtime018-heat-changes.png`
- `docs/screenshots/runtime018-dagger-blocked.png`

The existing `--catalog` smoke path also passed against 0.18: all 80 player views, 21 writable fire-mode controls, 130 terminal selectors, 419 read-only attachments and all 35 support graphs. The four fire-mode/terminal/explosion/projectile sample ZIPs rebuilt successfully. `--grouping` confirmed the Concussive DamageInfo transaction, shared-object approval and visible impact + expiry export conflict.

Windows Debug build: zero warnings/errors. Windows Release self-contained publish:

```powershell
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj
dotnet publish HD2RuntimeGUI/HD2RuntimeGUI.csproj -c Release `
  -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
  -o artifacts/publish-0.18/win-x64
```

Launch `artifacts/publish-0.18/win-x64/HD2RuntimeGUI.exe`; distribute the entire folder. WebView2 Evergreen is required, developer tooling is not.

## Contract limits preserved

Runtime 0.18 still publishes a single-target transaction and no ordered multi-target completion plan. Replacement plus dependent projectile-object edits, incompatible impact/expiry targets and terminal/scalar combinations sharing one object continue to block export with a visible composition error. They are never split into racing ensure jobs. Heat uses the existing object planner without changing these constraints or project persistence.

Attachment selections/effect writes remain unavailable. Warmup and post-overheat timing remain unproven; duplicate heat weapons remain fail-closed. No new heat integration blocker was found. HD2Runtime was not modified; no mod was deployed and the game was not launched.
