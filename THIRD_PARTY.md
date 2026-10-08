# Upstream contracts and attribution

HD2Runtime ModBuilder ([SkyeShade/HD2Runtime-ModBuilder](https://github.com/SkyeShade/HD2Runtime-ModBuilder); executable and data-folder name: HD2RuntimeGUI) is a separate project by SkyeShade. It uses public SDK metadata and generates dependent gameplay mods for [SkyeShade/HD2Runtime](https://github.com/SkyeShade/HD2Runtime). ModBuilder's own material is licensed under [CC BY-NC-SA 4.0](LICENSE); its names and brand assets are not (see [TRADEMARKS.md](TRADEMARKS.md)). The items below keep their own terms.

- `HD2RuntimeGUI.Core/Metadata/Bundled/` comes unchanged from the [HD2Runtime 0.30.0 SDK release](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.30.0) (`HD2Runtime-0.30.0-sdk.zip`, release candidate commit `893d31f`, SHA-256 `a6875afbd144f3874d72e5b28c4baad4adf5671e991b4af5e6a32f1d3075ca42`). So do the custom content target files (`CustomContent/Targets/`: `CustomStratagemSchema.json`, `ModelBaseCapabilities.json` and the LuaLS stub), the two Runtime example templates in `CustomContent/Templates/` and `Scripting/Bundled/WeaponSoundCatalogue-0.30.0.json`. It is authoring metadata, not runtime implementation.
- Resource-ID validation, manager GUID derivation, the single-resource archive writer, manager metadata and dependency wrapper follow HD2Runtime's public `starter/build.ps1` contract. The adjacent source checkout inspected during implementation was commit `361a7c5d056cbcee72300ab857c87de882badea0`. The archive writer is binary file packaging only.
- MAUI, ASP.NET Core Blazor WebView and Microsoft logging are referenced as NuGet packages. xUnit and the Microsoft test SDK are development-only dependencies.
- The HD2Runtime ModBuilder wordmark uses Big Shoulders Display (Latin subset, `HD2RuntimeGUI/wwwroot/brand/fonts/`), licensed under the SIL Open Font License 1.1 (`BigShouldersDisplay-OFL.txt`).
- `HD2RuntimeGUI.Core/GameAssets/Bundled/HudIconSprites.json` comes unchanged from the HD2Runtime SDK (`sdk/HudIconSprites.json`). The atlas sprite reader (`GameAssets/HudIcons.cs`) is a C# port of the HD2Runtime SDK's `tools/hd2_hud_icons.py`, and its stratagem recolouring follows the HD2R Editor's `tools/game_icons.py`, both by SkyeShade. The icons are read from the user's own game install and are never shipped.
- The initial project supplied the Open Sans font in `Resources/Fonts`; it is used by the MAUI host font registration.
- **Localization.** Special thanks to [@CChusky](https://github.com/CChusky) for proposing ModBuilder's localization support and providing the original UI string inventory and Simplified Chinese translation baseline. In [issue #1](https://github.com/SkyeShade/HD2Runtime-ModBuilder/issues/1) they:
  - requested first-class i18n support;
  - supplied the original v1.3.0 UI string inventory (576 strings);
  - supplied the initial zh-Hans translation resource set;
  - supplied the English/Chinese review material that became the translation baseline.

  In `HD2RuntimeGUI.Core/Resources/Strings/Strings.zh-Hans.resx`, entries whose comment is `issue #1` or `issue #1 (adapted)` come from that material. Because of that contribution, `Strings.zh-Hans.resx` is excluded from ModBuilder's CC BY-NC-SA 4.0 license (see [LICENSE](LICENSE)); it ships with ModBuilder with the contributor's permission. The ModBuilder 1.4.0 interface has 2,252 keys: strings added or reworded since v1.3.0 were translated or adapted during implementation and are marked `new: needs native review`. See [docs/localization.md](docs/localization.md).

No HD2Runtime memory safety engine, resolver, scanner, runtime Lua implementation, SDK scripts, game addresses or game process access code was copied into the ModBuilder's application services or generated mods.
