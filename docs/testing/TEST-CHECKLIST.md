# ModBuilder 1.6.0 release candidate: manual check (10-20 minutes)

Start **`Launch ModBuilder RC (test data).cmd`** in `build/release-candidate-1.6.0/`. It uses its own `test-data` folder, so your projects and SDK cache are untouched. Tick each line; note anything odd next to it. No step should ever ask you to tick an acknowledgement: opt-ins are shown as warnings only.

## Start
1. [ ] ModBuilder starts; the sidebar footer shows **SDK 0.28.1**.
2. [ ] **Settings & SDK** (bottom of the sidebar): version **1.6.0**; installed SDK **0.28.1**; supported up to **0.28.1**.
3. [ ] **Project library → Create New Mod**, name it `RC Check`. The overview shows SDK 0.28.1.

## Weapons
4. [ ] **Player Weapons → JAR-5 Dominator**: change a damage or stat value; the row turns modified and autosaves.
5. [ ] **Player Weapons → MA5C Assault Rifle → Projectile**: *Penetration slowdown* is editable; its warning explains the shared projectile row (`allow_shared`).
6. [ ] **Player Weapons → AR-23 Liberator → Primary → projectile output**: choose **LAS-58 Talon**. It shows its package and opt-in warnings.
7. [ ] **Projectile builder → Hosts → EAT-17 Expendable Anti-Tank**: choose **PLAS-1 Scorcher** (live-proven: no warnings). Choosing **EAT-411 Leveller** instead shows the unverified warning. Leave Scorcher.
8. [ ] **Stratagems → Support → MG-206 Heavy Machine Gun → Rate of fire & functions**: set Z to **900**; the default mode (Y) and the selector order are shown.
9. [ ] Same panel, **programmable ammunition**: the **Base mode** card (MG-206, *Vanilla / default*) and an empty **Alternate mode** card. Click **Add alternate mode**, search **Hyena** and choose **R-4 Hyena** (*Live-proven*: no warnings). Each card has its own *Mode label and icon*.
10. [ ] **Stratagems → Support → S-11 Speargun → Add alternate mode**: choose the **spare twin** (Stun); in the Alternate card set the label to *STUN* and the icon to **auto**.
    - [ ] **AC-8 Autocannon**: the Alternate card shows the native **FLAK** mode, with no Remove button. **Change projectile** to another donor, then **Restore native projectile**.
    - [ ] **Player Weapons → AR-23 Liberator → Add alternate mode**, choose **AR-23C Liberator Concussive**, then **Remove alternate mode**. The card goes back to *No alternate mode configured.*, and Lua Preview has no `function_ammo`.

## Projectile builder
11. [ ] **Projectile builder → Rows → LAS-58 Talon**: set **Impact explosion** to the **GL-21 Grenade Launcher** blast. The row shows who fires it; the Liberator is listed as firing it through its swap.
12. [ ] Back on **Player Weapons → AR-23 Liberator**, open the Talon row from the projectile output (**donor row**): it says a slot on a donor row changes every weapon firing that row and does not follow the swap.
13. [ ] **Player Weapons → AR/GL-21 One-Two**: the **underbarrel** is its own section below the rifle. Change the launcher's **horizontal spread** and **spare rounds**; the rifle's own fields are unchanged.
14. [ ] **Projectile builder → Hosts → EXO-45 Patriot Exosuit / right_gun**: choose **EAT-17** (live-proven: no warnings).
15. [ ] **Projectile builder → Rows → EXO-45 Patriot Exosuit / right_gun** (the mount's own bullet row): set **Impact explosion** to the GL-21 blast. It is marked as the host row, fired by several entities (`allow_shared` warning), and separate from the EAT-17 the mount now fires.

## Equipment and scripting
16. [ ] **Stratagems → Mission → Resupply**: change the cooldown and one drop-pod payload slot.
17. [ ] **Stratagems → Support → SH-51 Directional Shield** (or SH-32 / LIFT-182 Warp Pack / LIFT-860 Hover Pack): change a shield or pack value.
18. [ ] **Stratagems → Support → AX/AR-23 Guard Dog**: in the drone section change the drone's **health**, and its gun's **magazine**.
19. [ ] **Custom Lua → Add custom Lua**, then **Reference**: **player_hit** and **player_damage_dealt** are listed; **Insert handler** on player_hit, then **Save**.
20. [ ] In the editor type `hd2.` to see completion, then a lone `local x =` line: a syntax error appears. Remove it and save.

## Language
21. [ ] **Settings & SDK → Language → 简体中文** while `RC Check` has an unsaved custom Lua edit: the interface switches; the project and the draft stay.
22. [ ] Open the MG-206 functions panel and the One-Two underbarrel in Chinese; switch back to **English**.

## Export
23. [ ] **Changes** lists every edit above; **Lua Preview** wraps each operation in `add(function() return ... end)`.
24. [ ] **Build / Export**: the ZIP is written.
25. [ ] In Lua Preview, `allow_shared` / `allow_unverified_effect` / `allow_unverified_reference` appear only where the editors showed those warnings.
26. [ ] The ZIP's `hd2runtime.json` has `requires.hd2runtime.min_version` **0.28.1**.

## Additional files (1.5.0)
27. [ ] **Build / Export → Additional files → Add file…**: pick any image (for example a `thumbnail.png`). It is listed with its full path and **Path in ZIP** `thumbnail.png`.
28. [ ] Change the path to `images\thumbnail.png` and leave the field: it becomes `images/thumbnail.png`. Then type `../thumbnail.png`, then `README.md`: the row shows why each is refused, and **Build / Export** is refused with a message naming the file. Set it back to `images/thumbnail.png`.
29. [ ] **Build / Export**: the ZIP contains `images/thumbnail.png`, identical to your file, next to the usual files.
30. [ ] Rename or move the image on disk, then **Build / Export**: the export is refused with *Cannot package additional file: thumbnail.png* / *Source file does not exist: …*; the row says the same. Put the file back and export again.
31. [ ] Close and reopen the project from the library: the file is still listed. **×** removes it; the next export contains only the generated files.

## Arsenal presentation (1.5.0)
32. [ ] **Build / Export → Arsenal presentation**: type a two-line **Arsenal description** and click outside the box; the section shows *Configured*. Type `<b>x</b>` instead: the section says Arsenal would remove the tag and **Build / Export** is refused; restore the plain text.
33. [ ] **Select image…** (the picker offers PNG, JPG, GIF and WebP): pick an image. It shows *In the ZIP as <name> (IconPath)*.
34. [ ] **Build / Export**: the ZIP has the image at its root, and `manifest.json` has your description, then the dependency line, and `"IconPath": "<name>"`.
35. [ ] Optional, with HD2Arsenal installed: add the ZIP in Arsenal. The library shows your icon and description, and deploying installs only the `mod/` files.
36. [ ] Move the image away: the section reports it and the export is refused naming the icon. Put it back; **×** removes the icon (the image is not deleted).

## In-game options without a value change (new in 1.6.0)
37. [ ] **Overview → In-game options**: switch them on. Open **Stratagems → Support → TD-110 Maelstrom** and point at the missile pod's (**slot_3**) *Standard damage* row without editing it: **Expose as option** appears (it is hidden until you point at or tab into the row).
38. [ ] Click it: the editor says *Vanilla 1100, not edited: only the in-game option changes it*; the slider defaults to **1100**. **Cancel**: nothing is added (the row stays unedited, **Changes** shows no new edit).
39. [ ] Click it again and **Save option**: the row still reads *Base 1100* (no Modified badge) and shows the **In-game option** badge. **Lua Preview** has `local option_…=options:slider({…default=1100})` and the slot_3 operation inside `hd2.ensure({ enabled=enabled, …` with `expect=1100` and `value=option_…`, plus `allow_shared=true`.
40. [ ] Expose the pod's **Capacity** the same way, then try to expose **slot_4**'s *Capacity*: the option editor refuses it, naming slot_3, and nothing is saved.
41. [ ] Type **1200** into the exposed damage field, then **1100** again: the field keeps its **In-game option** badge. Open the badge → **Stop exposing**: the edit goes too; Lua Preview no longer has the slot_3 damage operation.
42. [ ] Close and reopen the project with an exposed unedited field: it is still exposed. **Build / Export**: `hd2runtime.json` lists `mod_options_menu` under `optional`.
43. [ ] Optional, in game with Mod Options Menu: the slider starts at vanilla and changing it changes the missile damage; without the menu the missiles stay vanilla.
