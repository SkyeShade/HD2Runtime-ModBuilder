# Runtime 0.23.1 integration: magazine attachments

## Published source

- Release: https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.23.1. The tag resolves to `4e1a77d`.
- SDK asset: `HD2Runtime-0.23.1-sdk.zip`, 914,785 bytes, SHA-256 `2c7984a625b8cfca81d4f639d85fd5dae8ab9328780572bda022fff3d4e7774f`.
- Bundled metadata is byte-identical to that SDK, including the new `MagazineAttachmentCapabilities.json` (`hd2runtime.weapon_attachment.magazine.v1`). The other consumed files differ from 0.23.0 only in their version line.
- HD2Runtime was not modified.

## Loaded capabilities

| Area | Loaded |
| --- | ---: |
| Magazine attachment definitions | 43 |
| Attachment fields (all writable, shared, `allow_unverified_effect`) | 172 |
| Weapons with published magazine attachments | 20 |
| Weapons whose record capacity is a placeholder | 14 |
| Weapons with a native default magazine | 19 |
| Catalog options: native default / unique match / ambiguous / no native match | 12 / 7 / 14 / 3 |

Evidence tiers: native_owner 108, native_owner_effect_consistent 48, native_owner_value_consistent 16. Magazine selection is published as not writable.

## Reader and safety rules

- The catalog is read only for SDK 0.23.1 and later. An unknown contract or schema is reported as an unsupported SDK, and the safety flags and SDK version must match.
- **Every field must be:**
  - an integer on `hd2.weapon_attachment` path `magazine`;
  - editable and shared, requiring `allow_shared` and the `allow_unverified_effect` acknowledgement;
  - backed by an API field constant of the form `hd2.fields.<semanticFieldId>`;
  - defaulted to its attachment's published value;
  - tagged with its attachment's evidence tier.
- Each attachment has one backing object, operation group, shared scope and plan group of at most four fields, and its field list must match exactly. Consumer scope must be declared incomplete.
- Every listed weapon must exist in the player-weapon catalog. Default options must be `native_resource_default`; other resolved options must be a unique catalog match with no blocker. Unresolved options must be ambiguous (more than one candidate) or absent (no candidate) and must carry a blocker. Summary counts are recomputed.
- Ownership comes only from the published relationships. Weapon and attachment names are never used to join.

## GUI

- **Player Weapons:** each of the 20 weapons gets a **Magazine attachments** section. It shows each resolved attachment separately: option or native name, stable semantic ID, a Default magazine badge, evidence tier, capacity, starting magazines, magazines from supply and spare magazines. Compact amber badges show `allow_shared` and `allow_unverified_effect` with Runtime's wording. The section also shows consumer scope and other patched effects (handling, reload, visual), which are not editable.
- **Unresolved options:** Short and Extended Magazine on the Liberator Concussive are kept as unresolved options. They show the blocker, the published effects and the candidates, which are not chosen, and have no controls.
- **Selection:** changing which magazine is equipped is not offered, and Runtime's reason is shown.
- **Weapon-level ammo:** for these weapons the weapon record's `capacity` is never aliased. It stays read-only, with a placeholder notice where published and a note that ammo is owned by the attachment.
- **AttachmentOptionCapabilities:** its magazine ownership information is marked as superseded by the Magazine attachments section.
- **Changes:** one "Magazine attachment" entry per edited definition, titled with its native name and the weapons it is the default for. Reset works per field, per attachment and for all entity changes.
- **Acknowledgement:** one checkbox per edited attachment covers the shared scope and the unverified effect. Build / Export stays blocked until it is checked.

## Persistence and Lua

- Project format 6 is unchanged. Attachment edits are `entityChanges` with resource `weapon_attachment`, the semantic ID as the entity and path `magazine`. Instance keys are prefixed `attachment:`. Saved entities must match `weapon-attachment/v1/magazine/<slug>/<16 hex>`, so raw resource IDs are rejected.
- Lua writes to `hd2.weapon_attachment('<semantic id>')` with `hd2.fields.attachment.*`, `allow_shared=true` and `allow_unverified_effect=true`. A single field is a patch, and several fields of one attachment form a transaction.
- **Older SDKs:**
  - Projects bound to 0.23.0 or older keep their previous behaviour, with no attachment section.
  - Weapon-owned magazines (for example AR-2 Coyote `magazine.capacity`) are unchanged.
  - Rebinding a 0.23.0 project to 0.23.1 keeps all existing keys.

## Validation

- **GUI test suite:** 477 passed, 0 failed. `MagazineAttachmentTests` covers attachment-owned versus weapon-owned capacity, the Concussive Drum, ambiguous and absent options, required write flags, older SDK compatibility, Lua generation, persistence, export, reset and malformed catalogs. The 0.23.0 regressions are pinned to the published 0.23.0 archive.
- **Runtime acceptance:** `tools/HD2RuntimeGUI.Sample --runtime-validate` against Runtime's `v0.23.1` Lua sources, in HD2's standalone `lua51.dll` with no game process: **8,060 passed, 0 failed**. It covers every writable stratagem, vehicle, backpack and magazine-attachment field, and 4,692 unsafe variants were correctly rejected, including attachment writes missing `allow_shared` or `allow_unverified_effect`.
- **Desktop smoke:** `node tools/magazine-smoke.mjs` passed. It covers the Concussive drum (60 rounds, default, semantic ID, flags), unresolved options, blocked selection, the acknowledgement gate, Lua, Changes, export and reset. Screenshots: `docs/screenshots/runtime0231-concussive-drum.png`, `runtime0231-changes.png`.

## Stale SDK cache from an older GUI (fixed)

**Symptom:** "SDK is missing magazine attachment capabilities." Settings showed no installed SDK, "Unknown" as the latest release and "/ 1" as the Runtime API.

**Root cause:** GUI builds from before this integration already accepted the 0.23.1 release. The 0.23.0 build added 0.23.1 to its published-artifact allowlist, but it consumed only 17 metadata files. Installing 0.23.1 with such a build cached `Sdk\0.23.1\` without `MagazineAttachmentCapabilities.json` and pointed `current.json` at it. A newer GUI then:

- loaded that immutable cache, which is preferred over the bundled copy;
- failed the version-gated requirement for the magazine file;
- could not reinstall, because the cache rejected an existing version directory that lacked files.

Initialization therefore threw, `SdkStatus` stayed null, and Settings rendered empty values. "/ 1" was `@null` API plus the fixed schema label, not a release-parsing error. The GitHub release, asset selection, tag parsing (`v0.23.1` → `0.23.1`), download and archive contents were all correct. The public SDK asset, the zip used for integration and the bundled files are byte-identical.

**Fix:**

- **Completing a cached version:** a cached version that lacks a required file is completed from the bundled metadata when it is the same release (every cached file byte-identical). Otherwise, the online check reinstalls that exact release, and installation adds only missing files. Differing files are never replaced or mixed.
- **Load error in Settings:** Settings shows the load error instead of blank values.
- **Build identity:** Settings/About and the status bar show the build commit. Builds from uncommitted changes are marked `.dirty`.

**Regression tests:** `SdkCacheUpgradeTests` covers:

- a fresh 0.23.1 install, online and offline first launch;
- 0.23.0 → 0.23.1 upgrade;
- offline load of an installed 0.23.1;
- 0.22.1 and 0.23.0 staying valid without the magazine file;
- a missing or malformed magazine file in a 0.23.1 archive;
- stale-cache completion, refusal to mix differing files, and reinstall merging;
- online and offline incomplete-cache handling;
- published release JSON parsing;
- build identity.

## Remaining Runtime/API blockers

- **Selection:** magazine selection and preset ownership are unresolved. The GUI cannot choose which magazine a weapon equips.
- **Unresolved options:** 14 catalog options are ambiguous and 3 have no native match. Among them are the Liberator Concussive Short and Extended magazines. LAS-5 Scythe has no resolved magazine attachment.
- **Consumer scope:** the weapon-to-attachment compatibility table is not resolved, so consumer scope is incomplete and every write needs `allow_shared`.
- **Re-application:** in-game re-application of edited attachment values is unproven (`allow_unverified_effect`).
- **Other effects:** handling, reload and visual effects of an attachment are published but not writable.
