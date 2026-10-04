# Decisions

A chronological log of significant design decisions, one short entry per batch. This records *why* choices were made; the current state of the system lives in [Domain](DOMAIN.md), [Database](DATABASE.md), and the README.

## 2026 — Batch 0: initial prototype

- Modeled `Nation` with a combined `Type` field, `Rank` under `Nation`, and mutable tree state (RP/SL cost, position, folder, prerequisites) directly on `Vehicle`. Chosen for speed of prototyping; known to conflict with the versioning goal.
- Prerequisites were explicit edges (`VehiclePrerequisite`), not inferred from tree layout, from the start — progression is a graph, rows/columns are presentation only.
- Calculator requires explicit filler targets (no auto-picking) and stops at the first unmet rank gate.

## 2026 — Batch 1: explicit Nation / VehicleType / ResearchTree

- Introduced `VehicleType` and `ResearchTree` (unique Nation + VehicleType) and reparented `Rank` from `Nation` to `ResearchTree` instead of keeping the combined `Nation.Type`. Why: one nation must be able to own several tree types, and tree identity must exist before versioning can attach to it.
- Migrated existing data with SQL inside the migration (deriving `VehicleType` rows and `ResearchTree` rows from old `Nation.Type` values) rather than resetting the database, because the project was already deployed to a development database and the data was derivable.

## 2026 — Batch 2: versioning backbone (GameUpdate + ResearchTreeVersion)

- Added `GameUpdate` (unique `Version` string, optional `Name`/`ReleaseDate`) and `ResearchTreeVersion` (belongs to exactly one `ResearchTree` and one `GameUpdate`; unique `(ResearchTreeId, GameUpdateId)` pair). Why unique: one tree must not have two snapshots for the same update unless an explicit revision model is introduced later.
- Stored a minimal `Status` (`Draft`/`Published`, int-backed enum) on the version now rather than later. Why: adding a status column later would be a schema change touching every version row, while a simple enum today does not block the future publishing workflow (`Archived` can be appended without renumbering).
- Cascade delete on both version FKs, matching the existing convention (`Nation → ResearchTree → Rank → Vehicle`). Tradeoff: acceptable while versions are empty shells; once versions hold published history, `Restrict` should be considered so history cannot be silently deleted.
- Deliberately **not** done in this batch: moving RP/SL cost, rank, prerequisites, folder, or layout into versioned entities; calculator input switching to a version; publishing/immutability workflows; admin endpoints. Versions exist but carry no versioned content yet.
- Seed data: one sample `GameUpdate` (`dev-sample`) with one published version per seeded tree — clearly development sample data, not real War Thunder update history.

## 2026 — Batch 3: stable Vehicle identity and versioned tree entries

- Introduced `VehicleTreeEntry` as the membership/state of one stable `Vehicle` in one `ResearchTreeVersion`, unique by `(ResearchTreeVersionId, VehicleId)`. Moved RP cost, SL cost, and rank association onto the entry because those values can change between updates.
- Restricted deletion of a `Vehicle` while entries reference it. Why: deleting stable identity must not silently erase membership from historical snapshots.
- Preserved existing data with a guarded migration that required exactly one version for each legacy vehicle's tree. The migration failed on missing or ambiguous versions instead of selecting a version or dropping data silently.
- Kept calculator and flat API inputs vehicle-based temporarily. When more than one entry makes the version ambiguous, the transitional behavior fails clearly or returns null flat projections rather than choosing an arbitrary version. Explicit version selection remains Batch 7.

## 2026 — Batch 4: version-owned rank configuration

- Replaced tree-level `Rank` with `TreeRank`, owned by `ResearchTreeVersion` and unique by `(ResearchTreeVersionId, RankNumber)`. Why: rank numbering and unlock thresholds can differ between snapshots.
- Copied each legacy rank into every version of its research tree, then remapped each vehicle entry by rank number within its own version. Guard SQL aborts when a mapping is missing or non-deterministic.
- Kept `RequiredVehiclesUnlocked` as stored version data rather than a calculator constant. This preserves data-driven rank gates without introducing publication or full-version validation workflows.
- The entry-to-rank same-version invariant remains enforced by migration/application behavior and tests rather than a version-carrying composite foreign key. Strengthening that schema relationship is separate from the prerequisite and folder invariants.

## 2026 — Batch 5: version-scoped prerequisite graph

- Replaced stable-vehicle prerequisite edges with edges between `VehicleTreeEntry` rows. Why: progression dependencies can change between updates and must belong to one snapshot.
- Used a composite primary key to reject duplicate edges, a check constraint to reject self-edges, and two foreign keys carrying the same `ResearchTreeVersionId` to make PostgreSQL reject cross-version endpoints.
- Migrated each legacy vehicle edge only where both vehicles have entries in a shared version. Guard SQL aborts when an old edge cannot be represented, preventing silent prerequisite loss.
- The first Batch 5 migration created the alternate key and edge table but omitted the physical endpoint foreign keys. A follow-up migration, `EnforceVehiclePrerequisiteVersionForeignKeys`, corrected that database-integrity defect. PostgreSQL now enforces the invariant itself.
- Kept calculator requests vehicle-based and projected versioned entry edges back to vehicle IDs internally. Explicit `ResearchTreeVersionId` selection remains Batch 7.

## 2026 — Batch 6: versioned layout and folder membership

- Moved integer row/column values and folder membership from `Vehicle` to `VehicleTreeEntry`. Why: these values can change between snapshots and therefore are not part of stable vehicle identity.
- Modeled folder membership as an optional entry-to-entry parent relationship, with `ResearchTreeVersionId` included in the composite foreign key. Why: the database itself must reject cross-version grouping, following the same integrity lesson as Batch 5 prerequisites.
- Removed the stored `IsFolderParent` flag and derive it from children. Tradeoff: the migration rejects inconsistent legacy marker/relationship data instead of choosing which source is authoritative.
- Kept folder grouping independent from prerequisite edges and left calculator/version request selection unchanged. Explicit version selection remains Batch 7.

## 2026 — Batch 7: explicit calculator version selection

- Added `ResearchTreeVersionId` to the single research calculation request contract and removed target-based version inference and multi-version ambiguity failures. Why: stable vehicle IDs do not identify update-dependent RP, rank, or graph state.
- Load the selected version as one read-only split aggregate query containing its entries, vehicle identities, ranks, and prerequisite edges, then traverse in memory. This keeps recursion query-free and makes the version boundary visible in the data-access code.
- Reject owned and filler IDs that have no entry in the selected version instead of ignoring them. Tradeoff: clients must clear stale selections when switching versions, but calculations cannot silently undercount rank progress or accept cross-version input.
- Map an unknown version to HTTP 404 and selected-version membership errors to HTTP 400 through calculation-specific application exceptions. This keeps EF and dictionary implementation exceptions out of the API contract.

## 2026 — Batch 8: RP, SL, and concurrent match estimation

- Total RP and vehicle-purchase SL from the same deduplicated remaining-entry set produced by the existing traversal. Why: ownership boundaries, shared ancestors, explicit fillers, cycle handling, and version isolation must affect both resources identically.
- Require positive average RP and average net SL per match, calculate each ceiling estimate independently, and use their maximum as the simple combined estimate. Why: both resources accrue concurrently, so the slower constraint determines the estimate; broader economy simulation remains out of scope.
- Use `long` totals and integer ceiling division, and expose each required vehicle's remaining SL alongside its RP. Why: this avoids premature truncation or narrow total overflow while giving a future frontend enough information to explain the totals. No persistence change or migration was needed because `VehicleTreeEntry` already owns both costs.

## Log format

Append new entries at the end. Keep each entry to the decisions and tradeoffs that are not obvious from the code; do not duplicate content that belongs in DOMAIN.md, DATABASE.md, or ARCHITECTURE.md.
