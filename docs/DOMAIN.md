# Domain

## Principles

A `Vehicle` is the stable identity of a War Thunder vehicle. Update-dependent values belong to versioned tech-tree data rather than replacing that identity. Published tree versions are immutable historical snapshots. Progression is a directed graph of explicit prerequisites; visual row and column placement does not define dependencies. Rank unlock requirements are data-driven.

## TARGET — conceptual model and invariants

- `Nation` identifies a country independently of vehicle-tree type. `VehicleType` identifies Aviation, Ground, Helicopter, Coastal Fleet, or Bluewater Fleet. A `ResearchTree` is one nation plus one vehicle type.
- `GameUpdate` identifies a War Thunder update. A `ResearchTreeVersion` is a snapshot of one research tree for an update. Draft versions are editable; publishing creates the immutable historical snapshot players can select. The version lifecycle is managed by `IVersionLifecycleService`: existing versions can be cloned into isolated `Draft` snapshots for a new update, edited, published to `Published` status, or unneeded drafts can be deleted. Published versions cannot be deleted, modified, or have ranks/entries/prerequisites added/modified/removed. Corrections use a new version rather than silently rewriting history.
- `Vehicle` retains stable identity across versions. A versioned vehicle placement/state records its membership in a tree version and its RP cost, SL cost, rank, visual position, research availability, and folder/group placement. Changes to these values create new version data; they do not rewrite published history.
- Rank and rank-gate configuration belong to the tree version. The required count or other rank rule must come from data, not code constants. The exact eligibility and counting rules need validation against the game.
- A versioned prerequisite edge explicitly links a vehicle placement to a prerequisite in the same snapshot. Traversing edges toward prerequisites finds required vehicles. Shared prerequisites should be counted once. An already-owned vehicle that is required on the route is a traversal boundary: do not automatically charge historical prerequisites behind it again.
- Visual tree position and folder/group placement describe presentation. Neither implies a progression edge. Progression graph acyclicity (DAG), rank consistency, folder hierarchy limits (1 level, same rank, no cycles), coordinate collision rules, and acquisition progression invariants are strictly validated by `IVersionLifecycleService.ValidateVersionAsync` prior to publication. Historical snapshots can be compared via `IVersionLifecycleService.CompareVersionsAsync`.

## CURRENT — initial match estimate

RP matches = ceiling(total remaining RP / average RP per match). SL matches = ceiling(total remaining vehicle-purchase SL / average net SL per match). Because RP and SL are earned concurrently, the overall estimate is the greater of these two values. Both averages must be greater than zero. A zero remaining cost produces zero matches for that resource. This is an initial estimation rule, not a complete simulation of War Thunder economy mechanics.

## CURRENT — acquisition types and progression eligibility

Acquisition type is version-specific and modeled on `VehicleTreeEntry` via `VehicleAcquisitionType`:
- `Researchable` (0): standard tech-tree vehicle that can be targeted or selected as filler.
- `Premium` (1), `Squadron` (2), `Event` (3), `Pack` (4), `Hidden` (5): non-standard acquisition categories.

Progression rules:
- Only `Researchable` vehicles can be researched via normal vehicle RP progression (targeting or selecting non-researchable entries as fillers throws a calculation error). Non-researchable entries are never auto-selected as rank-gate fillers.
- Any owned/unlocked vehicle (regardless of acquisition type, including Premium, Squadron, and Event vehicles) counts towards fulfilling rank-gate unlock quotas for higher ranks.
- Owned vehicles are never added to the remaining required research set or costs.
- When explicit fillers leave a shortfall for a rank gate below the target rank, the calculator automatically selects eligible `Researchable` vehicles in that rank to satisfy the quota.
- Auto-selected vehicles transitively pull any unresearched prerequisite vehicles they require, and all of them are included in `RequiredVehicles` with `IsRankGateFiller = true`.
- Route optimization mode (`ResearchOptimizationMode`) controls candidate ordering:
  - `LeastRp` (default): lowest `RpCost` ascending, then `SlCost` ascending, then `Vehicle.Name` ascending (greedy baseline).
  - `LeastSl`: lowest `SlCost` ascending, then `RpCost` ascending, then `Vehicle.Name` ascending.
  - `FewestVehicles`: lowest additional vehicle count (including transitive prerequisites) ascending, then total chain RP ascending, then total chain SL ascending, then `Vehicle.Name` ascending.
  - `FewestMatches`: lowest projected concurrent match requirement $\max(\lceil\text{TotalRP} / \text{avgRP}\rceil, \lceil\text{TotalSL} / \text{avgSL}\rceil)$ ascending, then additional RP ascending, then additional SL ascending, then `Vehicle.Name` ascending.
- Tie-breaking across all modes deterministically ends on `Vehicle.Name` ascending.
- Evaluation continues across all prerequisite ranks; if available researchable candidates in a rank are exhausted before the quota is satisfied, remaining deficits are reported in `RankDeficits`.

## Current model gaps

`Nation.Type` was replaced by the `Nation`/`VehicleType`/`ResearchTree` model. `Vehicle` now contains stable identity and presentation metadata only (`Id`, `Name`, and `ImageUrl`); its tree memberships are represented by `TreeEntries`. A `VehicleTreeEntry` is unique per `ResearchTreeVersion` + `Vehicle` and owns version-specific RP cost, SL cost, rank association, `AcquisitionType`, `TreeRow`, `TreeColumn`, and optional folder membership. Folder membership links an entry to a parent entry in the same version. Parent status is derived from whether an entry has folder children; it is not stored as a second source of truth. Folder relationships and coordinates are presentation/grouping metadata and never imply prerequisite edges.

`TreeRank` owns version-specific rank numbers and rank-gate requirements. `VehiclePrerequisite` links two versioned entries; composite foreign keys make the database reject cross-version prerequisite edges. Folder membership uses the same version-carrying composite-FK approach, so the database also rejects cross-version folder relationships. Folder layout never affects calculation.

Every calculator request selects one `ResearchTreeVersionId` and an optional `OptimizationMode` (defaulting to `LeastRp`). The target and every filler must have an entry in that version and must be `Researchable`. Every owned vehicle ID must have an entry in that version, but can be of any acquisition type. Unknown versions fail distinctly from invalid vehicle membership. The calculator reads RP/SL costs, ranks, rank gates, and prerequisite edges only from the selected snapshot; it never infers or falls back to another version. The same deduplicated remaining-entry set supplies both cost totals. Owned vehicles (including premiums) count toward rank gates, owned vehicles remain traversal boundaries, shared prerequisite ancestors are counted once, auto-selected candidates satisfy rank shortfalls according to the selected optimization mode without duplicating prerequisites, and traversal protects against cycles. Flat vehicle-list projections remain transitional: flat values are emitted only for a vehicle with exactly one entry, while entry-based projections include their version ID. The version draft/clone/publish workflow is handled by `IVersionLifecycleService` (which validates version integrity prior to publishing and provides historical comparison via `CompareVersionsAsync`) and published version immutability is enforced at the persistence layer.

## CURRENT — admin CRUD, draft editing, and tabular imports

- Catalog entity management: secured administrative endpoints permit CRUD operations for `Nation`, `VehicleType`, `Vehicle`, `GameUpdate`, and `ResearchTree` entities. Vehicle catalog records represent stable identities across all updates.
- Draft-scoped editing: endpoints under `/api/admin/drafts/{versionId}` manage draft-specific `TreeRank`s, `VehicleTreeEntry`s, costs, coordinates, folder parent links, and `VehiclePrerequisite` edges. All mutating actions are guarded by `EnsureDraftVersionAsync` ensuring that published snapshots cannot be modified (HTTP 409 Conflict).
- Canonical tabular import schema: defines application-owned column contracts (`VehicleName`, `RankNumber`, `RpCost`, `SlCost`, `TreeColumn`, `TreeRow`, optional `AcquisitionType`, `FolderParentVehicleName`, `PrerequisiteVehicleNames`, and `RequiredVehiclesUnlocked`). Scraped reference datasets are converted into this schema; raw visual placements or unstructured files are not accepted.
- Two-phase import workflow (`ITechTreeImportService`):
  - Phase 1 (Preview): parses CSV, TSV, or XLSX files via `TabularDataParser` (zero-dependency BCL streaming) and executes comprehensive validation (vehicle catalog matching with optional auto-creation, rank creation detection, root coordinate collisions intra-file and against existing draft entries, folder nesting constraints, prerequisite existence, rank consistency, DAG acyclicity, and rank gate feasibility across all preceding ranks) without mutating database state.
  - Phase 2 (Commit): verifies `Draft` status, executes inside an atomic database transaction, auto-creates missing catalog vehicles if enabled, creates/updates `TreeRank` quotas, upserts `VehicleTreeEntry` records in two passes to link folder parent hierarchies, and synchronizes versioned `VehiclePrerequisite` edges while preserving single-version composite foreign key integrity.

