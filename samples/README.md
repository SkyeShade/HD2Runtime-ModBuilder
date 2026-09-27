# Player-weapon samples

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
