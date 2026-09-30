# ModBuilder 1.4.0 release candidate: manual check (10-20 minutes)

Start **`Launch ModBuilder RC (test data).cmd`** in `build/release-candidate-1.4.0/`. It uses its own `test-data` folder, so your projects and SDK cache are untouched. Tick each line; note anything odd next to it.

## Start
1. [ ] ModBuilder starts; the sidebar footer shows **SDK 0.28.0**.
2. [ ] **Settings & SDK**: version **1.4.0**; installed SDK **0.28.0**, supported up to **0.28.0**; Application updates shows 1.4.0 as current.
3. [ ] **Project library → Create New Mod**: name it `RC Check`. The overview shows SDK 0.28.0.

## Weapons
4. [ ] **Player Weapons → JAR-5 Dominator**: change a damage or stat value; it turns modified and autosaves.
5. [ ] **Player Weapons → MA5C Assault Rifle → Projectile**: *Penetration slowdown* is editable; the shared-row warning explains `allow_shared` (no checkbox).
6. [ ] **Player Weapons → AR-23 Liberator → projectile output**: choose **LAS-58 Talon**. The package and the opt-in warnings are shown; nothing needs ticking.
7. [ ] **Stratagems → Support → M-105 Stalwart** (or EAT-17): swap its projectile (for example APW-1 on the Stalwart, PLAS-1 Scorcher on the EAT-17). Live-proven pairs carry no unverified warning.
8. [ ] **Stratagems → Support → MG-206 Heavy Machine Gun → Rate of fire & functions**: set the X / Y / Z rates (for example 450 / 600 / 750); the default mode and selector order are shown.
9. [ ] Same weapon, **programmable ammo**: choose **Incendiary** (R-4 Hyena), **Stun** (AR-32 Pacifier) or **Gas** (P-35 Re-Educator); the binding input is shown and the labels can be set.
10. [ ] **Stratagems → Support → S-11 Speargun → programmable ammo**: choose the **Stun** spare twin (Gas stays the base projectile); set the primary and alternate labels, icon **auto**.

## Projectile builder
11. [ ] Open a projectile row (for example the **LAS-58 Talon** row) and set its **impact explosion** to the GL-21 grenade blast. The slot shows what it changes and is written to the row, not to the Liberator.
12. [ ] On the Liberator (now firing Talon), confirm the editor says the slot belongs to the **donor row** and names the other weapons that fire it (sharing warning).
13. [ ] **Player Weapons → AR/GL-21 One-Two**: the **underbarrel launcher** is its own nested section. Change the **spread** and the **spare / starting grenades**; the rifle's own fields are unchanged.
14. [ ] **Stratagems → Support → EXO-45 Patriot Exosuit → right gun**: swap its projectile to **EAT-17** (live-proven: no unverified warning).
15. [ ] On the Patriot, open the gun's **bullet row** and set an **impact explosion**; the editor distinguishes this host row from a donor row and shows the number of entities firing it.

## Equipment and scripting
16. [ ] **Stratagems → Mission → Resupply**: change the cooldown and one payload slot.
17. [ ] **Stratagems → Support → SH-51 Directional Shield** (or SH-32 / Warp Pack / Hover Pack): change a shield or pack value.
18. [ ] **Stratagems → Support → AX/AR-23 Guard Dog**: change the drone's health and its gun's magazine.
19. [ ] **Scripting**: insert an event snippet (for example *player died → explosion*) from the event reference; the new events **player hit** and **player damage dealt** are listed.
20. [ ] **Custom Lua**: the editor shows syntax highlighting, autocomplete (type `hd2.`), and a syntax error when you type `local x =` on its own line. Remove it again.

## Language
21. [ ] **Settings → Language → 简体中文** while `RC Check` has an unsaved custom Lua edit: the interface switches, the project and the draft stay.
22. [ ] Browse Player Weapons and Stratagems in Chinese; switch back to **English**.

## Export
23. [ ] **Changes** lists every edit above; **Lua Preview** wraps each operation in `add(function() return ... end)`.
24. [ ] **Build / Export**: the ZIP is written.
25. [ ] In Lua Preview, the edits carry `allow_shared` / `allow_unverified_effect` / `allow_unverified_reference` only where the warnings said so.
26. [ ] Open the ZIP's `hd2runtime.json`: `requires.hd2runtime.min_version` is **0.28.0**.
