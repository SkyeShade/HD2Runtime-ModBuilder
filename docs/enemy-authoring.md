# Enemy and structure authoring (development)

Internal notes for the **Enemies** and **Structures** pages. They are built against an **unreleased** HD2Runtime SDK
(the work intended for Runtime 0.28.0). Nothing here is released: the public ModBuilder version, the SDK compatibility pin
(`SdkCompatibility.NewestSupportedVersion`, still 0.27.0) and the update manifests are unchanged.

## Running against a local Runtime SDK

The local SDK path already exists for development: `--sdk-path <HD2Runtime>\sdk` (or `HD2RUNTIME_SDK_PATH`).

- The `sdk/` directory (or an SDK ZIP) is read and fully validated in memory and served for this run only.
- It is never written to the SDK cache, and published SDK installs are refused while it is active.
- Settings shows the local source. The authoring pages add "LOCAL DEVELOPMENT SDK" to their heading.

The local build still reports `runtime_version` 0.27.0, the version its `VERSION` file carries. A project created against it is
bound to 0.27.0. Opened again with the published 0.27.0 SDK, its enemy edits are flagged as missing capabilities (never dropped)
and the build is blocked until the local SDK is back or the edits are reset.

`artifacts/dev/` holds the portable development build. Its launcher starts ModBuilder with the local SDK and a separate data root,
so real projects are never validated against unreleased metadata.

## SDK files consumed

| File | Contract | Use |
| --- | --- | --- |
| `EnemyAuthoringCapabilities.json` | `hd2runtime.enemy.guarded_authoring.v1` | Classes (enemies and structures), zones, attacks, field definitions and field instances. Read whenever present, and required from SDK 0.28.0. |

`EnemyAuthoringReader` (`HD2RuntimeGUI.Core/Metadata/EnemyAuthoring.cs`) is fail-closed.

- **Envelope:** contract, schema, SDK version, safety block (no runtime addresses or raw resource IDs, no writes during generation).
- **Definitions:** each field definition has a typed API constant, a type, a range, an opt-in and an evidence tier.
- **Classes:**
  - semantic ID `enemy/v1/<faction>/<class>`;
  - `name == wikiName ?? className`;
  - accessor `hd2.enemy` or `hd2.structure` by kind;
  - wiki names and native class names are unique and never collide (Runtime resolves both).
- **Zone and attack IDs:** `zone_<index>` and `slot_<n>…`. Every attack role is known, and the row each role names is fixed.
- **Instances:**
  - the target (path, zone or attack) exists;
  - the field domain matches the target: `entity.*` on the class, `zone.*` on zones, `damage.*`, `projectile.*` or `explosion.*` on the matching attack row;
  - writable baselines lie inside the published range; read-only fields carry a reason;
  - opt-ins are the definition's own, or the per-kind structure gate Runtime publishes on the instance;
  - attack rows are shared, with their published consumers;
  - live evidence matches the definition.
- **Summary:** every count is recomputed.

The other readers accept the rest of the development SDK:

- Player-weapon, support and vehicle-weapon **status references** are validated and shown, but kept read-only (this build has no status-reference editor).
- Projectile selectors follow the new **projectile source** (only `ACTIVE_DIRECT` sources are writable).
- **Sentry turret, targeting and minefield** targets use their published accessors, and **mine explosions** use `hd2.stratagem(name):mine()`.
- **External shared consumers** are shown by their description.

## Model

Enemy fields use the shared entity pipeline (`EntityField` → `EntityChange` → `EntityLua`), like vehicles, boosters and throwables:

| | Enemy / structure |
| --- | --- |
| `EntityTarget` | `Resource` = kind (`enemy` / `structure`), `Path` = `entity` / `damage_zone` / `attack`, `Zone` / `Attack` = published ID, `Enemy` = class semantic ID, `EnemyClass` = native class name |
| `EntityField.InstanceKey` | `<kind>:<semantic ID>\|<path>\|<zone or attack>\|<field>`. Built from semantic IDs, so it survives a wiki name proven later. Runtime's own instance key embeds the display name. |
| Operation / plan group | One per target object (the class, one zone, one attack row) |
| Backing object | The class's health record, or the published settings-row identity for attacks |
| Consumers | Bound by class semantic ID (Runtime publishes them by name) |
| Evidence hash | Target, type, writability, API constant, row kind, sharing and consumers, opt-in, tier. Opaque native row identities are excluded, since they move between game builds without a semantic change. |

`EntityAuthoring.Field(key)` is indexed once per SDK load. `EnemyCatalog` precomputes per-class field arrays, class lookup by
semantic ID, and search text (name, native class, wiki candidates, attack names, faction, semantic ID).

### Project format

An `EntityChange` for an enemy stores:

- `Resource` (`enemy` / `structure`);
- `Entity` = class semantic ID;
- `Path`, `Zone` / `Attack`;
- `InstanceKey`, `SemanticFieldId`, `FieldType`;
- expected and desired values, `BaselineSdkVersion`, capability evidence;
- `Enabled` / `EnsureEnabled`, `Group` ("Enemies" / "Structures") and `Notes`.

A shared attack row also stores `SharedConsumers`: the class semantic IDs that reach it when saved. Projects with enemy edits are
**format 10**, so older ModBuilder versions refuse the format instead of failing on the edits. Projects without them keep their format.

## Hierarchy and UI

```
Enemies / Structures (faction filter · search · show: wiki-named / native / with attacks / modified)
  Faction groups → class (Runtime name; native names in monospace)
    General                main health block (6 fields)
    Body zones             compact table of every zone (health · armor · to main · flags); one selected zone's fields
    Attacks                grouped by mount slot; one selected attack row's fields
      slot_N               direct-hit damage (DamageInfo)
      slot_N_projectile    projectile settings
      slot_N_impact        impact explosion damage (DamageInfo)
      slot_N_impact_explosion  impact explosion settings (radii)
      (expiry / spray rows where published)
```

- **Names:** a wiki attack name appears only where Runtime matched all nine values on the class's own page. `rowWikiMatches` is
  shown as describing the shared row, not the class. Zone labels are the wiki label, else the native zone name, else the zone ID.
- **Rendering:** only the selected class renders an editor, only the selected zone and attack render field rows, and the zone table
  scrolls within its own panel.
- **Field rows:** every row is the shared `EntityFieldEditor`, with the same number input, range, reset, On toggle, Mod Options and
  review/accept behaviour as vehicles and throwables.
- **Structures:** use the same `Enemies` and `EnemyEditor` components with `Kind = structure` (`hd2.structure`).

## Sharing and opt-ins

Opt-ins are implicit (no acknowledgement checkboxes). The generated Lua carries exactly what Runtime requires, and the UI warns:

- `allow_shared` on every attack row. The attack header lists the reviewed classes reaching the row. Other native users are
  possible, so the row always shows "Shared".
- `allow_unverified_effect` on the fields Runtime gates:
  - constitution, durable and explosive shares;
  - every attack row;
  - structure health (the instance-level gate).

  Live-proven enemy health and zone armor need none.
- A shared health record (none in this snapshot) would carry `allow_shared` the same way.

One native row reached through two targets (for example the Gunship's two rocket racks) is one value. A second edit of the same
row is refused where it is made, naming the target that already edits it. This check applies to every entity domain.

## Generated Lua

Each target object is its own `hd2.ensure` request: a patch for one field, a transaction for several fields of the same object.
Unrelated edits of one class are never bundled into one plan.

```lua
hd2.ensure({patch={id='entity-…',target=hd2.enemy('charger'),field=hd2.fields.entity.health,expect=2400,value=1200}})
hd2.ensure({transaction={id='entity-…',target=hd2.enemy('charger'):zone('zone_0'),changes={
    {field=hd2.fields.zone.armor,expect=4,value=3},{field=hd2.fields.zone.health,expect=1200,value=900}}}})
hd2.ensure({patch={id='entity-…',target=hd2.enemy('boomer_burrower'):attack('slot_1'),allow_shared=true,
    allow_unverified_effect=true,field=hd2.fields.damage.player_standard_damage,expect=500,value=50}})
hd2.ensure({patch={id='entity-…',target=hd2.structure('spawner_factory_conscript_base'),allow_unverified_effect=true,
    field=hd2.fields.entity.health,expect=1500,value=150}})
```

Classes are addressed by native class name, which Runtime always accepts and which does not change when a wiki name is proven
later. Zones and attacks are addressed by their published IDs.

## Rebinding between development snapshots

A project bound to 0.27.0 uses whichever local SDK is active, so every open re-validates against the current snapshot.

- **Same semantic identity (class semantic ID, target, field):** the edit stays. A new display or wiki name changes nothing.
- **Changed baseline:** flagged, then "Accept current capability / baseline" keeps the desired value.
- **Changed writability, opt-in or consumer set:** the edit is flagged for review.
- **Removed zone or attack:** flagged as a missing capability, offered for removal, and never retargeted to another zone or class.

## Tests

- `HD2RuntimeGUI.Tests/EnemyAuthoringTests.cs` (17 tests) runs against the committed development SDK fixture
  `Fixtures/sdk-dev-834716f.zip` (HD2Runtime 834716f `sdk/*.json`). It covers:
  - catalog and navigation counts, native-only identities, search;
  - main, zone and attack edits and their exact Lua;
  - opt-ins, sentinels, ranges and types, shared-row refusal;
  - structures, save/load, Mod Options, format 10 and validation;
  - opening without enemy metadata;
  - snapshot rebinds (rename, removed zone, changed baseline, read-only or removed attack, consumer change);
  - fail-closed metadata and indexing.
- `DevelopmentSdkTests.cs` (3 tests) covers the other domains' development changes.

## SDK gaps found

- **Status on enemy attacks:** Runtime reaches the DamageInfo rows but publishes no status slots, so none is shown.
- **Not mapped:** melee, ability and beam attacks, and movement, AI and targeting fields.
- **Vehicle weapons:** 16 explosion status fields (type and strength) have no `apiFieldConstant`. They stay read-only.
- **Version:** the local SDK still reports 0.27.0 (`VERSION`, `metadata.json`), so it cannot be told apart from the published
  0.27.0 by version alone.
- **Instance keys:** Runtime's embed the display name (`enemy:<class>:<name>_…`), so they change when a wiki name is proven.
  ModBuilder derives its own from semantic IDs.
- **Summary counts:** `summary.proven` / `summary.acknowledged` count definition-level opt-ins only, not the structure-health
  gate published on instances.
