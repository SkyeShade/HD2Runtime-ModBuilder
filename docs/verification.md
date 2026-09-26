# Verification

Verified on Windows with .NET SDK 10.0.300 and MAUI 10.0.20.

## Automated tests

60 xUnit cases pass without network access or a running game. Coverage includes:

- Public GitHub release parsing, filtering, semantic-version ordering, request headers/no token, repository validation, bounded streams, redirect rejection before contact.
- Current versus newer SDK, latest compatible selection, one-time ignore semantics, repeated checks, successful installation, retained historical SDK, failed download/digest/schema/version preserving the previous SDK, offline cached release and offline first launch.
- Unsupported schema/API, malformed/duplicate JSON, unsafe Lua identifiers, SDK path traversal and malicious ZIP entries.
- Resource ID validation, project creation, JSON persistence/reload, duplicate identity, rename, removal preserving files and reopening removed projects.
- Metadata-derived JAR-5 change, patch generation, transaction generation, transaction-only single fields, ensure defaults, disabled changes, duplicate fields and unsupported/injected values.
- Deterministic ZIP bytes, exact ZIP inventory, dependency declarations, gameplay archive header/resource identity, no runtime implementation/stubs, and folder-opening abstraction.
- Complete create/change/preview/export/open-folder/reload sequence through application services.

`dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj`

## Live release check

The real C# GitHub and SDK services successfully discovered and installed public release 0.5.1 using no token. The published SDK asset is `HD2Runtime-0.5.1-sdk.zip`, 26,610 bytes, SHA-256:

`2b92d12a7f97f87b4e24babb715aa7a60f7954900a82bd6ea08b2e009b54b1e7`

The embedded `metadata.json` is the byte-for-byte metadata from that verified release. Its SHA-256:

`8f259710d63a3333800e3ea9ec129772e20c4814fd582a0557e1ba9f2cc971d5`

## Desktop verification

The actual Debug MAUI application was driven through its WebView2 debugging protocol using an isolated data root under `artifacts/ui-smoke`. The following passed:

1. Launch and public release check.
2. Create JAR-5 AP4 and persist the project.
3. Select Weapons, JAR-5 Dominator and metadata-supplied Armor Penetration.
4. Enter 4 for the SDK baseline of 3 and add the modification.
5. Inspect the saved summary and generated Lua.
6. Build the gameplay ZIP and invoke Open Export Folder successfully.
7. Close/relaunch the executable, find the project card, reopen it, and verify the persisted modification.

A second isolated desktop library used a metadata fixture labelled 0.5.0 to exercise the update screen against the real 0.5.1 release. The UI showed the warning, **Ignore This Time** continued with 0.5.0, the next create action warned again, and **Install Update** verified/installed 0.5.1 and continued into its creation form. The 0.5.0 fixture is synthetic; no claim is made that it is a published SDK.

Screenshots are captures from the actual MAUI WebView, rather than design mockups:

- [Library](screenshots/library.png)
- [Create project](screenshots/create-project.png)
- [Weapon editor](screenshots/weapon-editor.png)
- [Changes and Lua](screenshots/changes.png)
- [Export complete](screenshots/export.png)
- [SDK update warning](screenshots/sdk-update.png)

The restricted agent launch did not initialize the embedded UI. A normal launch outside that restriction did. Rider was attached during triage and then detached without changing user breakpoints or application startup behavior. No app runtime defect was established from that restricted launch.

To reproduce the optional desktop smoke with Node 22+ (not needed by the application): set `HD2RUNTIMEGUI_DATA_ROOT` to an isolated empty directory, `WEBVIEW2_USER_DATA_FOLDER` to an isolated browser profile, and `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223` before launching a Debug build. Run `node tools/ui-smoke.mjs`, optionally `node tools/ui-smoke.mjs --open-export`, then close/relaunch and run `node tools/ui-smoke.mjs --relaunch`. `HD2GUI_CDP_PORT` changes the smoke script's port. Never set the debugging port in a distributed build. The data-root override is compiled only in Debug.

## Packaging parity and limitations

The C# gameplay archive writer was compared against `starter/build.ps1` from the adjacent read-only HD2Runtime checkout at commit `361a7c5d056cbcee72300ab857c87de882badea0`. A temporary copy of that standalone script generated identical archive bytes for the same Lua payload. Its `guid="auto"` build independently produced the same manager GUID, `707295c4-83e8-5c66-ad03-a648c82fb693`, for `mods/skyeshade/jar5_ap4`; this is now a regression assertion. The upstream repository was not changed. No Python was used.

Both the Windows Debug build and a self-contained Windows x64 Release publish were produced. The publish includes .NET and Windows App SDK. WebView2 Evergreen Runtime is an end-user prerequisite. There is no installer, code signing or auto-update system for the GUI itself in this milestone.

Game execution, deployment and live-memory behavior were intentionally outside verification. Runtime compatibility is grounded in published metadata and starter packaging contracts; no claim of in-game execution is made. Future schema/API versions are rejected or skipped until a compatible GUI adapter is added. Existing projects require their pinned metadata version in the local cache; moving a project between machines may require copying that version's `Sdk/<version>/metadata.json` alongside the project library.
