# Player-weapon samples

`player-weapons.json` contains three sample requests, not a gameplay catalog. Defaults, types and permissions are looked up in the installed SDK. The developer helper uses the same `WeaponChangeService`, project storage, generator and exporter as the GUI:

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/sample-workspace-0.13
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/live-release-0.13 --online
```

`--online` explicitly verifies and installs the latest compatible public SDK. No Runtime or mod is deployed.

Golden gameplay source:

- [Concussive1100](../HD2RuntimeGUI.Tests/Golden/Concussive1100.lua): 400 → 1100 RPM.
- [VerdictFlatTrajectory](../HD2RuntimeGUI.Tests/Golden/VerdictFlatTrajectory.lua): drag 1.2 → 0.1; gravity 1 → 0.2.
- [ReprimandFlatTrajectory](../HD2RuntimeGUI.Tests/Golden/ReprimandFlatTrajectory.lua): the same two projectile edits.

The three manually exercised desktop workflows exported to `artifacts/player-weapons-ui/Exports/`. This isolated smoke-test library also retains the GUI-created projects. ZIPs and screenshots are local artifacts, excluded from commits. `addon.lua` is the historical JAR-5 sample retained for legacy reference.
