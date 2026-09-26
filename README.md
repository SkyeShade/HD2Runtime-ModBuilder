# HD2RuntimeGUI

A Windows-first, .NET 10 MAUI Blazor Hybrid desktop mod builder for the separately installed [HD2Runtime](https://github.com/SkyeShade/HD2Runtime). Create a project, configure SDK-backed changes, preview generated Lua, and export a Bingus-compatible gameplay mod ZIP.

![Weapon editor](docs/screenshots/weapon-editor.png)

## Run

The self-contained Windows x64 publish is in `artifacts/publish/win-x64/` after publishing. Launch `HD2RuntimeGUI.exe` from that folder. Distribute the **entire folder**, not the EXE alone. .NET and Windows App SDK are included; users do not need an SDK, Python, Rider, or other developer tools. The Microsoft Edge WebView2 Evergreen Runtime must be installed on the machine.

This repository is independent of HD2Runtime. It neither launches the game nor deploys mods. It contains no game process access or memory-reading/writing implementation.

## First workflow

1. Launch the app. It loads the library and checks public GitHub releases without a token. A verified SDK 0.5.1 metadata snapshot supports first launch offline.
2. Select **Create New Mod**. The app checks releases again. If a newer compatible SDK exists, choose **Install Update** or **Ignore This Time**; ignoring never disables later checks.
3. Enter the name, author, resource ID (`mods/author/mod_id`), version and optional description. The project saves immediately.
4. Open **Weapons → JAR-5 Dominator → Armor Penetration**, change **3** to **4**, and select **Add Modification**.
5. Review **Changes** and the read-only Lua preview. Edit, group, remove, enable or disable changes here.
6. Select **Build / Export Mod**, then **Open Export Folder**. Explorer selects the ZIP.
7. Close and relaunch: the project remains in the library.

The library also supports opening existing project JSON, renaming, duplication with a new resource identity, removal from the library, opening the project folder and exporting. Removal keeps project files and ZIPs. Overview lets you update the display name, author, version and description. Export lets you save a custom absolute output directory.

## Structure and architecture

```text
HD2RuntimeGUI.sln
├── HD2RuntimeGUI/                  .NET 10 Windows MAUI Blazor Hybrid host
│   ├── Components/                Razor presentation, editor and Lua preview
│   ├── Services/                  Windows Explorer and native file picker adapters
│   ├── Platforms/Windows/         WinUI host
│   └── wwwroot/                   Custom dark desktop CSS; modal keyboard handling
├── HD2RuntimeGUI.Core/             Plain net10.0, no MAUI/game dependency
│   ├── Models/                    Project/change records and semantic versions
│   ├── Services/                  Workspace use cases and SDK update decisions
│   ├── Metadata/                  Schema reader, versioned SDK cache, bundled snapshot
│   ├── Projects/                  Project creation, identity, library persistence
│   ├── Generation/                Structured changes → Lua → gameplay archive → ZIP
│   ├── Storage/                   Paths and atomic JSON writes
│   └── GitHub/                    Public release discovery and bounded downloads
├── HD2RuntimeGUI.Tests/            Offline xUnit tests; no game dependency
├── tools/HD2RuntimeGUI.Sample/     Developer sample generator and optional live SDK check
├── tools/ui-smoke.mjs             Developer-only WebView2 UI smoke test
├── samples/                       Generated JAR-5 AP4 ZIP and Lua
└── docs/                          Architecture/verification notes and actual UI captures
```

DI registers `IGitHubReleaseClient`, `IMetadataReader`, `ISdkCache`, `ISdkUpdateService`, `IProjectStore`, `IProjectService`, `IChangeService`, `ILuaGenerator`, `IModExporter`, `IFolderOpener` and `IProjectFilePicker`. `BuilderWorkspace` coordinates use cases. Razor and its code-behind contain presentation state and service calls, with no direct filesystem, GitHub, Lua-generation or ZIP logic.

## SDK cache and compatibility

The source is `https://api.github.com/repos/SkyeShade/HD2Runtime/releases`. Stable releases must contain the exact asset name `HD2Runtime-{version}-sdk.zip` in the expected repository. Discovery sorts semantic versions, inspects newer candidates without activating them, and skips unsupported schemas/APIs. Schema 1 and runtime API 1 are supported. Discovery is bounded to five pages of 100 releases and at most 20 candidate SDK inspections.

The release API has no separate schema manifest. Candidate inspection downloads the asset into a temporary ZIP and validates asset size, optional GitHub SHA-256 digest, all ZIP entry paths, duplicate names, symlinks, expanded sizes, root `metadata.json`, version, JSON structure and metadata graph. Redirects are checked before each request. Only metadata is retained; SDK scripts are never extracted or executed. Inspection is cached in memory until installation, so accepting an offered update need not redownload it.

Installation writes an immutable versioned metadata file, then atomically switches `current.json`. A failed download or validation leaves the current SDK untouched. Prior SDKs remain cached for existing projects, which stay pinned to their original version. The last successfully verified release is saved to disk. Network/rate-limit/malformed-release failures use cached information and show **Offline / not verified**. **Settings → Check for SDK updates** provides a manual check and explicit installation.

SDK 0.5.1 currently defines Weapons, Vehicles, Stratagems and Equipment. There is no Entities builder in that metadata. The sidebar and resources follow the SDK, including read-only fields, numeric controls, enum choices, switches, expected values and evidence badges. Static metadata is never presented as a read of the current game process.

## Project persistence

Default root: `%LOCALAPPDATA%\HD2RuntimeGUI\`.

```text
library.json                         Library index (formatVersion 1)
Projects/<project-guid>/project.hd2mod.json
Sdk/current.json
Sdk/last-release.json
Sdk/<sdk-version>/metadata.json
Exports/<mod-name>-<version>.zip
```

Project JSON stores identity, author, version, description, UTC creation/modification dates, SDK/API pin, output directory and a list of typed changes. Each change records domain, target key, field, expected/new JSON values, type, confidence, group, enabled state and persistence choice. Saves use a temporary sibling followed by replacement. The index is updated after the project file; a project omitted by an interrupted index update can be recovered through **Open project**.

Resource validation and deterministic manager GUID generation match the standalone HD2Runtime starter: SHA-256 of `hd2runtime-mod:` plus the resource, first 16 bytes, starter version/variant bit adjustments, .NET Guid byte order. Resource identity remains stable when the display name changes. Duplicating requires a distinct resource ID.

## Generation and export

The GUI consumes the public `contracts` in SDK metadata. In 0.5.1 these define JAR-5 AP 3→4 and the reviewed Shield Relay transitions. Other mapped fields remain inspectable, and unsupported values receive an actionable validation error. The GUI does not claim that an arbitrary numeric value is accepted by the runtime.

- A patch-capable single field generates `hd2.patch()`.
- Related fields on the same target/group generate `hd2.transaction()`.
- API 1's Shield Relay fields require a transaction even when only one is selected. The generator respects this upstream constraint.
- Persistence wraps the operation with `hd2.ensure()` and omits timing overrides, retaining the standard 60-second interval and startup behavior.
- Different targets/groups/persistence choices produce independent operations. Disabled changes are omitted; duplicate enabled target fields are rejected.

The ZIP contains `manifest.json`, `hd2runtime.json`, `build-report.json`, `README.md`, `src/addon.lua`, and one gameplay resource archive under `mod/` with empty `.stream`/`.gpu_resources` companions. It declares Bingus release 15+/API 1 and HD2Runtime minimum version/API dependencies. The loader runtime guard uses `loader.version >= 16`, exactly as the upstream starter does for Bingus release 15. No runtime implementation, SDK stubs, build tools or Python are packaged.

Entries have sorted names, fixed timestamps and stable content. Re-exporting an unchanged project produces identical ZIP bytes. An existing same-name/version ZIP is atomically replaced; increase the project version to keep separate releases.

Sample: [JAR-5 AP4 ZIP](samples/JAR-5-AP4-0.1.0.zip) and [generated Lua](samples/addon.lua).

## Build, test and publish

Development requires Windows, .NET SDK 10.0.300 or a compatible 10.0 feature band, the MAUI workload, and WebView2. NuGet access is needed for the first restore.

```powershell
dotnet workload install maui
dotnet build HD2RuntimeGUI.sln
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj

dotnet publish HD2RuntimeGUI/HD2RuntimeGUI.csproj `
  -c Release -f net10.0-windows10.0.19041.0 -r win-x64 `
  --self-contained true -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None -o artifacts/publish/win-x64
```

Run the Debug EXE in `HD2RuntimeGUI/bin/Debug/net10.0-windows10.0.19041.0/win-x64/`, or use Rider's Windows launch profile. Close the application before rebuilding the same output directory.

Regenerate the sample or explicitly exercise public release download/installation:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/sample-workspace
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/live-release-check --online
```

These developer helpers are excluded from the shipped application. See [verification notes](docs/verification.md) for test coverage, desktop smoke steps, screenshots and practical limits.
