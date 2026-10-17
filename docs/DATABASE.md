# Database

## CURRENT — EF Core/PostgreSQL model

Migrations build the following entities/tables. `Nation` and `VehicleType` are stable identities; a `ResearchTree` is one Nation + VehicleType combination. `GameUpdate` identifies a War Thunder update, and `ResearchTreeVersion` is one tree's snapshot for one update with version-owned ranks, entries, prerequisites, and layout/folder data.

| Entity | Current data and relationships |
| --- | --- |
| `Nation` | ID and name; has many `ResearchTree` rows. |
| `VehicleType` | ID and name (Ground, Aviation, ...); has many `ResearchTree` rows. |
| `ResearchTree` | Unique Nation + VehicleType combination; owns `Vehicles` and `ResearchTreeVersion` rows. Deletion is restricted while vehicles or versions exist. |
| `GameUpdate` | ID, unique version string, optional name and release date; has many `ResearchTreeVersion` rows. Deleting a game update is restricted while versions exist. |
| `ResearchTreeVersion` | Belongs to one `ResearchTree` and one `GameUpdate` (pair is unique); has a `Status` (Draft/Published). Foreign keys from `ResearchTree` and `GameUpdate` use `DeleteBehavior.Restrict` to protect historical snapshot data. |
| `TreeRank` | Belongs to a `ResearchTreeVersion` (version + rank number is unique); holds `RequiredVehiclesUnlocked`; has many `VehicleTreeEntry` rows. Deleting a version cascades its ranks. |
| `Vehicle` | Stable identity within exactly one `ResearchTree`: required `ResearchTreeId`, name, optional image URL (`ImageUrl`), and optional direct wiki page URL (`WikiUrl`, max length 2048). Unique `(ResearchTreeId, NormalizedName)` with database-generated `NormalizedName = lower(trim(Name))`. Alternate key `(ResearchTreeId, Id)` supports entry integrity. Has many `VehicleTreeEntry` rows; owns no progression/layout state. Tree deletion is restricted. |
| `VehicleTreeEntry` | Version-specific placement: belongs to one `ResearchTreeVersion`, one `Vehicle`, and one `TreeRank` (version + vehicle pair is unique). Required `ResearchTreeId` is shared by composite FKs to `Vehicles(ResearchTreeId, Id)` and `ResearchTreeVersions(ResearchTreeId, Id)`, preventing cross-tree entries. Holds RP/SL costs, acquisition type (`VehicleAcquisitionType`, integer-backed, default `Researchable`), integer tree row/column, and an optional same-version folder-parent entry. |
| `VehiclePrerequisite` | Composite key of vehicle tree entry ID and prerequisite vehicle tree entry ID (`VehicleTreeEntryId`, `PrerequisiteVehicleTreeEntryId`); both reference `VehicleTreeEntry(ResearchTreeVersionId, Id)` via composite foreign keys with shared `ResearchTreeVersionId`, forming version-isolated explicit progression edges. |
| `AdminAccounts` | Persistent administrator accounts: GUID `Id` primary key, `Username` (max 64), unique index on `NormalizedUsername` (max 64), `PasswordHash` (PBKDF2), `IsActive`, and integer `SecurityStamp`. Has many `AdminAccountRoles` with `DeleteBehavior.Cascade`. |
| `AdminAccountRoles` | Administrator role assignments: composite primary key `(AdminAccountId, Role)`. `Role` is restricted by check constraint `CK_AdminAccountRoles_SupportedRole` to `('Admin', 'Publisher', 'Editor')`. Foreign key to `AdminAccounts` cascades on account deletion. |
| `PlayerAccounts` | Player accounts: GUID `Id` primary key, `Username` (max 64, unique index on `NormalizedUsername`), `Email` (max 254, unique index on `NormalizedEmail`), `PasswordHash` (PBKDF2), `OptimizationMode` (check constraint `IN (0, 1, 2, 3)`), nullable `AverageRpPerMatch` and `AverageNetSlPerMatch` (check constraints `IS NULL OR > 0`), and UUID `ProgressStamp`. Has many `PlayerUnlockedVehicles` with `DeleteBehavior.Cascade`. |
| `PlayerUnlockedVehicles` | Persistent unlocked vehicle ownership: composite primary key `(PlayerAccountId, VehicleId)`. Foreign key to `PlayerAccounts` cascades on player deletion (`DeleteBehavior.Cascade`); foreign key to `Vehicles` restricts deletion (`DeleteBehavior.Restrict`), preventing vehicle deletion while unlocked by any player. Index on `VehicleId`. |

`Vehicle` holds stable identity and presentation metadata only. Version-specific costs, rank association, coordinates, and folder membership live on `VehicleTreeEntry`; rank definitions and rank-gate requirements live on `TreeRank`; progression edges live on `VehiclePrerequisite`. Both prerequisite endpoints use composite foreign keys referencing `VehicleTreeEntries(ResearchTreeVersionId, Id)`. Folder membership is an optional self-reference using `(ResearchTreeVersionId, FolderParentEntryId)` to the same alternate key, so a parent from another version cannot satisfy the database constraint. `IsFolderParent` is derived from child entries and is not stored.

Administrative identity and authorization:
- `AdminAccounts` enforces case-insensitive username uniqueness via physical index `IX_AdminAccounts_NormalizedUsername`. `SecurityStamp` increments whenever credentials, roles, or active state mutate, immediately invalidating active JWTs.
- `AdminAccountRoles` maps roles (`Admin`, `Publisher`, `Editor`) with check constraint `CK_AdminAccountRoles_SupportedRole`. Foreign key to `AdminAccounts` cascades upon deletion.

Player identity and progress synchronization:
- `PlayerAccounts` enforces case-insensitive uniqueness on `NormalizedUsername` and `NormalizedEmail`. Physical check constraints guard calculator preferences (`OptimizationMode` within defined enum range, positive match averages). `ProgressStamp` (UUID) enables optimistic concurrency during full-set progress replacements.
- `PlayerUnlockedVehicles` records ownership by stable `VehicleId` rather than versioned entries. It cascades on `PlayerAccounts` deletion (`DeleteBehavior.Cascade`) and restricts `Vehicles` deletion (`DeleteBehavior.Restrict`), protecting player progress from catalog vehicle deletions.

Research calculation is a read-only, version-rooted query. It selects one `ResearchTreeVersion` by the caller-provided ID and loads that version's `TreeRanks`, `VehicleTreeEntries` with stable vehicle identities, and entry prerequisite edges using `AsNoTracking` and a split aggregate query. Recursive traversal then runs in memory, so it does not issue per-node database queries and cannot discover data from another version. Caller-provided target, owned, and filler vehicle IDs are validated against the selected version's entry map before traversal.

The `VersionVehicleLayoutAndFolders` migration adds entry layout/folder columns, copies every legacy vehicle's row/column to each of its entries, and maps a legacy vehicle parent to the matching parent entry in each child's version. It aborts before dropping legacy columns if a relationship lacks a same-version entry mapping, if the legacy marker disagrees with the relationship data, or if a marked parent cannot be derived in an entry version. Deleting a `Vehicle` remains restricted while entries reference it. Development seed data writes layout coordinates directly to sample entries; folder membership remains independent from prerequisite seed edges.

## TARGET — conceptual persistence model

| Concept | Responsibility |
| --- | --- |
| `Nation`, `VehicleType` | Stable nation and tree-type identities. |
| `ResearchTree` | One unique Nation + VehicleType combination, conceptually. |
| `GameUpdate`, `ResearchTreeVersion` | Identify the update and a historical snapshot of a tree; implemented with version-owned ranks, entries, prerequisites, and layout data. |
| `Vehicle` | Stable vehicle identity within one ResearchTree across its snapshots. |
| `TreeRank` or equivalent | Version-specific rank and rank-gate configuration. |
| `VehicleTreeEntry` or equivalent | Version-specific vehicle membership, cost, rank, availability, visual position, and folder/group placement. |
| Versioned prerequisite edge | Explicit progression dependency within a tree snapshot. |

These concepts have concrete implementations in the current schema. Published versions are immutable historical snapshots: a new game update creates new tree state by cloning an existing version into an editable `Draft` via `IVersionLifecycleService.CloneVersionAsync`, editing the draft, and publishing via `PublishVersionAsync`. Immutability is enforced in `ApplicationDbContext`, rejecting any additions, modifications, or deletions of `ResearchTreeVersion`, `TreeRank`, `VehicleTreeEntry`, and `VehiclePrerequisite` entities belonging to published versions.

Acquisition type is stored on `VehicleTreeEntry.AcquisitionType` as an integer-backed `VehicleAcquisitionType` enum (default `Researchable = 0`). The `AddVehicleAcquisitionType` migration safely adds the non-null integer column with default 0.

## Batch 23 ownership migration and constraints

Vehicle is stable identity within one ResearchTree. Update-specific progression state remains in VehicleTreeEntry.

`20261015142100_ScopeVehiclesToResearchTree` audits each vehicle through its entries and versions. Exactly one distinct tree permits backfill, including vehicles present in several versions of that tree. Zero or multiple trees abort the transaction with a total count and up to 20 vehicle IDs, names, and candidate tree IDs. Duplicate `lower(trim(Name))` values within a tree also abort with IDs rather than merging identities. Resolve reported rows explicitly before retrying; no arbitrary tree selection, vehicle duplication, or player-ownership deletion occurs. The migration locks the source tables against concurrent writes while auditing and backfilling, then makes ownership columns required and installs the constraints.

`Vehicles.ResearchTreeId -> ResearchTrees.Id` uses Restrict, as do entry-to-vehicle and player-to-vehicle references. The existing version-to-entry cascade for deliberate draft deletion remains; no new path cascades from trees to vehicles, versions, or player progress. Published immutability stays enforced in `ApplicationDbContext`. Composite alternate keys on vehicles and versions also make tree ownership immutable for tracked EF entities. No update API reparents vehicles.

The generated normalized-name column makes the unique index authoritative for concurrent API requests and direct SQL. Normalization uses the database's `lower(trim(Name))` semantics. No new package, collation extension, or application-maintained normalization field is required.

Down restores the previous entry foreign keys without changing vehicle IDs or player rows. It refuses to drop ownership while entryless vehicles exist, because their tree mapping could not be recovered on a later Up. Historical migrations remain unchanged; some legacy raw SQL omits trailing semicolons, so the PostgreSQL verifier recreates its baseline using individual migration commands, matching EF's runtime execution.

Run `powershell -NoProfile -File tests/GrindingThunder.Api.Tests/Verify-VehicleOwnershipPostgres.ps1` against the configured development PostgreSQL database. It audits existing pre-Batch-23 data, tests safe/unowned/multi-tree/duplicate fixtures, Up/Down/Up, both composite FKs, uniqueness, required ownership, delete restrictions, stable player IDs, synchronization, and version-specific calculation. Fixtures use private schemas inside rolled-back transactions. It does not migrate the development schema. Batch 23 was additionally applied to development PostgreSQL after the audit passed; an exact before/after comparison preserved existing vehicle identities, entries, versions, ranks, prerequisites, and player ownership. Subsequent live REST checks removed all their fixtures; the existing Development startup initializer added one missing `dev-sample` published version with eight ranks, without modifying any pre-existing records.

## Batch 36 vehicle wiki metadata migration

The `20261017033110_AddVehicleWikiUrl` migration adds `WikiUrl` (`character varying(2048)`, nullable) to `Vehicles`. It does not modify primary keys, foreign keys, constraints, or existing rows. All existing stable `VehicleId` values, player progress (`PlayerUnlockedVehicles`), and versioned tech-tree entries remain fully preserved. Down drops the column cleanly without data loss to other fields.
