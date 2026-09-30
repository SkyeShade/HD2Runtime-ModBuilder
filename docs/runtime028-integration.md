# HD2Runtime 0.28.0 integration (ModBuilder 1.4.0)

Internal notes on how ModBuilder 1.4.0 consumes the frozen **HD2Runtime 0.28.0** release candidate: release commit `39aabe3`, SDK generated
at `e304f26`, API 1, schema 1, SDK archive SHA-256 `42b9cac4e0d3328a638b766d70bf04e03e064a357f897bc1f89e0066f188851e`.

- **Pin.** `SdkPin` records the version, commits, archive SHA-256 and a content fingerprint of every SDK file ModBuilder reads. The bundled
  SDK (`HD2RuntimeGUI.Core/Metadata/Bundled/`) is byte-identical to the release asset; `SdkPinTests` checks it against the fixture
  `HD2RuntimeGUI.Tests/Fixtures/sdk-0.28.0.zip` (the release-candidate asset itself). `SdkCompatibility.NewestSupportedVersion` is 0.28.0.
- **Upgrade.** A bundled SDK newer than the cached current one becomes current on start (`SdkCache.AdoptNewerBundled`); projects stay on
  their own SDK until the user rebinds them.
- **Local SDKs.** A local SDK with the pinned version but other contents is reported as a different build (`SdkMetadata.IsPinnedBuild`),
  never taken for the pin, and never cached.
- **Exports** declare `requires.hd2runtime.min_version` = the SDK they were built on (0.28.0).
- **Fail closed.** Every reader validates the frozen catalogs strictly; value types or targets this build could not author would stay
  visible and read-only (`AuthoredTypes`), and `CapabilityAudit` would report them. At 1.4.0 it reports none missing.

## Local SDK selection

A local SDK is bound in this order:

1. `--sdk-path <path>`
2. `HD2RUNTIME_SDK_PATH`
3. **Settings → Developer · local HD2Runtime SDK**, which is new. It stores `developer.json` in the data root and applies from the next start.

About the Settings entry:

- You can pick an HD2Runtime checkout; it resolves to its `sdk/` folder. An SDK zip also works.
- A path that no longer exists is ignored, and the published SDKs are used.
- Settings shows where the local SDK came from and the Runtime commit, read from the checkout's `.git`.
- **Restart ModBuilder now** relaunches the app with the same arguments.

The local SDK is still validated in memory and never cached. Exports require its version, and nothing from the Runtime is packaged.

## SDK files consumed

| File | Contract | Use |
| --- | --- | --- |
| `EventCatalog.json` | `hd2runtime.events.v1` | Frozen 0.28.0: 20 events (19 available, including the new `player_hit` and `player_damage_dealt`; `entity_damage_pre` blocked with its reason) with payloads (type, nil-ability, phase, source); the scripting API (6 functions, 28 classes including `HD2StatSource` with `hits` and `damage`); the `HD2EntityHandle` / `HD2PlayerHandle` live/snapshot handles; the dispatch model; actions: explosions (2 named + 13 weapons), 66 projectiles, 11 statuses (limits, host only, live proof, options, assets), spawn-entity blocked with its reason. Required from 0.28.0. |
| `AttackOutputCapabilities.json` | `hd2runtime.attack_outputs.v1` schema 2 | Active projectile source per attack (ACTIVE_DIRECT / INDIRECT / AMBIGUOUS / BLOCKED), ammunition sources, proven compositions, host model, 105 outputs (66 selectable). Cross-checked against the player catalog. Read when present, required from 0.28.0. |
| `stubs/mods/skyeshade/hd2runtime.lua` | LuaLS annotations | Autocomplete and the diagnostics reference: all 195 classes of the frozen stub with their fields and functions (inherited members included, so an `HD2Event_*` payload has the common `event`, `time`, `frame`, `mission`, `cause`), the sub-tables declared on `hd2` (`hd2.diagnostics` through `---@type`, `hd2.compatibility.*`), every string alias (weapon, attack output, status, event names ...), and each function's first parameter type. Cached as `hd2runtime-stubs.lua` for every SDK that ships it; an unreadable stub only turns autocomplete off. |
| `LiveEvidenceCatalog.json` | `hd2runtime.live_evidence.v1` | For scripting: the `events` / `event_actions` families (`event_action_heal`, `event_damage_source_attribution`, `event_weapon_in_hand`, `event_player_died_position`, `event_action_explosion_named`, `event_action_projectile`, `event_action_status`), shown next to the event, action or handle method they name. |
| `PlayerWeaponAuthoringCapabilities.json` | (existing) | 0.28.0: rate-of-fire slots, weapon functions, programmable-ammunition projectiles, armory traits and penetration labels, status references, heat levels, and `subweapons` (underbarrels). |
| `SupportWeaponAuthoringCapabilities.json` | (existing) | 0.28.0: the same composition types, support projectile hosts (`attack.projectile`) and weapon_selector operation groups spanning two components. |
| `VehicleWeaponCapabilities.json` | (existing) | 0.28.0: mounted projectile hosts, Guard Dog drone carriers, status slots, shared beam and arc rows, live-proven values. |
| `StatusEffectCatalog.json` | `hd2runtime.status_effects.v1` | Status names and attachability for every status-reference editor. |
| `WeaponPresentationCapabilities.json` | `hd2runtime.weapon.presentation.v1` | Trait and penetration labels; the presentation-only and refresh notes. |
| `WeaponFeedCapabilities.json` | `hd2runtime.weapon.feeds.v1` | Native, rounds-magazine and programmable feeds per weapon (SG-20 Halt, AC-8, GR-8, RL-77, addable weapons). |
| `BackpackAuthoringCapabilities.json` | (existing) | Now with `damageZones`, per-field `effect`, `rangeReason`, and linked entities (Guard Dog drones, the SH-51 energy shield) with their zones. |
| `PodPayloadCapabilities.json` | (existing) | Now with per-slot `liveVerifiedPickups` (Resupply: Grenade Box). |
| `StratagemAuthoringCapabilities.json` | (existing) | Resupply (family `mission`), sentry turret/targeting and minefield salvo fields. |

## Custom Lua (event scripting)

**Model and storage**

- Project format 11 (`CustomLuaSettings`): `Enabled` and `Source`.
- The project JSON keeps the saved text exactly: CRLF, tabs and trailing space are preserved.
- `src/addon.lua` in the project folder is the working copy.
- ModBuilder writes the working copy only on Save, or to recreate a missing file from the saved text. It reads it only on Reload.

**Outside edits**

- While the page is open, the working copy is compared with the saved text every 2 s.
- A difference shows a banner: reload from disk, or overwrite the file with ModBuilder's text.
- Save refuses to overwrite an outside change unless the overwrite is explicit (`CustomLuaConflictException`).
- Export is refused until the difference is resolved.

**External editor**

- **Open in editor** uses the `.lua` shell association.
- **Open folder** opens the folder with the file selected.
- **Copy path**.

**Checks** (nothing is executed)

- `LuaParser`: Lua 5.1 plus goto/labels, verified on every Runtime example addon. A syntax error blocks the build and export and reports its line.
- `LuaScriptAnalyzer` rejects the addon header and `---@meta`, which the Runtime builder refuses in `addon.lua`.
- It warns on literal names the catalog does not publish:
  - events, and blocked events (with Runtime's reason);
  - explosions, projectiles and statuses;
  - keybind ids without a namespace;
  - unknown `enemy/v1/...` ids.
- It warns when a script turns telemetry on (`hd2.diagnostics.telemetry({enabled = true, ...})`): that enables it for every player of the mod.
- The generated file itself checks clean: the operations wrapped in `local function add(build) … pcall(build) … end`, then the custom Lua
  (tested on a project with generated operations and every snippet inserted).

**Editor** (`wwwroot/lua-editor.js`)

- A plain textarea over a highlighted layer, with a gutter and error/warning markers. Ctrl+S saves.
- Ctrl+Space completion from the frozen 0.28.0 stub:
  - members after `.` / `:` along a call chain, including `hd2.diagnostics.`, `hd2.compatibility.`, `weapon:programmable_ammo():`,
    `weapon:underbarrel():`, `weapon:feed(…):`, `backpack:drone():` / `:energy_shield():`, `attack:projectile_source():`,
    `hd2.attack_output(…):`;
  - the first string argument of any stub function or method, from its parameter's alias or literals (`hd2.weapon('`,
    `hd2.attack_output('` with every output id and owner name, `:feed('primary'|'alternate'|'programmable')`, `:attack('`);
  - typed locals: event handler parameters (the nearest `hd2.events.on('…', function(event)` wins, with the event's own and the common
    fields), `local x = <call chain>` and `for _, x in ipairs(<array>)` (`event.sources` → `HD2StatSource`);
  - catalog strings for event names (available only), explosions, projectiles and statuses.
- Tab, Enter-indent, completion and snippet insertion go through `insertText`, so Ctrl+Z undoes them.
- `hd2LuaEditor.completions(text, caret, data)` returns what the popup would offer, for smoke checks.

**Side panel**

- Snippets (`LuaSnippets`): 13, built from real signatures. The Liberator baseline, field constant, K-2 Throwing Knife name and keybind prefix
  come from the SDK and project. New for 0.28.0: `player-hit-accuracy` (shots and projectile hits per weapon from `player_fired` /
  `player_hit`), `damage-dealt-heal` (heal 25 on K-2 knife damage from `player_damage_dealt`, the live-proven pair) and
  `write-conflicts-report` (logs `hd2.diagnostics.write_conflicts()` at mission end).
- Reference (`EventReference`): every catalog event, available or blocked (with its reason), each payload field with its type and doc, and
  the fields of plain records inside a payload (`sources[].hits` for `player_hit`, `sources[].damage` for `player_damage_dealt`,
  `previous.name` for `weapon_changed`); live evidence per event and handle method; an **Insert handler** button that inserts an
  `hd2.events.on` subscription listing the payload fields (and a `for _, source in ipairs(event.sources)` loop for stat events); every API
  class, including the field-only ones (`HD2StatSource`, options, `HD2EquippedWeapon`).
- Action pickers (`EventActionPicker`): an Events group (every event, blocked ones with their reason), explosions, projectiles and statuses
  (limits, published options, per-item live proof and the family's scope / not-proven list), healing (`hd2.actions.heal`, `player:heal`,
  live-proven heal(25)), the weapon in hand (`player:equipped_weapon()`, live-proven) and spawn entity (blocked, with its reason). Live proof is
  shown only where the catalog or the live-evidence catalog records it; evidence is informational, never a checkbox.
- Diagnostics (`DiagnosticsReference`, from `RuntimeDiagnostics` and the stub): see below.

**Diagnostics reference** (the SDK's `docs/diagnostics.md`, calls from the stub)

- Write conflicts (always on): how an ensure detects another writer, when Runtime logs `possible write conflict` (3 external changes in the
  operation's window), what is not counted, how to read a report (remove one of the two mods or edits), and that `gui-` operation ids are
  ModBuilder's; `hd2.diagnostics.write_conflicts()` with its stub doc and an insertable report snippet.
- Telemetry (off by default): **ModBuilder never turns it on**. No generated operation, snippet, handler or picker insert contains it, and
  there is no insert button: the documented opt-in `hd2.diagnostics.telemetry({enabled=true, report_seconds=60})` is shown as text for a
  modder to add by hand, with what it logs and the report procedure (a small mod, one mission, `HD2Runtime.log`). A test scans the sources
  for any other opt-in.
- Logs: `mod:log`, callback failures and `max_failures`, and the `[ModBuilder] operation skipped:` line of a generated operation that failed
  to build.
- Status queries from the stub: `hd2.events.status()`, `hd2.actions.status()`, `hd2.metrics()`, `hd2.compatibility.status()` /
  `incompatible()`, `sub:describe()`.
- Sections whose calls the stub does not publish are left out; an SDK without `hd2.diagnostics` (0.27.0) says so.

**Generated code**

- One `require`, then the generated operations collected in `generated`.
- Then `-- Custom Lua: src/addon.lua`, the text inside `local function addon(...) … end`, `addon()` and `return generated`.
- The text is copied unchanged apart from LF line endings.

## Attack outputs through the active projectile source

- `AttackOutputSelector` replaces the projectile selector when the SDK publishes attack outputs. It shows:
  - what the attack fires;
  - the active source (status, mechanism, member, reason, ammunition item and sharing);
  - searchable donors with class, proof, asset loading and the opt-ins each carries;
  - refused donors, disabled with Runtime's reason.
- Cross-class donors are behind **Show other classes**, except those proven on this host (Liberator × EAT-700 / GL-52).
- A same-class, player-owned donor that the projectile graph also offers stays a classic reference swap, so projectile and explosion edits can follow it.
- Everything else becomes an `AttackOutputChange` (format 11). Changes records the id, weapon, role, mechanism, output, opt-ins and evidence.
- Generated code:
  - component host: `hd2.weapon(W):attack(role)` + `hd2.fields.attack.projectile`;
  - ammunition host (INDIRECT: AR-23 Liberator, JAR-5, R-63, SG-225, P-2, P-19): `hd2.weapon(W):ammunition()` + `hd2.fields.ammunition.projectile`, expecting `…:ammunition():projectile()`;
  - value: `hd2.attack_output('<output id>')`;
  - the published opt-ins are always written, with warnings and no checkboxes.
- Projectile and explosion edits cannot follow an output. Choosing one while such edits exist asks, inline, whether to discard them.
- A dormant reference is never offered: the SG-20 Halt's rounds ammunition is read-only with Runtime's reason.
- Changes lists outputs (toggle, reset, "accept current SDK"). The weapon header counts them, and counts an ammunition write as shared.

## Unified projectile hosts and the projectile builder (frozen 0.28.0 SDK)

- **Hosts.** Support weapons, vehicle mounts and the Guard Dog drone gun are projectile hosts by the same active-source rule as player
  weapons (`ProjectileHosts`). Their opt-ins come from their own `attack.projectile` field: `allow_unverified_effect` (dropped for the
  field's exact live-proven donors), `allow_shared` for one weapon entity in several mounts (the two FRV guns: one swap per entity).
  Cross-class donors add the output's cross-class opt-ins unless `provenCompositions` names the pair.
- **One donor pool.** Every selectable projectile output (player, support, mounted, stratagem). Rows catalogued only for
  `function_ammo.projectile` are never offered; a donor owned by neither a player nor a support weapon needs a catalogued package (the
  Guard Dog gun: `ASSET_UNAVAILABLE`). Beam, arc, spray and melee outputs and rows another selector owns are listed with their reason.
- **Saved as** `AttackOutputChange` with `hostKind` (`support_weapon` / `vehicle_weapon`; absent for player weapons, so their JSON, ids
  and evidence are unchanged). Lua:
  - support: `target=hd2.support_weapon(S):attack(role)`, `expect=<target>:projectile()`;
  - mounted: `target=<mount>:attack(role):projectile_source().target`, `expect=<mount>:attack(role)`, where `<mount>` is
    `hd2.vehicle(V):weapon(M)` or `hd2.backpack(B):drone():weapon()`.
- **Row writes** (`OutputRowChange`, `outputRowChanges`): the three builder slots and the mode label / icon of a row,
  `hd2.attack_output(row)`. One transaction per row for its slots and one for its label and icon (so `auto` resolves with the label).
  Opt-ins: the slot's or presentation's own; exact live-proven values drop `allow_unverified_effect`, never `allow_shared`; row writes never
  carry `allow_unverified_reference` (Runtime refuses it there). A player terminal-explosion edit on the same row member is a conflict.
- **UI.** `ProjectileHostSwap(s)` on support weapon pages, vehicle mounts and Guard Dog backpacks; the player selector shares
  `ProjectileDonorList`; `ProjectileRowLinks` opens the host's own row or, after a swap, the donor's row (`ProjectileRowEditor`, with the
  owner's flight fields in their existing editors and `ModePresentationEditor`); **Projectile builder** page lists every host, row and
  non-projectile output. Smoke: `tools/projectile-builder-smoke.mjs`.

## Other items

- **SG-20 Halt:** fields stay per feed (feed_primary → `damage.primary.*`, feed_alternate → `damage.alternate.*`), grouped under each feed. The planner emits the branch-qualified constant the target publishes (`hd2.fields.damage.primary_standard_damage`, `…alternate_*`). Every generated operation is also built inside `pcall` (`LuaGenerator.Isolated`), so an operation whose request cannot be built is logged (`[ModBuilder] operation skipped: …`) and the others still register (ModBuilder#2; `HaltRegressionTests`, and the Runtime issue variants validate with isolation probes).
- **SH-20 Ballistic Shield:** the shield plate is edited as "Shield zone · Armor"; `entity.armor` is read-only, badged **Not active**, and names the active field.
- **Numeric bounds:** published ranges are enforced where the edit is made, with their reason (for example the 1023 backpack-ammo limit, sentry and minefield ranges).
- **Resupply:** Stratagems → Mission, cooldown and uses; its shared drop pod has four slots; a pickup live-proven in that exact slot (Grenade Box) carries no `allow_unverified_reference`.
- **Status references** (player, support and mounted weapons, heat levels) are chosen by name from the published statuses; only attachable statuses, the last used slot can be cleared.
- **Export page:** for 0.28 SDKs it explains apply timing (`registered operations settled`).

## Hardcoded assumptions that remain

- The opt-in titles and default reasons (`OptInWarnings`), and the cross-class reason text in `AttackOutputSelector`.
- The structural knowledge in the analyzer and completion of which API calls take catalog names: `hd2.events.on/once`, `explosions.spawn/prepare/of`, `projectiles.spawn/prepare`, `status.apply`, `input.bind`, and the telemetry opt-in `hd2.diagnostics.telemetry({enabled = true})`. Every other call completes its first string argument from the stub's declared parameter type.
- Two sentences in the reference: the common payload fields, and the snapshot/live handle rule. Both are paraphrased from the catalog's model text.
- Snippet choices: the Hellbomb alias, `soldier_mg`, R-36 Eruptor, AR-23 Liberator, `fire`, the K-2 Throwing Knife and heal(25). Tests check that the SDK publishes each.
- The diagnostics reference: its four sections, the status calls it lists, and the example log lines, quoted from the SDK's `docs/diagnostics.md` and `docs/events.md` (the SDK docs are not part of the cached SDK). Its explanations paraphrase those docs as UI text.
- Live evidence is matched to scripting subjects by each family's `fields` (an event name, `hd2.actions.heal`, `player:equipped_weapon`). A handle method's subject is derived from the handle name (`HD2PlayerHandle` → `player:`).
- The planner's branch list for grouping shared rows (`primary|alternate|feed_*|impact|expiry`).
- The classic-versus-output rule: same class and a player-owned donor stay a classic swap.
- The Mission category's family key (`mission`) and colour.

## Tests and smoke

- Unit tests: `SdkPinTests`, `HaltRegressionTests`, `OldProjectCompatibilityTests` (projects saved by ModBuilder 1.3.1 itself), `CapabilityAuditTests`
  (unexpected missing = 0; writes `capability-audit.json` and `COVERAGE.md`), `ExportFixtureTests`, `WeaponCompositionTests`, `ProjectileHostTests`,
  `ProjectileBuilderTests`, `EquipmentTests`, `ScriptingReferenceTests`, `Runtime028IntegrationTests`.
- Export validation: `tools/validate-exports.py` runs every export fixture through HD2Runtime 0.28.0's own validator from the extracted release
  tree (read-only, `git archive 39aabe3`), in snapshot mode, with an isolation probe per export.
- Desktop smokes against the packaged app (`scripts/run-rc-smokes.ps1`): `scripting`, `language`, `runtime028`, `projectile-builder`, `export`,
  `old-project`. Screenshots go to the release candidate's `smokes/screenshots/`.
- Event scripting against the frozen 0.28.0 SDK: `ScriptingReferenceTests` (10 tests) checks that every catalog event and action reaches the
  reference and pickers with its availability, blocked reason, options and live evidence; the `player_hit` / `player_damage_dealt` payloads,
  source records and snippets; that every snippet, handler and picker insert parses and checks clean; that completion covers every class,
  field and function of the stub; the diagnostics reference built from the stub; that the generated operation wrapper checks clean; and that
  no code path turns telemetry on.

## Weapon composition, projectile hosts and equipment (frozen 0.28.0)

- **Rate-of-fire slots, weapon functions and programmable ammunition** (`WeaponFunctionsPanel`): X / Y / Z rates, the default slot and selector
  order, input bindings resolved inline, the function projectile from the one donor pool (spare twins and function-ammo-only rows included),
  written with its binding in one `weapon_selector` transaction. Mode labels and icons for the base and alternate rows are edited in place
  (`ModePresentationEditor`).
- **Armory presentation** (`WeaponPresentationPanel`): traits and the penetration label, presentation only.
- **Underbarrels** (`SubweaponSection`): a nested section on the parent weapon, written to `hd2.weapon(parent):underbarrel()`.
- **Unified projectile hosts** (`ProjectileHostSwaps`): support weapons, vehicle mounts and the Guard Dog gun, with read-only hosts and refused
  donors shown with Runtime's reason.
- **Projectile builder** (Projectiles page, `ProjectileRowEditor`): row slots (direct damage, impact and expiry explosion), host row versus donor
  row, the row's flight values, and who fires it.
- **Equipment:** backpack-linked entities (`:drone()`, `:energy_shield()` and their zones), Guard Dog drone weapons through the backpack, mounted
  status slots, Resupply and pod payloads.
- **Beam, arc, spray and melee outputs** are listed with their blocked reason; no projectile editing is invented for them.
