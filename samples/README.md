# Mod builder samples

For SDK 0.20.1 support authoring, run:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/0201-samples --support
```

This creates RecoillessTuning020, ArcThrowerTuning020, C4Explosion020 and SupportAMRTuning020 through the same autosave, shared-scope approval, generation and export services as the editor. These are sample requests, not capability tables; exact baselines, target paths, permissions and grouping come from the SDK's canonical instances. See [verification and desktop-generated ZIPs](../docs/runtime0201-verification.md).

## Player-weapon samples

`player-weapons.json` contains three sample requests, not a gameplay catalog. Defaults, types and permissions are looked up in the installed SDK. The developer helper uses the same `WeaponChangeService`, project storage, generator and exporter as the GUI:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/sample-workspace-0.14
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/live-release-0.14 --online
```

`--online` explicitly verifies and installs the latest compatible public SDK. No Runtime or mod is deployed.

Golden gameplay source:

- [Concussive1100](../HD2RuntimeGUI.Tests/Golden/Concussive1100.lua): 400 → 1100 RPM.
- [VerdictFlatTrajectory](../HD2RuntimeGUI.Tests/Golden/VerdictFlatTrajectory.lua): drag 1.2 → 0.1; gravity 1 → 0.2.
- [ReprimandFlatTrajectory](../HD2RuntimeGUI.Tests/Golden/ReprimandFlatTrajectory.lua): the same two projectile edits.

The three manually exercised desktop workflows exported to `artifacts/player-weapons-ui/Exports/`. This isolated smoke-test library also retains the GUI-created projects. ZIPs and screenshots are local artifacts, excluded from commits. `addon.lua` is the historical JAR-5 sample retained for legacy reference.

## Ammo samples (SDK 0.14)

`player-weapon-ammo.json` uses the same generic sample pipeline:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/ammo-samples --ammo
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/ammo-online --online --ammo
```

- [VerdictMagazine](../HD2RuntimeGUI.Tests/Golden/VerdictMagazine.lua): capacity 10 ? 15, spare magazines 10 ? 8, starting magazines 6 ? 8, supply magazines 10 ? 8.
- [PunisherDualFeed](../HD2RuntimeGUI.Tests/Golden/PunisherDualFeed.lua): feeds 8 + 8 ? 10 + 10, spare/supply rounds 60 ? 80, starting rounds 32 ? 40.

Both use guarded semantic transactions with `hd2.ensure`, expected SDK baselines, and an HD2Runtime 0.14.0 dependency. Derived total/ammo-box fields are never written. Desktop-generated ZIPs are in `artifacts/ammo-ui/Exports/`; install and gameplay-test them manually.

## Guarded projectile swap (SDK 0.15)

Create `Jar5VerdictProjectile` with resource ID `mods/skyeshade/jar5_verdict_projectile`. In Player Weapons choose JAR-5 Dominator, then Composition -> primary Projectile -> P-113 Verdict primary projectile. The semantic replacement autosaves. Review Changes / Lua Preview, then Build / Export Mod.

The verified desktop sample is at `artifacts/composition-ui/Exports/Jar5VerdictProjectile-0.1.0.zip`. Install HD2Runtime 0.15.0 and Bingus separately before manually testing it. The GUI does not deploy it. `HD2RuntimeGUI.Tests/Golden/projectile-swap.lua` covers equivalent semantic output using the test resource ID.

## Runtime 0.17 samples

These four projects were created and built through the desktop GUI using the same generic editor and project services as ordinary projects. Their JSON definitions survive app restarts in `artifacts/017-ui/Projects/`. All exports require separately installed Bingus and HD2Runtime 0.17.0; none were deployed.

| Project | Editor changes | ZIP in `artifacts/017-ui/Exports/` |
| --- | --- | --- |
| FireModeSample | Concussive: Full Auto → Semi Auto | `FireModeSample-0.1.0.zip` |
| TerminalExplosionSample | Eruptor: expiry explosion → None | `TerminalExplosionSample-0.1.0.zip` |
| ExplosionTuningSample | Eruptor: outer radius 7 → 10; standard damage 225 → 500 | `ExplosionTuningSample-0.1.0.zip` |
| ProjectileCompositionSample | Verdict: replace projectile with self-contained JAR-5; selected object velocity 180 → 350 with shared acknowledgement | `ProjectileCompositionSample-0.1.0.zip` |

The historical composition sample above combined a reference swap and a shared JAR-5 object edit. **That combination is now blocked:** Runtime 0.17 cannot express a completion dependency between those asynchronous operations. Statement order did not guarantee execution order. Keep only one operation enabled; do not use the old combined ZIP as a sequencing example.

Developer verification uses `tools/runtime017-smoke.mjs` with a Debug app launched against an isolated `HD2RUNTIMEGUI_DATA_ROOT` and a WebView2 CDP port. `--relaunch --catalog` reloads/rebuilds the four projects and checks every player/support view. Node is only a development verification tool, never an application dependency. Screenshots, generated projects, ZIPs and publish output remain ignored by Git.

The historical samples above retain their SDK-era semantics. For 0.17 projectile scalar edits, use Composition and explicitly approve the published shared-object scope.

## Grouped Concussive regression samples

For SDK 0.19, run `dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/019-sample --plans`. This generates **ConcussiveComposition019** (fire rate, five DamageInfo fields, impact and expiry) and **ProjectileSwapAndTune019** (Verdict → self-contained JAR-5, then velocity/drag via phased `target_from`). See [0.19 verification and complete Lua goldens](../docs/runtime019-verification.md). The older single-target restrictions below apply only to pinned SDK 0.17/0.18 projects.

For SDK 0.18 heat authoring, run `dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/018-sample --heat`. The metadata-driven `player-weapon-heat.json` preset creates **SickleHeatTuning**: capacity 100 → 140, cooling 8 → 12, spare heatsinks 3 → 5. All three direct component fields share one guarded transaction. Attachment presets remain read-only. See [0.18 validation](../docs/runtime018-verification.md).

Run `dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/grouped-generation --grouping` to build the exact fire-rate/push/AP regression mod through the normal project/change/generator services. See [operation grouping and before/after Lua](../docs/generation-planning.md).

- `ConcussiveGrouped-0.1.0.zip`: fire rate 400 → 1100; push 60 → 30; all four AP lanes 2 → 3. Six ensures become two (component patch + DamageInfo transaction).
- `ConcussiveGroupedImpact-0.1.0.zip`: adds impact None → Eruptor explosion; three ensures.
- `ConcussiveGroupedExpiry-0.1.0.zip`: adds expiry None → Eruptor explosion; three ensures.

ZIPs are in `artifacts/grouped-generation/Exports/`. The terminal variants are alternatives to test individually, not together. The combined impact+expiry project is saved but blocked because the released transaction API accepts only one terminal phase. No deployment or game launch is performed.

## Runtime 0.22 defensive samples

`dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/runtime022/samples --defensive` generates AntiTankEmplacement022, ConventionalSentry022, ExplosiveSentry022 and UnusualSentry022 through the same project, persistence, generation and export services as the UI. Shared scopes are acknowledged explicitly for these samples. No mine attack sample exists because Runtime 0.22 does not resolve individual mine attacks. Nothing is deployed. See [0.22 verification](../docs/runtime022-verification.md).
