# Upstream contracts and attribution

HD2Runtime ModBuilder ([SkyeShade/HD2Runtime-ModBuilder](https://github.com/SkyeShade/HD2Runtime-ModBuilder); executable and data-folder name: HD2RuntimeGUI) is a separate project by SkyeShade. It uses public SDK metadata and generates dependent gameplay mods for [SkyeShade/HD2Runtime](https://github.com/SkyeShade/HD2Runtime).

- `HD2RuntimeGUI.Core/Metadata/Bundled/` comes unchanged from the verified [HD2Runtime 0.27.0 SDK release](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.27.0) (`HD2Runtime-0.27.0-sdk.zip`, SHA-256 `aad2330fa6251b37639fa1671ba191699498bf97367cdccfe7ad9d5b1a180808`). It is authoring metadata, not runtime implementation.
- Resource-ID validation, manager GUID derivation, the single-resource archive writer, manager metadata and dependency wrapper follow HD2Runtime's public `starter/build.ps1` contract. The adjacent source checkout inspected during implementation was commit `361a7c5d056cbcee72300ab857c87de882badea0`. The archive writer is binary file packaging only.
- MAUI, ASP.NET Core Blazor WebView and Microsoft logging are referenced as NuGet packages. xUnit and the Microsoft test SDK are development-only dependencies.
- The HD2Runtime ModBuilder wordmark uses Big Shoulders Display (Latin subset, `HD2RuntimeGUI/wwwroot/brand/fonts/`), licensed under the SIL Open Font License 1.1 (`BigShouldersDisplay-OFL.txt`).
- The initial project supplied the Open Sans font in `Resources/Fonts`; it is used by the MAUI host font registration.
- The Simplified Chinese interface translation (`HD2RuntimeGUI.Core/Resources/Strings/Strings.zh-Hans.resx`) originated from the i18n bundle contributed by [@CChusky](https://github.com/CChusky) in [issue #1](https://github.com/SkyeShade/HD2Runtime-ModBuilder/issues/1) (string inventory and translation of the v1.3.0 interface). Entries marked `issue #1` in that file are the contributor's text; see [docs/localization.md](docs/localization.md).

No HD2Runtime memory safety engine, resolver, scanner, runtime Lua implementation, SDK scripts, game addresses or game process access code was copied into the ModBuilder's application services or generated mods.
