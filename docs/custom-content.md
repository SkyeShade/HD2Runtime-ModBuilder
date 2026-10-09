# Custom content (experimental): custom stratagems and custom projectiles

ModBuilder 1.7 can create **new** Runtime-owned objects as well as change existing game objects. The target is HD2Runtime 0.30.0. It
releases the custom stratagem API but calls it experimental with several players, and custom projectiles use internal Runtime modules
with no public API, which may change. So the design rule of this area is: **the project stores what the author means; one adapter turns it into whatever the
target Runtime expects** — and for custom stratagems, the adapter goes through the Runtime's own published project format, so what
ModBuilder builds is what the Runtime's own tools accept.

```
ModBuilder semantic model (project.hd2mod.json, customContent, version 1)
        │   CustomContentValidator: ModBuilder's rules (ids, images, models, references, the Lua escape hatch)
        ▼
ICustomContentTarget  ──  Runtime030Target (the only code that knows Runtime field names, families, donors, limits, refusals and Lua)
        │   RuntimeProjectForm: the stratagem as HD2Runtime's custom_stratagems.json holds it (format hd2runtime-custom-stratagems/1)
        │   RuntimeProjectValidator: port of the SDK's validator (sdk/tools/custom_stratagem_project.py validate), same checks, same words
        │   RuntimeRegistrationLua: port of the SDK's compiler (compile_lua), the register call's fields byte for byte
        │   reads Runtime's own CustomStratagemSchema.json (bundled snapshot, or HD2RUNTIME_CUSTOM_SCHEMA)
        ▼
src/addon.lua (register calls + callbacks, carrier swaps, projectile bindings) + compiled icons in the mod's archive
                                                                             + derived models in a patch of their base's archive
```

## What is separate from what

| Concept | Where | Styling | Storage |
|---|---|---|---|
| Editing an existing game object (a weapon field, a projectile swap, a stratagem cooldown) | Player Weapons, Stratagems, Projectile Builder, … | ordinary (brand yellow, category colours) | `weaponChanges`, `attackOutputChanges`, `stratagemChanges`, … |
| Creating a new Runtime-owned object | **Custom Stratagems**, **Custom Projectiles** (sidebar group *Custom Content · Experimental*) brand yellow + small *Experimental* badge | `customContent` |
| A reference to a custom object from an existing one (a weapon that fires a custom projectile) | every projectile selector's violet *Custom — Experimental* group | violet chip | `customContent.assignments`, a typed `{"kind": "custom_projectile", "id": …}` |

Custom objects live in their own namespaces: custom stratagem ids and custom projectile ids are separate sets, and neither is ever mixed
into a vanilla catalogue. A custom projectile reference is never a vanilla projectile id, so validation, packaging, migrations, a future
multiplayer registry and dependency analysis can always tell them apart.

## The custom stratagem builder

**Custom Stratagems** lists the project's custom stratagems as cards (icon in its family's category colour, name, id, family, cooldown,
uses, traits, call-in code, HD2Runtime's play status for the family, readiness). From there:

- **A mod holds up to 16 custom stratagems.** The list is a card grid ending in an *Add another* card. While one is open, a strip above
  its editor lists every custom stratagem of the mod (switch to another, back to all, *+ Add another*), each with its readiness.
  **Overview** has a *Custom stratagems* panel listing them with their call-in codes and readiness, opening each one, with *New Custom
  Stratagem*.
- **New** asks for a name and a delivery family. The family cards say what each family delivers and what HD2Runtime has shown of it in
  play (`familyEvidence`: *live-tested*, *partly tested*, *not live-tested*, and its multiplayer status), and offer that family's
  examples. A new stratagem starts with the family's default carrier group, a unique call-in code (never a native one) and a placeholder
  icon.
- **Templates** (`CustomContent/Templates`, read through the project form) are seven examples. Five are ModBuilder's own simple
  examples for the main families, with nothing modified: Machine Gun Drop (support), Quick MG Sentry, Eagle Heavy Bomb, Pelican Flyover and
  Hellbomb Silo. Two are HD2Runtime's own examples, bundled byte for byte from `sdk/fixtures/custom_stratagems`: EAT-40 Expendable Gas
  (`EAT17GExample`) and Orbital Gas Barrage (`GasBarrageExample`). Every template is valid under the Runtime SDK's validator, alone and
  together, and compiles to exactly the registration the SDK compiles for it (tested), with a placeholder icon. The gallery card says
  which kind each one is.
- **Import Runtime project…** reads an HD2Runtime `custom_stratagems.json` (an SDK project), with its `images/<id>.png` when they are valid
  icons. Every stratagem gets an id and code of its own in this project; anything ModBuilder does not model is named in the import notes.
- **Copy as custom_stratagems.json** gives the project's custom stratagems in the Runtime's own format (for the SDK's `hd2.py
  custom-stratagem validate`), and cards can be reordered (the registration order).

The editor is a studio: a section bar that jumps to each section and marks the ones with problems, the sections themselves, and a
sticky **live preview** of how the stratagem presents (icon as the game draws it, cased and HUD names, CUSTOM STRATAGEM and its traits,
code, cooldown, uses, family, carrier) with its build readiness, every error one click from where it is fixed.

| Section | What |
|---|---|
| Identity | HUD name, panel name, description, **traits** (up to 4, after the automatic CUSTOM STRATAGEM), internal id (*Change ID…*) |
| Icon | 256 × 256 PNG or placeholder; preview as the game masks it; automatic or raw preparation |
| Call-in | arrow code (refuses other custom codes and native ones; tells when a mission-only native code is related), cooldown, **uses per mission** (Unlimited or Limited 1 to 100; below) |
| Carrier | **carrier groups** that can carry this payload (the family's default marked) with pod capacity for pod groups, or a custom beacon / family policy; preferred and allowed families, exclusions, extra call-in packages |
| Delivery | the family cards, Runtime's notes and play status, then only that family's editors (below) and what Runtime refuses, with its reason |
| Lua | the escape hatch (below) |
| Checks | every check, grouped: ✓ ℹ ⚗ ⚠ ✕ |
| Build | the register call as the build writes it, and the stratagem in the Runtime's project form; both copyable |

### The families

| Family | Runtime | Editors |
|---|---|---|
| Orbital barrage | `orbital` | Runtime barrage (shell, pattern, salvos, shells, intervals, scatter, impact; at most 64 shells) or the vanilla barrage with its impacts replaced |
| Eagle strike | `eagle` | the Eagle, its uses per rearm, rearm time, impact (only Eagles whose impact can be replaced); no cooldown or uses |
| Pelican gunship | `pelican` | hover time; approach; orbit (radius, altitude, duration, period, spiral-in, shorter than the duration); chin gun: round (`native` leaves the vanilla autocannon), Gatling behaviour, rate multiplier or rpm, casings, spread, no recoil, unlimited ammo, face target, aim height, firing sound (171 catalogued), impact explosion (slow donors need a low rate) |
| Support pod | `support` | the support weapon or backpack, its rack count, the delivered weapon's round, rate, spread, magazine, zero recoil, impact and converted rounds |
| Expendable weapon | `expendable` | the reviewed expendable donor, another reviewed round or an impact, clone level, the weapon fields, its **presentation** (name and marker/prompt icon: the stratagem's, the donor's or its own PNG) and its **pod** (the carrier's vanilla rack, or custom contents with the clone) |
| Custom pod | `pod` | up to 8 items (support weapons, backpacks, primaries), each with a count and, for weapons, its own reconfiguration; primaries are not live-tested in a pod: the build writes their opt-in and the editor warns; solo host only |
| Weapon variant | `weapon` | the reviewed weapon (M-1000 Maxigun), another round of its compatibility class, its presentation and its **model** |
| Sentry | `sentry` | the sentry, its gun's round, rate, spread, magazine, zero recoil and **firing sound** |
| Missile silo | `silo` | the silo, its blast (objective blasts flagged: about 300 MB of packages) and a fallback blast |
| Scripted (Lua) | `script` | nothing (the callbacks deliver) or a vanilla support pod `on_delivered` sees |

Fields are generated from `CustomFields` (each keyed by its path in the Runtime's project form, `payload.gun.rpm`) and described by the
target from the schema at that path: unit, range, default, choices (each catalogue, labelled: pod items by name, a variant's rounds by
their weapon, slow explosion donors with their rate limit, objective blasts) and status. Fields a mode or donor does not use are named in
one line with the reason; what Runtime refuses stays a disabled row with its reason (shared definitions: explosion sizes, an Eagle's
pattern, projectile and cooldown, a sentry's impact, backpack changes, another rack count, pod limits, an expendable's projectile).

### Custom assets

- **Icons.** Every custom stratagem has one (a generated placeholder until a PNG is imported). An expendable or variant can also have its
  own marker and prompt icon (`<id>_weapon`). Both compile like any icon (below); the build record names them all.
- **Models.** A weapon variant can get its own model: the base weapon's unit with its own colours per material zone. The palette is the
  debug palette (each of the 8 zones its own bright colour, to learn which zone is which part) or colours for all zones and per zone. The
  project stores only the palette (`customContent.models`); the export derives the unit, material and colour table from the user's
  installed game (`HD2_GAME_DATA`, else the Steam install ModBuilder finds), refusing when the installed base is not the reviewed one,
  and packs them into a patch of the base's own package archive (`mod/b2c627b3ba7e0c0a.patch_0` for the Maxigun), with the build record
  `<resource>/hd2runtime_models` in the mod's archive. *Check my installed game* runs the same derivation on demand. `CustomModelCompiler`
  is a C# port of the SDK's `hd2_model.py`, byte-identical on the installed game (`CustomModelCompilerTests`, oracle
  `tools/custom-content/make-model-oracle.py`). *Use it* or *Check only* (`model_use = 'check'`: Runtime only logs whether the model
  would load).
- **Sounds** are catalogue sounds (`pelicanSounds`): a sentry's or a Pelican gun's firing sound. The Runtime loads the stratagem that
  provides its bank.
- **Text** is the stratagem's own (name, cased name, description, traits, a delivered weapon's name): the Runtime registers it as its own
  text in every language. Per-language text is not public in the Runtime yet.

### The Lua escape hatch

The Lua section holds, per stratagem, everything data cannot say, in the order the Runtime runs it:

| Tab | Becomes |
|---|---|
| `setup` | Lua run once before the registration, in the same scope (local state and helpers the callbacks share) |
| `on_called`, `on_beacon_created`, `on_beacon_landed`, `on_activate`, `on_delivered` | `name=function(ctx) … end,` in the register call |
| `fields` | extra registration fields written as they are (for Runtime fields ModBuilder does not model, such as `selection = 'token',`) |

The editor is ModBuilder's Lua editor: completion from the **target Runtime's own stub** (bundled `hd2runtime-stubs-0.30.3.lua`;
`ctx` is typed as `HD2CustomStratagemCall`, so `ctx:` offers `barrage`, `log`, `associate`, `spawned`), syntax markers as you type, and
snippets built from real signatures (log the call, a barrage of the stratagem's own shell, an explosion at the beacon, a Pelican
gunship, converting delivered rounds, a call counter, token selection). A change is saved shortly after typing stops. `on_delivered` is
offered only when something is delivered (not a Runtime barrage, a Pelican, or a scripted call without a pod). A scripted stratagem needs
a callback or a vanilla pod. Behaviour in callbacks is flagged in the multiplayer checks: other players' Runtimes cannot derive it.

## Cloning a vanilla projectile

**From a template → Clone a vanilla projectile…** offers every projectile output the bound SDK maps (`BuilderWorkspace.CustomProjectileCloneSources`):
player, support and mounted weapons, then the rest, each by name. Spare twins are borrowed rows rather than vanilla projectiles, so they
are not listed. A clone (`CloneVanillaProjectileAsync`) is a custom projectile whose base is that output, with no components. Runtime's
`custom_projectiles.define` treats `components` as optional and defines a base-only spec as a byte-identical copy of the base row.
Runtime's own test `test_an_unchanged_clone_and_permitted_changes_are_valid` covers this. A ModBuilder-style
`{id, base, components = {}}` also defines a valid row equal to the base in Runtime's offline fixture world. The generated definition is
`{ id = …, base = … }`, because an empty components table is omitted. Projectiles Runtime cannot use as a base
(`ICustomContentTarget.ProjectileOutputIssue`: an unknown asset package, in SDK 0.28.1 19 of 107) stay listed, disabled, with that
reason. "No component is taken from another projectile" is an ℹ note, not a warning: an exact copy is a valid starting point.

## Choosing and creating custom projectiles from a weapon

Every selector that picks what a weapon fires shows two groups: **Vanilla** (the donors, as before) and **Custom — Experimental**. The
custom group is `CustomProjectileGroup`, the same component in every selector:
- a player weapon's Projectile section and the Projectile Builder's player hosts (`AttackOutputSelector`);
- support and mounted hosts (`ProjectileHostSwap`);
- the alternate programmable-ammunition mode (`ProgrammableAmmoEditor`).

Older SDK bindings (`ProjectileSelector`) and a custom stratagem's delivered weapon round list custom projectiles as a disabled violet
`<optgroup>` with the reason. A host is a `CustomProjectileHost` (kind, weapon, attack). `ICustomContentTarget.DescribeProjectileHost`
says whether it can fire a custom projectile, or why not. The 0.30.0 target binds player weapon attacks that can fire its
carrier. Runtime's internal source-bound binding for mounted weapons (live-tested on the EXO-45 Patriot) is not offered yet.
`AssignCustomProjectileAsync(host, …)` refuses a host the target cannot bind.

**Create New Custom Projectile…** starts a draft (`BuilderWorkspace.BeginCustomProjectileDraftAsync`):
- **The projectile.** It is saved in the project at once as an ordinary custom projectile. Its base is what the attack fires now, when
  that can be a base.
- **The flow.** The flow itself (`CustomProjectileDraft`: the projectile, the attack it was created for, the copied projectile) is
  session state and is never saved.
- **The builder.** The Custom Projectile Builder opens with a banner. **Assign to … and return** is enabled once the projectile has no
  error. It assigns the projectile (`CompleteCustomProjectileDraftAsync`) and returns to that attack: the Player Weapons editor with the
  attack in view, or the Projectile Builder host it came from.
- **Cancelling.** **Cancel and return** (`CancelCustomProjectileDraftAsync`) deletes the draft, unless it was assigned elsewhere
  meanwhile. Deleting the draft from its editor also ends the flow. Either way, the attack keeps what it fired.
- **Duplicate and edit…** on an assigned custom projectile starts the same flow with a copy.
- **IDs.** While nothing refers to the draft and its id was not set explicitly, its id follows its name
  (`RenameCustomProjectileAsync`). Afterwards a rename keeps the id, and **Change ID** moves every reference.

Conflicts are asked inline, where the choice is made, both on a selector and on the draft banner
(`CustomAssignmentConflictException`):
- a vanilla swap, or projectile edits, on the attack;
- the target's binding limit (`ICustomContentTarget.ProjectileBindingLimit`, one attack at a time for the 0.30.0 target). The
  exception lists the attacks that would lose their custom projectile (`Displaced`).

The selector shows the assigned projectile's errors and warnings, and its assignment's, inline with **Open to fix**. The weapon's
Projectile summary marks an invalid reference. Navigation between a selector and the builder goes through `BuilderNavigator`, which Home
handles, so selectors at any depth can open the builder and be returned to.

## The project model (format 14, `customContent.version` 1)

`HD2RuntimeGUI.Core/Models/CustomContent.cs`. Every name is ModBuilder's own; none is Runtime's.

```json
"customContent": {
  "version": 1,
  "stratagems": [{
    "id": "orbital_gas_barrage", "name": "ORBITAL GAS BARRAGE", "nameCased": "Orbital Gas Barrage",
    "description": "Calls down a barrage of gas shells.", "icon": "orbital_gas_barrage",
    "code": ["right", "right", "down", "left", "down", "left"], "cooldown": 60, "uses": 4, "traits": ["Orbital", "Caustic"],
    "carrier": { "group": "any_red", "prefer": [], "allow": [], "exclude": [] },
    "delivery": { "family": "orbital", "orbital": { "mode": "native", "pattern": "Orbital 120mm HE Barrage",
                  "impactPayload": { "kind": "vanilla_explosion", "id": "Orbital Gas Strike" } } },
    "assets": [],
    "callbacks": { "onBeaconLanded": "ctx:log('landed')" }
  }, {
    "id": "laser_maxigun", "name": "LASER MAXIGUN", "description": "…", "icon": "laser_maxigun", "code": ["down", "left", "down", "up", "up", "left"],
    "traits": [], "carrier": { "group": "weapon", "prefer": [], "allow": [], "exclude": [] },
    "delivery": { "family": "weapon", "variant": { "weapon": "M-1000 Maxigun", "round": "output/v1/projectile/las-58-talon",
                  "presentation": { "nameMode": "custom", "name": "LASER MAXIGUN", "iconMode": "custom", "icon": "laser_maxigun_weapon" },
                  "model": "laser_maxigun_model" } },
    "assets": []
  }],
  "projectiles": [{ "id": "talon_hybrid_bolt", "name": "Talon Hybrid Bolt", "base": "output/v1/projectile/las-58-talon",
                    "visual": "output/v1/projectile/plas-1-scorcher", "damage": "output/v1/projectile/rs-422-railgun" }],
  "assignments": [{ "id": "…", "hostKind": "player_weapon", "weapon": "SMG-32 Reprimand", "attackRole": "primary",
                    "projectile": { "kind": "custom_projectile", "id": "talon_hybrid_bolt" }, "enabled": true }],
  "images": [{ "id": "orbital_gas_barrage", "png": "<base64>", "sha256": "…", "preparation": "auto", "sourceName": "gas.png" }],
  "models": [{ "id": "laser_maxigun_model", "base": "M-1000 Maxigun", "debug": false, "all": "#FF2A2A", "zones": { "2": "#20E0FF" } }]
}
```

- **Ids.** A custom object's id is its identity (Runtime registry, multiplayer agreement, resource names). It is suggested once from the
  first name (`CustomIds.Suggest`), changed only through *Change internal ID…* (with a warning; a custom projectile's references move with
  it in the same save) and never re-derived from a display name. Rule: `^[a-z][a-z0-9_]*$`, at most 48 characters, unique in its namespace.
- **Delivery.** One family; only that family's object is saved. Switching family asks first when settings would be lost, then starts the
  new family from its defaults (`CustomFields.NewDelivery`) and its carrier from the target (`ApplyFamilyDefaults`: the default group, and
  the only carrier family a sentry or an Eagle may take).
- **Carrier.** `group` (a carrier group; null with a custom policy), `slots`, `beacon` (null: the group's), `prefer`, `allow`, `exclude`.
  Projects from earlier 1.7 builds (a beacon and families, no group) still read and build as a custom policy.
- **References** are typed (`ContentRef`): `custom_projectile` (a project custom projectile), `vanilla_round` (the round a reviewed
  vanilla support weapon fires), `vanilla_explosion` (a reviewed explosion donor). Donors that can only be vanilla (a support item, a pod
  item key, a sentry, an Eagle, an orbital, a silo, a blast, a sound) are their catalogued names.
- **Images** keep the editable source PNG in the project itself, so a project reopens, duplicates and imports complete. An image no
  custom stratagem shows (as its icon or a delivered weapon's) is dropped on save; so is a model no variant uses. At most 300 KB per PNG.
- **Structure vs. meaning.** `CustomContentShape` (called by `ProjectIdentity.Validate`) enforces structure only: valid unique ids,
  bounded lists and texts, typed references, finite numbers, colours. Whether Runtime would accept the values is a diagnostic, shown where
  the object is edited and refusing the build, so an edit in progress always saves.

## Migrations

`CustomContentMigrations` runs on the project JSON when a project is opened or imported, before it is deserialized (only for projects
that contain `customContent`; every other project is read exactly as before).
- A section older than `CustomContentSection.CurrentVersion` is migrated step by step (`Step(From, …)` moves version N to N+1).
- A section newer than this ModBuilder understands is refused with the reason (an older ModBuilder must not drop what it does not know).
- Version 1 is the first model, so there is no step yet; the framework is tested with injected steps. The fields added during 1.7's
  development (uses, traits, carrier group and slots, five families, presentation, models, the extra callbacks) are additive and optional.

A Runtime API change normally needs **no** migration: only the target adapter changes. A migration is for a change in what ModBuilder's
model means (for example splitting a field in two).

## The target adapter

`ICustomContentTarget` (`CustomContent/CustomContentTarget.cs`) is the boundary. `Runtime030Target` implements it for HD2Runtime
0.30.0 and is the only place that knows the Runtime's names. For custom stratagems it no longer keeps its own copy of the Runtime's
rules:

1. **`RuntimeProjectForm`** writes a stratagem in HD2Runtime's project format (`custom_stratagems.json`), and reads one back (templates,
   imports). The scripted family, which that format cannot hold, is `payload: {family: "script"}`.
2. **`RuntimeProjectValidator`** is a port of the SDK's `validate()`: the same checks, in the same order, with the same `where: what`
   problems (except where the SDK's validator refuses what the Runtime registers: a carrier group whose beacon is `any`, `orbital` or
   `any`, implies that beacon, as in the Runtime), over every custom stratagem of the project at once (codes collide across them; native codes come from the schema). With
   `allowScript`, a scripted stratagem's metadata and carrier are checked against the script family. Its problems become diagnostics on
   the field they name (`payload.gun.rpm` → the Pelican gun's rpm field), marked *Runtime*.
3. **`RuntimeRegistrationLua`** is a port of the SDK's `compile_lua`: a stratagem's register fields exactly as the SDK writes them.

The adapter adds what the project format has no place for (images and models of the project, custom projectile references, the Lua
escape hatch, multiplayer and play-status notes) and describes fields for the editors: `Describe(path, stratagem)` (supported / refused /
unavailable, reason, unit, min/max, exclusive minimum, default, required, choices), with the context rules the schema states (an Eagle has
no cooldown or uses; only some Eagles' impacts are replaceable; a native barrage takes only pattern and impact; a backpack has no weapon
fields; a rate-selector weapon takes no rpm; a Pelican's rpm, face target and aim height follow the Gatling AI; an expendable's round and
impact exclude each other). It also knows the carrier groups that can carry a payload and the family's default group, and the play status
of each family (`familyEvidence`).

If Runtime later renames a field, adds a family or publishes a public custom projectile API, the change is a new or updated target (plus,
only if the meaning changed, a migration step). Editors never mention a Runtime field name except as the path a field is keyed by.

### The schema

The adapter reads HD2Runtime's machine-readable `sdk/CustomStratagemSchema.json` (`CustomSchemaReader`): field types, units, ranges,
defaults, typed enum values (numbers and booleans included), the families with their carrier rules and groups, the pod and variant
families and family options, the play status per family, the selection note, the refusals with reasons, and every catalogue (support
donors with rack counts and rate selectors, rounds, clone and variant donors, sentries, Eagles, orbitals with their vanilla patterns,
explosion donors, Pelican explosion donors with their rate limits, sounds, silos, blasts, pod items and racks, carrier groups, native
codes, carrier families, stratagems). Unknown keys are read leniently, as the Runtime asks builders to.

ModBuilder bundles the schema of HD2Runtime 0.30.3 at `7da218b` (`CustomContent/Targets/CustomStratagemSchema-0.30.3.json`, byte for
byte, SHA-256 `aa898c24…d74e`; 0.30.2's was `9dc2f986…6945`, and 1.7.0 bundled 0.30.0's, `624d7d1c…9a9e`), with that Runtime's model facts
(`ModelBaseCapabilities-0.30.3.json`), LuaLS stub and example projects. 0.30.2 changed only the sound catalogue of the custom stratagem API (`primary/smg37/seaf` is now
`seaf/3`, `secondary/gp31/alt` is now `other/wep_grenadier_rifle/1`; the Runtime still plays the old names). A saved custom stratagem
with an old name shows the schema's refusal under its sound field with a **Use** button for the new name. The custom stratagem API is
0.30.0's, so custom content still requires HD2Runtime 0.30.0, or 0.30.2 when it uses one of the two new names.

0.30.3 changed only the range of a stratagem's `uses`: -1 to 100, where 0 or -1 (or none) mean unlimited (0.30.2: 1 to 100). The editor
offers **Unlimited** (no value, which every 0.30 release reads as unlimited) or **Limited** with a count of 1 to 100; a count of 0 is
unlimited too. ModBuilder never writes 0 or -1 itself, but keeps one read from an imported Runtime project, so the project round-trips:
the field then says the value requires HD2Runtime 0.30.3 and offers **Use unlimited (no value)**, and the export requires 0.30.3
(`RuntimeRequirement.Release0303`) until it is removed. Below -1 is refused in Runtime's words. An Eagle's uses stay per rearm.

`HD2RUNTIME_CUSTOM_SCHEMA=<path>` makes a run use another schema file (a Runtime developer testing schema changes); a schema
whose format the target does not know is refused with the reason and the bundled one is used.

The bound SDK and the custom content target are independent: a project bound to an older SDK (0.28.1) can have custom content (its
export then requires the target's HD2Runtime 0.30.0 and still declares its bound SDK). Binding the 0.30 SDK is a separate integration
([runtime030-integration-plan.md](runtime030-integration-plan.md)).

Custom projectiles have no schema in Runtime yet. Their fields are exactly Runtime's definition model (`runtime/custom_projectiles.lua`
`define`: `base` and `components = {visual, damage, impact_explosion, ballistics}`), and their donors come from the bound SDK's projectile
catalogue (`AttackOutputCapabilities.json`).

## Validation

`CustomContentValidator.Validate` = ModBuilder's rules + the target's, as `CustomDiagnostic`s with a severity:

| Mark | Severity | Meaning |
|---|---|---|
| ✓ | Pass | a check that holds (*Internal ID is unique*, *HD2Runtime's own validator accepts this custom stratagem*) |
| ℹ | Info | worth knowing (uses counted natively, a related mission-only native code, a model derived at export, an objective blast) |
| ⚗ | Experimental | works through Runtime machinery the Runtime calls experimental or internal (a family not yet live-tested) |
| ⚠ | Warning | builds, but may not behave as hoped (a primary in a pod, callbacks, extra fields, a solo-only pod) |
| ✕ | Error | refuses the build (anything the Runtime's validator reports, a missing icon or model, a callback that does not parse) |

Each diagnostic names its owner (`stratagem:<id>`, `projectile:<id>`, `assignment:<guid>`) and field (the project form path), so the editor
shows it under the field, pod item or editor it is about, marks the section, and lists it in the live preview with a jump to the section.
The Runtime's own problems are shown in its own words (as every Runtime reason is). The build-blocked banner on other pages opens the
first failing custom object.

**Multiplayer and play status** (informational; networking is Runtime's): *payload is fully declarative* (every compatible Runtime can
derive it) versus *callbacks run on the caller's machine*, and each family's status as HD2Runtime publishes it (`familyEvidence`). Custom
projectiles exist on the host only.

## Dependencies

`CustomContentGraph` records what uses what: custom stratagem → icon and weapon icon images, model, donors, pod items, rounds, impact
payloads and blasts, extra assets, a delivered weapon's round (a custom projectile, when the model says so; the target refuses it with the
reason); custom projectile → its base and component donors; weapon attack → custom projectile. Deleting a used custom projectile is
refused (`CustomReferenceException` with the uses); the editor offers to move the uses to another custom projectile or remove them with
it. Changing its id moves every reference.

## Build output

- `src/addon.lua`: the generated operations as before, then a custom content block (inside `hd2.events.run_as(<mod>)` when available)
  with one registration per custom stratagem, each isolated (a failing setup or a refused registration is logged; the others register):

  ```lua
  register_custom_stratagem('orbital_gas_barrage', function()
      return {
          id='orbital_gas_barrage',
          name='ORBITAL GAS BARRAGE',
          name_cased='Orbital Gas Barrage',
          description='Calls down a barrage of gas shells.',
          icon=hd2.resources.image('orbital_gas_barrage'),
          code={'right','right','down','left','down','left'},
          cooldown=60,
          uses=4,
          traits={'Orbital','Caustic'},
          carrier={group='any_red'},
          orbital={native=true,pattern='Orbital 120mm HE Barrage',impact_explosion='Orbital Gas Strike'},
          on_beacon_landed=function(ctx)
              ctx:log('landed')
          end,
      }
  end)
  ```

  The field lines are exactly the SDK compiler's (indented). Setup Lua runs before `return {`; extra fields follow the callbacks.
- A weapon that fires a custom projectile: its Projectile section hides the attack's own projectile field editors (such edits cannot
  apply together with a custom projectile). The carrier swap is an ordinary guarded patch among the generated operations
  (`value=hd2.attack_output('output/v1/projectile/td-110-maelstrom-slot-2')`, with the donor rules' opt-ins), and the block defines the
  projectile (`custom_projectiles.define{id = "<author>/<mod>/<id>", base = …, components = {…}}`) and binds
  `actions.replace_projectiles(definition, {carrier = …, weapon = …})` at each mission start. Both modules are required with `pcall`; a
  Runtime without them logs that custom projectiles are not active.
- Icons: each PNG compiled into a BC1 texture with its 9-level mip chain and the GUI material that names it, under
  `<resource>/images/<id>`, plus the image record `<resource>/hd2runtime_images`, all in the mod's own archive in the layout of the game's
  archives with a `.gpu_resources` companion. `CustomIconCompiler` and `ResourceArchive` are C# ports of the SDK's `hd2_image.py` and
  `hd2_archive.make_resource_archive`; the tests compare them byte for byte with the SDK's output
  (`tools/custom-content/make-image-fixtures.py`). ModBuilder needs no Python.
- Models: per base package, `mod/<archive>.patch_0` (+ `.stream`, `.gpu_resources`) with each model's unit, material and colour table,
  and the record `<resource>/hd2runtime_models` in the mod's archive; `custom_content.models` in the build report (base, game build,
  digests).
- The ZIP adds `images/<id>.png` (the editable sources, outside `mod/`), `custom_content` in `build-report.json` (target, schema digest,
  ids, image sources, models) and `requires.hd2runtime.min_version` = 0.30.0 (at least). The addon wrapper of a custom content project compares
  versions as SemVer (a prerelease is older than its release), tells an older Runtime (`hd2.compatibility.require_runtime`) before it
  fails closed, and **declares the bound SDK** (`local x,y,z=version('<bound SDK>')`) so the Runtime's legacy acknowledgements apply to the
  project's other edits; the requirement is `local required_runtime`. Projects without custom content keep the previous wrapper, manifest
  and archive byte for byte.

Generated Lua is output. Reopening a project never reads it back: the project's `customContent` is the source.

## Current Runtime limitations (0.30.0)

- Experimental with several players. Per family, HD2Runtime publishes what it has live-tested (`familyEvidence`): the editor shows it. With several players, a
  client runs its own support, expendable, sentry and silo deliveries and native orbitals; the host spawns Pelicans and requests silo
  blasts; Eagles and Runtime barrages stay host only; a carrier pod is solo only.
- A delivered weapon (support, pod item, expendable, sentry) fires only reviewed vanilla rounds, not a custom projectile.
- Refused as shared definitions: an explosion's size, an Eagle's pattern, projectile and cooldown, a sentry's impact explosion, backpack
  changes, another rack count, two donors in one support pod, secondaries and throwables in a pod, more pod items than the rack holds, an
  expendable's own projectile.
- Models: only the M-1000 Maxigun is a reviewed base; only the base colour column of the material's colour table changes.
- Per-language text is not public in the Runtime: a custom stratagem's texts show the same in every language.
- Custom projectiles have no public API: their modules are internal, host only, and Runtime binds one weapon per carrier
  projectile with one proven carrier, so one weapon attack at a time can fire a custom projectile. Player weapons only; live-verified on the
  SMG-32 Reprimand. Other players do not see custom projectiles.

## Tests

- `CustomStratagemConformanceTests` — the ports against HD2Runtime's own SDK tools, from the oracle
  `Fixtures/custom-content/oracle/runtime-oracle.json` (`tools/custom-content/make-runtime-oracle.py --sdk <0.30.0 sdk>`):
  - the validator reports exactly the SDK validator's problems for 140 projects that each break a rule (and does not fail where the SDK's
    does);
  - every Runtime example, every template and every valid case compiles to the SDK's `addon.lua` byte for byte; the oracle's templates are
    the bundled ones (regenerate it when a template changes), and the two Runtime examples among them are the SDK's fixtures;
  - every Runtime example and every template round-trips through ModBuilder's model into the SDK's registration;
  - the build registers every template with the SDK's field lines and no error; scripted stratagems, callbacks, setup and extra fields;
    pod opt-ins; problems landing on their fields.
- `CustomModelCompilerTests` — the model pipeline against the SDK's `hd2_model.py` (half floats, mips, the record), and against the installed
  game when there is one.
- `CustomContentEngineTests` (schema, field descriptions, refusals, ids and codes, generated Lua, icon and archive parity with the SDK,
  migrations) and `CustomContentProjectTests` (workspace create / duplicate / rename / id change / delete, icons, families, custom
  projectiles and assignments, conflicts and the binding limit, the create-from-a-weapon draft, references, graph, save and reopen, export
  contents and wrapper, duplicate project, and that projects without custom content are unchanged).
- `tools/custom-content/validate-runtime.py` loads exported addons in the HD2Runtime 0.30.0 tree's own Lua harness, compares each
  registered definition with the one Runtime's SDK compiles from its example of the same id (the two Runtime examples among the templates;
  the others only have to register), and checks that Runtime's image log
  finds every icon in the archive's build record. `tools/custom-content-smoke.mjs` drives the desktop app (the `custom-content` RC smoke,
  whose export `scripts/run-rc-smokes.ps1` validates against `-CustomRuntimeTree`, by default `..\HD2Runtime`).
