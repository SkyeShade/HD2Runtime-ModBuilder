# HD2Runtime ModBuilder: upcoming release (draft)

> **Draft, not released.** These notes collect the work since 1.3.1 for the release that adds **HD2Runtime SDK 0.28.0**. The version
> number, download name and SDK compatibility pin are set when the release is cut, after HD2Runtime 0.28.0 is published. Until then
> this work runs only against a local development SDK.

## New with SDK 0.28.0

### Enemies and structures
- **Enemies** and **Structures** pages (`hd2.enemy` / `hd2.structure`), with faction filter and search by name, native class, wiki name or attack.
- Each class has main health and armor, its body damage zones, and its attacks by mount slot, including their projectile and explosion rows.
- Shared rows and unverified effects are shown as warnings next to the edit.

### Custom Lua and event scripting
- A project can carry its own `src/addon.lua`, built into the mod after ModBuilder's generated changes.
- The editor has syntax highlighting, autocomplete from the SDK, a Lua syntax check, and warnings for unknown event, explosion, projectile or status names.
- **Open in editor**, **Open folder** and **Copy path** work with any external editor. Outside edits are detected and never overwritten without asking.
- The side panel has an event reference, pickers for explosions, projectiles, statuses and healing, and ready-to-insert snippets.

### Projectile donors through the active projectile source
- The projectile picker follows the member each shot actually reads. The AR-23 Liberator, JAR-5 Dominator, R-63 Diligence, SG-225 Breaker, P-2 Peacemaker and P-19 Redeemer write their ammunition's projectile.
- Runtime's proven pairs are marked, such as Liberator × EAT-700 and Liberator × GL-52. Cross-class donors show the opt-ins they carry, and refused donors show Runtime's reason.
- Support weapons, vehicle mounts and the Guard Dog drone gun swap their projectile from the same donor pool, on their own pages. Read-only hosts show why (another selector, a weapon function, not magazine-fed, …).
- **Projectile builder:** a projectile row's direct hit, impact and expiry explosion take another row's, and its weapon-function mode label and icon take a native value. A row write changes every weapon that fires the row and never follows a swap; after a swap, the donor's row is one click away, with who else fires it.

### Stratagems, backpacks and weapons
- **Resupply** is under the new **Mission** category, with cooldown, uses and its shared drop pod. Grenade Box is marked as live-verified in its slots.
- The **SH-20 Ballistic Shield** is armored through its shield damage zone. Its inactive default armor is read-only and names the field that takes effect.
- The **SG-20 Halt** writes each feed with its branch-qualified field. This also fixes Halt edits failing on HD2Runtime 0.27.0.
- Published value limits are enforced and explained, for example the 1023 limit on backpack ammo.
- New sentry, minefield, support and player weapon fields appear automatically.

### Developer
- **Settings & SDK → Developer** can remember a local HD2Runtime SDK for testing unreleased Runtime builds. It is never cached or packaged.

## Languages
- The interface is available in **English** and **简体中文 (Simplified Chinese)**. Choose it in **Settings & SDK → Language**: System default, English or 简体中文.
- Switching is immediate and keeps your open project, unsaved edits and SDK.
- Generated Lua, project files and exported mods are identical in every language.
- Game names, SDK text, identifiers and Lua stay as published.
- Translations are standard `.resx` resource files, so adding a language needs no code changes. See [docs/localization.md](../localization.md).

## Compatibility
- Projects from earlier versions open unchanged. Enemy edits save the project in format 10; custom Lua and attack outputs save it in format 11. Older ModBuilder versions refuse these formats rather than dropping edits.
- The language choice is stored in `preferences.json` in `%LOCALAPPDATA%\HD2RuntimeGUI\`, next to your projects and settings.

## Known limits
- Needs HD2Runtime 0.28.0, which is not released yet.
- The weapon-composition work still to come: multi-RPM fire modes, alternate ammunition and feeds, cross-family beam/arc/spray outputs, and armory labels.
- Some Chinese text was written during implementation and still awaits review by a native speaker. Text published by the SDK stays in English.

## Thanks
Special thanks to [@CChusky](https://github.com/CChusky) for proposing ModBuilder's localization support and providing the original UI string inventory and Simplified Chinese translation baseline ([issue #1](https://github.com/SkyeShade/HD2Runtime-ModBuilder/issues/1)).

Their inventory and zh-Hans resources covered the v1.3.0 interface. Text added or reworded since then was translated or adapted during implementation, following their terminology.
