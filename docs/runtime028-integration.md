# HD2Runtime 0.28.0 integration (development)

Internal notes for the local compatibility pass against the **unreleased** HD2Runtime SDK at commit `e15d5bf` (the Runtime work
finished before the weapon-composition pass; its `VERSION` still reads 0.27.0). Nothing here is released. The ModBuilder version,
the SDK compatibility pin (`SdkCompatibility.NewestSupportedVersion`, 0.27.0) and the update manifests are unchanged.
The development fixture is `HD2RuntimeGUI.Tests/Fixtures/sdk-dev-e15d5bf.zip`.

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

**The live checkout currently has uncommitted weapon-composition work in `sdk/`:**

- new files: `OutputCompositionCapabilities.json`, `WeaponFeedCapabilities.json`, `WeaponFireRateCapabilities.json`, `WeaponPresentationCapabilities.json`;
- reshaped player and support catalogs (`fire_rate.modes`, `nativeSlots`).

This build refuses that SDK, fail-closed ("Malformed player-weapon capability catalog"). Until that pass is integrated, bind a clean snapshot:

```
git -C ..\HD2Runtime archive --format=tar -o %TEMP%\sdk.tar e15d5bf sdk
tar -x -f %TEMP%\sdk.tar -C %TEMP%\hd2runtime-sdk-e15d5bf
```

Then point `--sdk-path` or Settings at `%TEMP%\hd2runtime-sdk-e15d5bf\sdk`. This does not modify the Runtime checkout.

## SDK files consumed

| File | Contract | Use |
| --- | --- | --- |
| `EventCatalog.json` | `hd2runtime.events.v1` | Events with payloads (type, nil-ability, phase, source); the scripting API (functions, classes, methods); live/snapshot handles; the dispatch model; actions: explosions (named + weapons), projectiles, statuses (limits, host only, live proof, assets), spawn-entity status. Read when present, required from 0.28.0. |
| `AttackOutputCapabilities.json` | `hd2runtime.attack_outputs.v1` schema 2 | Active projectile source per attack (ACTIVE_DIRECT / INDIRECT / AMBIGUOUS / BLOCKED), ammunition sources, proven compositions, host model, 105 outputs (66 selectable). Cross-checked against the player catalog. Read when present, required from 0.28.0. |
| `stubs/mods/skyeshade/hd2runtime.lua` | LuaLS annotations | Autocomplete: classes, fields, methods, aliases, the `hd2` root class. Cached as `hd2runtime-stubs.lua` for every SDK that ships it; an unreadable stub only turns autocomplete off. |
| `BackpackAuthoringCapabilities.json` | (existing) | Now with `damageZones`, per-field `effect`, `rangeReason`. |
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
  - events, and blocked events;
  - explosions, projectiles and statuses;
  - keybind ids without a namespace;
  - unknown `enemy/v1/...` ids.

**Editor** (`wwwroot/lua-editor.js`)

- A plain textarea over a highlighted layer, with a gutter and error/warning markers.
- Ctrl+Space completion from the stub and catalog strings (event, explosion, projectile and status names). Ctrl+S saves.
- Tab, Enter-indent, completion and snippet insertion go through `insertText`, so Ctrl+Z undoes them.

**Side panel**

- Snippets (`LuaSnippets`): 10, built from real signatures. The Liberator baseline, field constant and keybind prefix come from the SDK and project.
- Reference (`EventReference`).
- Action pickers (`EventActionPicker`): live proof is shown only where the catalog records it.

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

- **SG-20 Halt:** fields stay per feed (feed_primary → `damage.primary.*`, feed_alternate → `damage.alternate.*`), grouped under each feed. The planner now emits the branch-qualified constant the target publishes (`hd2.fields.damage.primary_standard_damage`, `…alternate_*`) instead of the generic name. The Runtime's user report (2026-09-29) found the generic name fails on 0.27.0. The change applies only to projectile-object fields whose published id is branch-qualified; only the Halt has those, so no other weapon's output changes.
- **SH-20 Ballistic Shield:**
  - The shield plate is edited as "Shield zone · Armor" (`hd2.backpack(name):damage_zone('zone_0')`, `hd2.fields.zone.armor`).
  - `entity.armor` is read-only, badged **Not active**, and names the active field.
  - Any field's `effect` is shown generically: how it takes effect, whether that is proven, spawn-only, and its damage rule.
- **Numeric bounds:** published ranges are enforced as before, and their reason is shown in ⓘ (for example the 10-bit deposit limit, 1023, on the Cremator, GL-28 and Maxigun backpacks).
- **Resupply:**
  - listed under Stratagems → Mission, with cooldown and uses (unlimited or 1–100);
  - its shared drop pod has four authorable slots and spawn count 1–4;
  - Grenade Box shows **Live-verified pair**, from the slot's `liveVerifiedPickups`;
  - the rack is shared with `AmmoRack_PresidentReward`.
- **Sentries and mines:** yaw/pitch speed and limits, targeting range, minefield salvos and mines per salvo are all generic fields, with no field list in ModBuilder.
- **Support, player and throwables:** audited against 0.27.0.
  - New support and player fields are authored, including `stationary_while_firing`, status strengths and `projectile.lifetime` / `penetration_slowdown`.
  - Status *type* references stay read-only with a reason.
  - Throwables publish no new fields.
- **Bug fixed during the pass:** the attack-output operations were first inserted between `if (sdk.Plans != null)` and its `else`, which made plan projects emit every operation twice. A test now checks that operation ids are unique.
- **Export page:** for 0.28 SDKs it explains apply timing. Weapons built before a change applied keep their copy, and the Runtime logs `registered operations settled`.

## Hardcoded assumptions that remain

- The opt-in titles and default reasons (`OptInWarnings`), and the cross-class reason text in `AttackOutputSelector`.
- The structural knowledge in the analyzer and completion of which API calls take names: `hd2.events.on/once`, `explosions.spawn/prepare/of`, `projectiles.spawn/prepare`, `status.apply`, `input.bind`.
- Two sentences in the reference: the common payload fields, and the snapshot/live handle rule. Both are paraphrased from the catalog's model text.
- Snippet choices: the Hellbomb alias, `soldier_mg`, R-36 Eruptor, AR-23 Liberator and `fire`. Tests check that the SDK publishes each.
- The planner's branch list for grouping shared rows (`primary|alternate|feed_*|impact|expiry`).
- The classic-versus-output rule: same class and a player-owned donor stay a classic swap.
- The Mission category's family key (`mission`) and colour.

## Tests and smoke

- 19 new tests in `Runtime028IntegrationTests` cover:
  - custom Lua persistence, outside-edit conflict and reload, syntax and diagnostics;
  - the event catalog and stub, and snippets end to end;
  - Liberator donors, active-source codegen and opt-in persistence, same-class classic swaps, discarding object edits;
  - validation of the saved attack outputs;
  - Resupply, the SH-20 zone, 1023 bounds, Halt branch constants, unique operation ids;
  - older projects, and the developer SDK setting.
- Three existing tests were updated for the new nav item, the Mission category and the cached stub. The suite has 755 tests.
- Desktop smoke: `tools/scripting-smoke.mjs`, run with the app on the e15d5bf snapshot and CDP port 9241. Screenshots are in `docs/screenshots/`: `custom-lua-*`, `attack-output-liberator`, `backpack-sh20-shield-zone`, `resupply-drop-pod`, `settings-local-sdk`.

## Next: the weapon-composition pass

These are the capabilities of the Runtime pass in progress, already visible as uncommitted files in the checkout:

- **Multi-RPM fire modes** (`WeaponFireRateCapabilities.json`, `fire_rate.modes`): a per-mode rate editor on the fire-mode panel.
- **Alternate ammunition / feed modes** (`WeaponFeedCapabilities.json`): the selectable ammunition items per weapon (the Liberator's ten alternates, the Halt's rounds feed). Donors then follow the equipped feed.
- **Cross-family beam / output composition** (`OutputCompositionCapabilities.json`): beam, arc and spray outputs, and the component changes they need. Today those are BLOCKED with a reason.
- **Presentation / armory labels** (`WeaponPresentationCapabilities.json`): names and labels shown in the armory, edited next to the weapon.
