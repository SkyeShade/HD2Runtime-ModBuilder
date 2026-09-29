# HD2Runtime SDK 0.27.0 verification

Source: [HD2Runtime v0.27.0](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.27.0), `HD2Runtime-0.27.0-sdk.zip`,
SHA-256 `aad2330fa6251b37639fa1671ba191699498bf97367cdccfe7ad9d5b1a180808`. The same metadata is bundled for offline first launch and
used as the test fixture `HD2RuntimeGUI.Tests/Fixtures/sdk-0.27.0.zip`.

## Consumed

| File / field | Use |
| --- | --- |
| `AssetDependencyCapabilities.json` (`hd2runtime.asset_dependencies.v1`) | 331 objects (260 known, 71 unknown), reference-family proof levels, live evidence, load policy. Validated fail-closed; no package identities are read. |
| `PodPayloadCapabilities.json` `pickups[].packageDependency`, `packageResidency`, `liveVerifiedPairs` | Pickup asset state; cross-checked against the asset catalog. |
| Projectile graphs `attacks[].residency` (`PACKAGE_AUTO_LOADED`, `package`, `packageResidency`, `liveTested`) | Projectile / explosion source asset state; Talon offered as a source. |
| `VehicleAuthoringCapabilities.json` mounted weapons | Joined to `mounted_weapon/<semanticId>` asset entries (all 79 present). |
| `PlayerWeaponAuthoringCapabilities.json` `apiFieldConstant` | Accepted (typed API constant). |

`ThrowableAuthoringCapabilities.json` (`hd2runtime.throwable.guarded_authoring.v1`) drives the Throwables page (ModBuilder 1.3.0): 23 throwables, 411 fields (388 editable, 316 on shared settings rows), validated fail-closed (accessor chains, typed API constants against the published field definitions, ranges, opt-ins, consumer blocks, summary). `HD2RuntimeGUI.Tests/ThrowableTests.cs` (19 tests) covers frag, incendiary, gas, stun, knife, shield, mine and Pineapple submunitions, shared-row identity, save/load and a 0.26 upgrade.

## Tests

`HD2RuntimeGUI.Tests/Runtime027Tests.cs` (19 tests) covers:
- SDK binding;
- asset parsing (known, unknown, live-tested) and fail-closed tampering;
- pod, mount and projectile asset states, and their separation from `allow_unverified_reference`;
- the in-game waiting / `ASSET_UNAVAILABLE` explanation;
- typed Lua without preload calls;
- 0.26.0 → 0.27.0 rebind with identical Lua and no review flags;
- the SDK compatibility pin, and skipping unreadable releases.

`tools/runtime027-smoke.mjs` drives the Release build.

## Remaining Runtime blockers

- 71 objects have no known package, including 59 mounted weapons, Ammo Box (pod), Health Pack (pod) and GP-31 Grenade Pistol projectiles: shown as **Assets unknown**.
- Vehicle-mount and explosion-reference loading is offline-proven only. The mount live test C was inconclusive.
- Pod and mount swaps remain gameplay-unverified (`allow_unverified_reference`).
- Loading is local; clients without the mod may still see missing assets.
