# Guarded operation planning for Runtime 0.17?0.18

The Composition generator emitted one independent `hd2.ensure({patch=...})` per field. Sibling edits captured the same DamageInfo record independently; the first write invalidated the other jobs' non-target snapshots. Calling asynchronous `ensure` jobs in Lua statement order did not establish an execution dependency. Runtime correctly rejected those jobs.

`ISemanticOperationPlanner` now groups enabled semantic changes by the SDK's backing object identity before emitting Lua. Component owners use resource identity + component type; settings owners use the published settings type/group/record identity. Offsets, weapon names alone and user-facing change groups do not determine atomicity. These identities are planning inputs only: generated Lua still contains exclusively semantic Runtime handles/constants.

One object emits one patch for one field, or one transaction for multiple fields, with one ensure wrapper when persistence is enabled. Fields and operations are sorted deterministically. Multiple handles to a shared object select one published target that accepts all requested fields. Conflicting values/baselines, unsupported target combinations, mixed persistence or more than 32 fields block export instead of splitting an object into racing jobs. Existing pre-0.17 pinned generation remains unchanged.

Shared approval applies to the exact object and affected-consumer/write scope. One acknowledgement covers sibling edits, including later edits. Revoking it revokes that scope's saved approvals. Another backing object or changed SDK consumer evidence requires its own approval. Saved baselines and Runtime's validation remain intact.

SDK 0.18 heat/heatsink fields use this same planner: the published WeaponHeatComponentData owner combines their scalar changes into one transaction. Project persistence and the grouping algorithm are unchanged. The 0.18 SDK still has no multi-target completion/dependency plan API; every composition conflict below remains enforced.

## Released syntax and limitations

Authority: the published 0.17 SDK `hd2runtime.lua` (`HD2TransactionRequest`, `HD2EnsureRequest`) and `player-weapon-composition.md`. Read-only inspection of the matching release commit `5500113a1c5f7db6678d78fc143eac10b648afb7` confirmed the constraints in `domains/player_weapon_writes.lua` and `api/ensure.lua`. HD2Runtime was not modified.

The released shape is **one `target` plus `changes`**. There is no multi-target `patches` request:

```lua
hd2.ensure({
    transaction={
        id='stable-operation-id',
        target=hd2.weapon('AR-23C Liberator Concussive'):attack('primary'):projectile(),
        allow_shared=true,
        changes={
            {field=hd2.fields.damage.ap_direct,expect=2,value=3},
            {field=hd2.fields.damage.ap_extreme,expect=2,value=3},
            {field=hd2.fields.damage.ap_large,expect=2,value=3},
            {field=hd2.fields.damage.ap_slight,expect=2,value=3},
            {field=hd2.fields.damage.push_force,expect=60,value=30},
        },
    }
})
```

Two combinations cannot be scheduled safely with this released contract and now produce explicit build errors:

- **Impact + expiry on one ProjectileSettings**, or terminal + projectile scalar edits on that same record. A terminal target accepts only its own phase, and a projectile target accepts only projectile/damage fields. The GUI detects the common backing owner but cannot place these different target kinds in one released transaction.
- **Projectile replacement + dependent object edits.** Patch/transaction/ensure return scheduled watches; the public write requests provide no completion/dependency callback. Declaration order or guessed delays are insufficient. Replacement identity/baselines remain bound to the intended source in the saved project, but exporting both operations together is blocked. Source objects shared with other weapon handles are included in this check. Keep only the replacement or only the object edits enabled until Runtime exposes suitable orchestration; installing two separate mods is not a sequencing solution.

Explosion radii group by ExplosionSettings. Linked explosion damage/AP/effects group separately by their DamageInfo identity. Native weapon fields group by actual component owner. For the exact Concussive sample, the SDK locates **fire rate in ProjectileWeaponComponentData**, not WeaponDataComponentData; handling/default-mode fields in WeaponData form their own group.

## Verification and generated samples

**241 automated tests pass**, including the reviewed [Concussive Lua golden](../HD2RuntimeGUI.Tests/Golden/ConcussiveGrouped.lua). Coverage includes the exact damage group, separate weapon/component owners, projectile siblings, terminal rejection, explosion radii/damage groups, shared consumers, approval inheritance/revocation/rebind, reset-to-baseline, non-projectile families and byte-stable Lua/ZIP exports. Previous swap tests now verify fail-closed dependency handling instead of assuming statement order was sufficient.

Windows build: zero warnings/errors. The desktop `tools/runtime017-smoke.mjs --grouping` workflow passed, creating the exact Concussive project, checking that one DamageInfo acknowledgement covers all AP lanes, inspecting the two-ensure preview, exporting it, and verifying the separate terminal acknowledgement plus impact/expiry rejection.

Developer reproduction (no game or deployment):

```powershell
dotnet run --project tools/HD2RuntimeGUI.Sample -- artifacts/grouped-generation --grouping
```

| Sample | Old ensures | New ensures | Local result |
| --- | ---: | ---: | --- |
| Fire rate + push + four AP lanes | 6 | 2 | `artifacts/grouped-generation/Exports/ConcussiveGrouped-0.1.0.zip` |
| Above + impact None → Eruptor explosion | 7 | 3 | `artifacts/grouped-generation/Exports/ConcussiveGroupedImpact-0.1.0.zip` |
| Above + expiry None → Eruptor explosion | 7 | 3 | `artifacts/grouped-generation/Exports/ConcussiveGroupedExpiry-0.1.0.zip` |
| Above + both impact and expiry | 8 | blocked | `artifacts/grouped-generation/ConcussiveGroupedBothTerminals.blocked.txt` |

The impact and expiry ZIPs are **alternative test variants**, not mods to install together: both edit the same ProjectileSettings. Each successful ZIP contains only the validated gameplay payload and required dependencies. Generated `.before.lua` files reproduce the former per-field emission for comparison only; `.after.lua` files match the new packaged source. Neither diagnostic files nor GUI metadata enter the ZIPs. The GUI-created base sample is also at `artifacts/grouping-ui-verified/Exports/ConcussiveGrouping-0.1.0.zip`.

No game was launched, no mod was deployed, and no Runtime guards, rollback, protection restoration or expected-value checks were weakened. Gameplay confirmation remains a manual step.
