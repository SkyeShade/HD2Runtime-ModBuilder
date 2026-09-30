<p align="center"><img src="HD2RuntimeGUI/wwwroot/brand/modbuilder-icon.svg" width="96" alt="HD2Runtime ModBuilder reticle icon"></p>

# HD2Runtime ModBuilder

**Visual authoring for [HD2Runtime](https://github.com/SkyeShade/HD2Runtime) mods.**

HD2Runtime ModBuilder is a Windows app for building Helldivers 2 gameplay mods on top of HD2Runtime, without writing Lua or knowing memory layouts. Pick what to change, set the value, review it, and build a mod ZIP.

- **Edit weapons, stratagems, vehicles, backpacks and Boosters.** Player Weapons; Stratagems split into Support (support weapons, vehicles and backpacks), Offensive and Defensive; and Boosters. Fields are grouped in compact editors with the base value next to yours.
- **Guarded Runtime Lua, generated for you.** Every change becomes guarded HD2Runtime calls (`hd2.patch`, `hd2.transaction`, `hd2.plan`, `hd2.ensure`) with the expected base value, so a mod refuses to write when the game data does not match. Shared or unproven writes are flagged with a warning, and the Lua always carries the opt-in flag Runtime requires (`allow_shared`, `allow_unverified_effect`, `allow_unverified_reference`); nothing needs to be acknowledged by hand.
- **SDK-driven.** Everything you can edit comes from the published HD2Runtime SDK metadata, never from a hand-kept list. Projects stay bound to their SDK version until you choose to rebind, and the app keeps the SDK up to date from HD2Runtime's releases.
- **Review, then Build / Export.** Changes lists every edit; Lua Preview shows the exact generated source; Build / Export writes a mod-manager-ready ZIP.
- **Your own game icons.** Stratagem and booster icons are read from your local Helldivers 2 installation and stored on your PC. No game artwork is shipped.
- **Optional in-game Mod Options.** Expose edited values as sliders and choices, with one master toggle, in the game's MODS menu (CowboyBingus Mod Options Menu, HD2Runtime SDK 0.25+).
- **Updates itself.** New ModBuilder releases are offered in the app and installed with one click.

## Download

1. Download **`HD2Runtime-ModBuilder-v1.3.1-win-x64.zip`** from the [latest release](https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/latest).
2. Extract it to a folder you can write to (for example `Documents\HD2Runtime ModBuilder`).
3. Run **`HD2RuntimeModBuilder.exe`**.

The ZIP also contains `HD2RuntimeGUI.exe`, an identical copy kept so that HD2Runtime ModBuilder 1.0.0 (which was `HD2RuntimeGUI.exe`) can update itself. It starts the same app; you can ignore or delete it.

Windows 10/11 x64 with the Microsoft Edge WebView2 Runtime (included with Windows 11) is required; .NET is bundled. Install [HD2Runtime](https://github.com/SkyeShade/HD2Runtime) and its dependencies to use the mods you build. The executable is unsigned, so SmartScreen may warn on first launch.

Your projects, SDK cache, imported icons and settings live in `%LOCALAPPDATA%\HD2RuntimeGUI\`, outside the app folder, so updating or moving the app never touches them.

### Updating

HD2Runtime ModBuilder checks [GitHub Releases](https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases) when it starts and every 10 minutes while it runs. When a newer version exists, a banner offers **Install update**: the app downloads the release, verifies its SHA-256, closes, replaces its own files and restarts. **Settings → Application updates** shows the current and latest version and has **Check for updates**. If the app folder is not writable, it links to the release page instead. See [the update design](docs/app-updates.md).

Updating 1.0.0 to 1.0.1 or later replaces `HD2RuntimeGUI.exe` with `HD2RuntimeModBuilder.exe`. A taskbar pin or shortcut to the old exe needs to be pinned again.

Release notes: [1.3.1](docs/release-notes/v1.3.1.md) · [1.3.0](docs/release-notes/v1.3.0.md) · [1.2.0](docs/release-notes/v1.2.0.md) · [1.1.2](docs/release-notes/v1.1.2.md) · [1.1.1](docs/release-notes/v1.1.1.md) · [1.1.0](docs/release-notes/v1.1.0.md) · [1.0.1](docs/release-notes/v1.0.1.md) · [1.0.0](docs/release-notes/v1.0.0.md).

## Why "HD2RuntimeGUI" still appears

The product was renamed from HD2RuntimeGUI to HD2Runtime ModBuilder, and the repository moved to [SkyeShade/HD2Runtime-ModBuilder](https://github.com/SkyeShade/HD2Runtime-ModBuilder). Since 1.0.1 the application files are `HD2RuntimeModBuilder.*`. A few technical identifiers keep the old name so existing installations and projects continue to work:
- the data folder `%LOCALAPPDATA%\HD2RuntimeGUI\`;
- the ApplicationId `dev.skyeshade.hd2runtimegui`;
- the project format;
- the source project/namespace names;
- the `HD2RuntimeGUI.exe` compatibility copy in the release ZIP.

---

## For developers

```powershell
git clone https://github.com/SkyeShade/HD2Runtime-ModBuilder.git
```

A Windows-first .NET 10 MAUI Blazor Hybrid app. Version 1.3.1 supports **HD2Runtime SDK 0.27.0** (throwables and automatic asset loading for reference swaps) and **0.26.0** (every magazine option, third-person reticles, fire-mode lists, mounted vehicle weapons, mission uses, backpack-fed support ammunition and drop-pod contents), **0.25.1** (stratagem icons, in-game Mod Options) and the **0.24 guarded booster, vehicle, backpack, magazine-attachment, shield-relay, defensive and offensive stratagem, support/player-weapon, heat, ammo and composition-plan APIs**; older projects stay pinned to their SDK.

The published `PlayerWeaponAuthoringCapabilities.json` drives player-weapon identities, controls, defaults, evidence and permissions: **80 weapons, 3,814 entries, 104 field definitions (84 writable, 20 read-only, 9 derived)** in SDK 0.27.0. There is no manually maintained weapon/field catalog in the UI. The nine derived definitions are included in the read-only count.

This repository is separate from HD2Runtime. The app does not launch the game, deploy mods, or access game processes. Snapshot inspection reads only a user-selected local file.

## Navigation

The sidebar lists only public authoring destinations: **Overview, Player Weapons, Stratagems** (Support · Offensive · Defensive), **Boosters** (SDK 0.24.0+), **Changes, Lua Preview, Build / Export**, with **Snapshot Research** in a separate Developer / Research group. Legacy mapped-resource categories (the old mapped Vehicles and Equipment resources and legacy mapped stratagems) are no longer navigation destinations. Vehicles and backpacks live under **Stratagems → Support**, which has All Support, Support Weapons, Vehicles, Backpacks and Other / Standalone tabs built from SDK families and published call-in links; a vehicle or backpack stratagem opens one editor with its call-in and the delivered entity. Existing legacy changes still appear on Changes and can be edited, toggled or removed there. Navigation stays visible without a project; project destinations then show "Select or create a project to begin editing." Counts come from the installed SDK; the Changes count appears only with an open project.

Stratagem categories come from published families: Support (`support`, blue), Offensive (`orbital`, `eagle`, red) and Defensive (`sentry`, `emplacement`, `mine`, including the Shield Generator Relay, green). Boosters are their own domain and use yellow. Reusable `cat-support` / `cat-offensive` / `cat-defensive` / `cat-booster` classes set a `--cat` token used by pills, dots, list accents and panel headers.

Support call-ins and their support weapons are shown as one entry when HD2Runtime publishes a structural link (SDK 0.22.1+, `hd2runtime.support_callin_linkage.v1`). ModBuilder joins `weapons[].semanticId` ↔ `stratagems[].semanticId` through `linkedStratagem`, `delivers` and `supportCallInLinks.relationships`, and requires the forward link, reverse link, relationship and audit to agree; any mismatch rejects the SDK. Display names are never link evidence. For the 32 linked pairs, Stratagems → Support shows the call-in controls (cooldown; max uses read-only), the published delivery graph (for example Solo Silo: call-in → deployable silo → missile → detonation/impact explosions), and the weapon's existing guarded controls. Write targets and saved changes stay separate: `hd2.stratagem(...)` and `hd2.support_weapon(...)` with their own operation groups, guards and opt-in flags. Linked weapons with duplicate runtime identities stay read-only. With SDK 0.24.0, Runtime resolves MG-43, M-105, MG-206 and CQC-20 through their call-in delivery chain (`DELIVERY_RESOLVED`), so they are writable; EAT-17, LAS-98 and B/FLAM-80 remain read-only. Changes groups linked weapon edits under their stratagem. There is no separate Support Weapons category: equipment without a published link (B/MD C4 Pack, SG-88, CQC-72) is listed in a small **Unlinked support equipment** group at the end of Stratagems → Support, with Runtime's blocker. On SDK 0.22.0 (no linkage) every support weapon appears in that group; only SDKs without a stratagem catalog (0.17–0.20.x) keep a **Support equipment** destination. See the [linkage notes](docs/support-stratagem-linkage.md).

## Layout and game icons

Each stratagem is presented as one object:
- **Header:** icon, name, category, type and writable/read-only/shared badges.
- **Call-in:** cooldown, uses and call-in settings.
- **Delivered object:** deployed entity, support equipment or mounted weapon, with durability, ammo, handling, firing, projectile, damage and explosion groups.
- **Advanced / provenance:** collapsed by default; holds the structure outline, read-only reasons and inspection data.

Fields are compact rows with name, value, baseline and short safety badges (shared, effect unproven, evidence tier). Details sit behind each row's ⓘ.

**Game icons** are imported locally: automatically at startup when a Helldivers 2 install is detected and none are imported, from the prompt on the Boosters page, or from Settings → Game icons (removing them there turns automatic import off until the next manual import). ModBuilder reads the game's own vector icon libraries (`content/ui/shared/resources/generated_icons/stratagem_icons` and `booster_icons`) read-only from your installed Helldivers 2 data folder, fat or slim edition. It converts them to SVG in the local data folder. Game assets are not part of this repository or its releases.

Icons attach only through published identities:
- **Boosters:** Runtime's `identity.uiIcon` for the booster's native member must match the game template's own binding. That covers 18 of 20 boosters; Integrated Extinguishers and Surplus EAT Allocation have none.
- **Stratagems:** ModBuilder uses `uiIcon.iconKey` when the SDK publishes it with state `resolved` (HD2Runtime after 0.24.0: native stratagem type → the game's own icon binding). SDK 0.24.0 publishes none, so stratagems show category-coloured glyphs until a Runtime release includes it.

See [readability pass notes](docs/readability-pass.md).

## Vehicles, backpacks and the Shield Generator Relay (SDK 0.23.0)

**Vehicles** (`hd2.vehicle`, Stratagems → Support → Vehicles; the two native-only vehicles are under Other / Standalone) covers 11 vehicles (9 stratagem vehicles plus the native-only Super Earth FRV and GATER Oil Rig). Each editor shows:

- the linked call-in (cooldown);
- main health and armor;
- 226 native damage zones, kept separate, each with its own health, armor and damage forwarded to main health, in a collapsible, filterable list;
- weapon mounts.

Mount selectors list only Runtime-published replacements of the same attack family. Non-weapon slots have no control. Each swap shows Runtime's package-loading warning and is generated with `allow_unverified_reference=true`.

**Backpacks** (`hd2.backpack`, Stratagems → Support → Backpacks) covers 13 backpacks: 9 writable fields (Jump Pack recharge and launch velocity, Hover Pack recharge, Shield Generator Pack radius and health, Ballistic and Directional Shield health and armor) and read-only fields with Runtime's reasons.

The **FX-12 Shield Generator Relay** separates the physical emitter/base (health, armor, lifetime, body zone) from its shield projector (radius, shield health). Evidence badges distinguish in-game tested, schema-proven, live-write confirmed and structure-only fields. See [0.23 verification](docs/runtime023-verification.md).

## Boosters and expanded support weapons (SDK 0.24.0)

**Boosters** (`hd2.booster`, `BoosterAuthoringCapabilities.json`) lists all 20 published boosters. Each shows its identity status, relationships and Runtime's blockers. Boosters whose values Runtime cannot link to a native record stay visible as read-only; candidate enum values are never guessed.

- **Armed Resupply Pods:** the deployed resupply turret's fire rate (640 rpm) and magazine capacity (140).
- **Experimental Infusion:** the stim status effect's strength, duration and incoming damage scale. Also requires `allow_shared`.
- **Integrated Extinguishers:** relationship only; nothing is writable.

Every booster write carries `allow_unverified_effect=true`, and the booster page warns that its effect is unverified. Booster edits appear on Changes, Lua Preview and Build / Export like other entity edits.

**Support weapons** now cover 31 writable weapons and 970 fields. The new families are `reload.duration`, `projectile.lifetime`, `projectile.penetration_slowdown` and wind-up/wind-down time. Ownership and opt-ins are exactly as published: shared projectile fields are written with `allow_shared`, and fields Runtime marks `allow_unverified_effect` (reload duration, wind-down time) carry that flag and a warning. MG-43, M-105, MG-206 and CQC-20 are writable through their call-in delivery chain; EAT-17, LAS-98, B/FLAM-80 and CQC-72 stay blocked. See [0.24 verification](docs/runtime024-verification.md).

## Magazine attachments (SDK 0.23.1)

For 20 weapons, HD2Runtime publishes that round count and magazines are owned by magazine attachment definitions (`hd2.weapon_attachment`, `MagazineAttachmentCapabilities.json`), not by the weapon record. Those weapons get a **Magazine attachments** section in Player Weapons. It lists each resolved attachment separately: name, stable semantic ID, default-magazine flag, evidence, capacity, starting magazines, magazines from supply and spare magazines. For example, the AR-23C Liberator Concussive's Drum is its default magazine, with 60 rounds.

Ownership comes only from the published relationships. Ambiguous or unmatched options (for example the Concussive's Short and Extended magazines) stay unresolved, with Runtime's blocker. The weapon-level capacity is never aliased and stays read-only. Magazine selection is not offered.

Every attachment write edits a shared definition whose in-game re-application is unproven. The editor shows compact `allow_shared` / `allow_unverified_effect` badges and one warning per attachment; its writes always carry `allow_shared=true, allow_unverified_effect=true`. Projects on older SDKs are unchanged. See [0.23.1 verification](docs/runtime0231-verification.md).

## Authoring

**Stratagems** has its own browser with All, Orbital, Eagle, Support Call-Ins, Sentries, Emplacements and Mines / Deployables tabs. The published Runtime 0.22 canonical catalog supplies 73 roots, 1,382 field instances (1,311 writable), 226 backing semantic objects, 327 target-specific operation groups and 115 reviewed shared scopes. The two unresolved call-ins remain read-only. Branch-specific fields retain separate identities; search and system/availability filters come from metadata. Every field autosaves with baseline → desired comparisons and reset actions.

Defensive stratagems render their published graph: **Stratagem → Deployed Entity → Mounted Weapon → Attack branches**. All 10 sentries, 4 conventional emplacements and the 4 mine deployment entities expose cooldown and reusable **Entity Stats** (health and armor). Twelve mounted weapons show only the sections present in metadata: Ammo, Weapon (the 9 published fire-rate fields), Heat/Heatsinks, Projectile, Damage, Explosion, Explosion Damage, Beam, Arc and per-slot Status. Mines expose only the stratagem root and deployment entity; individual mines, triggers, distribution and mine attacks have no controls. The Grenadier Battlement's unproven mounted weapon, targeting, deployed and projectile lifetime, penetration slowdown, max uses and spray remain "Not currently writable" with Runtime's reasons. See [0.22 verification](docs/runtime022-verification.md).

Eagle cooldown and uses-per-rearm are per stratagem. Eagle rearm time is one shared system: all eight handles show the same saved override and one warning listing its reviewed consumers. Max uses, call-in time and unproven barrage scheduling remain read-only with SDK reasons. Projects stay pinned until explicitly rebound; changed baselines and ownership require review.

Runtime 0.21 projects stay pinned to 0.21 and keep its conservative status-slot limitation ([0.21 verification](docs/runtime021-verification.md)). After an explicit rebind, saved changes move to the 0.22 canonical instance with the same semantic target; changed ownership and baselines require review. 0.22's target-specific operation groups allow distinct status slots on one DamageInfo object in one `hd2.plan`.

Create a mod with a display name, author, resource ID such as `mods/skyeshade/my_mod`, version and optional description. The library supports open, rename, duplicate, remove from library, open project folder and export. Removing a library entry preserves its files.

Player Weapons provides name/category search and slot, category, implementation-family, editable and shared filters. Only applicable capability cards appear. Numeric controls have no invented gameplay bounds. Defaults, units, provenance, modified state and shared/derived/read-only badges are visible; backing offsets remain under Advanced.

Seven ambiguous resource identities remain visible but have no write controls. Shared writes show a warning listing their consumers, including unnamed additional ones. Composition reuses one approval for fields on the exact same backing object and consumer scope. Pending approval can be saved, but blocks Lua generation and export. Reset field, reset weapon, reset all weapon changes, enable/disable, groups and notes are supported.

Try these ordinary editor operations:

| Project | Weapon | Changes |
| --- | --- | --- |
| Concussive1100 | AR-23C Liberator Concussive | Fire rate 400 → 1100 RPM |
| VerdictFlatTrajectory | P-113 Verdict | Drag 1.2 → 0.1; gravity 1 → 0.2 |
| ReprimandFlatTrajectory | SMG-32 Reprimand | Drag 1.2 → 0.1; gravity 1 → 0.2 |

These historical trajectory samples preserve velocity 285 and mass 15. In 0.17, use Composition → Projectile Settings; shared projectile-definition writes carry `allow_shared`. No fictitious damage-dropoff field is introduced. See [sample definitions and generation](samples/README.md).

Ammo / Magazine groups the SDK's detachable-magazine and rounds-feed controls alongside calculated, read-only baselines. `PlayerWeaponAmmoCapabilities.json` supplies ownership, default-preset and discrepancy evidence; the reader cross-checks it against the semantic authoring catalog. There are 360 ammo entries, 194 writable, covering 45 weapons with writable ammo fields.

The 19 customization-supplied default presets (including Concussive and the shared Liberator/Penetrator/Suppressor preset) remain read-only. Thirteen weapons have no applicable ammo controls. GP-31's ambiguous identity remains blocked. Arbitrator and One-Two retain SDK baselines of 45 and 40 with visible catalog-disagreement warnings. Calculated values are explicitly labelled SDK baselines, not live simulations of overrides.

Capability schema v2 supplies explicit `aliasOf`, `canonical`, `preferred`, `deprecated`, `acceptedForWrites` and `semanticTarget` metadata. Only preferred canonical fields appear as independent controls. Old project aliases resolve per weapon for display/editing and generated Lua; equal desired values appear once. Conflicting desired values (or different saved baselines) block building until a saved source is explicitly selected or the field is reset. Source records remain intact on opening/rebinding; ordinary edits save the canonical identifier while retaining the saved expected baseline. Fields are never merged merely because they share an offset or semantic target.

## Heat and heatsinks

SDK 0.18 adds a dedicated **Heat / Heatsink** section for seven energy weapons. The typed heat companion is cross-checked against the authoring catalog for identities, fingerprints, baselines, ownership, permissions and counts. Five weapons expose 30 writable field instances (15 heat, 15 heatsink); Scythe and Dagger stay blocked by duplicate identities. Dagger's native/catalog disagreements remain visible.

Direct heat mechanics and heatsink inventory are separate from read-only attachment presets. Warmup, post-overheat cooldown and derived values remain read-only with SDK explanations. Heat overrides use the existing project format, baseline-aware autosave, reset and Changes/Lua Preview paths. Sibling heat/heatsink edits share one component transaction and ensure. See [0.18 verification and sample](docs/runtime018-verification.md).

## Throwables (SDK 0.27.0)

**Throwables** (sidebar, right after Player Weapons) lists every throwable `ThrowableAuthoringCapabilities.json` (`hd2runtime.throwable.guarded_authoring.v1`) publishes: all 23 armory grenades, the K-2 Throwing Knife, the two mines and the G/SH-39 Shield. They are tagged and filterable by the published category (Standard / Special) and family (High Explosive, Fragmentation, Incendiary, Gas, Stun, Throwing Knife, Shield, …). Each throwable shows only its published targets, in the compact field-row editor used everywhere else:

- **Inventory** (starting, maximum and from-supply counts) and **Detonation** (fuse, where it is a real fuse);
- **Explosion** settings and damage (radii, shrapnel/bomblet count, standard/durable damage, AP, demolition, stagger, push);
- **Status effect** chains (fire, gas, gas confusion, stun): per-hit strength and the shared status duration, with Runtime's label confidence;
- **Shrapnel**, **Bomblets** and **Bomblet explosion** submunitions; the knife's **Direct hit**; the mines' **Deployed entity** health; the shield's radius and health.

388 of 411 published fields are editable; read-only ones show Runtime's reason (for example multi-setting or impact fuses). Every write is generated as `hd2.throwable(name)` plus its published accessor chain (`:explosion()`, `:shrapnel()`, `:explosion():status_effect('fire')`, `:bomblets():explosion()`, `:damage()`, `:shield()`, …) with Runtime's published typed field constants, and carries `allow_unverified_effect` (no changed throwable value is gameplay-tested yet). Settings rows are global definitions: they show a **Shared · N** badge and a warning listing the throwables and other entities that use them, and are written with `allow_shared`. The same row reached through two throwables (for example the frag shrapnel used by the G-6 Frag and the TM-1 Lure Mine, or the fire status definition) is one value: editing it through both is refused. Throwable edits appear on Changes, reset per field or throwable, work with in-game Mod Options, and save as project format 9.

## SDK 0.27.0 automatic asset loading

HD2Runtime 0.27.0 loads the package a reference swap needs (the donor item's assets) before writing it, so nobody has to carry the donor item. `AssetDependencyCapabilities.json` (`hd2runtime.asset_dependencies.v1`) and the per-pickup / per-projectile residency data drive one compact state on every reference-swap editor:

- **Assets live-verified**: Runtime loads the package automatically, and that package was loaded in a passing live test (EAT-700 and Grenade Box pod payloads, the LAS-58 Talon projectile).
- **Assets loaded automatically**: the dependency is known and Runtime loads it before the write.
- **Assets always loaded**: the pickup ships in an always-resident package (Supply Box).
- **Assets unknown** (warning): Runtime cannot name the package, so the replacement may appear as a missing asset unless the item is already in the mission.

The package name, whether it was live-tested, the loader's proof level for that kind of reference (live-proven for pod payloads and projectiles, offline-proven for explosions and vehicle mounts) and Runtime's blocker sit in a collapsed **Asset dependency** section. In game the write waits for the assets (`waiting_for_assets`, at most 90 s); if they cannot be loaded, Runtime rejects it with `ASSET_UNAVAILABLE` and the original reference stays in place.

Asset loading does not make a swap gameplay-compatible. Drop-pod replacements and vehicle mount swaps keep their separate **Gameplay compatibility unverified** warning and are still written with `allow_unverified_reference`, exactly as Runtime requires; the two live-verified rack/pickup pairs are shown as **Live-verified pair**. The old package-risk warning is gone wherever Runtime now handles residency. Projectile swaps from another weapon's package are offered when Runtime knows that package, so the LAS-58 Talon is now a valid source. Generated mods keep using the same typed APIs: no `hd2.require_assets` or package calls are generated, because Runtime infers and loads dependencies itself.

Rebinding a 0.26.0 project to 0.27.0 keeps every edit and produces the same Lua. Evidence that changed only because Runtime now loads the assets automatically (reworded pod/mount package warnings, projectile sources reclassified as `PACKAGE_AUTO_LOADED`) is carried over instead of asking for review. See [0.27 verification](docs/runtime027-verification.md).

## SDK 0.26.0 authoring

Every control below comes from the 0.26.0 capability files; nothing is inferred from names. Projects bound to an older SDK keep their old feature set and show none of these controls until they are rebound.

- **Magazines** (`MagazineAttachmentCapabilities.json` schema 2): every resolved option of a weapon appears in a compact variant switcher, with its default badge, semantic ID, compatible weapons, capacity, starting/supply/spare magazines, reload duration and ergonomics modifier. Each option is edited independently (`hd2.weapon_attachment(semanticId)` with `allow_shared` and `allow_unverified_effect`). Editing an option never changes which magazine is equipped; selection stays read-only. Unresolved options (the LAS-5 heatsinks) are listed without controls.
- **Third-person reticle** (`hd2.fields.weapon.third_person_reticle`): an On/Off switch on 70 player weapons (`allow_unverified_effect`) and on the Support pages of 14 support weapons. The APW-1 change is gameplay-proven, so no warning is shown for it. Read-only reticles show Runtime's blocker only.
- **Fire modes** (`WeaponFireModeCapabilities.json`): the native four-slot list, with slot 1 as the default. Modes can be added, removed or reordered only where Runtime publishes the field (`hd2.fields.fire_mode.modes`, `allow_unverified_effect`). Beam, charge and special fire-control weapons show their blocker. The older single default-fire-mode row is hidden where the list is editable.
- **Mounted vehicle weapons** (`VehicleWeaponCapabilities.json`), under Stratagems → Support → Vehicles: one section per mount (`hd2.vehicle(v):weapon(label)`), with Weapon, Projectile, Damage and Explosion groups. Each group is labelled mount-local, shared with other mounts, or a shared definition with its other users; the shared group carries the `allow_shared` warning. Exosuit arms and tank cannon/machine-gun mounts stay separate.
- **Mission uses** (`hd2.fields.stratagem.max_uses`): (o) Unlimited ( ) Limited: [N], offering only the transitions Runtime publishes and writing Runtime's `'unlimited'` token. Exosuit 3 → Unlimited is gameplay-proven; every other change is written with `allow_unverified_effect` and a warning. Eagles keep `uses_per_rearm` and have no mission-use control.
- **Backpack ammo** for the M-1000 Maxigun, B/FLAM-80 Cremator and GL-28: capacity, starting ammo and ammo from supply on the weapon's Support page, written through `hd2.support_weapon(name):backpack()` with `allow_unverified_effect`.
- **Drop pod contents** (`PodPayloadCapabilities.json`): the call-in's rack with its spawn count and slots 1–4. Each slot offers Empty or a pickup from Runtime's catalog, grouped by published category, with Proven / Rack-compatible / Unverified / World pickup / Package risk badges. Slots 5–8 and read-only racks have no controls. Non-vanilla contents are written with `allow_unverified_reference` and a warning that they may not load correctly; the spawn count is written with `allow_unverified_effect`. Shared racks such as EAT-17 + Surplus EAT Allocation warn that the edit changes both and are written with `allow_shared`. Surplus EAT shows Granted stratagem → Drop pod → Spawn count → Slots on its booster page.
- **Mod Options:** spawn counts, magazine and vehicle numbers, and finite mission-use counts can become in-game sliders within the published ranges. Pickups, fire-mode lists, Unlimited and reticles cannot, because Runtime binds option values only to numbers.

A 0.26 edit raises the project to format 8. Projects from 0.25.x open and rebind without migration, and their edits and Lua are unchanged. See [0.26 verification](docs/runtime026-verification.md).

## Composition, fire modes and read-only catalogs

Published Runtime contracts drive these controls; there are no manually maintained capability tables:

- **Fire Mode:** 21 uniquely resolved weapons expose allowed Full Auto / Semi Auto choices. Other weapons remain read-only. Native values 3 and 5 have no invented global meaning; JAR-5 cannot be changed to Full Auto.
- **Composition:** 66 weapons expose 67 attacks and 57 guarded replacement selectors. Sources are filtered by published compatibility class and residency. JAR-5 is self-contained; Talon is source-weapon-required and excluded. Runtime-permitted unresolved dependencies remain selectable with a warning.
- **Projectile Settings:** controls belong to the selected projectile object. Replacing a projectile changes the settings shown; if the current projectile has edits, ModBuilder asks right there whether to keep their values on the new projectile or discard them. Shared-definition object writes show a warning with the consumers listed by the SDK and possible dynamic consumers.
- **Terminal Actions:** 134 readable slots, 130 writable (65 impact / 65 expiry), with typed ExplosionSettings sources and semantic None. Add/remove impact or expiry explosions without numeric IDs. Shared slots show a warning.
- **Explosion:** 144 writable scalar capability entries across 12 semantic fields: three radii, standard/durable damage, four AP lanes, demolition/stagger/push. Only proven controls appear. Separate outer damage and shrapnel writes are not published.
- **Attachments:** all 419 options (Optics 195, Underbarrel 117, Muzzle 71, Magazine 36) remain read-only. Default state, catalog compatibility, native identity evidence, normalized effects and ownership reasons are visible. This leaves existing direct magazine/rounds editing unchanged.
- **Support Weapons:** the published `hd2runtime.support_weapon.guarded_authoring.v2` catalog supplies 828 canonical field instances for 27 writable identities across all 35 visible weapons. Its 146 backing objects, 155 operation groups and 81 shared scopes drive validation, approval and generation directly. Repeated branch fields remain separate; the legacy flattened view is never used for authoring. Family graphs, parent/child attacks, ownership chains, backpack dependencies, statuses and unresolved nodes stay visible. The eight duplicate identities and Railgun Max Charge remain blocked. See [0.20.1 verification and sample ZIPs](docs/runtime0201-verification.md).

Modified values autosave and show baseline → desired in editor cards, Overview, Changes and exact Lua Preview. Returning to baseline removes the override using typed scalar equality. Read-only inspection data never becomes a project change.

Projectile reference overrides and object/terminal/explosion overrides store semantic weapon/attack/phase handles, saved baselines, SDK version, enabled/persistence/group/notes and acknowledgement/evidence digests. No native IDs or offsets are saved as runtime targets. Rebinding preserves values and blocks changed identities, residency, compatibility, allowed modes, shared scope or baselines until reviewed. Older projectile scalar overrides can explicitly **Move to Composition** while retaining their desired and expected values; newly shared writes require fresh acknowledgement.

Object edits bind to the selected projectile handle. SDK 0.19 coordinates related objects with one guarded plan instead of independent ensure jobs. Same-object scalar fields stay grouped; impact and expiry use separate typed operations in the same plan. A reference replacement plus dependent edits uses two phases and target_from for fresh projectile/terminal resolution. Simple independent edits retain patch/transaction output. SDK-pinned 0.17/0.18 projects retain their old safe export blocks until explicitly rebound.

Shared approvals appear once per modified object/consumer scope in Composition. DamageInfo approval cannot authorize ProjectileSettings or explosion data. Scope changes after rebind require review. Project files still store semantic intent, never Runtime plan requests or native targets. See [0.19 verification, samples and remaining limits](docs/runtime019-verification.md).

See [0.17 sample projects](samples/README.md#runtime-017-samples) and [verification](docs/verification.md).

## Branding

The reticle icon (`HD2RuntimeGUI/Resources/AppIcon/appicon.svg`, identical to `wwwroot/brand/modbuilder-icon.svg`) is the executable, window, taskbar and Alt-Tab icon; the build generates `appicon.ico` from it. The full HD2Runtime ModBuilder lockup appears in the sidebar, About, the empty project library and the startup splash. The brand yellow is `--brand-yellow: #FDD00E`; the Boosters category keeps its own `--booster-category-yellow`. The wordmark uses Big Shoulders Display (SIL Open Font License, `wwwroot/brand/fonts/`).

## Languages

The interface is available in English and Simplified Chinese (简体中文); choose it in **Settings & SDK → Language** (System default follows Windows). Switching is immediate and never changes what ModBuilder generates: Lua, project files and exported mods are identical in every language. Translations are standard `.resx` resources in `HD2RuntimeGUI.Core/Resources/Strings/`; adding a language is a data file plus one registry line. See [docs/localization.md](docs/localization.md). The Chinese translation originated from [@CChusky](https://github.com/CChusky)'s contribution in [issue #1](https://github.com/SkyeShade/HD2Runtime-ModBuilder/issues/1).

## Architecture

`HD2RuntimeGUI/` is the MAUI Windows host, with Razor `Components/`, native desktop adapters in `Services/`, and custom dark CSS in `wwwroot/`.

`HD2RuntimeGUI.Core/` is an independently testable net10.0 library:

- `Models/`: project identity, semantic overrides, semantic versions.
- `Metadata/`: typed capability catalog, strict readers and immutable versioned SDK cache.
- `GitHub/`: public release discovery and bounded, validated SDK downloads.
- `Projects/` and `Storage/`: project library and atomic JSON persistence.
- `Generation/`: validation, semantic Lua generation, gameplay archive codec and verified ZIP packaging.
- `Services/`: application workflows, one-use SDK update decisions, file-only snapshot inspection.
- `Updates/`: ModBuilder's own update check, manifest and package verification, and staging.

`HD2RuntimeModBuilder.Updater/` is the standalone updater (self-contained, trimmed, single file). It shares `UpdateContract.cs` with Core.

GitHub access, SDK cache/readers, project storage, change validation, Lua generation, export, snapshot reading, file pickers and Explorer opening have interfaces and DI registrations. Razor components delegate these operations to services.

## HD2Runtime SDK cache and updates

This section is about the HD2Runtime SDK; ModBuilder application updates are described in [docs/app-updates.md](docs/app-updates.md).

Startup and each new-project action check public GitHub releases without a token. Discovery identifies the runtime, SDK, ModTemplate and example-project artifacts; the application downloads **only the SDK**. A verified copy of the published 0.27.0 metadata supports first launch offline. Each ModBuilder release declares the newest HD2Runtime SDK it supports (1.3.1: **0.27.0**); newer SDK releases are reported in Settings as needing a newer ModBuilder and are never downloaded or offered, and a release whose metadata cannot be read is skipped without affecting the installed SDK.

Updates validate repository, exact asset names/URLs, ZIP content types, stable semantic version, size and available SHA-256 digest. Archive inspection rejects path traversal, duplicate paths, links and excessive entry/expanded sizes. No downloaded scripts are executed. Only nineteen fixed metadata files are consumed: base metadata, authoring/ammo/heat catalogs, the four composition graphs, and the published ProjectileCompositionCapabilities, AttachmentOptionCapabilities, ExplosionAuthoringCapabilities, SupportWeaponCapabilities, SupportWeaponAuthoringCapabilities, StratagemAuthoringCapabilities, CompositionPlanCapabilities, VehicleAuthoringCapabilities, BackpackAuthoringCapabilities, MagazineAttachmentCapabilities and BoosterAuthoringCapabilities contracts (the last four only for the SDK versions that publish them, and then required). Each file is bounded by its own reader size limit. Named contracts are checked against their equivalent graph payloads; schema, fingerprints, permissions, baselines and counts must agree.

The SDK metadata and capability catalog must agree on version and supported schemas/API. For 0.20.1's reused 0.19 player artifacts, only the exact published bytes are accepted by digest; all other checks still apply. All metadata files are staged in a temporary directory; a validated version directory is installed before the current pointer changes. Cached version content is immutable: a file is never replaced. An older ModBuilder caches only the files it knows, so a later version may find a required file missing from a cached version. It then adds only the missing file. It takes it from the bundled metadata when that is the same release (every cached file byte-identical), or from reinstalling the same release online. It never mixes in files from a different release. Failed updates preserve the previous SDK. Offline mode retains the cached SDK and last successful release information, with an explicit unverified status.

An update warning offers **Install Update** or **Ignore This Time** before project creation. Ignoring is single-use. Settings also provides manual update checks. Existing projects remain pinned until **Rebind to installed SDK** is explicitly selected in Overview.

## Persistence and SDK migration

The default root is `%LOCALAPPDATA%\HD2RuntimeGUI\`:

```text
library.json
Projects/<project-guid>/project.hd2mod.json
Icons/                              # locally imported game icons
app-update.json                     # last ModBuilder update check, dismissed version
Sdk/current.json
Sdk/last-release.json
Sdk/<version>/metadata.json
Sdk/<version>/PlayerWeaponAuthoringCapabilities.json
Sdk/<version>/PlayerWeaponAmmoCapabilities.json  # SDK 0.14+
Sdk/<version>/PlayerWeaponHeatCapabilities.json  # SDK 0.18+
Sdk/<version>/CompositionPlanCapabilities.json   # SDK 0.19+
Sdk/<version>/SupportWeaponAuthoringCapabilities.json # SDK 0.20.1+
Exports/<mod-name>-<version>.zip
research.json
```

Project format 5 adds published entity/weapon graph identities and a target kind (stratagem, deployed_entity or mounted_weapon) to stratagem changes; it retains support for formats 1–4 and legacy reviewed changes. `weaponChanges` stores weapon name, semantic field ID, expected and desired scalar values, scalar type, baseline SDK version, enabled/persistence state, group, notes and explicit shared acknowledgement/scope/affected identities. It stores no resolved addresses. Stable manager GUID generation matches the ModTemplate's resource-ID algorithm.

Rebinding keeps desired values and saved baselines. Missing fields, changed types, read-only capabilities, changed shared scope and changed defaults block affected builds and appear in Changes. A changed default requires an explicit **Accept baseline** action; opening or rebinding never silently rewrites it. Older SDK cache entries remain available.

## Lua and ZIP output

Single semantic changes use `hd2.patch`; multiple writes to the same SDK backing object use one `hd2.transaction`; persistence wraps each object operation in one `hd2.ensure` with Runtime's standard 60-second behavior. Modern generation groups by published owner identity, not user labels or field offsets. A transaction is limited to 32 fields and is never silently split. Mixed persistence or incompatible target kinds on one object block export. Shared operations always carry `allow_shared`, with a warning listing every included consumer scope. Conflicting shared overrides and duplicate weapon/field edits are rejected.

Targets start with `hd2.weapon(name)` or `hd2.support_weapon(name)` and fields are published `hd2.fields` constants. Support targets and operation/plan groups come directly from canonical SDK descriptors. The player generator handles the API's `player_` aliases for collisions with legacy constants. IDs are deterministic per mod/weapon/group. Float32 baselines use their shortest round-trip scalar spelling, so catalog `1.2000000476837158` generates `expect=1.2`. The original project baseline is retained.

The package follows the published ModTemplate archive, discovery-header, manager GUID and dependency conventions. It contains `manifest.json`, `hd2runtime.json`, `build-report.json`, `README.md`, `src/addon.lua`, and one gameplay archive plus empty stream/GPU companions under `mod/`. It requires Bingus release 15+/API 1 and the project's HD2Runtime minimum version/API. The template's Bingus check is `loader.version >= 16`.

No Runtime implementation, SDK stubs, ModBuilder catalog, snapshots or research artifacts enter the package. ZIP inventory and content are reopened and verified before success. Sorted entries, fixed timestamps, deterministic IDs and stable content make unchanged exports byte-identical. Preview shows exactly the gameplay source included as `src/addon.lua`; the archive adds the template dependency/discovery wrapper. Export directory is configurable, and Explorer can select the result.

## Snapshot Research

The separate read-only browser loads/relinks `.hd2snap`, persists its last path, shows capture metadata/build fingerprints, validates the v1 header/index and performs bounded raw-byte reads. It can link an existing Runtime schema-2 `.weapon-map.json` for resource/name/component lookup, resolved fields and attack settings. It verifies matching build fingerprints and read-only snapshot mode. A matching build alone does not prove that a report came from the same capture; the UI states that limitation.

The published mapper report omits record addresses, so automatic semantic-field-to-raw-byte navigation is unavailable. Raw inspection accepts an address from the selected snapshot or a captured-region selection. The browser does not execute Runtime's scanner or feed data into authoring/generation.

## Release package

The release is a self-contained directory ZIP (.NET runtime, Windows App SDK and MAUI files next to the executable), because MAUI Blazor Hybrid and WebView2/native dependencies are more reliable that way than as a single file. WebView2 is not bundled. HD2Runtime remains a separate dependency, and generated mods/exports are never bundled into the ModBuilder. Mutable data lives in `%LOCALAPPDATA%\HD2RuntimeGUI\` (`Projects\`, `Exports\`, `Sdk\`, `Icons\`), so the extracted application directory can be moved or replaced safely.

## Build, test and publish

Development requires Windows, .NET 10 SDK, MAUI workload and WebView2. NuGet access is needed for first restore. Close the app before rebuilding its output directory.

```powershell
dotnet workload install maui
dotnet build HD2RuntimeGUI/HD2RuntimeGUI.csproj
dotnet test HD2RuntimeGUI.Tests/HD2RuntimeGUI.Tests.csproj
./scripts/publish-windows.ps1
```

The application version is `Hd2RuntimeGuiVersion` in `Directory.Build.props`. `scripts/publish-windows.ps1` refuses a dirty working tree (`-AllowDirty` is for local test builds only), publishes the app and the standalone updater, and writes to `artifacts/release/`:

- `HD2Runtime-ModBuilder-vX.Y.Z-win-x64.zip`: the flat application folder, which contains:
  - `HD2RuntimeModBuilder.exe` and the other `HD2RuntimeModBuilder.*` files;
  - `HD2RuntimeModBuilder.Updater.exe`;
  - the `HD2RuntimeGUI.exe` compatibility copy for 1.0.0;
  - `modbuilder-files.json` (inventory: path, size and SHA-256 of every file).
- `HD2Runtime-ModBuilder-vX.Y.Z-win-x64.zip.sha256`.
- `modbuilder-update-v2.json`: the update manifest read by 1.0.1 and later (format 2: version, tag, asset, size, SHA-256, entry point, updater, commit).
- `modbuilder-update.json`: the same data in the format-1 form that 1.0.0 reads (entry point `HD2RuntimeGUI.exe`).

Before zipping, the script checks:
- the product name and version in the executable's metadata, and the commit;
- the reticle icon in both `appicon.ico` and the executable;
- the entry point, the updater, and the compatibility copy (identical to the entry point);
- that no other `HD2RuntimeGUI.*` file ships;
- that no symbols, SDK cache, projects, imported icons, screenshots or other user/development data are included.

It then checks the ZIP layout and that both manifests match the ZIP. Upload all four files to the GitHub release tagged `vX.Y.Z` (see [the update design](docs/app-updates.md)).

See [verification results](docs/verification.md) for the historical verification log. Agent screenshots, caches, publish output and generated ZIPs are ignored by Git.
