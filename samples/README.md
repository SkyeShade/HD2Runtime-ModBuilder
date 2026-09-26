# JAR-5 AP4

`JAR-5-AP4-0.1.0.zip` is a generated gameplay mod using the published HD2Runtime 0.5.1 SDK's JAR-5 armor-penetration contract: expected 3, requested 4, standard `hd2.ensure()` behavior.

Resource: `mods/skyeshade/jar5_ap4`. Dependencies: separately installed Bingus Shared Loader release 15+/API 1 and HD2Runtime 0.5.1+/API 1.

The ZIP includes its generated source as `src/addon.lua`. A copy is available as `addon.lua` here. No runtime implementation or SDK tooling is bundled. This artifact was generated and inspected but not deployed or run in game.

Regenerate with `dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/sample-workspace`; output is in that workspace's `Exports` directory.
