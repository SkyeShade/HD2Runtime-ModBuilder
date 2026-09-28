# Runtime 0.23.0 integration: shield relay, vehicles, mounts and backpacks

## Published source

- Release: https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.23.0. The tag resolves to `9486e72`, the build commit the release notes name.
- SDK asset: `HD2Runtime-0.23.0-sdk.zip`, 897,156 bytes, SHA-256 `96e8eac4a711682ba58a5fd2fd29b7a280d813eb36401fa1999c6486e34e6129` (matches GitHub's digest).
- Bundled metadata is byte-identical to that SDK, including the new `VehicleAuthoringCapabilities.json` (`hd2runtime.vehicle.guarded_authoring.v1`) and `BackpackAuthoringCapabilities.json` (`hd2runtime.backpack.guarded_authoring.v1`).
- HD2Runtime was not modified. v0.23.1 (tag `4e1a77d`) was published during this work. Its consumed files are identical to 0.23.0 except for the version line, and it adds `MagazineAttachmentCapabilities.json`, which this GUI does not consume yet. The GUI recognises 0.23.1 as a compatible release, so the update check no longer fails at startup.

## Loaded capabilities

| Area | Loaded |
| --- | ---: |
| Stratagem instances / writable | 1,468 / 1,375 (86 new; no existing key changed) |
| Vehicles (stratagem / native-only) | 11 (9 / 2) |
| Vehicle fields | 718 writable (22 main, 678 zone, 18 mount) |
| Native damage zones | 226 |
| Mount slots (swappable / non-weapon) | 22 (18 / 4) |
| Discovered mounted weapons / allowed replacements | 79 / 960 |
| Backpacks / fields / writable | 13 / 31 / 9 |
| Vehicle + backpack call-ins linked both ways | 22 |

Vehicle evidence tiers: gameplay_proven 64, schema_proven 636, live_write_verified 2, structural_reference 16. Backpack tiers: gameplay_proven 2, schema_proven 7.

## Reader and safety rules

- **Stratagem catalog:** now accepts the `damage_zone` and `shield` paths and the `zone` target key. Each deployed entity's `damageZones` and `shield` instance lists must equal the published zone/shield fields. The native-identifier scan exempts the domain names used as keys in `summary.writableByDomain` (for example `payload`).
- **Vehicle and backpack catalogs:**
  - They are rejected unless the contract/schema, SDK version and safety flags match, and the instance audit matches exactly.
  - Every field must match its backing object and its target-specific operation group.
  - Evidence tiers must be among the published tiers.
  - Read-only fields must carry a reason.
  - Summary counts are recomputed.
- **Mount rules:**
  - A swappable mount must hold a weapon.
  - Its `allowedValues` must equal the mount's `allowedReplacements`, and every replacement must be a discovered mounted weapon of the vanilla occupant's attack family.
  - Non-weapon slots (racks, shields, fuel tanks and similar) must have no field and must carry a blocked reason.
- **Call-in links:** `callInStratagem.semanticId` and the stratagem's `delivers.semanticId` must agree in both directions. One-sided links fail closed. Display names are never used to join.

## GUI

- **Navigation:** Stratagems (Support / Offensive / Defensive), then **Vehicles** and **Backpacks**, then Support Weapons. Vehicle and backpack call-ins with a published link are edited inside the vehicle or backpack editor, so the Stratagems list and its counts leave them out (73 listed; Support 35).
- **Shield Generator Relay:** a **Physical emitter / base** section (health, armor, deployed lifetime), a **Body damage zones** subsection (body_front: health, armor, damage forwarded to main health) and a separate **Shield projector** section (radius, shield health). Recharge, broken and restart members remain under "Not currently writable" with Runtime's reasons. Every other deployed entity also shows its published damage zones.
- **Vehicles:**
  - The editor shows the call-in (cooldown; max uses read-only), main durability, a collapsible and filterable list of native damage zones (each with its own health, armor and forwarded damage), weapon mounts and blocked fields.
  - Zones are labelled with their native name when resolved and `zone_N` otherwise, with child zones and actor counts.
- **Mounts:**
  - Each swappable slot has a selector listing only published compatible replacements. Entries show readable names with a semantic-ID suffix and referencing vehicles, because display names repeat; the value written is the semantic ID.
  - Runtime's package-residency warning is shown, and each swap needs an explicit acknowledgement that covers that exact replacement. Only then is `allow_unverified_reference=true` emitted.
  - Non-weapon slots have no control.
- **Backpacks:** the call-in plus Runtime's setting groups. Read-only fields keep Runtime's reasons (Supply Pack and Guard Dog deposit charges, Hover Pack launch velocity), and the Warp Pack shows that no behavior fields are published.
- **Evidence badges:** In-game tested, In-game (combined edit), Schema-proven, Live write confirmed and Structure only, with Runtime's tier description, reference mod and proof in the tooltip.
- **Changes:** one entry per vehicle or backpack that includes its call-in edits, with per-field, per-entity and all-entity reset.
- **Overview:** projects bound to an SDK older than 0.23.0 that contain stratagem or support-weapon edits get a note. Runtime 0.23.0 fixed typed stratagem writes and restored `hd2.support_weapon`, so rebinding is recommended.

## Persistence and Lua

- **Project format 6** adds `entityChanges` / `entityApprovals` (resource, published entity name, path, `zone_N` / `slot_N`, instance key, expected/desired values, SDK baseline, capability evidence and the mount acknowledgement). Stratagem changes gain an optional `zone`. Formats 1–5 still load, and existing keys are unchanged, so rebinding from 0.22.x needs no review.
- **Lua targets:** `hd2.vehicle(name)`, `…:damage_zone('zone_N')`, `…:mount('slot_N')`, `hd2.backpack(name)`, `hd2.stratagem(name):deployed_entity():shield()` and `…:deployed_entity():damage_zone('zone_0')`.
- **Request shape:** one request per published plan group. That is a patch for a single field, a transaction for one operation group, or an `hd2.plan` across groups.
- **Mount values:** mount `expect`/`value` are the published semantic-ID strings that Runtime's validator accepts.
- **Retry behaviour:** the GUI does not poll, retry or re-submit. Runtime's `hd2.ensure` owns bounded startup retries, reset detection and guarded reapplication. No GUI surface displayed runtime metrics before, so `hd2.metrics()` is not surfaced.

## Validation

- **GUI test suite:** 459 passed, 0 failed. The 0.22 and 0.22.1 regressions are pinned to the published 0.22.1 archive; 0.21 regressions stay on 0.21.0. The published 0.23.0 and 0.23.1 archives install through the cache.
- **Runtime acceptance:** `tools/HD2RuntimeGUI.Sample --runtime-validate` against Runtime's `v0.23.0` Lua sources, in HD2's standalone `lua51.dll` with no game process: **7,329 passed, 0 failed**. This covers every writable stratagem, vehicle and backpack field, all 942 non-vanilla mount replacements, whole-entity plans, and 4,176 unsafe variants correctly rejected (missing `allow_shared` / `allow_unverified_reference`, or a stale baseline).
- **Desktop smoke:** `node tools/runtime023-smoke.mjs` passed, alongside `navigation-smoke`, `runtime022-smoke` and `support-link-smoke`.

## Runtime observations

- The relay's family-level `deployedEntity.blockedFields` still lists "deployment lifetime" while the relay publishes a writable, gameplay-proven `payload.lifetime`. The GUI shows both as published: the field is editable and the family note is listed.
- Mounted-weapon display names are not unique (79 weapons, 37 names), so the selector disambiguates by semantic-ID suffix.
- Remaining Runtime-declared blockers: shield recharge / broken delay / recharge rate / restart charge; zone constitution, death flags and explosive damage percentage; unpopulated zone slots; vehicle motion, collision and seats; backpack deposit writes; Hover Pack launch velocity; further launch members and horizontal impulse; Warp Pack behavior; package loading for mount swaps (in-game firing and rendering unconfirmed).
