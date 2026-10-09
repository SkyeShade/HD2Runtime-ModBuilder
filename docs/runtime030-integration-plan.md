# HD2Runtime 0.30.0 integration plan (ModBuilder 1.7.0)

Planning document, written 2026-10-07 against the HD2Runtime development line (`wip/custom-projectile-rows`, `VERSION` 0.30.0-dev, commit
`46e8900`). It lists, in implementation order, what ModBuilder needs so projects can bind to the 0.30 SDK and author what it adds. ModBuilder 1.7.0's
custom content is already on the 0.30 development API (see [custom-content.md](custom-content.md)); everything here is about the rest of the SDK.

**Development SDK artifact.** `HD2Runtime-0.30.0-dev-sdk.zip` was built from a clean checkout of `46e8900` with the Runtime's own
`scripts/build_release.py` (all generators in `--check` mode, the full test suite, example validation and the packaged-runtime validation). Two
tests fail in every clean (LF) checkout of that commit and were tolerated; see [Runtime-side fixes](#runtime-side-fixes-to-report-upstream), item 1.
The artifact's path and SHA-256 are in the [build record](#development-sdk-build-record) at the end.

**How this was found.**
- The SDK trees `v0.28.1` and `6b566fb`/`46e8900` were diffed structurally, file by file.
- Every ModBuilder SDK reader was run against the 0.30.0-dev SDK in a scratch copy of `HD2RuntimeGUI.Core`. About nine strict readers reject it.
- Each rejection was reproduced, and a minimal patch for each was confirmed to make the whole `SdkCache` local load pass, including every
  cross-file link (content fingerprint `7889baa1…` in that scratch run).
- Line numbers below refer to the 1.7.0 working tree.

---

## Phase 0 — Before anything: what does not change

- **Contracts.** Every catalog keeps its contract string and `schemaVersion`. `metadata.json` keeps `schema_version` 1 and `api_version` 1;
  only `runtime_version` becomes `0.30.0-dev`.
- **Eight graph files are byte-identical to 0.28.1.** They are AttachmentOption, Explosion, PlayerWeaponFireModeGraph, PlayerWeaponHeat,
  PlayerWeaponMagazineOptionGraph, PlayerWeaponProjectileReferenceGraph, PlayerWeaponTerminalActionGraph and ProjectileComposition, and they are
  still labelled 0.19.0.
- **0.28.1's fixes are in the development line.** The dev branch forks from v0.28.0, but 0.28.1 was folded into the r30 snapshot (`6b49353`;
  `docs/releases/0.30.0-dev.md`, "0.28.1 merged into the development line"). `core/sdk_compatibility.lua` is byte-identical to 0.28.1's.
- **There is no 0.29.** The next public SDK is 0.30.0. Plan for a prerelease (`0.30.0-dev`) being bound locally until then.

## Phase 1 — Make the SDK load (blocking; small, mechanical) — *done in 1.7.0*

Each step is a reader change plus a regression test that loads the bundled 0.28.1 SDK unchanged *and* a `sdk-0.30.0-dev.zip` test fixture.

**Status (1.7.0).** All eleven items are done, and **0.30.0 is the bundled pin** (the Runtime release candidate `893d31f`; see
[Swapping the bundled SDK](#swapping-the-bundled-sdk) and Phase 10). The 0.30.0-dev test SDKs still load (local, or cached by a 1.7.0 test
build). Older SDKs load and export as before (`OldProjectCompatibilityTests`, the 1.4.2 project
fixtures, `ExportFixtureTests`). `Runtime030Tests` covers the load, the prerelease rules, the new models and the read-only gates.

What 0.30 adds is modelled in typed records, so later phases need no re-parsing:
- stratagems: `StratagemCalldownCapability`, `StratagemPresentationCapability`, `StratagemEagleAttack`, `StratagemSentryFields` (with deferred
  fields and the engagement / proximity sensor), the barrage scheduling fields, values and shell types, `StratagemAirstrikePatterns`,
  `StratagemPresentationSources`, and on `StratagemField`: length bounds, directions, the acknowledgement rule, resource types, apply and read
  timing, native reader, range justification, sentinel values, allowed patterns, shell types;
- support: `SupportCharge` (meaning, family, effect, `min` / `max` / `rangeReason`, notices, deprecation, aliases, allowed values and donors) and
  `SupportChargeLevel`;
- vehicles and backpacks: `Vehicle.TuningFieldInstanceKeys`, `EntityNotice`, `Lifecycle`, `AppliesWhen`, `Order`, the 0.30 evidence keys;
  mounted weapons: `Min`, `Max`, `RangeReason`, `Order`, `Lifecycle`;
- enemies: the gore definition's `Semantics`, `Lifecycle`, `DisabledValue`, `ValueRule`;
- attack outputs: `IsDonor`, `Role`, `Names`, `ProjectileType`, `Member`, `Flight`, `Package.Via`.

Read-only until authored (`AuthoredTypes`, each listed as `INTENTIONALLY_NOT_EXPOSED` with its phase in the capability audit):
- call-in code and presentation (Phase 4.1, 4.2);
- vehicle tuning (5.1) and mounted turret motion (5.2);
- ~~`charge.overcharge_explosion` and the support roles `full_charge`, `full_charge_impact` and `overcharge_explosion` (6.2, 6.3)~~ authored
  in 1.7.0 (Phase 6);
- projectile donors (7.1): listed as `NOT_AUTHORED` outputs until Phase 7 offered them (done in 1.7.0).

Plain number fields with a published range and opt-in are authored through the existing editors, with Runtime's rules:
- call-in time, the orbital pattern, the Eagle fields and the sentry fields (`StratagemScalar.CheckRange`);
- jump and hover fields;
- charge scalars: their range comes from the `charge` object (`SupportChangeService.CheckComposition`);
- the gib threshold: "-1, or 0 < value ≤ 100000" (`EntityRange.Disabled`, `EntityScalar.CheckRange`).

Their dedicated editors (sentinel toggle, ordering, defaults) remain Phases 4 to 6.

1. **Prerelease SDK versions.** *Done.*
   - `Metadata/SdkMetadata.cs:94` refuses `runtime_version` with a prerelease. Accept SemVer prereleases (`SemVersion` already parses them).
   - `SdkCompatibility.NewestSupportedVersion` (`SdkCompatibility.cs:12`) and `SdkPin` (`SdkPin.cs:15-24`): add 0.30.0-dev as supported for
     **local** SDKs only.
   - `GitHub/GitHubReleaseClient.cs:56,86` already skips prereleases. Keep that: a dev SDK arrives only as a local SDK, never as a download.
2. **`Metadata/PublishedArtifactVersion.cs:48`.** *Done* (also `0.30.0`). Map `0.30.0-dev` (and later `0.30.0`) to `Reused027` for the eight byte-identical graphs.
   - Without this, `PlayerWeaponComposition.cs:67`, `PlayerWeaponHeatCatalog.cs:57` and `AdvancedCapabilities.cs:67` refuse the SDK.
3. **`Metadata/PlayerWeaponCatalog.cs:25-27`.** *Done.* Add `string? Reason` to `SemanticAliasRule`. Deserialisation at `:186` is strict, and 0.30 adds
   `reason` to two new semantic aliases.
4. **`Metadata/SupportAuthoring.cs:189` `ValidValue`.** *Done* (16 MiB). Accept `("reference", "overcharge_explosion_reference")`.
   - `AuthoredTypes.Support` keeps it read-only until Phase 6.
   - The support catalog is 8.07 MB; `SupportAuthoring.MaxBytes` is 8 MiB (96 % used). Raise it to 16 MiB with a comment, before 0.30.x grows it.
5. *Done.* **`Metadata/StratagemCatalog.cs`** (2,406 instances instead of 1,606; new blocks `calldownCapability`, `presentationCapability`,
   `eagleAttack`, `sentryFields`; new top-level keys `eagleAirstrikePatterns` and `presentationSources`):
   - `:158-160` (native-identifier scan): `offset` and `payload` are now legitimate property names. Allow-list them inside
     `sentryFields.deferred[]`, `proximitySensor` and `eagleAttack`, not globally.
   - `:270` (hex-in-string rule): new prose fields (`reason`, `provenance`, `nativeReader`, `acknowledgementReason`, `rangeJustification`) contain
     hex addresses. Restrict the rule to value-bearing keys.
   - `:193`: the type gate gets the new value types `calldown_code` and `stratagem_presentation`. They stay read-only until Phase 4.
   - `:220`: `StratagemScalar.Normalize` must skip non-scalar values (objects in `allowedValues`).
6. **`Metadata/AssetDependencies.cs`.** *Done.*
   - `:23`: add `ObjectivePackage` to `AssetSummary` (strict at `:61`).
   - `:51-57,76`: accept the new kind `projectile_donor` and the derivations `explosion_effect_package` and `explosion_sound_package`.
7. **`Metadata/EnemyAuthoring.cs`.** *Done.* A gore record has its own backing object and operation.
   - `:127`: add the evidence tier `native_code_proven`.
   - `:164`: add the `gore` id prefix.
   - `:202`: the domain check must allow `gore.*` on the `entity` path.
   - `:220`: non-attack fields may carry `BackingObjectId`.
   - `:229`: the per-class entity-field count changes.
   - Read `valueRule` and `disabledValue`: the gib threshold is "-1, or 0 < value ≤ 100000". The plain range would wrongly allow 0.
8. **`Metadata/EntityAuthoring.cs`.** *Done.* `notices` turned out to be a field key, on vehicles and backpacks alike.
   - `:193`: add the evidence tier `native_consumer_proven` (vehicles, backpacks).
   - `:281`: model the new vehicle key `tuningFieldInstanceKeys` and the backpack key `notices`.
9. **`Metadata/AttackOutputs.cs:250`.** *Done.* SMG-37 and GP-31 own donor rows only, so they now have no own row. Selectable outputs of `kind: projectile_donor` have no `slots` or `presentation`; accept them.
   - Make `ProjectileRowLinks` and the Projectile Builder handle `Slots == null`.
   - Fix `Own()`/`OwnedBy()` (`:123,126`). Owners now have several rows (SMG-37 Defender, GP-31), and "first own row" picks a donor row.
10. **Capability audit.** *Done* (zero unexpected missing on the pinned SDK). Run `CapabilityAudit` on the 0.30.0-dev SDK. Every new value type or target must be either authored or listed as
    read-only (`AuthoredTypes`), never silently dropped.
11. **Fixture.** *Done* (`Runtime030Tests`; the fixture is the `356056c` build). Add `HD2RuntimeGUI.Tests/Fixtures/sdk-0.30.0-dev.zip` (the artifact above) and a `SdkLoadTests` case that loads it fully.

## Phase 2 — Export correctness (blocking for any 0.30 binding) — *done in 1.7.0*

1. **The SDK-version wrapper fails on a prerelease.** *Done.*
   - `ModExporter.WrapFor` uses the SemVer wrapper for every project bound to 0.30.0-dev or later, and whenever the required Runtime is
     newer than the bound SDK. Older bindings keep the plain wrapper byte for byte.
   - Runtime 0.30 reports `runtime.version` as the core `0.30.0` (`version_label` keeps `-dev`); `at_least('0.30.0', '0.30.0-dev')` holds. `ModExporter.Wrap` (`ModExporter.cs`, the second `Wrap`) does
   `version('{{p.SdkVersion}}')` with `^(%d+)%.(%d+)%.(%d+)$`. A project bound to `0.30.0-dev` would therefore fail to start ("Invalid HD2Runtime
   version").
   - Use the SemVer-aware `semver`/`at_least` pair the custom content wrapper already has.
   - Keep `local x,y,z=version(<bound SDK>)` with `version` returning the three core numbers: `core/sdk_compatibility.lua` reads exactly those
     locals.
2. **The declared SDK must stay the bound SDK.** *Done in 1.7.0 for custom content.*
   - **The bug.** The custom content wrapper declared `local minimum='0.30.0-dev'` beside `at_least`. The Runtime's `recognise()` reads that pair
     as the mod's SDK. A 0.28.x project with custom content would then lose the legacy acknowledgements (for example 0.30's new
     `allow_unverified_effect` on 30 PLAS-45 Epoch fields) and have operations refused.
   - **The fix.** The requirement is now `local required_runtime`, and the bound SDK is declared as `x,y,z`. Covered by
     `CustomContentProjectTests` (export).
   - **Remaining work.** Port the same shape to the plain wrapper in step 1.
3. **Minimum versions for 0.30 fields.** *Done:* `Generation/RuntimeRequirement.cs` mirrors the three tables. Exports declare its `Minimum`. The Runtime's `scripts/validate_examples.py` now forces `min_version >= 0.30.0-dev` for every new field and
   role (`UNRELEASED_FIELDS`, `KIND_FIELD_FLOORS`, `SUPPORT_ROLES_UNRELEASED`).
   - ModBuilder must compute `requires.hd2runtime.min_version` the same way: the bound SDK's version, raised by any 0.30 field the project
     writes. `CustomContentBuild.RequiredRuntime` already does this for custom content; generalise it.
4. **Rebinding 0.28.x → 0.30.0.** *Done.*
   - `SupportChangeService.Rebind` carries a support edit over when its write is unchanged apart from opt-ins, opaque scope / consumer
     keys and research labels. The Epoch fields also changed `resolution.linkage` and their scope keys.
   - The operation then carries 0.30's `allow_unverified_effect`; there is never a manual acknowledgement.
   - Test: `OldProjectCompatibilityTests.An_SDK_0_28_1_Epoch_edit_rebinds_to_0_30_with_the_opt_in_0_30_requires`.
   - The edits are kept, and the acknowledgements 0.30 requires are written (the Epoch `primary` / `primary_impact` fields).
   - The export then declares 0.30.0.
   - Extend `OldProjectCompatibilityTests` with a 0.28.1 → 0.30.0-dev rebind case.
5. **Validate exports with the Runtime's own validator.** *Done.*
   - `build-rc.ps1` and `run-rc-smokes.ps1` default to a `git archive` of `893d31f` (0.30.0; first `356056c`).
   - The custom content export goes through `validate-runtime.py` and `validate-exports.py`.
   - Run on 2026-10-07:
     - the F1–F9 and Halt H0–H3 exports: 26 of 26 `VALIDATED`, with isolation probes;
     - `C1-custom-templates.zip`: every custom stratagem registers (the two Runtime examples as the SDK fixtures do), and `validate-exports` passes.
   - The smoke scripts expect SDK 0.30.0.
   - Point `tools/validate-exports.py --runtime` at a `git archive` of the dev commit.
   - Add the custom content export (`C1-custom-templates.zip`, written by `CustomContentProjectTests`) to `scripts/run-rc-smokes.ps1`, through
     `tools/custom-content/validate-runtime.py`.

## Phase 3 — Fix what 0.30 makes misleading (small)

*Status:* item 1 is done (shown as blocked only when `writable` is false; its fields are the `orbital.*` stratagem fields). Item 2 is done for
vehicle, backpack and enemy fields (with Phase 5). Items 3–5 are done in 1.7.0 (see their notes).

1. **Barrage scheduling.** `Components/Stratagems.razor:157` lists all 10 orbitals' barrage scheduling as *blocked* with an empty reason. In 0.30
   it is writable (`barrageScheduling[].writable: true`, with `fields`, `values`, `shellTypes`, `rule`, `writeScope`).
2. **Dormant fields.** Show `notices` with `kind: dormant`, for example the LIFT-860 Hover Pack's launch thrust, which is dormant on that pack.
   *Done in 1.7.0* for every entity field (`EntityFieldEditor`): a **Not active** badge and the published note inline under the field
   ("Not read here (dormant): …"), informational only; the write is kept and built (Runtime logs its one-time `DORMANT`). Other notice kinds
   (`lifecycle`, …) are listed in the field's ⓘ section, and a mounted field's `lifecycle` (turret fields carry it outside `effect`) too.
   The support charge notices (`SupportCharge.Notices`, 4 dormant on ARC-3 / Meltagun) are shown by item 3.
3. **Relabelled and deprecated charge ids.** *Done in 1.7.0.*
   - `charge.level_1/2/3` are now "Minimum/Full/Overcharge charge time" in **seconds**; they were multipliers.
   - `charge.minimum_seconds` and `charge.maximum_seconds` are deprecated aliases of `charge.speed_multiplier_min` / `_overcharge`.
   - The Runtime logs `DEPRECATED`, `DORMANT` (ARC-3, Meltagun) and `CHARGE ORDER`. Show the same notes inline, never as blockers.
   - *Done:* charge fields carry ModBuilder's names and units on every SDK (`ChargeText`), so a project still bound to 0.28.x shows
     `charge.level_*` as the seconds they always were and the legacy ids as speed multipliers (with a note to rebind). The SDK's notices
     (`deprecated`, `dormant`) are shown on the row with **Deprecated** / **Dormant** badges. `CHARGE ORDER` is computed from the enabled
     edits (`ChargeFields.Times`) and shown under the full / overcharge time as a warning, never a build error. The charge-time evidence is
     unchanged from 0.28.1, so those edits survive rebind byte for byte; an edit saved on an older SDK says it was a "Charge level" multiplier
     and that the written value is unchanged. A saved deprecated id is offered **Move to** the field it writes (the alias, same bytes), **Use as**
     the charge time it was meant as, or **Remove** where it is dormant (`MoveSupportChangeAsync`).
4. **Fire rate on rate-selector weapons.** `hd2.fields.weapon.fire_rate` writes only the Y slot (`docs/fire-rate-modes.md`). Say so where the
   field is edited. *Done:* the `weapon.fire_rate` row of a player or support weapon with more than one filled rate slot says so
   (`WeaponFieldEditor`, `SupportFieldEditor`).
5. **Shared rows.** CONFLICT lines now name the holding mod. Surface the Torcher / Hot Dog shared-row note on both weapons. *Done:* the
   Torcher's damage rows name the Hot Dog gun (reverse lookup of the mounted weapons' `otherConsumers`), the Hot Dog gun's groups name the
   Torcher, and the diagnostics reference explains `held by` / `the same native record is also used by` when the bound SDK is 0.30 or later.

## Phase 4 — Stratagem authoring surfaces (the live-proven ones first) — *done in 1.7.0*

All of these are in `StratagemAuthoringCapabilities.json`.

**Status (1.7.0).** All six items are done. `AuthoredTypes.Stratagem` authors `calldown_code` and `stratagem_presentation`; their
`INTENTIONALLY_NOT_EXPOSED` entries are gone and the audit lists them as *call-in code* / *presentation*. Values live in the existing
`StratagemChange` (no new project property): a code is a JSON list of directions, a presentation value a stratagem name or
`{"image": id}`; a project that saves either is format 14 (`ProjectIdentity.RequiredFormat`). Value rules, Lua and the native-code check are
in `Generation/StratagemValues.cs` (`StratagemCode`, `StratagemPresentation`); the editors are `StratagemDefinitionFields`,
`StratagemCodeEditor`, `StratagemPresentationPicker` and `StratagemFieldEditor`, also in a vehicle's / backpack's call-in section
(`CallInFields`). Tests: `Runtime030StratagemTests` (with the `S1-stratagem-authoring` export fixture); older SDKs publish none of these
fields, and their projects keep their format and `min_version`.

1. **Call-in code** (`hd2.fields.stratagem.calldown_code`, type `calldown_code`). *Live-proven.* *Done.*
   - Arrow editor (reuse `CustomCodeEditor`): 1–9 directions, on all 94 stratagems.
   - `allow_unverified_effect` only when the value equals another stratagem's native code (`acknowledgementRule`). Check against every
     `currentDefault` code.
   - The HUD slot is redrawn (`hudSynchronization`).
   - *Done:* `StratagemCodeEditor` edits a draft (add, reorder, remove) and applies it in one save. **Runtime checks more than the
     catalogue:** `domains/stratagem_writes.lua` `validate_calldown` compares with all 149 native codes of the game
     (`domains/stratagem_calldown.lua` `nativeCodes`), including 55 stratagems outside the catalogue (Reinforce, mission stratagems).
     `StratagemCode.NativeCodes` therefore adds the uncatalogued ones from the custom stratagem schema's `catalogs.nativeCodes` (the same
     table), so a code equal to Reinforce's also carries the opt-in. The opt-in is shown as a warning naming the stratagem(s); a code that
     starts another (or is started by one) gets an ℹ note. A code this project also gives another stratagem (another code edit, or a
     custom stratagem's code) is asked about where it is applied (*Apply and restore their own codes / Apply and keep both / Cancel*,
     `SetStratagemCodeAsync`, one save) and reported inline afterwards with restore buttons. The HUD redraw is an info note.
2. **Presentation** (`presentation_name`, `_name_cased`, `_description`, `_icon`; type `stratagem_presentation`). *Live-proven.* *Done.*
   - Pickers over `presentationSources.stratagems` (94). The LIFT-860's `name_cased` is excluded as a source.
   - `_icon` also takes `hd2.resources.image(id)`. Reuse the custom icon pipeline (`CustomIconCompiler`, `ResourceArchive`, the
     `hd2runtime_images` record) so a plain stratagem edit can carry a custom icon.
   - Note "apply aboard the ship" (`applyTiming`).
   - *Done:* per-field pickers (grouped by family, the field's own value first, exclusions applied) and *Look like another stratagem*
     (`SetStratagemPresentationAsync`, one save; asks before replacing the mod's own icon). Runtime accepts no free text (raw localization
     ids and hashes are refused; custom text is not public yet), so there is none. The own icon is a PNG in the project's existing image
     store, `CustomContentSection.Images` (no second store; a section holding only such images does not make the project "have custom
     content"); images no custom object or icon edit shows are pruned in both workspaces. The export compiles it with
     `Runtime030Target.CompileImage` and packs it as custom content's are (`ModExporter.PackArchive`, the `hd2runtime_images` record,
     `images/<id>.png`, `image_sources` in `build-report.json`). The apply-aboard-the-ship note is shown on the group.
3. **Call-in time** (`hd2.fields.stratagem.call_in_time`, 0–60 s, `allow_unverified_effect`; read at beacon creation). *Done:* in the
   call-in group, with the *Next beacon* timing badge (every field with a `readTiming` gets one; its note is in the field's ⓘ).
4. **Orbital pattern.** *Done.*
   - Fields: `orbital.salvos` (1–16), `shells_per_salvo` (1–64), `shell_interval` / `_random` (0–10), `salvo_interval` / `_random` (0–30),
     `scatter` / `salvo_scatter` (0–100, record units). All need `allow_unverified_effect` and are live-read.
   - The editor shows the vanilla pattern as defaults, as the custom orbital editor does.
   - *Done:* a *Barrage pattern* group (salvos / shells in a salvo) with the vanilla pattern in words, this mod's pattern beside it, the
     shell-type cycle and Runtime's notes (rule, custom orbitals reading the same record). This is the Phase 3.1 barrage scheduling: the
     Advanced section shows it as blocked only when `writable` is false.
5. **Eagle attack.** *Done.*
   - `eagle.airstrike_pattern`: a dropdown over `eagleAirstrikePatterns` (value, name, bombs, length, lateral). Three jets need `allow_shared`
     (shown as a warning, never a checkbox).
   - `drop_interval`, `fire_duration`, `attack_sweep_length`, `target_radius`, `attack_angle`.
   - Read-only: `payload`, `bombs_per_strike`, `strafe_rounds_per_run`.
   - *Done:* an *Eagle attack* group with the attack kind, bombs per strike for the chosen pattern, the jet's other readers. A value outside
     `allowedValues` is refused as Runtime refuses it (`StratagemScalar.CheckRange`). The three shared jets (Gas, Napalm, Strafing Run)
     name their other readers in the group; the existing shared-scope warning applies, never a checkbox.
6. **Sentries.** *Done.*
   - Spread and recoil drift/climb.
   - `turret.pitch_yaw_coupling`.
   - `targeting.side_range` / `rear_range`, where −1 means "use targeting.range" (`sentinelValues`): an explicit "same as range" toggle.
   - Wind-up and wind-down (Gatling).
   - The Laser Sentry's `beam.fire_rate`.
   - *Done:* **the turret, targeting and minefield fields (paths `turret` / `targeting` / `minefield`, writable since 0.28.0) were not shown
     by the stratagem editor at all**; `StratagemEntityNode.Components` now shows them on the deployed entity, with the hard-coded
     engagement distance. A sentinel is accepted besides the range (as Runtime does) and is a *Same as range* toggle (unticking starts from
     the sentry's targeting range). The mounted weapon groups are *Spread*, *Recoil*, *Windup* and *Beam*. Deferred and excluded sentry
     fields are listed with Runtime's reason in Advanced.

## Phase 5 — Vehicles, backpacks, enemies — *done in 1.7.0*

Every field below is an ordinary entity change (`EntityChange`, no new project property, no format change), generated by `EntityLua` with the
opt-ins Runtime publishes (`allow_unverified_effect`; never a checkbox), raising `requires.hd2runtime.min_version` to 0.30.0-dev through
`RuntimeRequirement` (the vehicle_weapon `turret.*` floors are `KIND_FIELD_FLOORS`). Projects bound to older SDKs are unaffected: those SDKs
publish none of these fields (the 0.28.1 launch velocity is below). Tests: `Runtime030VehicleTests` (Lua forms of the Runtime's own examples,
ranges, ordering, rebind, save / reopen, the capability audit). A sample export with every field validated with the Runtime's
`tools/validate-exports.py` against a `git archive` of `356056c`.

1. **Vehicle tuning** (tier `native_consumer_proven`, all `allow_unverified_effect`, listed in `tuningFieldInstanceKeys`).
   - `vehicle.steering_response_speed`: 7 vehicles.
   - `rotation.turn_speed`, `acceleration`, `deceleration`: 4 Exosuits.
   - *Done.* `AuthoredTypes.VehicleTuning`. Stratagems → Support → (vehicle) has a **Handling** section (`VehicleEditor`) with the published
     ranges and reasons, Runtime's lifecycle notes and an informational unverified-effect warning; Changes names the part "Handling". The
     Exosuit's three rotation fields share one record: one transaction, as in `examples/projects/VehicleTuningTest`.
2. **Mounted turrets.** `turret.yaw_speed` / `pitch_speed` (1–720 °/s), `pitch_min/max` (−90..90), `yaw_min/max` (−180..180).
   - On 5 mounts. Each carries `min`, `max`, `rangeReason` and an ordering constraint `order: [pitch_min, pitch_max]`, enforced inline.
   - Extend `VehicleWeapons.cs:12-17`, which reads none of these keys today.
   - *Done.* `AuthoredTypes.MountedTurretMotion`; `VehicleWeaponFieldInstance` already carried the keys (Phase 1). Each mount's **Turret motion**
     section (`TurretMotionEditor`, after the weapon values) shows the speeds, then each limit pair. Runtime refuses an operation that leaves
     a pair with low ≥ high (`TURRET_LIMIT_ORDER`, `domains/player_weapon_writes.lua`; a limit the operation does not write keeps its reviewed
     value). `EntityOrder` mirrors that: every turret field of a mount is one transaction, so a pair is checked with each side's enabled edit,
     else its baseline. An edit that would cross is held in the pair and decided there (**Swap them** / **Keep** / **Cancel**). A kept or
     otherwise unordered pair is refused at build time on both rows, and the pair offers **Swap** or **Reset … to vanilla** in place
     (`SetEntitiesAsync` saves both limits at once). An in-game option on a limit has each sample checked against the other limit's edited
     value. Not covered: two limits of one pair both bound to options (each is checked against the other's edited value, not its samples).
3. **Jump and Hover Packs** (13 `jump.*` and 7 `hover.*` fields, group `movement`).
   - Unit change: `jump.vertical_launch_velocity` is now "Launch thrust" in m/s².
   - Dormant on the Hover Pack (Phase 3.2).
   - *Done.* The fields were already writable through the generic backpack editor (Phase 1). The published `movement` group is now
     `BackpackMovement`, grouped by flight phase (take-off hop, launch, sustain, air control, hover) with what each does, after Runtime's
     `docs/backpack-authoring.md`, and the "next frame" note. **The stored value did not change:** Runtime keeps the id and the baseline (40)
     and only corrected the unit ("a thrust, not a velocity"). The capability evidence of every 0.28.1 backpack and vehicle field is
     identical in 0.30.0-dev, so a 0.28.1 edit opens untouched and rebinds without review, same value, same write
     (`A_0_28_1_launch_velocity_edit_rebinds_…`). No rebind step was needed. The field is labelled **Launch thrust** (m/s²) for every SDK;
     on an older SDK a note gives that SDK's name.
4. **Enemy gib threshold.** `hd2.fields.gore.whole_body_gib_damage` on 23 Terminid classes; −1 disables it, or 0 < value ≤ 100000. It is read at
   hit time, so it also applies to living enemies.
   - *Done.* The enemy editor has a **Gibbing** section (the class's gore record) with what the threshold means and the hit-time note. Any field
     with a `disabledValue` gets an explicit switch in `EntityFieldEditor` (**Disable bursting** here) instead of a typed −1. Turning it off
     returns to vanilla; on the Hive Guard (vanilla −1) it opens the box for a threshold above 0. 0 and other non-positive values are refused
     with Runtime's value rule.

## Phase 6 — Support weapon charge — *done in 1.7.0*

**Status (1.7.0).** All four items are authored (`Generation/ChargeFields.cs`, `Services/SupportChargeWorkspace.cs`, `SupportChargePanel`,
`SupportChargeLevelNote`; tests `RuntimeChargeTests`, export fixture `F10-support-charge`). The generated Lua has the shape of the Runtime's
`RailgunChargeTest` and `EpochExplosionsTest`. Exports declare 0.30.0-dev through `RuntimeRequirement`. The reference value is saved in the
existing support change model; a project that saves one needs format 14 (1.7.0's), so an older ModBuilder refuses it instead of misreading it.

1. **Charge scalars.** `charge.speed_multiplier_*`, `damage_multiplier_*`, `penetration_multiplier_*`, `arc_distance_multiplier_*`,
   `auto_fire_at_full`, `explode_at_overcharge` (hazard), `overcharge_limit_seconds`, `burst_shots`, `burst_interval_seconds`.
   - Ranges come from the new per-field `charge` object (`min`, `max`, `rangeReason`, `meaning`, `family`, `effect`), not from `value`. Extend
     `SupportField` (`SupportAuthoring.cs:43`).
   - *Done:* a **Charge** section per charge weapon (Railgun, Epoch, ARC-3, Meltagun: the four the SDK lists; the Quasar and Laser Cannon have
     no charge record in it), grouped by stage (minimum, full, overcharge, burst, deprecated ids), with the meaning, the range (the number
     input's min / max, and the inline refusal with `rangeReason`), the vanilla value, booleans as toggles, the family badge and the
     `lifecycle`. The hazards (`explode_at_overcharge`, `overcharge_limit_seconds`) carry a **Hazard** badge and note. All charge fields of a
     weapon share one operation group, so related times are one transaction, as the Runtime asks.
2. **New attack roles.** `full_charge`, `full_charge_impact` and `overcharge_explosion` (Epoch); `overcharge_explosion` (Railgun).
   - Every field on these rows needs `allow_shared` and `allow_unverified_effect`.
   - `full_charge_impact` and `overcharge_explosion` share a damage row. Resolve it inline where the second is edited, never
     with a banner.
   - *Done:* the roles are writable (`AuthoredTypes.SupportChargeRoles`); each branch shows its `chargeLevel` (level, when it fires, read timing,
     sharing, multiplayer, self-damage). `ChargeFields.SameBytes` finds fields of one weapon that write the same bytes (same backing object and
     field constant, or a deprecated alias). Editing the second asks in the row (*Keep both in sync at V* / *Cancel*) and keeps one edit; the
     other row shows *Changed through …*. Two saved edits (an older file, a script) are an issue on both rows with **Keep this edit**, and a
     build error: the Runtime would refuse them as overlapping changes in one plan, whatever their values.
3. **`charge.overcharge_explosion`** (`reference/overcharge_explosion_reference`). Its value is `'RS-422 Railgun'` or `'PLAS-45 Epoch'`;
   another weapon's needs `allow_unverified_reference`.
   - *Done:* a choice of `charge.allowedValues` with the donor summary and its package. `allow_unverified_reference` is emitted when the value
     differs from the reviewed one (the Runtime's `value ~= expect`). With another weapon's explosion, the Runtime refuses writes through the
     weapon's own `attack('overcharge_explosion')` (its row is re-proven through the reference). Choosing it asks to discard those edits, and
     editing such a row asks to reset the reference. Both saved is an issue and a build error.
4. **Epoch rebinding.** The 30 existing Epoch `primary` / `primary_impact` fields now need `allow_unverified_effect`. They are written on rebind
   (Phase 2.4) and declared legacy for older bindings.
   - *Verified:* after a 0.28.1 → 0.30.0-dev rebind the edit is kept, `CompositionOptIns.EffectFor` is true, so the row shows the *Effect
     unproven* badge and the branch its warning panel, and the branch says *Partial charge* (`RuntimeChargeTests`).

## Phase 7 — Projectile donors — *done in 1.7.0*

1. **32 new donors** (`kind: projectile_donor`). Owners: 23 stratagems (Eagle payloads, orbital shells including the Railcannon, sentry rounds, the
   HMG Emplacement, the TD-220 Bastion), 5 player weapons and 4 support weapons. *Done* (`AuthoredTypes.ProjectileDonors` is true; the audit
   exemption is gone).
   - Offer them in every projectile selector's Vanilla group. *Done:* `AttackOutputCatalog.Donors` offers them on player component and
     ammunition hosts, support and mounted hosts (the Projectile Builder's host swaps), and `FunctionProjectile.Donors` as a
     programmable-ammunition projectile. Every donor has `compatibleHostFamilies: ["projectile"]` and no `referenceScope`. A donor is never a
     classic reference swap (`ClassicSource`: the graph names the owner's first projectile, not a second projectile or donor row), and a
     weapon's own second projectile is offered to that weapon too.
   - Both `sameClass` and `crossClass` need `allow_unverified_reference` and `allow_unverified_effect`, shown as warnings. *Done:* added to
     every donor write whatever the host's own opt-ins (`Donors`; `CompositionOptIns.Donor` for `function_ammo.projectile`, player and
     support). Runtime's `require_unverified_donor` refuses a donor without them on both paths: removing one from the D1 export makes the
     validator skip that operation.
   - `package.autoLoad` is true; show `package.via`. *Done* (the auto-load tooltip names the route). Runtime loads the package itself: a
     stratagem donor through its `dependencyKey` (`projectile_donor/<name>`), a weapon donor through its owner weapon. Nothing extra is
     generated: the swap is `value=hd2.attack_output('<semantic id>')`, exactly as in `docs/attack-outputs.md`
     (`Golden/projectile-donor-swap.lua`).
   - `requires.hd2runtime.min_version`: `RuntimeRequirement.Floors(project, sdk)` raises it to 0.30.0-dev for every enabled donor swap or
     donor function projectile. Runtime's `validate_examples.py` detects donors only by a `(projectile <n>)` name (`usesMoreDonors`), so it
     reports `need=0.28.0` for ModBuilder's semantic ids; ModBuilder declares the floor itself.
   - Donors are labelled with Runtime's name, `<owner> (projectile <type>)` (`AttackOutput.Label`): the SMG-37 Defender and the Eagle
     Smoke Strike own two each.
   - Edits a swap invalidates are still resolved inline in the selector (switch and discard / cancel). A donor has no builder slots or mode
     presentation, so the row links and the mode label editor stay hidden for it.
   - Tests: `Runtime030DonorTests` (every host kind, both opt-ins, golden Lua, round trip, the floor, programmable ammunition) and the
     `D1-projectile-donors` export fixture, validated with `tools/validate-exports.py --isolation` against `356056c` (2 of 2 `ok`).
2. **Display `flight`, `names` and `emitter`** in the donor list. *Done* (`DonorDetail`): role, projectile type, speed, mass and unit-driven in
   every donor list (player, support, mounted, programmable ammunition); the emitter, Runtime's names, the package route, the flight note and
   the `projectile_more_donors` live evidence where a donor is chosen.

## Phase 8 — Scripting — *done in 1.7.0*

1. **Stub.** *Done.*
   - Bind the 0.30 stub (+513 lines): `HD2Image`, `HD2Model`, `hd2.resources.image` / `model`, `HD2Fields_rotation` / `_gore`, the new field
     constants, `hd2.diagnostics.performance()` and `hd2.version_label`. All of them complete; `Runtime030ScriptingTests` walks every class,
     field and function of the stub.
   - **`hd2.enemies` is now a callable table** (`---@type HD2Enemies|fun(filter?…)`). *Done:* `TypeLine` reads the whole type
     (`SplitType`), `LuaApiIndex.Union` splits a union only at a top-level `|`, and `FirstType` skips function types (so does
     `lua-editor.js`). The table is the class `HD2Enemies` and keeps its call as its signature (`enemies(filter?: {…}) → string[]`).
     Completion is checked in C# and by running `lua-editor.js` under Node.
2. **References and pickers.** *Done* (`GameplayActionPicker`, at the end of the Custom Lua *Actions* tab; `Scripting/GameplayActions.cs`).
   Each section appears only when the stub publishes its call, shows the SDK's signature and doc, and inserts code built from it.
   - Homing (`hd2.projectiles.homing`): options `target`, `turn_rate` 1–1440, `cone`, `range`, `arm_distance`, `aim_height`, `retarget`,
     `multiplayer`. Read in their published order from the catalog's `HD2HomingOptions`, with the range and default each doc states; an
     out-of-range value is reported inline and disables the insert. Player and support weapons are offered; stratagem payloads and sentries
     home by the names `homing_list()` returns in game (that 173-name list is research data, not in the SDK).
   - Spawn weights (`hd2.enemies.spawn_weight`, with the acknowledgement in `opts`): the enemy catalogue's ids, the 0..10 multiplier from the
     doc, optional difficulties. `allow_unverified_effect = true` is always written and explained in a note, never a checkbox.
   - Limb injury and heal (the limbs from the signature); velocity (≤25 m/s change, read from the doc, refused inline); loadout, held weapon,
     backpack and ammo (with its slot literals); `resupply_from_pack`.
   - `hd2.pelican.spawn` (the catalog's `actions.pelican` limits: 4 alive, 4 at once and 0.25/s, hover ≤120 s; not live-proven);
     `hd2.sounds` (`WeaponSoundCatalogue.json`, 171 sounds) as a sound picker. The catalogue is embedded byte for byte as
     `Scripting/Bundled/WeaponSoundCatalogue-0.30.0.json` (like the custom content schema), so the SDK pin and its fingerprint are
     unchanged; it is offered only when the stub publishes `hd2.sounds`. A sound can be inserted or used for the Pelican's chin gun.
     Refresh the copy with each SDK swap (the test compares it with the fixture).
   - `hd2.resources.image` / `model`: reference only, with their live evidence (no insert: the files must be in the mod).
3. **Snippets.** *Done.* The live-proven 0.30 writes a hand-written script can make: *Change a stratagem's call-in code* (the proven Orbital
   120mm HE Barrage, its native code as `expect`; `allow_unverified_effect` only if the new code equals another native code) and *Make a
   stratagem look like another* (the four proven presentation fields). Plus *Log the slowest mods* (`hd2.diagnostics.performance`, always
   on), also offered from the diagnostics reference, which now documents the `PERFORMANCE:` lines. The custom stratagem families
   (`custom_stratagem_weapon_variant`, `custom_stratagem_expendable_*`) belong to the custom content builder, and a custom image needs an icon
   family in the mod, so neither has a snippet.
4. **Live evidence.** *Done* where scripting shows it: `ScriptingReference.Evidence` also takes a family of another domain whose field is
   exactly the API (`enemy_spawn_weights`; `custom_stratagem_weapon_variant` on `hd2.resources.model`), and links
   `custom_icon_resource_family` and `stratagem_presentation_custom_image` to `hd2.resources.image`. `projectile_homing` shows with homing and
   `projectile_more_donors` with every donor. The stratagem field families (`stratagem_calldown_code`, `stratagem_presentation`, the pending
   Eagle, orbital, call-in time and sentry families) reach the stratagem editor through the catalog's own per-field `liveEvidence` (Phase 4);
   the custom stratagem and panel families through the custom content schema's `familyEvidence`.
5. **Event catalogue.** Its events are unchanged (20). Its API gains 20 classes and `actions.pelican`. *Done:* `EventActions.Pelican`
   (`PelicanActions`, with a fractional rate); the reference lists the new classes, and method evidence uses the stub where the catalog's API
   has no field (`hd2.enemies.spawn_weight`).

## Phase 9 — Custom content follow-ups (1.7.0 shipped the overhaul; see custom-content.md)

1. **Mod Options choice handles.** The Runtime accepts an `hd2.options` choice for an expendable's `level`, a variant's `model_use`, and a Pelican
   gun's `round`, `spread`, `aim_height` and `impact_explosion`.
   - ModBuilder 1.6.0 has in-game options. Let these fields bind an option exactly as other edits do.
2. **Custom text.** The planned `hd2.resources.text(id, {default=…, fr=…})` with 15 languages is documented but not implemented. When it ships,
   give names and descriptions per-language entries.
3. **More model bases.** Only the M-1000 Maxigun has `ModelBaseCapabilities`. `CustomModelCompiler` is base-agnostic: a new base needs only the
   updated facts file.
4. **Custom projectiles stay behind a hard caveat.** They are internal Runtime modules, not exported by `hd2`, and host-only. HD2Runtime 0.30.0
   moved their multiplayer to its next version ("Not in 0.30.0"). Keep them Experimental until the Runtime publishes an API.
5. **Re-check the families after each live test.** `familyEvidence` (live / partial / offline, multiplayer) drives the badges. Refresh the bundled
   schema with each dev SDK; `tools/custom-content/make-runtime-oracle.py` regenerates the conformance oracle.

## Phase 10 — Release

1. **Version.** Binding 0.30 changes what projects can hold, so the release is a **minor** version, per the release rules. 1.7.0 was not
   published before HD2Runtime 0.30.0, so the binding ships in **1.7.0** with custom content (project format 14 covers both).
2. **Pin.** *Done* (2026-10-08) with the HD2Runtime 0.30.0 release candidate `893d31f` (`build/release-candidate-0.30.0/`; its SDK is the
   release asset, see the [build record](#development-sdk-build-record)):
   - *done:* the SDK bundled byte for byte by `tools/swap-bundled-sdk.py`; `SdkPin`, `SdkCompatibility` (follows the pin) and
     `PublishedArtifactVersion` (the eight graphs are still byte-identical to 0.28.1's);
   - *done:* `CustomContent/Targets` and the sound catalogue renamed from `0.30.0-dev` to `0.30.0`; the target is `Runtime030Target`
     (`hd2runtime-0.30`);
   - *done:* "development" dropped from the custom content banner, as the Runtime did for the custom stratagem API. The Experimental badge
     stays: the Runtime still calls several players experimental, and custom projectiles are internal modules;
   - *done:* `RuntimeRequirement.Release030` is `0.30.0`, the version the released validator names. SDK checks use `First030`
     (`0.30.0-dev`), so projects bound to the test SDK still open, export (declaring 0.30.0-dev, or 0.30.0 once they use a 0.30 addition)
     and rebind to 0.30.0 (`OldProjectCompatibilityTests`).
   - **Publishing order:** HD2Runtime 0.30.0 must be public before ModBuilder 1.7.0. Until then the update check finds only 0.28.1, offers
     nothing and keeps the bundled SDK (`SdkPinTests.The_bundled_pin_works_before_and_after_its_release_is_published`).
3. **RC.** `build-rc.ps1` without `-AllowDirty`, from the release commit:
   - the full test suite;
   - Runtime export validation, including the custom content export through `validate-runtime.py`;
   - the packaged-app smokes, including `tools/custom-content-smoke.mjs`.

---

## Runtime-side fixes to report upstream

Defects found while building the SDK and porting its tools. None blocks ModBuilder (each is worked around or tolerated), but each should be
fixed in the Runtime.

1. **Two compiled examples carry CRLF hashes.**
   - `proof/EAT23Example/src/addon.lua` and `proof/ShredderSiloExample/src/addon.lua` were compiled from CRLF working copies of
     `custom_stratagems.json`. Their generated header records the SHA-256 of the CRLF bytes, while git stores the files as LF
     (`.gitattributes` `eol=lf`).
   - As a result, `test_new_stratagem_examples` and `test_custom_silo` (`..._is_its_compiled_project`) fail in every clean checkout, and the
     release build refuses.
   - Fix: hash the LF-normalised text in `compile_project`, or recompile both examples from an LF checkout.
2. **The SDK validator crashes on an unknown key inside `pelican.orbit` or `pelican.approach`.**
   - `custom_stratagem_project.py` `validate` reports the unsupported field, then runs `fields[key]['fields'][k]` → `KeyError`.
   - It should `continue` for unknown keys, as `_weapon` does. ModBuilder's port does that. The conformance oracle records the crash
     (`pelican-orbit` case).
3. **Stub drift.**
   - `HD2CustomStratagemSpec.selection` still says "default 'token'". Since r44 the carrier itself is the default.
   - `HD2CustomStratagemCarrier.group` lacks `weapon`.
   - `HD2CustomStratagemSpec.delivery` lacks the expendable and weapon forms.
   - `icon` refers to an undefined `HD2ImageHandle` (the class is `HD2Image`).
4. **Duplicate alias.** The named explosion `Cyborg Production Unit` has the alias list `["Portable Hellbomb"]`, which is the B-100's alias.
5. **`WeaponFeedCapabilities.json`.** The AC-8 Autocannon's and RL-77 Airburst's `feeds[0].presentation` became `null`; they were APHET and FLAK.
   Probably a regression.
6. **Catalog size.** `SupportWeaponAuthoringCapabilities.json` is 8.07 MB. Consider splitting it, or tell builders the expected ceiling.
7. **The SDK validator refuses the `orbital` and `any` carrier groups without a beacon.** `_family_carrier` implies a group's beacon only
   when it is not `any`, then requires the payload's beacon, so `{"group": "orbital"}` fails for an orbital or a Pelican ("a orbital payload
   needs a offensive or support or any beacon"). The Runtime registers it (`check_policy` makes the beacon optional with a group; the
   group's policy has beacon `any`). Fix: `if beacon is None: beacon = g['beacon']`. ModBuilder's port follows the Runtime (tested).
8. **The model validator accepts a palette with no colours** (`{"rows": {}}`), which changes nothing. ModBuilder refuses it with the SDK's own
   palette message.
9. **A crossed turret limit pair validates.** With `turret.pitch_min` at or above `turret.pitch_max` (or the yaw pair), the export validator
   reports VALIDATED while the Runtime skips the operation at apply time (`TURRET_LIMIT_ORDER`). The validator should refuse it, as it refuses
   other published constraints. ModBuilder refuses it at build time and resolves it where the limit is typed.
10. **`scripts/validate_examples.py` recognises a projectile donor only by its `(projectile N)` name.** A donor written as its semantic id
    (`hd2.attack_output('output/v1/projectile/…')`, as `docs/attack-outputs.md` shows) is reported as `need=0.28.0`, so the 0.30.0-dev floor
    is not enforced for it. ModBuilder declares the floor itself (`RuntimeRequirement`).
11. **0.30.2 catalog summaries counted before the last corrections.** `scripts/generate_weapon_authoring.py`:
    - `block_overridden` makes the LAS-5 Scythe's `heat.capacity` and three heatsink fields read-only after
      `summary.writableFieldInstances` was counted (3365 published, 3361 editable), and `PlayerWeaponHeatCapabilities.json` still lists
      the four writable;
    - `apply_root_corrections_ammo` re-resolves the GP-31's effective capacity and updates `effectiveCapacityResolved` but not
      `effectiveCapacityNativeResolved` (47 published, 48 `RESOLVED`);
    - it also makes the CQC-42 Machete's companion magazine fields writable although the authoring catalog publishes no magazine field
      for it.
    ModBuilder accepts exactly these differences (the authoring catalog decides what is writable). Fix: count the summaries after both
    corrections and apply `block_overridden` to the heat companion.
12. **The attack output catalogue names the vehicle accessor for the 0.30.2 sentry and emplacement hosts** (`projectileSources[].write`:
    `hd2.vehicle(vehicle):weapon(mount):...`); `VehicleWeaponCapabilities.stratagemHosts[].api` and the docs use
    `hd2.stratagem(name):attack(role):projectile_source()`.

## Swapping the bundled SDK

The same steps apply to any later SDK (they were used for the 0.30.0-dev builds, the final 0.30.0, 0.30.2 and 0.30.3). Steps 1 and 2, the custom content target's files (schema, model
bases, stub, the two templates that are Runtime examples) and the conformance oracle are scripted, byte for byte:
`py -3 -B tools/swap-bundled-sdk.py --zip <HD2Runtime-x.y.z-sdk.zip> --commit <full Runtime commit>`. It prints what a new version string
still needs by hand (step 3). Swapping the current 0.30.3 ZIP again changes nothing.

1. **Copy the files, byte for byte (no text-mode copy).**
   - Copy the SDK ZIP to `HD2RuntimeGUI.Tests/Fixtures/sdk-<version>.zip`.
   - Replace every file in `HD2RuntimeGUI.Core/Metadata/Bundled/` with the archive's file of the same name.
   - `hd2runtime-stubs.lua` comes from `stubs/mods/skyeshade/hd2runtime.lua`.
   - `WeaponSoundCatalogue.json` is not an SDK cache file: copy it to `HD2RuntimeGUI.Core/Scripting/Bundled/` (and update
     `WeaponSounds.Resource` for a new version string). `Runtime030ScriptingTests` compares it with the fixture.
2. **Update `SdkPin`.**
   - `Version`, `SdkCommit`, `RuntimeCommit`, `ArchiveName`, `ArchiveSha256` (the ZIP's SHA-256) and `ContentFingerprint`.
   - `SdkPinTests.The_fixture_is_the_pinned_release_asset_and_the_bundle_is_its_files` reports the fingerprint it computes.
   - `SdkCompatibility.NewestSupportedVersion` follows `SdkPin.Version`.
3. **For a new version string (the final 0.30.0), also:**
   - `PublishedArtifactVersion`: the eight player-weapon graphs. While a release republishes them byte for byte under the old `0.19.0`
     label (0.30.0, 0.30.1), map its version to that digest set. When a release regenerates them it labels them with its own version
     (0.30.2, 0.30.3): they then match the catalog version and need no mapping, and a regenerated graph still labelled `0.19.0` is refused;
   - `Runtime030Tests.Version` and `Fixture` (keep the previous pin's fixture as `Previous` for the projects bound to it), and the version
     literals in `OldProjectCompatibilityTests`' 0.30 cases;
   - a `RuntimeRequirement.Release0x0y` floor only for what the release adds (0.30.2: `Release0302`; 0.30.3: `Release0303`); existing
     floors keep naming the release that added their feature;
   - `$RuntimeCommit` / `$RuntimeVersion` / `$RuntimeTree` in `scripts/build-rc.ps1`, `$RuntimeTree` / `$SdkVersion` in
     `scripts/run-rc-smokes.ps1`, and the expected SDK in `tools/*-smoke.mjs`;
   - the custom content target (Phase 10).

   A 0.30.0 final is not a prerelease: `GitHubReleaseClient` offers it as a download. It replaces 0.30.0-dev for new projects. Projects
   bound to 0.30.0-dev keep that binding until they are rebound.
4. **Run the full test suite.**
   - The counts in `Runtime030Tests` (2406 stratagem fields, 31 donors since 0.30.2, 94 presentation sources) and `Runtime0302Tests` are checks of the pinned data.
   - A build that changes them needs those numbers reviewed, not just updated.

A same-version swap (another 0.30.0-dev build) leaves a user's already-cached 0.30.0-dev in place. The SDK cache treats a version as
immutable, and Settings reports the new bundle as "not the pinned build" until that cache folder is removed. This affects only testers who
ran an earlier dev build.

## Development SDK build record

- **Bundled since 1.7.1: HD2Runtime 0.30.3, release candidate `7da218b`** (2026-10-09; tag `v0.30.3` = `dcd996d`, which adds only
  migration post-validation records). Swapped with `tools/swap-bundled-sdk.py` from the **published** release asset
  `HD2Runtime-0.30.3-sdk.zip`, SHA-256 `f1f53735470328968fab658427188b93ba8f49d925f0a6e469b30e2ae997af9d` (identical to
  `HD2Runtime/build/release-candidate-0.30.3/`: 2,060 Runtime tests, 121 examples VALIDATED, 211 packaged-runtime scenarios), content
  fingerprint `491a89a90717841ecbba19cfcea213f6e3b90d11eb01afffab0d018027d72ede`. Against 0.30.2 the SDK changed only version strings (the
  eight graphs are 0.30.2's, relabelled 0.30.3, so `PublishedArtifactVersion` needs no digest) and the custom stratagem `uses` range. The
  rest of 0.30.3 is Runtime fixes (a false *Another mod changed the stratagem data* card; one stray entity map row blocking every write).
  What 0.30.3 changed for ModBuilder:
  - **Unlimited custom stratagem uses.** `metadata.uses` is -1 to 100, where 0 or -1 (or none) mean unlimited (0.30.2: 1 to 100; `tune`
    and `register` accept them too). `RuntimeProjectValidator` follows the schema's range. The editor offers **Unlimited** (no value, which
    every 0.30 release reads as unlimited) or **Limited** 1 to 100. An explicit 0 or -1 is kept only from an imported Runtime project, so it
    round-trips; it raises the export to 0.30.3 (`RuntimeRequirement.Release0303`: 0.30.2 refuses it), said in the field with **Use
    unlimited (no value)**, which removes the floor. New oracle cases: `uses-minus-one` (valid), `uses-minus-two` (refused) and
    `uses-hundred-valid`; `uses-zero` is now valid.
  - **Custom content target** files renamed `*-0.30.3.*` (`CustomStratagemSchema-0.30.3.json`, SHA-256 `aa898c24…d74e`;
    `ModelBaseCapabilities-0.30.3.json`, `hd2runtime-stubs-0.30.3.lua`) and `WeaponSoundCatalogue-0.30.3.json`. The target's floor stays
    0.30.0 and its banner is unchanged.
  - **Floors:** 0.30.0's features keep `Release030` and 0.30.2's keep `Release0302`; a project bound to 0.30.3 declares 0.30.3. Projects
    bound to 0.30.2 (fixture `sdk-0.30.2.zip`), 0.30.0 or 0.30.0-dev open and export on their SDK and rebind to 0.30.3
    (`OldProjectCompatibilityTests`). The project format stays 14, so ModBuilder 1.7.1 is a patch release. Export validation and the
    smokes use a `git archive` of `7da218b` (`%TEMP%\hd2runtime-0.30.3-7da218b`).

- **Bundled by the 1.7.x SDK move (after 1.7.0, not released; 1.7.1 bundles 0.30.3): HD2Runtime 0.30.2, release candidate `dd32795`** (2026-10-09; tag `v0.30.2` =
  `4118452`, which adds only migration post-validation records). Swapped with `tools/swap-bundled-sdk.py` from the **published** release
  asset `HD2Runtime-0.30.2-sdk.zip`, SHA-256 `eff875477cf7a3c70696b3fcbf15e92ad5456329d6c4c1aad1eebe4a2139c56e` (identical to
  `HD2Runtime/build/release-candidate-0.30.2/`: 2,058 Runtime tests, 121 examples VALIDATED, 211 packaged-runtime scenarios), content
  fingerprint `8b249a051681c8b43a59a70d79362acfa4df6660574dd017f05d1a5ef15d00eb`. 0.30.1 (Public play warnings only) changed no SDK
  content; its eight graphs are 0.30.0's bytes, so `PublishedArtifactVersion` maps `0.30.1` to the same digests. What 0.30.2 changed for
  ModBuilder:
  - **Seven weapons resolved** to their proven roots (GP-31, P-72, LAS-5, LAS-7, CQC-42, CQC-73, SMG-37): UNIQUE and writable through the
    existing editors (233 authored instances); an edit on one requires 0.30.2 (`RuntimeRequirement.Release0302`, mirroring the validator's
    `WEAPON_ROOTS_0_30_2`). The GP-31 and the Dagger read their own values. All eight graphs were regenerated and are now labelled 0.30.2.
    The ammo companion carries `candidateRoots` (modelled) and drops `resourceValues`.
  - **Generator inconsistencies, accepted narrowly** ([Runtime-side fixes](#runtime-side-fixes-to-report-upstream), item 11): the authoring summary counts the LAS-5 Scythe's four
    heat fields as writable after `block_overridden` made them read-only (3365 vs 3361), the heat companion still lists them writable, and
    the ammo summary's `effectiveCapacityNativeResolved` omits the re-resolved GP-31 (47 vs 48). The CQC-42 Machete's ammo companion has
    writable magazine fields the authoring catalog does not publish (evidence only).
  - **New, read-only until authored** (`AuthoredTypes`, classified in the capability audit; survey in
    `artifacts/survey/sdk-0.30.2-new-capabilities.md`): status slots on player-weapon explosions (26 instances on 13 weapons), 23 extended
    statuses (`attachTier` enemy_slot / other_system, not offered by any status slot), 9 sentry and emplacement projectile hosts
    (`VehicleWeaponCapabilities.stratagemHosts`, 7 writable), and `noEffect.reason` on 11 backpack-fed ammunition rows (read; the fields
    stay authored).
  - **Retired:** the attack output `SMG-37 Defender (projectile 150)` (31 donors). A saved swap to it reports at its selector after a rebind.
  - **Sounds:** `primary/smg37/seaf` is now `seaf/3` and `secondary/gp31/alt` is `other/wep_grenadier_rifle/1` (the catalogue's
    `aliases`; the Runtime plays both). The custom stratagem schema lists only the new names: a saved custom stratagem with an old one
    shows the refusal under its sound field with the new name offered there. The new names require 0.30.2.
  - **Custom content target** files renamed `*-0.30.2.*` (`CustomStratagemSchema-0.30.2.json`, SHA-256 `9dc2f986…6945`; only its sound
    list changed) and `WeaponSoundCatalogue-0.30.2.json`. The target stays `hd2runtime-0.30`; its floor stays the API's release, 0.30.0
    (`Runtime030Target.MinimumRuntime`), and the API status is unchanged, so the banner is too.
  - **Floors:** what 0.30.0 released keeps `Release030`; a project bound to 0.30.2 declares 0.30.2. Projects bound to 0.30.0 or 0.30.0-dev
    open and export on their SDK and rebind to 0.30.2 (`OldProjectCompatibilityTests`). Export validation and the smokes use a
    `git archive` of `dd32795` (`%TEMP%\hd2runtime-0.30.2-dd32795`).

- **Bundled: HD2Runtime 0.30.0 release candidate `893d31f`** (2026-10-08; `wip/custom-projectile-rows`, VERSION 0.30.0, research
  `db778ed`). The Runtime built it with `scripts/build_release.py` into `HD2Runtime/build/release-candidate-0.30.0/` (2,026 Runtime tests,
  118 of 118 examples, 208 packaged-runtime scenarios); its release commit `d208a4b` changes only release notes and migration logs.
  `HD2Runtime-0.30.0-sdk.zip` SHA-256 `a6875afbd144f3874d72e5b28c4baad4adf5671e991b4af5e6a32f1d3075ca42`, content fingerprint
  `1343fa2b5c500aae96d506a656036587947309243fb922d3ee6d2fe20286766e`. Against the last dev pin (`4bae2c6`, `b3f42ec` plus the armor
  catalogues) only version strings changed (`hd2RuntimeVersion`, schema and stub headers), the asset summary's `unnamedPackages` (26 → 24)
  and the custom stratagem API's status (released). Export validation uses a `git archive` of `893d31f`, which leaves `research/` empty:
  `validate_examples.py` and the harness `validate-runtime.py` loads read nothing there.

- **Bundled since 2026-10-08: Runtime `b3f42ec`** (`wip/custom-projectile-rows`, research `d267a13`), built from the clean detached worktree
  `../HD2Runtime-devsdk-b3f42ec` with the same wrapper (2,004 Runtime tests; only `test_new_stratagem_examples` tolerated, the CRLF-compiled
  EAT23Example header). `HD2Runtime-0.30.0-dev-sdk.zip` SHA-256 `327c93b742cf6149e8eac650938fa28aeb33831573131ee3ff42c0305e580829`, content
  fingerprint `4b45d69c63732c7ecef617628721db4e0f935686e8ad7e0db6ea1b0d3ce2c5cf`. What changed for ModBuilder: weapon variants on every
  support weapon (32 deliverable hosts; shared hosts written with `allow_shared = true` and a warning; hosts without a round), the expendable
  `modify.direct_damage`, 24 model bases (slot names may be null), the read-only `weapon.sound` field, the asset catalogue's explosion and
  passive-effect packages, and the `explosion` event. New SDK files ModBuilder does not read yet (Helldiver fields, attachment modifiers,
  explosion and sound catalogues) are classified in the capability audit.

- **Bundled since 1.7.0: Runtime `356056c`** (`HD2Runtime/build/dev-0.30.0-356056c/HD2Runtime-0.30.0-dev-sdk.zip`, SHA-256
  `9633b121f0bc8445ca3688f816c3df104d72bf192c00045d8c55493dc9bbde7a`). It differs from `46e8900` only in custom stratagem files
  (`max_per_player`), which ModBuilder does not read from the SDK. The content fingerprint is therefore the same for both:
  `7889baa15e8cce5b274f591a2e36e0ff80f5f97163cb52b4d6b4b968856e8ebd`.
- First build: Runtime commit `46e8900` (`wip/custom-projectile-rows`), built from a clean detached worktree (`../HD2Runtime-0.30.0-dev`); the user's
  Runtime checkout was not touched.
- Command: `py -3 -B build/devsdk/build_dev_sdk.py`, a thin wrapper around the unchanged `scripts/build_release.py`.
  - It supplies the local research artifact `build/weapon-composition-raw.json`, which a clean checkout lacks.
  - It tolerates exactly the two tests of upstream fix 1; any other failure still refuses.
- Results: 1,850 Runtime tests (two tolerated, above), 117 examples `VALIDATED`, packaged-runtime validation passed (200 scenarios, no real writes,
  no game process access). The SDK's `CustomStratagemSchema.json` (SHA-256 `91a8d917…2ef1`) is byte-identical to the schema ModBuilder 1.7.0 bundles.
- Artifacts, copied to the Runtime checkout's ignored `build/dev-0.30.0-46e8900/` (with `sdk-release-report.json`, `SHA256SUMS.txt` and the wrapper):

  | File | SHA-256 |
  |---|---|
  | `HD2Runtime-0.30.0-dev-sdk.zip` | `34adfbf244195fd179673971f6b26b14f35bf4bd7cbe1de6e0736652329558bc` |
  | `HD2Runtime-0.30.0-dev-runtime.zip` | `b964f74df6e19c691c975caa6dc75a290da0f1398beb63a1ade90656ae862f3d` |
  | `HD2Runtime-ModTemplate-0.30.0-dev.zip` | `bc6c3a2b22921ef1482b57a607c5ca333a21e705381f5df45749dfc38863c957` |
  | `HD2Runtime-0.30.0-dev-example-projects.zip` | `0ac24849ed0ed4e02768b52820c342c0e83d7d8636e26c12c89342b870e90329` |

- **Settings → Developer · local HD2Runtime SDK** also accepts a 0.30.0-dev ZIP or folder.
