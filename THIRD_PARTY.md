# Upstream contracts and attribution

HD2Runtime ModBuilder (repository and executable name: HD2RuntimeGUI) is a separate project by SkyeShade. It uses public SDK metadata and generates dependent gameplay mods for [SkyeShade/HD2Runtime](https://github.com/SkyeShade/HD2Runtime).

- `HD2RuntimeGUI.Core/Metadata/Bundled/metadata.json` comes unchanged from the verified [HD2Runtime 0.5.1 SDK release](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.5.1). It is authoring metadata, not runtime implementation. Hashes are recorded in `docs/verification.md`.
- Resource-ID validation, manager GUID derivation, the single-resource archive writer, manager metadata and dependency wrapper follow HD2Runtime's public `starter/build.ps1` contract. The adjacent source checkout inspected during implementation was commit `361a7c5d056cbcee72300ab857c87de882badea0`. The archive writer is binary file packaging only.
- MAUI, ASP.NET Core Blazor WebView and Microsoft logging are referenced as NuGet packages. xUnit and the Microsoft test SDK are development-only dependencies.
- The HD2Runtime ModBuilder wordmark uses Big Shoulders Display (Latin subset, `HD2RuntimeGUI/wwwroot/brand/fonts/`), licensed under the SIL Open Font License 1.1 (`BigShouldersDisplay-OFL.txt`).
- The initial project supplied the Open Sans font in `Resources/Fonts`; it is used by the MAUI host font registration.

No HD2Runtime memory safety engine, resolver, scanner, runtime Lua implementation, SDK scripts, game addresses or game process access code was copied into the GUI's application services or generated mods.
