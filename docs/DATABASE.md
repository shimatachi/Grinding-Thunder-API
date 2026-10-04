# Database

## CURRENT — EF Core/PostgreSQL model

Migrations build the following entities/tables. `Nation` and `VehicleType` are stable identities; a `ResearchTree` is one Nation + VehicleType combination. `GameUpdate` identifies a War Thunder update, and `ResearchTreeVersion` is one tree's snapshot for one update — the versioning backbone introduced so far.

| Entity | Current data and relationships |
| --- | --- |
| `Nation` | ID and name; has many `ResearchTree` rows. |
| `VehicleType` | ID and name (Ground, Aviation, ...); has many `ResearchTree` rows. |
| `ResearchTree` | Unique Nation + VehicleType combination; has many `ResearchTreeVersion` rows. |
| `GameUpdate` | ID, unique version string, optional name and release date; has many `ResearchTreeVersion` rows. |
| `ResearchTreeVersion` | Belongs to one `ResearchTree` and one `GameUpdate` (pair is unique); has a `Status` (Draft/Published). |
| `TreeRank` | Belongs to a `ResearchTreeVersion` (version + rank number is unique); holds `RequiredVehiclesUnlocked`; has many `VehicleTreeEntry` rows. Deleting a version cascades its ranks. |
| `Vehicle` | Stable identity and presentation metadata: name and optional image URL. Has many `VehicleTreeEntry` rows; owns no tree layout or folder state. |
| `VehicleTreeEntry` | Version-specific placement: belongs to one `ResearchTreeVersion`, one `Vehicle`, and one `TreeRank` (version + vehicle pair is unique); holds RP/SL costs, integer tree row/column, and an optional same-version folder-parent entry. |
| `VehiclePrerequisite` | Composite key of vehicle tree entry ID and prerequisite vehicle tree entry ID (`VehicleTreeEntryId`, `PrerequisiteVehicleTreeEntryId`); both reference `VehicleTreeEntry(ResearchTreeVersionId, Id)` via composite foreign keys with shared `ResearchTreeVersionId`, forming version-isolated explicit progression edges. |

`Vehicle` holds stable identity and presentation metadata only. Version-specific costs, rank association, coordinates, and folder membership live on `VehicleTreeEntry`; rank definitions and rank-gate requirements live on `TreeRank`; progression edges live on `VehiclePrerequisite`. Both prerequisite endpoints use composite foreign keys referencing `VehicleTreeEntries(ResearchTreeVersionId, Id)`. Folder membership is an optional self-reference using `(ResearchTreeVersionId, FolderParentEntryId)` to the same alternate key, so a parent from another version cannot satisfy the database constraint. `IsFolderParent` is derived from child entries and is not stored.

Research calculation is a read-only, version-rooted query. It selects one `ResearchTreeVersion` by the caller-provided ID and loads that version's `TreeRanks`, `VehicleTreeEntries` with stable vehicle identities, and entry prerequisite edges using `AsNoTracking` and a split aggregate query. Recursive traversal then runs in memory, so it does not issue per-node database queries and cannot discover data from another version. Caller-provided target, owned, and filler vehicle IDs are validated against the selected version's entry map before traversal.

The `VersionVehicleLayoutAndFolders` migration adds entry layout/folder columns, copies every legacy vehicle's row/column to each of its entries, and maps a legacy vehicle parent to the matching parent entry in each child's version. It aborts before dropping legacy columns if a relationship lacks a same-version entry mapping, if the legacy marker disagrees with the relationship data, or if a marked parent cannot be derived in an entry version. Deleting a `Vehicle` remains restricted while entries reference it. Development seed data writes layout coordinates directly to sample entries; folder membership remains independent from prerequisite seed edges.

## TARGET — conceptual persistence model

| Concept | Responsibility |
| --- | --- |
| `Nation`, `VehicleType` | Stable nation and tree-type identities. |
| `ResearchTree` | One unique Nation + VehicleType combination, conceptually. |
| `GameUpdate`, `ResearchTreeVersion` | Identify the update and a historical snapshot of a tree; implemented with version-owned ranks, entries, prerequisites, and layout data. |
| `Vehicle` | Stable vehicle identity across snapshots. |
| `TreeRank` or equivalent | Version-specific rank and rank-gate configuration. |
| `VehicleTreeEntry` or equivalent | Version-specific vehicle membership, cost, rank, availability, visual position, and folder/group placement. |
| Versioned prerequisite edge | Explicit progression dependency within a tree snapshot. |

These are conceptual names, not committed table or column names. Published versions should be immutable historical snapshots: a new game update creates new tree state instead of overwriting an earlier published version. The repository is early-stage, so the current migrations and sample database may be reset when the corrected foundational model is implemented rather than preserving prototype migrations at all costs. The exact migration plan, keys, and constraints belong to that implementation milestone.

Acquisition type is expected to belong to versioned vehicle-tree state or closely related progression data. Its final persistence representation will be decided in a later milestone.
