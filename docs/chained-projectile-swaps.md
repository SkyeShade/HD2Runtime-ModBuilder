# Chained plain bullet swaps

An SDK may advertise `originalPlayerProjectiles: true` in
`CompositionPlanCapabilities.json`. ModBuilder then uses the Runtime's explicit
`attack:original_projectile()` handles when a selected donor weapon also has
its own projectile swap. This requires the corresponding Runtime change; it
is not supported by the published 0.30.3 Runtime or SDK.

Example: tune One-Two's original bullet to 80 standard / 85 durable, assign it
to StA-52, then switch One-Two to Tenderizer ammo without carrying those damage
values onto Tenderizer. The edits on the original bullet are retained under
the remaining borrower, including their saved option binding. Editing the
borrowed bullet through StA-52 before the final swap produces the same result.
Explicit recipient edits take precedence when the departing weapon discards
its own edits. Keeping values retains the existing behavior of moving them
onto the newly selected projectile.

The connected selectors and original definition edits export in one guarded
plan. Original targets need no live-selector dependency phase. Projectile and
damage writes remain separate owning objects; conflicting values, missing
identity evidence and unsupported SDKs still fail validation. Support weapons,
ammunition deltas, explosions and terminal references are outside this addition.

The new tests exercise both edit orders, persistence/export and the old SDK
guard. Runtime regression tests separately cover identity conflicts, rollback,
fresh resolution and a partial selector reload. Gameplay remains unverified.
