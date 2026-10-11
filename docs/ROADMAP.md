# Roadmap

A concise implementation roadmap. It records goals and sequencing only; it is not an architecture document. Completed behavior is documented in the current-state docs (`DOMAIN.md`, `DATABASE.md`, `ARCHITECTURE.md`) rather than inferred from this roadmap. The roadmap may change as implementation reveals better designs.

Statuses: **Completed** — implemented and tested; **Planned** — not started.

## Completed

- **Batch 0 — Characterization tests** (status: Completed)
  Goal: capture the existing calculator behavior in tests before any refactoring.
  Dependency: none.

- **Batch 1 — Nation + VehicleType + ResearchTree** (status: Completed)
  Goal: replace `Nation.Type` with `Nation`, `VehicleType`, and `ResearchTree` (one nation + one vehicle type).
  Dependency: Batch 0.

- **Batch 2 — GameUpdate + ResearchTreeVersion** (status: Completed)
  Goal: introduce `GameUpdate` and `ResearchTreeVersion` as versioned snapshots of a research tree.
  Dependency: Batch 1 (a version belongs to a `ResearchTree`).

- **Batch 3 — Vehicle identity + VehicleTreeEntry** (status: Completed)
  Goal: make `Vehicle` stable identity and move RP cost, SL cost, and rank association into a versioned `VehicleTreeEntry` (unique per version + vehicle).
  Dependency: Batch 2 (entries reference a `ResearchTreeVersion`).

- **Batch 4 — Move rank configuration under ResearchTreeVersion / introduce TreeRank** (status: Completed)
  Goal: make rank definitions and rank-gate rules versioned instead of shared across versions.
  Result: `TreeRank` is owned by `ResearchTreeVersion` (version + rank number unique, cascade from version); `VehicleTreeEntry` now references the TreeRank of its own version; the old tree-level `Rank` model and `Ranks` table were removed via the data-preserving `IntroduceTreeRank` migration.

- **Batch 5 — Version prerequisite edges** (status: Complete)
  Goal: make prerequisite edges versioned so each snapshot owns its progression graph.
  Dependency: Batches 3–4 (edges will reference versioned placements/ranks).
  Delivered: Redefined `VehiclePrerequisite` to link `VehicleTreeEntry` instances using composite foreign keys scoped to `ResearchTreeVersionId`. Added database check constraint enforcing no self-references, composite PK preventing duplicate edges, and dual composite FKs enforcing single-version isolation. Updated calculator and API projections. Added data migration script and test coverage.

- **Batch 6 — Move folder and visual layout state into versioned tree data** (status: Completed)
  Goal: move `IsFolderParent`, `FolderParentId`, `TreeColumn`, and `TreeRow` off `Vehicle` into versioned data.
  Result: `VehicleTreeEntry` owns row/column and an optional same-version folder-parent entry; parent status is derived from children. A guarded data migration preserves legacy values, and a composite foreign key prevents cross-version folder relationships.

- **Batch 7 — Make calculator explicitly ResearchTreeVersion-aware** (status: Completed)
  Goal: let calculation requests select a specific tree version, replacing the one-entry-per-vehicle transitional assumption.
  Result: every calculation loads one caller-selected version's entries, ranks, and prerequisite graph; target, owned, and filler IDs are validated against that version; no version inference or fallback remains.

- **Batch 8 — RP + SL + match estimation** (status: Completed)
  Goal: calculate remaining RP, remaining SL purchase cost, RP-match estimate,
  SL-match estimate, and one combined estimate for a selected ResearchTreeVersion.
  Result: RP and SL are totaled from the same deduplicated remaining-entry set;
  each resource estimate rounds up independently, and the combined estimate is
  the greater value. Both per-match averages must be positive.


- **Batch 9 — Acquisition types and progression eligibility** (status: Completed)
  Goal: model researchable, premium, squadron, event, pack/marketplace, hidden,
  and define which entries may participate in normal research/rank-gate progression.
  Dependency: Batch 7 and product/domain decisions.
  Result: modeled `VehicleAcquisitionType` enum on `VehicleTreeEntry`; configured integer-backed persistence with default `Researchable`; added `AddVehicleAcquisitionType` migration; calculator enforces that target and filler vehicles must be `Researchable`, while owned vehicles of any acquisition type count towards rank-gate thresholds; exposed `AcquisitionType` in vehicle and tree API projections.

- **Batch 10 — Automatic rank-gate candidate selection** (status: Completed)
  Goal: replace manual filler selection with automatic selection of valid additional
  vehicles required to satisfy rank-gate deficits.
  Dependency: Batches 8–9.
  Result: when explicit fillers leave a rank-gate shortfall, the calculator
  automatically selects the cheapest eligible researchable vehicles in that rank
  (RP asc, SL asc, name asc for deterministic tie-breaking). Each auto-selected
  vehicle's transitive prerequisite chain is gathered and marked `IsRankGateFiller = true`.
  Evaluation continues through all prerequisite ranks rather than stopping at the
  first deficit. Any remaining shortfall after exhausting candidates is reported
  in `RankDeficits`.

- **Batch 11 — Route optimization modes** (status: Completed)
  Goal: optimize valid progression routes by least RP, least SL, fewest additional
  vehicles, or estimated fewest matches.
  Dependency: Batch 10.
  Result: added `ResearchOptimizationMode` (`LeastRp`, `LeastSl`, `FewestVehicles`, `FewestMatches`)
  to calculation requests, defaulting to `LeastRp`. Auto-selection evaluates candidates
  according to the selected mode while enforcing researchable status, pulling transitive
  prerequisite chains, and ending on deterministic vehicle name tie-breaks.
- **Batch 12 — Draft / clone / publish workflow** (status: Completed)
  Goal: create editable drafts from existing versions and publish immutable historical
  snapshots. Revisit destructive cascade-delete behavior now that versions contain
  historical data.
  Dependency: Batches 3–7.
  Result: changed foreign-key deletion behavior from `ResearchTree` and `GameUpdate` to `ResearchTreeVersion` to `DeleteBehavior.Restrict` (preventing silent destruction of historical snapshot records) via `RestrictResearchTreeVersionCascadeDelete` migration. Enforced published-version immutability in `ApplicationDbContext` intercepting changes to `ResearchTreeVersion`, `TreeRank`, `VehicleTreeEntry`, and `VehiclePrerequisite`. Implemented `IVersionLifecycleService` / `VersionLifecycleService` supporting deep-cloning versions into isolated Draft snapshots (remapping ranks, layout coordinates, folder parents, and composite-FK prerequisite edges), publishing Draft versions to Published status, and deleting unneeded Draft versions while protecting Published snapshots.

- **Batch 13 — Version validation and historical diff** (status: Completed)
  Goal: validate graph/version integrity before publication and compare versions for
  added/removed vehicles, RP/SL/rank/prerequisite/layout changes.
  Dependency: Batch 12.
  Result: implemented comprehensive version validation engine in `IVersionLifecycleService` / `VersionLifecycleService` verifying prerequisite graph acyclicity (DAG DFS cycle detection), rank progression consistency (prerequisite rank <= entry rank, feasible rank-gate quotas against available preceding/same rank vehicles), folder grouping constraints (maximum 1 level of nesting, same rank membership, cycle prevention), coordinate and non-negative cost invariants (root entry collision rejection), and acquisition progression integrity (researchable vehicles cannot depend on non-researchable prerequisites). Integrated validation into `PublishVersionAsync` to reject publication of invalid drafts. Implemented historical diff engine (`CompareVersionsAsync`) evaluating added, removed, and modified vehicles (with RP/SL deltas, rank/coordinate/acquisition shifts), added and removed prerequisite edges, and rank quota changes with net economic summaries. Exposed read-only REST endpoints on `ResearchTreeVersionsController` (`/api/research-tree-versions/diff` and `/api/research-tree-versions/{versionId}/validate`).

- **Batch 14 — Admin authentication and authorization foundation** (status: Completed)
  Goal: introduce administrator authentication and Editor/Publisher/Admin permissions
  before exposing substantial mutation workflows.
  Dependency: Batch 12.
  Result: configured JWT Bearer authentication scheme and granular role-based authorization policies (`RequireEditorRole`, `RequirePublisherRole`, `RequireAdminRole`) in ASP.NET Core; defined administrative roles (`Editor`, `Publisher`, `Admin`); introduced `IPasswordHasher` using PBKDF2 (HMAC-SHA256, 128-bit salt, 100k iterations, constant-time verification) and `IJwtTokenService` for signed token issuance; configured credentials avoiding plaintext secrets in source control (supporting configuration, environment variables, user secrets, and secure development fallbacks); exposed administrative authentication endpoints on `AuthController` (`POST /api/auth/login`, `GET /api/auth/me`, and policy probe endpoints); preserved full backward compatibility for public endpoints (`GET /api/nations`, `GET /api/vehicles`, `POST /api/research/calculate`, `GET /api/research-tree-versions/diff`); and verified with unit and integration tests.

- **Batch 15 — Admin CRUD/workflow APIs** (status: Completed)
  Goal: manage nations, vehicle types, vehicles, updates, drafts, ranks, entries,
  prerequisites, layout, validation and publication through secured APIs.
  Dependency: Batches 12–14.
  Result: exposed secured administrative REST endpoints for catalog entities (`AdminNationsController`, `AdminVehicleTypesController`, `AdminVehiclesController`, `AdminGameUpdatesController`, `AdminResearchTreesController`), version lifecycle management (`AdminResearchTreeVersionsController`), and granular draft editing (`AdminDraftsController`). Enforced role-based authorization policies (`RequireEditorRole`, `RequirePublisherRole`, `RequireAdminRole`) and guarded draft mutations against published version immutability.

- **Batch 16 — Admin CSV/XLSX import workflow** (status: Completed)
  Goal: define a strict application-owned import schema, validate and preview uploaded
  data, and import only into Draft ResearchTreeVersions.
  Dependency: Batches 13–15.
  Note: scraped/source files such as the Israel Aviation CSV are input/reference data,
  not the application's canonical import format.
  Result: defined a strict canonical schema (`VehicleName`, `RankNumber`, `RpCost`, `SlCost`, `TreeColumn`, `TreeRow`, `AcquisitionType`, `FolderParentVehicleName`, `PrerequisiteVehicleNames`, `RequiredVehiclesUnlocked`); implemented a lightweight BCL-based tabular parser (`TabularDataParser`) supporting RFC 4180 CSV, TSV, and OpenXML XLSX without third-party dependencies; implemented `ITechTreeImportService` / `TechTreeImportService` delivering a two-phase workflow (Preview and Commit); preview inspects format, data types, vehicle catalog matching, rank definitions, coordinate collisions, folder hierarchy limits, prerequisite consistency, DAG acyclicity, and rank gate quotas without mutating database state; commit validates Draft status (rejecting published versions with 409 Conflict) and applies vehicles, ranks, entries, layout, folder links, and prerequisite edges inside an atomic database transaction; exposed secured admin endpoints under `/api/admin/drafts/{versionId}/import/preview` and `/api/admin/drafts/{versionId}/import` (Editor role, multipart/form-data); verified with unit and integration tests.

## Later

- Back-office web UI for the admin APIs
- Player accounts and synchronized owned-vehicle progress
- Vehicle modification progression
- Advanced War Thunder economy/RP mechanics
- Assisted scraping/data-conversion tools
- Performance/caching only when measurements justify it
