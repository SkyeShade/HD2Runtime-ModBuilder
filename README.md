# HD2RuntimeGUI

A Windows-first .NET 10 MAUI Blazor Hybrid mod builder for the separately installed [HD2Runtime](https://github.com/SkyeShade/HD2Runtime). Version 0.4.0 supports the **0.22 guarded defensive and offensive stratagem, support/player-weapon, heat, ammo and composition-plan APIs**.

Create a project → Player Weapons → search a weapon → save semantic changes → review Changes / Lua Preview → Build Mod. No Lua or memory-layout knowledge is required.

The published `PlayerWeaponAuthoringCapabilities.json` drives player-weapon identities, controls, defaults, evidence and permissions: **80 weapons, 3,574 entries, 89 field definitions (69 writable, 20 read-only, 9 derived)**. There is no manually maintained weapon/field catalog in the UI. The nine derived definitions are included in the read-only count.

## Run

For a development build, run `./scripts/publish-windows.ps1` and launch the staged `artifacts/release/HD2RuntimeGUI-vX.Y.Z-win-x64/HD2RuntimeGUI.exe`. For end users, use the ZIP instructions below. .NET and the Windows App SDK are included; users need the Microsoft Edge WebView2 Evergreen Runtime, but no development tools or Python.

This repository is separate from HD2Runtime. It does not launch the game, deploy mods, or access game processes. Snapshot inspection reads only a user-selected local file.

## Authoring

**Stratagems** has its own browser with All, Orbital, Eagle, Support Call-Ins, Sentries, Emplacements and Mines / Deployables tabs. The published Runtime 0.22 canonical catalog supplies 73 roots, 1,382 field instances (1,311 writable), 226 backing semantic objects, 327 target-specific operation groups and 115 reviewed shared scopes. The two unresolved call-ins remain read-only. Branch-specific fields retain separate identities; search and system/availability filters come from metadata. Every field autosaves with baseline → desired comparisons and reset actions.

Defensive stratagems render their published graph: **Stratagem → Deployed Entity → Mounted Weapon → Attack branches**. All 10 sentries, 4 conventional emplacements and the 4 mine deployment entities expose cooldown and reusable **Entity Stats** (health and armor). Twelve mounted weapons show only the sections present in metadata: Ammo, Weapon (the 9 published fire-rate fields), Heat/Heatsinks, Projectile, Damage, Explosion, Explosion Damage, Beam, Arc and per-slot Status. Mines expose only the stratagem root and deployment entity; individual mines, triggers, distribution and mine attacks have no controls. The Grenadier Battlement's unproven mounted weapon, targeting, deployed and projectile lifetime, penetration slowdown, max uses and spray remain "Not currently writable" with Runtime's reasons. See [0.22 verification](docs/runtime022-verification.md).

Eagle cooldown and uses-per-rearm are per stratagem. Eagle rearm time is one shared system: all eight handles show the same saved override and use one acknowledgement of its reviewed consumers. Max uses, call-in time and unproven barrage scheduling remain read-only with SDK reasons. Projects stay pinned until explicitly rebound; changed baselines and ownership require review.

Runtime 0.21 projects stay pinned to 0.21 and keep its conservative status-slot limitation ([0.21 verification](docs/runtime021-verification.md)). After an explicit rebind, saved changes move to the 0.22 canonical instance with the same semantic target; changed shared scopes require review and a fresh acknowledgement. 0.22's target-specific operation groups allow distinct status slots on one DamageInfo object in one `hd2.plan`.

Create a mod with a display name, author, resource ID such as `mods/skyeshade/my_mod`, version and optional description. The library supports open, rename, duplicate, remove from library, open project folder and export. Removing a library entry preserves its files.

Player Weapons provides name/category search and slot, category, implementation-family, editable and shared filters. Only applicable capability cards appear. Numeric controls have no invented gameplay bounds. Defaults, units, provenance, modified state and shared/derived/read-only badges are visible; backing offsets remain under Advanced.

Seven ambiguous resource identities remain visible but have no write controls. Shared writes require acknowledgement, including unnamed additional consumers. Composition reuses one approval for fields on the exact same backing object and consumer scope. Pending approval can be saved, but blocks Lua generation and export. Reset field, reset weapon, reset all weapon changes, enable/disable, groups and notes are supported.

Try these ordinary editor operations:

| Project | Weapon | Changes |
| --- | --- | --- |
| Concussive1100 | AR-23C Liberator Concussive | Fire rate 400 → 1100 RPM |
| VerdictFlatTrajectory | P-113 Verdict | Drag 1.2 → 0.1; gravity 1 → 0.2 |
| ReprimandFlatTrajectory | SMG-32 Reprimand | Drag 1.2 → 0.1; gravity 1 → 0.2 |

These historical trajectory samples preserve velocity 285 and mass 15. In 0.17, use Composition → Projectile Settings and explicitly acknowledge shared projectile-definition writes. No fictitious damage-dropoff field is introduced. See [sample definitions and generation](samples/README.md).

Ammo / Magazine groups the SDK's detachable-magazine and rounds-feed controls alongside calculated, read-only baselines. `PlayerWeaponAmmoCapabilities.json` supplies ownership, default-preset and discrepancy evidence; the reader cross-checks it against the semantic authoring catalog. There are 360 ammo entries, 194 writable, covering 45 weapons with writable ammo fields.

The 19 customization-supplied default presets (including Concussive and the shared Liberator/Penetrator/Suppressor preset) remain read-only. Thirteen weapons have no applicable ammo controls. GP-31's ambiguous identity remains blocked. Arbitrator and One-Two retain SDK baselines of 45 and 40 with visible catalog-disagreement warnings. Calculated values are explicitly labelled SDK baselines, not live simulations of overrides.

Capability schema v2 supplies explicit `aliasOf`, `canonical`, `preferred`, `deprecated`, `acceptedForWrites` and `semanticTarget` metadata. Only preferred canonical fields appear as independent controls. Old project aliases resolve per weapon for display/editing and generated Lua; equal desired values appear once. Conflicting desired values (or different saved baselines) block building until a saved source is explicitly selected or the field is reset. Source records remain intact on opening/rebinding; ordinary edits save the canonical identifier while retaining the saved expected baseline. Fields are never merged merely because they share an offset or semantic target.

## Heat and heatsinks

SDK 0.18 adds a dedicated **Heat / Heatsink** section for seven energy weapons. The typed heat companion is cross-checked against the authoring catalog for identities, fingerprints, baselines, ownership, permissions and counts. Five weapons expose 30 writable field instances (15 heat, 15 heatsink); Scythe and Dagger stay blocked by duplicate identities. Dagger's native/catalog disagreements remain visible.

Direct heat mechanics and heatsink inventory are separate from read-only attachment presets. Warmup, post-overheat cooldown and derived values remain read-only with SDK explanations. Heat overrides use the existing project format, baseline-aware autosave, reset and Changes/Lua Preview paths. Sibling heat/heatsink edits share one component transaction and ensure. See [0.18 verification and sample](docs/runtime018-verification.md).

## Composition, fire modes and read-only catalogs

Published Runtime contracts drive these controls; there are no manually maintained capability tables:

- **Fire Mode:** 21 uniquely resolved weapons expose allowed Full Auto / Semi Auto choices. Other weapons remain read-only. Native values 3 and 5 have no invented global meaning; JAR-5 cannot be changed to Full Auto.
- **Composition:** 66 weapons expose 67 attacks and 57 guarded replacement selectors. Sources are filtered by published compatibility class and residency. JAR-5 is self-contained; Talon is source-weapon-required and excluded. Runtime-permitted unresolved dependencies remain selectable with a warning.
- **Projectile Settings:** controls belong to the selected projectile object. Replacing a projectile changes the settings shown. Object writes require explicit shared-definition acknowledgement, including consumers listed by the SDK and possible dynamic consumers.
- **Terminal Actions:** 134 readable slots, 130 writable (65 impact / 65 expiry), with typed ExplosionSettings sources and semantic None. Add/remove impact or expiry explosions without numeric IDs. Shared slots require acknowledgement.
- **Explosion:** 144 writable scalar capability entries across 12 semantic fields: three radii, standard/durable damage, four AP lanes, demolition/stagger/push. Only proven controls appear. Separate outer damage and shrapnel writes are not published.
- **Attachments:** all 419 options (Optics 195, Underbarrel 117, Muzzle 71, Magazine 36) remain read-only. Default state, catalog compatibility, native identity evidence, normalized effects and ownership reasons are visible. This leaves existing direct magazine/rounds editing unchanged.
- **Support Weapons:** the published `hd2runtime.support_weapon.guarded_authoring.v2` catalog supplies 828 canonical field instances for 27 writable identities across all 35 visible weapons. Its 146 backing objects, 155 operation groups and 81 shared scopes drive validation, approval and generation directly. Repeated branch fields remain separate; the legacy flattened view is never used for authoring. Family graphs, parent/child attacks, ownership chains, backpack dependencies, statuses and unresolved nodes stay visible. The eight duplicate identities and Railgun Max Charge remain blocked. See [0.20.1 verification and sample ZIPs](docs/runtime0201-verification.md).

Modified values autosave and show baseline → desired in editor cards, Overview, Changes and exact Lua Preview. Returning to baseline removes the override using typed scalar equality. Read-only inspection data never becomes a project change.

Projectile reference overrides and object/terminal/explosion overrides store semantic weapon/attack/phase handles, saved baselines, SDK version, enabled/persistence/group/notes and acknowledgement/evidence digests. No native IDs or offsets are saved as runtime targets. Rebinding preserves values and blocks changed identities, residency, compatibility, allowed modes, shared scope or baselines until reviewed. Older projectile scalar overrides can explicitly **Move to Composition** while retaining their desired and expected values; newly shared writes require fresh acknowledgement.

Object edits bind to the selected projectile handle. SDK 0.19 coordinates related objects with one guarded plan instead of independent ensure jobs. Same-object scalar fields stay grouped; impact and expiry use separate typed operations in the same plan. A reference replacement plus dependent edits uses two phases and target_from for fresh projectile/terminal resolution. Simple independent edits retain patch/transaction output. SDK-pinned 0.17/0.18 projects retain their old safe export blocks until explicitly rebound.

Shared approvals appear once per modified object/consumer scope in Composition. DamageInfo approval cannot authorize ProjectileSettings or explosion data. Scope changes after rebind require review. Project files still store semantic intent, never Runtime plan requests or native targets. See [0.19 verification, samples and remaining limits](docs/runtime019-verification.md).

See [0.17 sample projects](samples/README.md#runtime-017-samples) and [verification](docs/verification.md).

## Architecture

`HD2RuntimeGUI/` is the MAUI Windows host, with Razor `Components/`, native desktop adapters in `Services/`, and custom dark CSS in `wwwroot/`.

`HD2RuntimeGUI.Core/` is an independently testable net10.0 library:

- `Models/`: project identity, semantic overrides, semantic versions.
- `Metadata/`: typed capability catalog, strict readers and immutable versioned SDK cache.
- `GitHub/`: public release discovery and bounded, validated SDK downloads.
- `Projects/` and `Storage/`: project library and atomic JSON persistence.
- `Generation/`: validation, semantic Lua generation, gameplay archive codec and verified ZIP packaging.
- `Services/`: application workflows, one-use SDK update decisions, file-only snapshot inspection.

GitHub access, SDK cache/readers, project storage, change validation, Lua generation, export, snapshot reading, file pickers and Explorer opening have interfaces and DI registrations. Razor components delegate these operations to services.

## SDK cache and updates

Startup and each new-project action check public GitHub releases without a token. Discovery identifies the runtime, SDK, ModTemplate and example-project artifacts; the application downloads **only the SDK**. A verified copy of the published 0.22.0 metadata supports first launch offline.

Updates validate repository, exact asset names/URLs, ZIP content types, stable semantic version, size and available SHA-256 digest. Archive inspection rejects path traversal, duplicate paths, links and excessive entry/expanded sizes. No downloaded scripts are executed. Only fifteen fixed metadata files are consumed: base metadata, authoring/ammo/heat catalogs, the four composition graphs, and the published ProjectileCompositionCapabilities, AttachmentOptionCapabilities, ExplosionAuthoringCapabilities, SupportWeaponCapabilities, SupportWeaponAuthoringCapabilities, StratagemAuthoringCapabilities and CompositionPlanCapabilities contracts. Each file is bounded by its own reader size limit. Named contracts are checked against their equivalent graph payloads; schema, fingerprints, permissions, baselines and counts must agree.

The SDK metadata and capability catalog must agree on version and supported schemas/API. For 0.20.1's reused 0.19 player artifacts, only the exact published bytes are accepted by digest; all other checks still apply. All metadata files are staged in a temporary directory; a validated version directory is installed before the current pointer changes. Cached version content is immutable. Failed updates preserve the previous SDK. Offline mode retains the cached SDK and last successful release information, with an explicit unverified status.

An update warning offers **Install Update** or **Ignore This Time** before project creation. Ignoring is single-use. Settings also provides manual update checks. Existing projects remain pinned until **Rebind to installed SDK** is explicitly selected in Overview.

## Persistence and SDK migration

The default root is `%LOCALAPPDATA%\HD2RuntimeGUI\`:

```text
library.json
Projects/<project-guid>/project.hd2mod.json
Sdk/current.json
Sdk/last-release.json
Sdk/<version>/metadata.json
Sdk/<version>/PlayerWeaponAuthoringCapabilities.json
Sdk/<version>/PlayerWeaponAmmoCapabilities.json  # SDK 0.14+
Sdk/<version>/PlayerWeaponHeatCapabilities.json  # SDK 0.18+
Sdk/<version>/CompositionPlanCapabilities.json   # SDK 0.19+
Sdk/<version>/SupportWeaponAuthoringCapabilities.json # SDK 0.20.1+
Exports/<mod-name>-<version>.zip
research.json
```

Project format 5 adds published entity/weapon graph identities and a target kind (stratagem, deployed_entity or mounted_weapon) to stratagem changes; it retains support for formats 1–4 and legacy reviewed changes. `weaponChanges` stores weapon name, semantic field ID, expected and desired scalar values, scalar type, baseline SDK version, enabled/persistence state, group, notes and explicit shared acknowledgement/scope/affected identities. It stores no resolved addresses. Stable manager GUID generation matches the ModTemplate's resource-ID algorithm.

Rebinding keeps desired values and saved baselines. Missing fields, changed types, read-only capabilities, changed shared scope and changed defaults block affected builds and appear in Changes. A changed default requires an explicit **Accept baseline** action; opening or rebinding never silently rewrites it. Older SDK cache entries remain available.

## Lua and ZIP output

Single semantic changes use `hd2.patch`; multiple writes to the same SDK backing object use one `hd2.transaction`; persistence wraps each object operation in one `hd2.ensure` with Runtime's standard 60-second behavior. Modern generation groups by published owner identity, not user labels or field offsets. A transaction is limited to 32 fields and is never silently split. Mixed persistence or incompatible target kinds on one object block export. Shared operations require current acknowledgement of every included consumer scope. Conflicting shared overrides and duplicate weapon/field edits are rejected.

Targets start with `hd2.weapon(name)` or `hd2.support_weapon(name)` and fields are published `hd2.fields` constants. Support targets and operation/plan groups come directly from canonical SDK descriptors. The player generator handles the API's `player_` aliases for collisions with legacy constants. IDs are deterministic per mod/weapon/group. Float32 baselines use their shortest round-trip scalar spelling, so catalog `1.2000000476837158` generates `expect=1.2`. The original project baseline is retained.

The package follows the published ModTemplate archive, discovery-header, manager GUID and dependency conventions. It contains `manifest.json`, `hd2runtime.json`, `build-report.json`, `README.md`, `src/addon.lua`, and one gameplay archive plus empty stream/GPU companions under `mod/`. It requires Bingus release 15+/API 1 and the project's HD2Runtime minimum version/API. The template's Bingus check is `loader.version >= 16`.

No Runtime implementation, SDK stubs, GUI catalog, snapshots or research artifacts enter the package. ZIP inventory and content are reopened and verified before success. Sorted entries, fixed timestamps, deterministic IDs and stable content make unchanged exports byte-identical. Preview shows exactly the gameplay source included as `src/addon.lua`; the archive adds the template dependency/discovery wrapper. Export directory is configurable, and Explorer can select the result.

## Snapshot Research

The separate read-only browser loads/relinks `.hd2snap`, persists its last path, shows capture metadata/build fingerprints, validates the v1 header/index and performs bounded raw-byte reads. It can link an existing Runtime schema-2 `.weapon-map.json` for resource/name/component lookup, resolved fields and attack settings. It verifies matching build fingerprints and read-only snapshot mode. A matching build alone does not prove that a report came from the same capture; the UI states that limitation.

The published mapper report omits record addresses, so automatic semantic-field-to-raw-byte navigation is unavailable. Raw inspection accepts an address from the selected snapshot or a captured-region selection. The browser does not execute Runtime's scanner or feed data into authoring/generation.

## Download a Windows release

Windows x64 releases are distributed as a self-contained directory ZIP, so no Rider or .NET SDK is required. From the GitHub Releases page:

1. Download `HD2RuntimeGUI-vX.Y.Z-win-x64.zip`.
2. Extract it to a folder you control.
3. Run `HD2RuntimeGUI.exe`.

The package includes the .NET runtime and MAUI/native files required by the application. It uses the Windows WebView2 Evergreen runtime supplied by Windows or installed separately; WebView2 is not bundled into the ZIP. HD2Runtime remains a separate dependency for generated mods, and generated mods/exports are never bundled into the GUI executable. The executable is currently unsigned, so Windows SmartScreen may show its standard warning on first launch.

The app stores mutable projects and exports in `%LOCALAPPDATA%\HD2RuntimeGUI\Projects\` and `%LOCALAPPDATA%\HD2RuntimeGUI\Exports\`, so the extracted application directory can be moved or replaced safely.

## Build, test and publish

Development requires Windows, .NET 10 SDK, MAUI workload and WebView2. NuGet access is needed for first restore. Close the app before rebuilding its output directory.

```powershell
dotnet workload install maui
dotnet build HD2RuntimeGUI/HD2RuntimeGUI.csproj
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj
./scripts/publish-windows.ps1
```

The release script cleans `artifacts/release/`, publishes a Release `win-x64` self-contained directory, removes debug symbols, and creates `HD2RuntimeGUI-vX.Y.Z-win-x64.zip`. A directory package is intentional: MAUI Blazor Hybrid and WebView2/native dependencies are more reliable when kept beside the executable than when forced into a single file.

See [verification results](docs/verification.md), including release provenance, test coverage, GUI scenarios and output locations. Agent screenshots, caches, publish output and generated ZIPs are ignored by Git.
