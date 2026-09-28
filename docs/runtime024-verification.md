# Runtime 0.24.0 integration: boosters and expanded support weapons

## Published source

- Release: https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.24.0. The release notes name `fd0c0d2` (on master) as the final release commit. The `v0.24.0` tag points to `e5850bf`, the commit before it, which differs only in version strings.
- SDK asset: `HD2Runtime-0.24.0-sdk.zip`, 962,527 bytes, SHA-256 `9440c78ca8cc8d8121dcfdcaf9c3959d16964bc03e95a4266fc5d3b1e9353bd3`. Its SDK files are byte-identical to `fd0c0d2`'s `sdk/` directory.
- Bundled metadata is byte-identical to that SDK, including the new `BoosterAuthoringCapabilities.json` (`hd2runtime.booster.guarded_authoring.v1`).
- HD2Runtime was not modified.

## What changed in the consumed metadata

- **Support weapons:** `SupportWeaponAuthoringCapabilities.json` changes are listed below.
- **Stratagems:** in `StratagemAuthoringCapabilities.json`, four support call-in links report `DELIVERY_RESOLVED` instead of `DUPLICATE`, and the ambiguous-identity audit drops from 7 to 3.
- **Player weapons:** `PlayerWeaponAuthoringCapabilities.json` adds five field definitions (the new support families). Player-weapon field instances are unchanged.
- **Other files:** unchanged apart from version strings and line endings (CRLF → LF).

## Boosters

| Area | Loaded |
| --- | ---: |
| Boosters listed | 20 |
| Writable boosters / fields | 2 / 5 |
| Identity: resolved / candidates / effect category / elimination | 7 / 11 / 1 / 1 |

**Reader rules:**
- **Contract and safety:** the contract and schema must be exactly as published, the safety flags and SDK version must match, and summary counts are recomputed.
- **Identity:** every booster needs identity evidence. A `RESOLVED` booster has exactly one enum value, and a `CANDIDATES` booster has several values and no chosen one.
- **Writability:** a writable booster must be `RESOLVED` and have fields. A non-writable booster must have none and must carry blockers.
- **Fields:** each field must be a `booster` target with path `deployed_entity` or `status_effect` and accessor `booster.<path>`. The API constant must be `hd2.fields.<semanticFieldId>`, and the baseline must equal the expected value.
- **Acknowledgements:** every field must list `allow_unverified_effect`, and `allow_shared` exactly where the shared scope requires acknowledgement.
- **Transaction groups:** a group never spans boosters, targets or sharing.
- **Tiers:** unknown evidence tiers fail closed.

**GUI:**
- **Boosters page:** the yellow category lists every booster with its identity status, native name, enum value or candidates, identity evidence, relationships and blockers.
- **Armed Resupply Pods:** a deployed entity · turret section with fire rate (640 rpm) and magazine capacity (140).
- **Experimental Infusion:** a status effect section with strength, duration and incoming damage scale.
- **Integrated Extinguishers:** its susceptibility-gate relationship and "susceptibility override" blocker, with no controls.
- **Acknowledgement:** one checkbox per booster records the unverified-effect acknowledgement for each edited field. It also records shared-scope approval where Runtime requires `allow_shared`. Build / Export stays blocked until it is checked.

**Lua:** targets are `hd2.booster('<name>'):deployed_entity()` and `…:status_effect()`. One operation is generated per Runtime transaction group, with one plan per booster target. The Armed Resupply Pods plan matches Runtime's `ArmedResupplyTurret` example. `allow_unverified_effect=true` is always emitted for booster writes, and `allow_shared=true` only for Experimental Infusion.

**Persistence:** booster edits are format-6 `entityChanges` (resource `booster`, entity = published booster name). Saved paths other than `deployed_entity` / `status_effect` are rejected. Older SDKs have no Booster category.

## Support weapons

| Area | 0.23.2 | 0.24.0 |
| --- | ---: | ---: |
| Writable weapons | 27 | 31 |
| Field instances | 828 | 970 |
| Backing objects / operation groups | 146 / 155 | 179 / 188 |

**New field families:**
- `reload.duration`: 14 fields, `allow_unverified_effect`.
- `projectile.penetration_slowdown`: 19 fields, shared.
- `projectile.lifetime`: 5 fields, shared.
- `windup.wind_up_seconds`: 1 field.
- `windup.wind_down_seconds`: 1 field, `allow_unverified_effect`.

**Existing fields:** all 828 existing instance keys and their published contents are unchanged.

**Newly writable (`DELIVERY_RESOLVED`):** MG-43 Machine Gun, M-105 Stalwart, MG-206 Heavy Machine Gun and CQC-20 Breaching Hammer. The reader requires Runtime's `identityResolution` evidence, a known call-in link, and unaffected non-delivered roots.

**Still blocked:** EAT-17, LAS-98 and B/FLAM-80 (duplicate identity with a call-in link), and CQC-72 (duplicate identity and unresolved call-in).

**GUI:**
- **Identity:** the weapon's identity badge and read-only notice now come from the authoring catalog, and the delivery-resolution evidence is shown.
- **Unverified fields:** fields marked `allow_unverified_effect` show an amber badge. Each needs a per-field acknowledgement, saved as `SupportChange.effectAcknowledgement`, before `allow_unverified_effect=true` is generated.
- **Shared fields:** shared approval is unchanged.
- **Evidence hashes:** null acknowledgements are left out of capability-evidence hashes, so 0.23.x support edits rebind to 0.24.0 without review.

## Validation

- **GUI test suite:** 524 passed, 0 failed. `Runtime024Tests` covers:
  - booster category presence, absence on 0.23.2/0.23.0/0.22.1, and yellow styling;
  - Armed Resupply Pods and Experimental Infusion fields, acknowledgements and Lua;
  - blocked booster rendering data;
  - changes, reset, persistence and export;
  - malformed and unknown booster catalogs;
  - a missing booster file and a stale cache;
  - the new support families, newly writable and still-blocked weapons, and every 0.24.0 support instance generating;
  - the 0.23.2 → 0.24.0 rebind.
- **Earlier regressions:** the 0.20.1–0.23.x support regressions are pinned to the published 0.23.2 archive.
- **Runtime acceptance:** `tools/HD2RuntimeGUI.Sample --runtime-validate` against Runtime `fd0c0d2` Lua sources, in HD2's standalone `lua51.dll` with no game process: **10,616 passed, 0 failed**. This covers every writable stratagem, vehicle, backpack, magazine-attachment and booster field, and all 970 support-weapon fields (new in this harness). 6,271 unsafe variants were correctly rejected (missing `allow_shared` / `allow_unverified_effect` / `allow_unverified_reference`, or a stale baseline).
- **Desktop smoke:** `HD2GUI_CDP_PORT=9237 node tools/runtime024-smoke.mjs` passed. Screenshots: `docs/screenshots/runtime0240-*.png`.

## Remaining Runtime/API blockers

- **Unwritable boosters:** 18 of the 20 boosters have no writable values. Their effects are applied by game code, their enum value is one of several candidates, or (Integrated Extinguishers) the override's semantics are unproven. No booster edit is gameplay-tested yet (`allow_unverified_effect`).
- **Armed Resupply Pods projectile:** the turret projectile is shared with player weapons and is not editable through the booster. The Resupply stratagem is not in the stratagem authoring catalog yet.
- **Support weapons:** EAT-17, LAS-98, B/FLAM-80 and CQC-72 remain blocked by duplicate identities. SG-88, CQC-72 and B/MD C4 keep unresolved call-in links. Some `reload.duration` and `projectile.lifetime` values are blocked where the native value is 0.
- **Release tag:** the `v0.24.0` tag does not point to the release commit named in the release notes.
- **Vehicle mounted-weapon authoring:** deferred to a later Runtime pass, as requested.
