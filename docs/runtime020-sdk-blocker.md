# Runtime 0.20 support authoring: released SDK blocker

**Resolved by published 0.20.1 schema v2.** See [the completed integration and verification](runtime0201-verification.md). The following records the original 0.20.0 audit.

The requested authoring upgrade is **blocked by missing public capability descriptors**. No application behavior, bundled SDK, project format, generator, Runtime source, or safety checks were changed. Support weapons remain read-only in this GUI revision. This is an SDK publication gap, not evidence that Runtime's guarded writes are unsafe.

## Verified release

- [Published v0.20.0](https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.20.0)
- Build report commit: `5f4872f232305dcedfd96c8035857d6692cf7017`
- SDK asset: `HD2Runtime-0.20.0-sdk.zip`, 479385 bytes
- SHA-256: `23464284b070e8b1e5d62b4570da330efe04d3386c187934ed9a2e15fe23bbee`
- Public contract: `hd2runtime.support_weapon.guarded_authoring.v1`, schema 1

The public catalog reports 35 weapons, 27 writable identities and 828 writable field instances. Domain totals are weapon 232, projectile 80, damage 198, explosion/linked damage 204, magazine 48, rounds 15, charge 20, heat 4, heatsink 3, arc 6, beam 2, status 16. These are **audited SDK totals, not controls implemented in the GUI**.

The eight blocked entries are B/FLAM-80, CQC-20, CQC-72, EAT-17, LAS-98, M-105, MG-206 and MG-43. Their public reasons preserve identity ambiguity. The seven scope categories are projectile, damage, explosion, explosion damage, arc, beam and status.

## Missing information

`SupportWeaponAuthoringCapabilities.json` publishes per-weapon `writableFieldsByDomain` string arrays, `attackBranches`, `sharedScopes`, blocked reasons and summary counts. It does not publish instance-level field descriptors containing:

1. The baseline value, scalar/storage type and unit for each writable field instance.
2. The field's semantic target and branch role, including multiple instances of the same field on different branches.
3. An object identity suitable for grouping exactly one native backing object per operation, including fields whose display domain differs from their backing object.
4. Per-object shared consumer evidence sufficient to scope acknowledgement and invalidate it on rebind.

The 828 reported instances collapse to **812 per-weapon field names** in the published arrays:

| Weapon | Reported instances | Published field names |
| --- | ---: | ---: |
| MS-11 Solo Silo | 24 | 12 |
| S-11 Speargun | 43 | 41 |
| TX-41 Sterilizer | 25 | 23 |

Branch lists alone do not establish which individual fields are writable on which branch. Applying every domain field to every resolved branch would invent permissions.

For example, LAS-99 advertises seven heat/heatsink fields but publishes no values for them in either support artifact. Its inspection `runtimeAttacks[].resolvedFields` contains projectile and explosion data, not heat baselines. Likewise, support weapon handling baselines are absent from the authoring catalog; `weaponComponents` in the inspection artifact describes ownership rather than those scalar values.

`SupportWeaponCapabilities.json` remains the `hd2runtime.support_weapon.read_only.v1` contract with `guardedAuthoringReady=false`. It supplies useful graph data and some attack/ammo/charge values, but cannot fill all these gaps. The player capability files cover player weapons; legacy `metadata.json` has only a read-only AMR crosshair entry for that support weapon, not a replacement support authoring contract. Example Lua proofs supply a few explicit baselines, not metadata for all 828 instances.

## Root cause in the release generator

The exact release's [generator](https://github.com/SkyeShade/HD2Runtime/blob/5f4872f232305dcedfd96c8035857d6692cf7017/scripts/generate_support_weapon_authoring.py) constructs full internal descriptors in `make_field`: `currentDefault`, `type`, `unit`, `target`, `backing`, `writeScope`, `sharedWithWeapons`, and editability. It stores those under `runtime_weapons[...]['fields']`.

The public projection instead calls `definition(field['semanticFieldId'])['id']`, groups generic IDs by domain, then applies `sorted(set(...))`. `public_weapons.append(...)` omits the descriptor list. Thus the public summary counts the internal instances while the GUI artifact loses the per-instance data and multiplicity.

Internal `schemas/support_weapon_authoring_catalog.json` and `domains/support_weapon_authoring.lua` are not a substitute public GUI contract. The application must not parse them, infer permissions from native offsets, or hardcode baseline tables. No such workaround was added.

## Required upstream correction

Publish a sanitized, versioned descriptor for every authorable instance, generated from the same authoritative data as Runtime. It needs semantic weapon/branch/field identity, type and baseline, target API form, canonical/alias semantics where applicable, editability and reasons, and a stable semantic or opaque object key for grouping. Shared descriptors need an exact approval scope and consumer information; that scope must distinguish different objects of the same category and change when relevant ownership evidence changes.

This does not require exposing process addresses, native IDs, offsets or record indices. Runtime retains all native resolution and safety checks. The correction must be published in an SDK asset before the GUI can honestly claim support for the published authoring surface. A private Runtime source modification would not repair the downloaded 0.20.0 SDK used by GUI users.

## Reproduce and validation

After extracting the published SDK, run:

```powershell
node tools/inspect-runtime020-sdk.mjs artifacts/sdk-0.20
```

The diagnostic checks version/contracts and summary consistency, prints field multiplicity loss, blocked entries, scope categories and artifact hashes. It reads JSON only, does not execute downloaded code, and is not part of the application or its runtime requirements.

Validation on 2026-09-27:

- Full existing GUI test suite: **286 passed**, zero failures/skips (`artifacts/020-baseline-tests.log`).
- Windows Debug build: succeeded, zero warnings/errors (`artifacts/020-audit-build.log`).
- Windows self-contained Release publish: succeeded (`artifacts/020-audit-publish.log`). Output: `artifacts/publish-020-audit/win-x64/HD2RuntimeGUI.exe`. This is the unchanged 0.19-capable GUI, **not** a completed 0.20 authoring release.
- Published JSON audit: succeeded (`artifacts/020-sdk-audit.json`). The audit is diagnostic evidence, not a new GUI authoring test.

No support-authoring sample ZIPs were generated: RecoillessTuning020, ArcThrowerTuning020, C4Explosion020 and SupportAMRTuning020 remain blocked. No game was launched and no mod was deployed.
