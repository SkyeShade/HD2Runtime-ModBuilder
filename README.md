# HD2RuntimeGUI

A Windows-first .NET 10 MAUI Blazor Hybrid mod builder for the separately installed [HD2Runtime](https://github.com/SkyeShade/HD2Runtime). Version 0.2 supports the **0.14.0 guarded player-weapon and ammo API**.

Create a project → Player Weapons → search a weapon → save semantic changes → review Changes / Lua Preview → Build Mod. No Lua or memory-layout knowledge is required.

The published `PlayerWeaponAuthoringCapabilities.json` drives all weapon identities, controls, defaults, evidence and permissions: **80 weapons, 3,027 entries, 60 field definitions (47 writable, 13 read-only, 6 derived)**. There is no manually maintained weapon/field catalog in the UI. The six derived definitions are included in the read-only count.

## Run

Launch `artifacts/publish/win-x64/HD2RuntimeGUI.exe` after publishing. Distribute the entire publish folder. .NET and Windows App SDK are included; users need the Microsoft Edge WebView2 Evergreen Runtime, but no development tools or Python.

This repository is separate from HD2Runtime. It does not launch the game, deploy mods, or access game processes. Snapshot inspection reads only a user-selected local file.

## Authoring

Create a mod with a display name, author, resource ID such as `mods/skyeshade/my_mod`, version and optional description. The library supports open, rename, duplicate, remove from library, open project folder and export. Removing a library entry preserves its files.

Player Weapons provides name/category search and slot, category, implementation-family, editable and shared filters. Only applicable capability cards appear. Numeric controls have no invented gameplay bounds. Defaults, units, provenance, modified state and shared/derived/read-only badges are visible; backing offsets remain under Advanced.

Seven ambiguous resource identities remain visible but have no write controls. Shared writes require a per-change acknowledgement, including unnamed additional consumers. Fifteen writable shared settings groups are represented. Pending approval can be saved, but blocks Lua generation and export. Reset field, reset weapon, reset all weapon changes, enable/disable, groups and notes are supported.

Try these ordinary editor operations:

| Project | Weapon | Changes |
| --- | --- | --- |
| Concussive1100 | AR-23C Liberator Concussive | Fire rate 400 → 1100 RPM |
| VerdictFlatTrajectory | P-113 Verdict | Drag 1.2 → 0.1; gravity 1 → 0.2 |
| ReprimandFlatTrajectory | SMG-32 Reprimand | Drag 1.2 → 0.1; gravity 1 → 0.2 |

The two trajectory samples preserve velocity 285 and mass 15. No fictitious damage-dropoff field is introduced. See [sample definitions and generation](samples/README.md).

Ammo / Magazine groups the SDK's detachable-magazine and rounds-feed controls alongside calculated, read-only baselines. `PlayerWeaponAmmoCapabilities.json` supplies ownership, default-preset and discrepancy evidence; the reader cross-checks it against the semantic authoring catalog. There are 360 ammo entries, 194 writable, covering 45 weapons with writable ammo fields.

The 19 customization-supplied default presets (including Concussive and the shared Liberator/Penetrator/Suppressor preset) remain read-only. Thirteen weapons have no applicable ammo controls. GP-31's ambiguous identity remains blocked. Arbitrator and One-Two retain SDK baselines of 45 and 40 with visible catalog-disagreement warnings. Calculated values are explicitly labelled SDK baselines, not live simulations of overrides.

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

Startup and each new-project action check public GitHub releases without a token. Discovery identifies the runtime, SDK, ModTemplate and example-project artifacts; the application downloads **only the SDK**. A verified copy of the published 0.14.0 metadata supports first launch offline.

Updates validate repository, exact asset names/URLs, ZIP content types, stable semantic version, size and available SHA-256 digest. Archive inspection rejects path traversal, duplicate paths, links and excessive entry/expanded sizes. No downloaded scripts are executed. Only the two fixed metadata files are consumed.

The SDK metadata and capability catalog must agree on version and supported schemas/API. Both are staged in a temporary directory; a validated version directory is installed before the current pointer changes. Cached version content is immutable. Failed updates preserve the previous SDK. Offline mode retains the cached SDK and last successful release information, with an explicit unverified status.

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
Exports/<mod-name>-<version>.zip
research.json
```

Project format 2 retains support for format 1 and legacy reviewed changes. `weaponChanges` stores weapon name, semantic field ID, expected and desired scalar values, scalar type, baseline SDK version, enabled/persistence state, group, notes and explicit shared acknowledgement/scope/affected identities. It stores no resolved addresses. Stable manager GUID generation matches the ModTemplate's resource-ID algorithm.

Rebinding keeps desired values and saved baselines. Missing fields, changed types, read-only capabilities, changed shared scope and changed defaults block affected builds and appear in Changes. A changed default requires an explicit **Accept baseline** action; opening or rebinding never silently rewrites it. Older SDK cache entries remain available.

## Lua and ZIP output

Single semantic changes use `hd2.patch`; multiple related writes use `hd2.transaction`; persistence wraps either in `hd2.ensure` with Runtime's standard 60-second behavior. A transaction group is limited to Runtime's 32 fields and is never silently split. Shared and ordinary writes form separate operations. Shared operations are emitted only after every included shared change validates its acknowledgement. Conflicting shared overrides and duplicate weapon/field edits are rejected.

Targets are `hd2.weapon(name)` and fields are published `hd2.fields` constants. The generator handles the API's `player_` aliases for collisions with legacy constants. IDs are deterministic per mod/weapon/group. Float32 baselines use their shortest round-trip scalar spelling, so catalog `1.2000000476837158` generates `expect=1.2`. The original project baseline is retained.

The package follows the published 0.14 ModTemplate archive, discovery-header, manager GUID and dependency conventions. It contains `manifest.json`, `hd2runtime.json`, `build-report.json`, `README.md`, `src/addon.lua`, and one gameplay archive plus empty stream/GPU companions under `mod/`. It requires Bingus release 15+/API 1 and the project's HD2Runtime minimum version/API. The template's Bingus check is `loader.version >= 16`.

No Runtime implementation, SDK stubs, GUI catalog, snapshots or research artifacts enter the package. ZIP inventory and content are reopened and verified before success. Sorted entries, fixed timestamps, deterministic IDs and stable content make unchanged exports byte-identical. Preview shows exactly the gameplay source included as `src/addon.lua`; the archive adds the template dependency/discovery wrapper. Export directory is configurable, and Explorer can select the result.

## Snapshot Research

The separate read-only browser loads/relinks `.hd2snap`, persists its last path, shows capture metadata/build fingerprints, validates the v1 header/index and performs bounded raw-byte reads. It can link an existing Runtime schema-2 `.weapon-map.json` for resource/name/component lookup, resolved fields and attack settings. It verifies matching build fingerprints and read-only snapshot mode. A matching build alone does not prove that a report came from the same capture; the UI states that limitation.

The published mapper report omits record addresses, so automatic semantic-field-to-raw-byte navigation is unavailable. Raw inspection accepts an address from the selected snapshot or a captured-region selection. The browser does not execute Runtime's scanner or feed data into authoring/generation.

## Build, test and publish

Development requires Windows, .NET 10 SDK, MAUI workload and WebView2. NuGet access is needed for first restore. Close the app before rebuilding its output directory.

```powershell
dotnet workload install maui
dotnet build HD2RuntimeGUI/HD2RuntimeGUI.csproj
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj
dotnet publish HD2RuntimeGUI/HD2RuntimeGUI.csproj `
  -c Release -f net10.0-windows10.0.19041.0 -r win-x64 `
  --self-contained true -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None -o artifacts/publish/win-x64
```

See [verification results](docs/verification.md), including release provenance, test coverage, GUI scenarios and output locations. Agent screenshots, caches, publish output and generated ZIPs are ignored by Git.
