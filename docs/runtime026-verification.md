# HD2Runtime SDK 0.26.0 verification

Source: [HD2Runtime v0.26.0](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.26.0), `HD2Runtime-0.26.0-sdk.zip`,
SHA-256 `41feb84cd652bec6a053381490cc9560c48044a3363e446a817caa7ac871576f` (Runtime commit `a4dc062`). The same metadata is bundled
for offline first launch and used as the test fixture `HD2RuntimeGUI.Tests/Fixtures/sdk-0.26.0.zip`.

## Loaded capabilities

| Area | Published |
| --- | --- |
| Player weapons | 80 weapons; 70 writable third-person reticles |
| Magazine attachments (schema 2) | 43 definitions, 233 fields, reload duration and ergonomics modifier |
| Fire modes | 115 entries (player and support), writable only where Runtime publishes `fire_mode.modes` |
| Mounted vehicle weapons | 11 vehicles, 480 fields in weapon-local, shared-mounted-weapon and shared projectile/damage/explosion scopes |
| Stratagems | 95 roots; 85 writable mission-use fields (Eagles excluded) |
| Backpack-fed support weapons | M-1000 Maxigun, B/FLAM-80 Cremator, GL-28 |
| Drop-pod racks | 56 racks (280 fields: slots 1–4 and the spawn count) |

## Tests

`HD2RuntimeGUI.Tests/Runtime026Tests.cs` covers:

- every magazine option, including a non-default one with its reload and ergonomics, and the unresolved heatsinks;
- the player reticle with its acknowledgement, the APW-1 reticle without one, and read-only reticles;
- the JAR-5 fire-mode list and blocked fire modes;
- the FRV gun, the Bastion cannon and machine gun, Maelstrom mounts that share one weapon, and the Emancipator arms;
- mission uses: Exosuit finite → Unlimited, FRV Unlimited → finite, and the Eagle exclusion;
- backpack ammo for the Maxigun, Cremator and GL-28;
- the EAT-17 / Surplus EAT shared rack, two different slots, empty and duplicate slots, the spawn count and package risk;
- Mod Options eligibility;
- rebinding a 0.25.1 project to 0.26.0.

`tools/runtime026-smoke.mjs` drives the Release build end to end and captures:

- `docs/screenshots/runtime026-magazine-variants.png`
- `docs/screenshots/runtime026-apw1-reticle.png`
- `docs/screenshots/runtime026-jar5-fire-modes.png`
- `docs/screenshots/runtime026-vehicle-weapon.png`
- `docs/screenshots/runtime026-maxigun-backpack-ammo.png`
- `docs/screenshots/runtime026-surplus-eat-payload.png`

## Remaining Runtime blockers (shown, not worked around)

- Fire modes stay read-only for beam, charge, wind-up and special fire-control weapons (for example LAS-5, M-1000, AR-11).
- Reticles are read-only where the crosshair type selects no mapped style (for example CQC-42).
- Magazine selection and presets are not writable; the three LAS-5 heatsink options are unresolved.
- Drop-pod slots 5–8 are never authored; 8 racks are read-only (for example Carry Data, MS-11, Jammed Pod).
- Some vehicle mounts hold no weapon component (FRV racks, the Maelstrom coaxial mount, the Breakthrough left arm).
- The Cremator's weapon-side fields stay blocked; only its backpack ammunition is writable.
- Mod Options cannot bind booleans (reticle), fire-mode lists, pickups or the Unlimited token: Runtime option values are numbers only.
