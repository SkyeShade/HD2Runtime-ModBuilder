# ModBuilder 1.4.0 release candidate: manual check (10-20 minutes)

Start **`Launch ModBuilder RC (test data).cmd`** in `build/release-candidate-1.4.0/`. It uses its own `test-data` folder, so your projects and SDK cache are untouched. Tick each line; note anything odd next to it. No step should ever ask you to tick an acknowledgement: opt-ins are shown as warnings only.

## Start
1. [ ] ModBuilder starts; the sidebar footer shows **SDK 0.28.0**.
2. [ ] **Settings & SDK** (bottom of the sidebar): version **1.4.0**; installed SDK **0.28.0**; supported up to **0.28.0**.
3. [ ] **Project library → Create New Mod**, name it `RC Check`. The overview shows SDK 0.28.0.

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
26. [ ] The ZIP's `hd2runtime.json` has `requires.hd2runtime.min_version` **0.28.0**.
