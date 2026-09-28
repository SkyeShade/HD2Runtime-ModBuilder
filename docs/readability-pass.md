# Information-architecture and readability pass

GUI-only change. Runtime/API behaviour, saved project formats and generated Lua are unchanged.

## Structure

- **Sidebar:** Player Weapons · Stratagems (Support · Offensive · Defensive) · Vehicles · Backpacks · Boosters. The permanent Support Weapons category is gone. SDKs without a stratagem catalog (0.17–0.20.x) keep a **Support equipment** destination, because they have no Support stratagem to hold it.
- **Support equipment:** equipment with a published structural call-in link (`hd2runtime.support_callin_linkage.v1`, 32 on SDK 0.24.0) is edited inside its Support stratagem under **Delivered object**. The rest (B/MD C4 Pack, CQC-72 Entrenchment Tool, SG-88 Break-Action Shotgun on 0.24.0) are listed once, in a small **Unlinked support equipment** group at the end of Stratagems → Support, with Runtime's blocker. Nothing is paired by name: the B/MD C4 Pack call-in exists, but Runtime publishes its delivery as unknown. On SDK 0.22.0 (no linkage) every support weapon is in that group.
- **Stratagem detail:**
  - **Header:** icon, name, category, type, writable/read-only counts, shared badge.
  - **Call-in:** cooldown, uses, Eagle rearm.
  - **Delivered object:** equipment, deployed entity, mounted weapons, attacks.
  - **Advanced / provenance:** collapsed; opens automatically when nothing is writable.
- **Support equipment details:** the long inspection panels (attack graph, runtime attacks, ammo/feed, relationships, identity evidence) are in one collapsed "Equipment inspection & provenance" section.
- **Fields:**
  - **Rows:** one compact row per field: name + unit, value, baseline (→ desired), and short badges (Shared, Effect unproven, evidence tier, Modified). The On toggle and Reset appear when edited, and provenance or the read-only reason sits behind ⓘ.
  - **Groups:** fields are grouped by `FieldGroups` into Ammo (capacity, magazines, rounds, reload), Handling (ergonomics, recoil, sway, spread), Firing (fire rate, wind-up, charge, heat), Projectile, Damage, Explosion, Arc, Beam and Status. Groups are presentation only.
- **Safety:** shared-write and unverified-effect acknowledgements, read-only blockers and evidence tiers are all still shown, as badges, tooltips or the ⓘ / Advanced sections.
- **Search:**
  - **Stratagems:** display name, semantic ID, family, family label and deployed-entity type.
  - **Boosters:** name, semantic ID, native name and identity status.

## Game icons

**Source:** the game's own vector icon libraries, read from the user's installed game:

| Resource | Templates with vector content |
| --- | ---: |
| `content/ui/shared/resources/generated_icons/stratagem_icons` (xaml) | 92 of 111 (19 are empty in the game file) |
| `content/ui/shared/resources/generated_icons/booster_icons` (xaml) | 19 |

**Reader:** `GameDataReader` reads Stingray archives read-only. It supports the fat edition and the slim edition (DSAR `.nxa` bundles, uncompressed or LZ4 chunks), following the layout documented by Filediver. `XamlIcons` converts WPF path geometry to SVG and accepts only validated path data and colours, so no markup from the file is copied. Import takes under a second.

**Storage:** icons go to `<data root>/Icons/` with a manifest recording the resource SHA-256 values and the game's own type → icon bindings. **Settings → Game icons** imports them or removes them.

**Licensing:** the icons are Arrowhead Game Studios assets. Redistributing them in this public repository or its release ZIPs is questionable, so none are committed or bundled. The tool extracts them on the user's machine from their own installation. Screenshots under `docs/screenshots/` (gitignored) can contain them.

**Mapping (published identities only):**
- **Boosters, 18 of 20:** an icon is attached when Runtime's `identity.uiIcon` for the booster's published native member equals the game template's `BoosterDataTemplate` binding for that member. Eleven of those boosters are `CANDIDATES` because their native enum value is ambiguous; that ambiguity does not change which icon the native member uses. Integrated Extinguishers (`EFFECT_CATEGORY`) and Surplus EAT Allocation (`ELIMINATION`) publish no icon identity and show a glyph.
- **Stratagems, 0:** the game binds icons to native stratagem *type* names (`StratagemTypeDataTemplate`, 110 bindings, kept in the manifest). HD2Runtime does not publish each stratagem's native type or icon key, and the GUI does not guess from display names, so every stratagem shows a category-coloured glyph. The stripped type library only gives name lengths, and a stratagem's package does not contain its HUD icon, so neither provides a structural join. Once Runtime publishes a native type or icon key per stratagem root, `GameIconStore.StratagemIconKey` becomes a one-line change.

## Validation

- **Unit tests:** `ReadabilityPassTests` covers:
  - merged/unlinked support with no duplicate entries, older-SDK navigation, and search;
  - field grouping;
  - XAML→SVG conversion, including injection rejection, and LZ4 decoding;
  - synthetic fat and slim archive imports;
  - booster icon mapping and missing-icon fallback.
- **Desktop smoke:** `tools/ia-smoke.mjs` and `tools/runtime024-smoke.mjs` passed.

## Icon import follow-up

- **Import timing:** automatic at startup when a valid install is detected (a data folder with `bundles.nxa` or the fat-edition boot archive) and no icons are imported. The Boosters page shows an **Import game icons** prompt while none are imported, and Settings → Game icons still works. Removing icons turns automatic import off (`icons-auto-import-off` in the data root) until the next manual import.
- **Refresh:** icons refresh in place when an import finishes. `GameIcon` listens for the store's `Changed` event and resolves a booster's key at render time, so a page opened before the import no longer keeps its letter glyphs.
- **Stratagems:** the GUI reads the optional `uiIcon` published by HD2Runtime after 0.24.0 (`resolved` states only; keys are validated against the stratagem icon library). The published 0.24.0 SDK has none, so stratagems still show glyphs, and the Stratagems page says why.
