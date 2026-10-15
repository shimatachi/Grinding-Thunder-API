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

- **Batch 16.5 — Repository reorganization (src / tests monorepo layout)** (status: Completed)
  Goal: reorganize repository into a clean monorepo layout with API project under `src/GrindingThunder.Api`
  and test project under `tests/GrindingThunder.Api.Tests`, without changing application behavior.
  Result: relocated API project and Dockerfile under `src/GrindingThunder.Api`; relocated tests under
  `tests/GrindingThunder.Api.Tests`; renamed root solution to `GrindingThunder.sln` with updated project paths;
  cleaned up redundant test folder compile/content exclusions from `GrindingThunder.Api.csproj`; updated Dockerfile
  paths for root build context; verified builds, all 235 unit/integration tests pass, EF migrations list resolves,
  and HTTP profile starts successfully.

- **Batch 17 — Admin back-office foundation** (status: Completed)
  Goal: add a separate ASP.NET Core Razor Pages admin client that authenticates
  against and consumes the existing REST API.
  Result: added separate ASP.NET Core Razor Pages application under `src/GrindingThunder.Admin` (port 5035);
  implemented cookie session authentication with login/logout workflows communicating with `POST /api/auth/login`;
  implemented `BearerTokenHandler` delegating handler for propagating stored JWT tokens to outgoing REST API calls;
  built responsive back-office navigation shell, dark theme layout, role badges, live session dashboard verifying connectivity
  via `GET /api/auth/me`, and placeholder modules for Batches 18–20; added test suite in `tests/GrindingThunder.Admin.Tests`
  covering unauthenticated redirects, login sessions, error feedback, and token propagation.

- **Batch 18 — Admin catalog management** (status: Completed)
  Goal: build Razor Pages CRUD screens for:
  - Nations
  - Vehicle Types
  - Vehicles
  - Game Updates
  - Research Trees

  UI direction:
  - keep Razor Pages;
  - adopt Tabler as the admin UI kit;
  - use Tabler's Bootstrap 5 foundation;
  - keep custom CSS minimal;
  - do not introduce Tailwind;
  - do not migrate the admin app to Blazor in this batch.

  Dependency: Batch 17.
  Result: adopted Tabler through pinned CDN assets and replaced the catalog placeholder with authenticated Razor Pages screens backed by the existing secured REST API; delivered list/create/edit/delete workflows where supported for nations, vehicle types, vehicles, game updates, and research trees, including Admin-only delete confirmation, API error feedback, and hermetic integration coverage. No API, persistence, or migration changes were required.

- **Batch 19 — Research-tree version editor** (status: Completed)
  Goal: provide admin workflows for:
  - research-tree versions;
  - drafts;
  - ranks;
  - vehicle entries;
  - prerequisites;
  - folder/group relationships;
  - visual row/column layout.

  UI direction:
  - keep Razor Pages as the default;
  - evaluate whether selective Blazor components or a dedicated JavaScript graph/tree component are justified for highly interactive editing;
  - do not convert the whole admin application to Blazor unless the editor requirements clearly justify it.

  Dependency: Batches 17–18.
  Result: delivered HTTP-backed Razor Pages version browsing, draft creation/cloning, Admin-only draft deletion, and draft rank/entry/cost/acquisition/layout/folder/prerequisite editing. Admin inherits Editor workflows; Publisher-only accounts browse trees and view both draft and published version details read-only through public projections (`GET /api/nations`, `GET /api/vehicles/tree`) without invoking Editor-only admin endpoints. Destructive forms require confirmation, published snapshots remain read-only, and displayed entries are scoped by exact version ID with no fallback. `TestMockApiHandler` enforces real backend role policies on admin routes. Single-role integration regressions pass; no API, domain, schema, migration, or dependency changes were required. Import, validation, diff, and publishing UI remain Batch 20.

- **Batch 20 — Import, validation, diff, and publishing UI** (status: Completed)
  Goal: expose the existing backend workflows through the back office:
  - CSV/XLSX upload;
  - import preview;
  - validation errors;
  - commit to Draft;
  - version validation;
  - historical diff;
  - publish confirmation/workflow.

  UI direction:
  - reuse Tabler forms, tables, alerts, tabs, badges, modals, and file-upload presentation;
  - use minimal JavaScript/interactivity only where useful.

  Dependency: Batch 19.
  Result: delivered Editor/Admin CSV, TSV, and XLSX upload preview and commit with diagnostics, counts, and a short-lived server-side copy of valid preview bytes; public validation and same-tree version comparison screens; and Publisher/Admin draft publication with explicit immutable-snapshot confirmation. Published versions transition to read-only presentation. The admin mock now simulates the endpoints with backend-matching role checks, and integration tests cover import, validation, diff, and publication permissions. No API, schema, domain, migration, or third-party UI changes were made.

- **Batch 21 — Persistent admin accounts** (status: Completed)
  Goal: replace configuration-backed admin credentials with database-backed administrator accounts and lifecycle management.

  Scope should include planning for:
  - persistent admin identity;
  - password hashes;
  - Admin / Publisher / Editor roles;
  - active/disabled state;
  - account creation/editing;
  - safe bootstrap of the first administrator.

  At planning time, compare:
  - minimal custom admin identity model;
  - ASP.NET Core Identity.

  Choose based on actual requirements rather than automatically introducing Identity.

  Dependency: Batch 20.
  Result: delivered custom `AdminAccounts` and `AdminAccountRoles` entities with PBKDF2 hashing, case-insensitive normalized username uniqueness, role check constraint (`Admin`, `Publisher`, `Editor`), and cascade deletion via the `AddAdminAccounts` migration. Implemented safe first-admin bootstrap from configuration (`AdminBootstrap:Username` / `AdminBootstrap:Password`) enforcing managed account length/strength validation, fail-fast on invalid/partial config, safe concurrency race recovery, and zero password/hash logging; runtime token revocation via `SecurityStamp`; last-active-Admin preservation in `AdminAccountService` with ambient transaction support; protected REST lifecycle endpoints on `AdminAccountsController`; and Admin-only Razor Pages management UI (`/Accounts`) in `GrindingThunder.Admin` with password replacement and conflict feedback.

- **Batch 22 — Player accounts and synchronized progress** (status: Completed)
  Goal: add player-facing identity and persist:
  - owned/unlocked vehicles;
  - calculator preferences;
  - progression state across sessions.

  Admin and player identity should remain conceptually separate even if they later share authentication infrastructure.

  Dependency:
  - stable production-quality tech-tree data;
  - Batch 21 identity decisions.
  Result: delivered `PlayerAccounts` and `PlayerUnlockedVehicles` entities via the `AddPlayerAccounts` migration, featuring PBKDF2 hashing, case-insensitive normalized username and email uniqueness, check constraints on preferences (`OptimizationMode`, `AverageRpPerMatch > 0`, `AverageNetSlPerMatch > 0`), and separate JWT audience (`GrindingThunder.Player`) with dedicated `RequirePlayerPolicy`. Unlocked vehicle progress attaches to stable `VehicleId` (cascading on player deletion, restricting catalog vehicle deletion). Implemented atomic full-set progress synchronization (`PUT /api/player/progress`) with optimistic concurrency (`ProgressStamp`). Traversal and calculator endpoints remain anonymous and decoupled, evaluating explicit caller-selected version intersections. Both migrations verified with transactional Up/Down/Up and constraint validation on PostgreSQL.

- **Batch 23 — Tree-scoped vehicle ownership** (status: Completed)
  Goal: make stable identity belong to exactly one ResearchTree while preserving
  version-specific progression state and player-owned Vehicle IDs.
  Result: required tree ownership, tree-scoped normalized-name uniqueness, shared-tree
  composite entry FKs, guarded data-preserving backfill, ownership-aware vehicle DTOs
  and creation, scoped import matching/auto-creation, and minimal Admin compatibility.
  Vehicle is stable identity within one ResearchTree. Update-specific progression
  state remains in VehicleTreeEntry.
  Verification: restore/build; all 338 API and 132 Admin tests; PostgreSQL safe,
  unowned, multi-tree and duplicate fixtures; Up/Down/Up; physical constraints;
  stable player IDs; live REST vehicle/import checks; development migration applied
  after a clean ownership audit, preserving all pre-existing progression records.
  Existing Development startup seed additionally created one missing dev-sample
  published version with eight ranks; no pre-existing records changed.
  Full admin workflow redesign remains Batch 24.
  Dependency: Batch 22.

---

## Next

- **Batch 24 — Nation -> Category -> Vehicle admin workflow** (status: Planned)
  Goal: reorganize the back office around the domain hierarchy
  Nation -> ResearchTree/category -> Vehicle so administrators manage game data
  contextually instead of through disconnected global CRUD screens.

  Intended admin flow:

  `Tech Trees -> Nation -> Category / ResearchTree -> Vehicles`

  Examples:
  - USA
    - Aviation
      - Vehicles
    - Ground
      - Vehicles
    - Helicopter
      - Vehicles

  Scope:
  - keep the existing Dashboard unless small navigation changes are necessary;
  - make Nation the primary entry point for tech-tree administration;
  - present each Nation's ResearchTrees using user-facing category names such as
    Aviation, Ground, Helicopter, Coastal Fleet, and Bluewater Fleet;
  - manage Vehicles from inside their owning ResearchTree;
  - support creating a Vehicle directly in its Nation/category context without
    requiring the administrator to choose ownership again;
  - allow Nation setup to associate/create applicable ResearchTrees from the
    existing master VehicleType/category set;
  - treat `VehicleType` as the existing master category concept rather than
    introducing a duplicate Category entity;
  - move low-frequency reference-data management under an Administration area;
  - preserve existing role and authorization rules;
  - preserve existing Razor Pages + Tabler + Bootstrap 5 approach;
  - keep `GrindingThunder.Admin` as an HTTP client of `GrindingThunder.Api`;
  - reuse existing API endpoints where practical and add only bounded API
    orchestration/support where required by the new workflow.

  Suggested navigation:

  - Dashboard
  - Tech Trees
    - Nation
      - Category / ResearchTree
        - Vehicles
        - Versions
  - Game Updates
  - Administration
    - Nations
    - Categories
    - Admin Accounts

  Out of scope:
  - major Game Update release-workspace redesign;
  - automatic multi-tree publishing;
  - public/player frontend redesign;
  - replacing Razor Pages with Blazor;
  - introducing Tailwind;
  - changing versioning or calculator semantics.

  Dependency: Batch 23.

## Later

- **Batch 25 candidate — Game Update release workspace**
  Rework Game Updates into a release-oriented workflow for creating/cloning
  ResearchTreeVersion drafts, reviewing affected trees, validating, diffing, and
  publishing individual tree snapshots for a War Thunder update.

- Public/player web frontend
- Vehicle modification progression
- Advanced War Thunder economy/RP mechanics
- Assisted scraping/data-conversion tooling
- Better visual research-tree editor if the simple editor proves insufficient
- Audit/history improvements for admin operations
- Performance/caching only when measurements justify it
