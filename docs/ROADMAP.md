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
  Contextual admin workflow is delivered in Batch 24.
  Dependency: Batch 22.

- **Batch 24 — Nation -> Category -> Vehicle admin workflow** (status: Completed)
  Goal: reorganize the back office around the domain hierarchy
  Nation -> ResearchTree/category -> Vehicle so administrators manage game data
  contextually instead of through disconnected global CRUD screens.

  Implemented admin flow:

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

  Navigation:

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

  Result: nation-first browsing, contextual stable vehicle CRUD with route-verified
  ownership, missing-category setup from VehicleTypes, and an Administration menu
  for reference data and accounts. Versions remain accessible within each tree;
  version detail and comparison links preserve context. Publisher browsing includes
  vehicles with no version entries through one bounded read-only API endpoint.
  Existing mutation/publication/deletion roles, Dashboard overview, HTTP-only Admin
  boundary, and published snapshots are preserved. No schema or dependency changes.
  Verification: solution build with zero warnings; all 343 API and 166 Admin tests
  pass, including route/form navigation, ownership tampering, cross-tree rejection,
  category conflict feedback, and single-role authorization regressions.
  Dependency: Batch 23.

- **Batch 25 — Shared visual research-tree viewer** (status: Completed)
  Goal: introduce a visual research-tree renderer that represents one
  ResearchTreeVersion using the same structure administrators will eventually edit
  and players will eventually view.

  Product direction:
  - move away from table-only presentation for research-tree versions;
  - render the tree as a visual progression graph similar in spirit to the
    War Thunder research-tree presentation;
  - use one canonical visual representation for both admin and future public/player
    views;
  - keep existing tables/forms/import workflows available as fallback tools.

  Scope:
  - add a read-only visual tree view for one explicit ResearchTreeVersion;
  - render version-defined ranks without assuming ranks start at 1 or are contiguous;
  - display each TreeRank's editable/configured `RequiredVehiclesUnlocked` value;
  - render VehicleTreeEntry nodes using their version-specific:
    - rank;
    - RP cost;
    - SL cost;
    - acquisition type;
    - tree column;
    - tree row;
    - folder/group membership;
  - render VehiclePrerequisite relationships as visual progression edges;
  - render folder/group relationships clearly;
  - support sparse trees such as Israel Aviation where the first rank may be IV;
  - preserve the exact selected ResearchTreeVersion with no fallback to another
    version;
  - provide a reusable tree data/view model suitable for both read-only and future
    edit modes;
  - integrate the viewer into the existing Razor Pages + Tabler admin application;
  - keep the admin application as an HTTP client of GrindingThunder.Api.

  UI direction:
  - use the existing Razor Pages page shell and authentication;
  - use a specialized JavaScript graph/tree component inside the page where useful;
  - do not rewrite the full admin application as a SPA;
  - do not migrate the full admin application to Blazor;
  - keep custom presentation code focused on tech-tree visualization;
  - allow zooming/panning where useful for large trees;
  - preserve usable responsive behavior.

  Important domain rules:
  - TreeRank definitions are version-specific;
  - rank numbers may be sparse and non-contiguous;
  - no code may assume Rank I exists;
  - no code may assume all trees share the same ranks;
  - `RequiredVehiclesUnlocked` is per TreeRank and must not be globally hardcoded;
  - the visual renderer must reflect persisted tree data rather than deriving
    prerequisites from visual proximity.

  Out of scope:
  - drag/drop persistence;
  - editing vehicle details directly from the canvas;
  - creating/removing prerequisite edges visually;
  - Game Update release orchestration;
  - public/player frontend implementation;
  - replacing existing import workflows.

  Result: added a shared, presentation-only visual-tree model that maps either
  secured Draft detail DTOs or public exact-version projections without database
  access. `/Trees/Version/{versionId}` now opens on a read-only Visual Tree tab
  while retaining the complete Table / Details editor as a fallback. Dynamic rank
  bands preserve sparse/non-contiguous rank numbers and show each configured quota;
  vehicle cards use persisted row/column coordinates and show image, costs,
  acquisition type, and folder state. Lightweight local JavaScript draws explicit
  prerequisite and distinct folder SVG paths and provides scroll, button/Ctrl-wheel
  zoom, fit, reset, and pointer panning. Drafts render for Editor/Admin and through
  Publisher-safe public projections; Published snapshots remain read-only for every
  role. No API, schema, migration, package, Node, Blazor, or SPA changes were made.
  Verification: solution build with zero warnings and errors; all 343 API and 173
  Admin tests pass (516 total), including Draft/Published mapping, sparse ranks,
  rank quotas, coordinate and edge mapping, exact-version filtering, and Publisher
  read-only behavior.
  Dependency: Batch 24.

- **Batch 26 — Visual vehicle-entry editor** (status: Completed)
  Goal: turn the visual research-tree viewer into an administrator editing workspace
  for VehicleTreeEntry placement and version-specific vehicle configuration.

  Scope:
  - add explicit edit mode to the visual tree;
  - allow Editor/Admin users to add a Vehicle into a Draft ResearchTreeVersion
    from the visual workspace;
  - allow choosing an existing tree-scoped Vehicle or creating one contextually
    where the current APIs permit it;
  - allow moving vehicle nodes visually;
  - persist visual movement using existing `TreeColumn` / `TreeRow` state;
  - allow editing selected VehicleTreeEntry details through a side panel or
    equivalent contextual editor;
  - support editing:
    - TreeRank;
    - RP cost;
    - SL cost;
    - acquisition type;
    - folder parent/group relationship;
  - preserve Published ResearchTreeVersions as read-only;
  - show validation/API errors in the visual editing workflow;
  - keep table/form editing available as a fallback.

  Intended interaction model:

  `visual canvas -> select node -> detail panel -> save`

  Example detail panel fields:
  - vehicle name / identity context;
  - rank;
  - RP;
  - SL;
  - acquisition type;
  - tree column;
  - tree row;
  - folder parent.

  Persistence rules:
  - visual position must remain application-owned structured data;
  - do not introduce arbitrary free-form pixel coordinates as the canonical
    persistence model unless later requirements prove TreeColumn/TreeRow inadequate;
  - do not mutate Published versions;
  - continue enforcing ResearchTree ownership boundaries.

  UI direction:
  - reuse the Batch 25 renderer;
  - add edit controls only when the selected version is Draft and the current user
    has Editor/Admin permission;
  - prefer a side-panel inspector over placing every editable field directly on
    the node;
  - preserve keyboard/mouse usability where practical.

  Out of scope:
  - visual prerequisite edge creation/removal;
  - undo/redo history unless required to make basic editing safe;
  - multi-user collaborative editing;
  - Game Update release orchestration.

  Result: extended the Batch 25 server-rendered canvas into an explicit Draft-only
  editing workspace for Editor/Admin accounts. Nodes can be selected, dragged or
  nudged on the integer `TreeColumn` / `TreeRow` grid, and saved through a side
  inspector that edits rank, RP/SL costs, acquisition type, coordinates, and
  same-version folder parent. The workspace can place an unused tree-scoped stable
  Vehicle or contextually create one in the owning ResearchTree before placement,
  and can remove an entry with confirmation. Razor handlers retain antiforgery,
  role, Draft-status, tree/version ownership, and validation guards before calling
  the existing HTTP API; API errors remain visible on the workspace. Published
  versions and Publisher-only sessions render no edit controls, and Table / Details
  remains the complete fallback. No backend, persistence, migration, framework,
  npm, or graph-library changes were required. Verification: solution build with
  zero warnings and errors; all 343 API and 185 Admin tests pass (528 total).

  Dependency: Batch 25.


- **Batch 27 — Visual progression and prerequisite editor** (status: Completed)
  Goal: allow administrators to create, inspect, and remove progression
  relationships directly from the visual research-tree editor.

  Scope:
  - render prerequisite edges distinctly from folder/group relationships;
  - allow Editor/Admin users to enter an edge-creation mode;
  - allow selecting a dependent VehicleTreeEntry and its prerequisite visually;
  - persist created relationships through the existing prerequisite API/domain
    rules;
  - allow removing prerequisite edges visually;
  - prevent self-dependencies;
  - prevent cross-version relationships;
  - preserve same-tree/version integrity;
  - surface DAG/cycle validation errors clearly;
  - enforce acquisition progression rules;
  - prevent prerequisites from pointing from a lower-rank vehicle to an invalid
    higher-rank prerequisite;
  - preserve existing validation as the authoritative backend guard.

  Intended interaction model:

  `select/connect node A -> node B -> preview relationship -> save`

  or equivalent interaction supported cleanly by the selected graph library.

  Important rule:
  - visual position must never implicitly create a prerequisite;
  - prerequisites remain explicit VehiclePrerequisite data;
  - moving a node must not alter progression relationships.

  UX:
  - visually distinguish:
    - progression/prerequisite edges;
    - folder/group membership;
    - selected node;
    - validation errors;
  - provide enough contextual information that an administrator can understand why
    an edge is rejected.

  Keep:
  - existing table-based prerequisite management as fallback;
  - import support;
  - validation and historical diff.

  Out of scope:
  - Game Update release workspace;
  - public editing;
  - real-time collaborative editing;
  - automatic prerequisite inference from layout.

  Result: added a Draft-only prerequisite mode for Editor/Admin users alongside
  the existing entry-movement mode. Administrators select the dependent first and
  its prerequisite second, inspect a dashed pending connector, and explicitly save
  through the existing antiforgery-protected `AddPrerequisite` Razor handler.
  Existing prerequisite paths have accessible enlarged hit targets and a selected
  state feeding the existing removal handler. API validation remains authoritative;
  rejected self-references, duplicates, and other returned API errors render inside
  the workspace while preserving edge mode and endpoints; whole-version validation
  remains responsible for cycle, rank, and acquisition progression diagnostics.
  Published versions and Publisher-only sessions render no mutation controls, and
  Table / Details remains the fallback. No API, schema, migration, dependency, npm,
  graph-library, calculator, account, import, validation, or diff changes were made.
  Verification: solution build with zero warnings and errors; all 343 API and 190
  Admin tests pass (533 total).

  Dependency: Batch 26.


- **Batch 28 — Game Update release workspace** (status: Completed)
  Goal: turn Game Updates from primarily catalog records into a release-oriented
  workspace for coordinating ResearchTreeVersion changes across nations and
  categories.

  Intended flow:

  `Create Game Update
    -> review research trees
    -> create/clone Draft versions
    -> edit/import
    -> validate
    -> review diff
    -> publish individual tree snapshots`

  Scope:
  - provide one Game Update detail/workspace page;
  - show relevant ResearchTrees grouped by Nation/category;
  - show whether each tree has:
    - no version for the update;
    - Draft version;
    - validation errors;
    - valid Draft;
    - Published version;
  - allow creating a Draft version for a tree;
  - allow cloning an appropriate existing Published version into the new update;
  - link directly into the visual tree editor;
  - expose validation status;
  - expose historical diff/review;
  - allow Publisher/Admin users to publish individual valid Draft versions;
  - show aggregate release progress across trees.

  Example presentation:

  `Game Update 2.xx`

  - USA
    - Aviation — Draft
    - Ground — Published
    - Helicopter — No Draft
  - Germany
    - Aviation — Valid Draft
    - Ground — Validation Failed

  Publishing direction:
  - continue publishing ResearchTreeVersions independently;
  - do not introduce an atomic "publish entire Game Update" operation in this
    batch;
  - Published versions remain immutable historical snapshots.

  Important rules:
  - no arbitrary source-version selection when cloning;
  - source and target ResearchTree must match;
  - preserve explicit GameUpdate/ResearchTreeVersion relationships;
  - reuse existing validation, diff, clone, and publish services rather than
    duplicating their logic in the UI.

  UI direction:
  - preserve the existing Dashboard;
  - reuse Razor Pages + Tabler;
  - link to the visual editor from each tree's Draft;
  - keep release status easy to scan.

  Out of scope:
  - atomic multi-tree publishing;
  - scheduled release automation;
  - public/player frontend;
  - redesigning core domain versioning.

  Dependency: Batches 25–27.

  Result: added `/Catalog/Game-Updates/{id}/Workspace` and direct links from the
  Game Updates catalog. The workspace displays update metadata, nation/category
  grouping, exact per-update tree status, validation diagnostics, and aggregate
  Published/valid-Draft/invalid-Draft/missing counts. Editor/Admin users can create
  blank drafts or clone the latest Published version belonging to the same tree;
  Draft rows link to the visual editor, import, and preselected diff, while
  Published rows remain inspect-only. Publisher/Admin users can confirm and publish
  one valid Draft at a time. Exact update matching is supported by an additive
  `GameUpdateId` field on the public nation/version projection, and Publisher has
  narrowly scoped read access to individual Game Update metadata. POST handlers
  re-check tree/update/version/source relationships before API calls. No database,
  migration, project-boundary, dependency, or atomic publish changes were made.
  Verification: solution build with zero warnings and errors; all 344 API and 198
  Admin tests pass (542 total), including aggregate calculations, validation error
  feedback, role-specific controls, cross-tree clone rejection, and publication
  status transition.


- **Batch 29 — Visual tree layout and editor polish** (status: Completed)
  Goal: improve readability and precision of the existing visual tree editor while
  preserving integer TreeColumn / TreeRow as the canonical layout model.

  Product direction:
  - make the visual tree comfortable enough to become the primary research-tree
    editing workflow;
  - improve prerequisite-line readability through better spacing and routing;
  - make node placement obvious and precise without introducing arbitrary pixel
    coordinates;
  - keep admin and future public rendering visually consistent.

  Scope:
  - increase and normalize horizontal/vertical spacing between logical grid cells;
  - review the current fixed visual column/row spacing and tune it for large trees;
  - ensure prerequisite connectors have sufficient gutter space between cards;
  - improve connector anchor points so lines enter/leave nodes predictably;
  - reduce connector overlap with vehicle cards where practical;
  - preserve TreeColumn / TreeRow as integer persisted coordinates;
  - add an optional edit-mode grid or cell guides;
  - visually indicate the selected node's current grid position;
  - preserve drag-to-grid behavior;
  - add keyboard or button-based node nudging:
    - column -1 / +1;
    - row -1 / +1;
  - make coordinate changes visible immediately before save;
  - preserve zoom, pan, fit, and reset behavior;
  - keep table/detail editing available as fallback.

  Do not:
  - persist raw browser pixel coordinates;
  - infer prerequisites from node proximity;
  - change graph/domain semantics;
  - modify Published snapshots;
  - introduce a new SPA framework.

  Layout rule:
  - `TreeColumn` and `TreeRow` remain semantic layout coordinates;
  - CSS/renderer spacing converts those logical coordinates into screen positions;
  - changing renderer spacing must not mutate domain data.

  Dependency: Batch 28.

  Result: normalized the presentation grid to 280 px columns and 204 px rows while
  retaining integer `TreeColumn` / `TreeRow` persistence; added edit-only logical
  cell guides, live card and inspector coordinates, guarded arrow-key nudging, and
  synchronized drag/button/input previews. Prerequisite and folder connectors now
  use direction-aware card anchors, gutter stubs, and cubic Bézier routing so paths
  enter and leave cards cleanly. Published snapshots and Publisher-only sessions
  remain read-only, and no API, domain, schema, migration, dependency, or persisted
  coordinate changes were made.
  Verification: Release solution build with zero warnings and errors; all 344 API
  and 199 Admin tests pass (543 total), including new renderer synchronization and
  keyboard/routing asset coverage plus existing draft inspector, prerequisite, and
  read-only regressions.


- **Batch 30 — Vehicle node inspector and direct editing UX** (status: Completed)
  Goal: make clicking a vehicle card the primary way to inspect and edit its
  version-specific information.

  Intended interaction:

  `click vehicle card -> inspector opens -> edit -> save -> canvas updates`

  Scope:
  - selecting a visual node opens/focuses the existing inspector immediately;
  - clearly display stable vehicle identity separately from version-specific data;
  - allow Draft Editor/Admin users to edit:
    - rank;
    - RP cost;
    - SL cost;
    - acquisition type;
    - TreeColumn;
    - TreeRow;
    - folder parent;
  - make TreeColumn and TreeRow especially easy to modify;
  - add coordinate nudge controls in addition to direct numeric input;
  - synchronize inspector changes and visual node position after successful save;
  - clearly indicate unsaved changes;
  - protect against accidental selection loss when changes are unsaved;
  - provide convenient previous/next selected-node navigation where useful;
  - preserve API validation and ownership rules as authoritative;
  - Published versions remain read-only;
  - Publisher-only sessions remain read-only.

  Optional UX if it remains bounded:
  - arrow-key node nudging while edit mode is active;
  - Enter or double-click to focus the inspector;
  - Escape to clear selection;
  - visible selected-node highlight.

  Do not:
  - auto-save every field keystroke;
  - bypass antiforgery/API validation;
  - move stable identity fields into VehicleTreeEntry;
  - introduce arbitrary pixel coordinates.

  Dependency: Batch 29.


- **Batch 31 — Rank-band and research-tree visual styling** (status: Completed)
  Goal: make the shared tree renderer visually closer to an actual War Thunder
  research tree while preserving Grinding Thunder's own presentation and data model.

  Reference direction:
  - strong horizontal Rank IV / V / VI / ... boundaries;
  - clear rank labels;
  - readable separation between progression areas;
  - visually obvious vehicle cards and progression paths.

  Scope:
  - render each TreeRank as a clear horizontal visual band;
  - show rank label prominently at the left edge;
  - show `RequiredVehiclesUnlocked` alongside the rank metadata where appropriate;
  - allow sparse ranks without generating missing bands;
  - add full-width separators between adjacent persisted ranks;
  - improve vertical padding at rank boundaries;
  - optionally use subtle alternating rank-band backgrounds for readability;
  - keep rank headers visible while horizontally navigating a large tree where
    practical;
  - improve card density without making vehicle names/costs unreadable;
  - visually distinguish acquisition types consistently;
  - ensure Premium/Squadron/Event/etc. vehicles remain visually understandable
    without changing progression semantics;
  - improve prerequisite and folder-edge contrast against rank backgrounds;
  - maintain dark-theme compatibility;
  - preserve responsive behavior.

  Important rules:
  - rank numbers are version-defined and may be sparse;
  - no renderer may assume Rank I exists;
  - `RequiredVehiclesUnlocked` remains persisted per TreeRank;
  - visual rank boundaries must derive from TreeRank data;
  - visual styling must never imply a prerequisite that is not explicitly stored.

  Dependency: Batch 30.

  Result: the shared renderer now presents each persisted `TreeRank` as a spacious,
  alternating full-width band with a strong separator, sticky Roman-numeral header,
  and configured unlock quota. Sparse ranks remain exact: the renderer creates no
  synthetic tiers. Vehicle cards use theme-aware semantic accents and badges for all
  six acquisition types, while rank, RP, and SL metadata remain compact and readable.
  Prerequisite and folder connectors retain the Batch 29/30 geometry and hit targets
  with brighter, shadowed strokes for contrast. Live inspector previews update rank
  and acquisition styling without changing persistence, authorization, or progression
  behavior. No API, domain, schema, migration, or dependency changes were made.
  Verification: solution build with zero warnings and errors; all 344 API and 202
  Admin tests pass (546 total), including new sparse-band, quota, acquisition styling,
  and existing Draft/Published/Publisher read-only coverage.


## Completed

- **Batch 32 — Visual layout assistance tools** (status: Completed)
  Goal: make building and maintaining large trees faster without replacing explicit
  administrator control.

  Scope:
  - add optional grid snapping controls;
  - add alignment helpers for selected nodes;
  - support bounded multi-selection if the existing editor architecture can do so
    cleanly;
  - provide actions such as:
    - align column;
    - align row;
    - move selection left/right/up/down;
    - distribute selected nodes vertically;
  - highlight coordinate collisions before save;
  - show occupied logical cells in edit mode;
  - show potential placement cells while dragging;
  - improve visual feedback for folder/group placement;
  - preserve deterministic integer TreeColumn / TreeRow positions.

  Optional:
  - compact / comfortable layout-density preview;
  - temporary line-visibility modes such as:
    - all prerequisites;
    - selected-node prerequisites only;
    - folder relationships only.

  Layout-density settings should be presentation settings unless a concrete product
  requirement proves they must become persisted ResearchTreeVersion data.

  Out of scope:
  - automatic graph layout replacing administrator-authored positions;
  - arbitrary free-form coordinates;
  - collaborative editing;
  - automatic prerequisite inference.

  Dependency: Batch 31.

  Result: added an Edit-mode-only layout toolbar with presentation-only drag
  snapping, bounded same-rank multi-selection (maximum 12 entries), selection
  nudging, row/column alignment, and vertical distribution. Occupied logical
  cells, drag targets, folder relationships, and root-coordinate collisions now
  receive live visual feedback. Multi-entry positions save through the existing
  per-entry coordinate API after the Admin client verifies draft membership,
  same-rank scope, non-negative integer coordinates, and the final root-cell
  collision invariant. Published snapshots and Publisher-only views remain
  read-only. No API contract, database schema, migration, dependency, automatic
  layout, or free-form coordinate model was added.
  Verification: JavaScript syntax check; solution build with zero warnings and
  errors; all 344 API and 205 Admin tests pass (549 total), including new layout
  toolbar, bounded persistence, same-rank guard, and existing read-only coverage.

- **Batch 33 — Shared public research-tree experience** (status: Completed)
  Goal: expose the same canonical visual research-tree representation to players so
  what administrators author is what users see.

  Product rule:

  `Admin visual tree == Public visual tree`

  with the difference that the public version is read-only.

  Scope:
  - reuse the visual-tree data contract and rendering rules established in
    Batches 25-32;
  - allow players to choose:
    - Nation;
    - category / ResearchTree;
    - Published ResearchTreeVersion;
  - default public browsing only to appropriate Published snapshots;
  - render the same:
    - ranks;
    - cards;
    - coordinates;
    - folders;
    - prerequisites;
    - acquisition presentation;
  - support zoom, pan, fit, and vehicle selection;
  - expose vehicle details without mutation controls;
  - ensure Draft versions are not accidentally exposed as ordinary public data;
  - keep the future player-facing application decoupled from Admin authentication.

  Architectural direction:
  - API remains the source of truth;
  - do not duplicate tree-layout business rules independently in Admin and public
    clients;
  - prefer a reusable rendering/data-contract strategy so both views stay
    consistent.

  Dependency: Batches 29-32.

  Result: added an explicitly anonymous `/Tree` Razor Page with cascading Nation,
  category, and Published-version selection, defaulting to the latest Published
  snapshot. The page reuses `VisualTreeMapper.FromPublic`, `_VisualTree.cshtml`, the
  existing CSS/JavaScript, and SVG connector routing, including pan, zoom, fit,
  reset, and read-only vehicle selection/details. Public API discovery now omits
  Draft versions by default; authenticated back-office catalog calls explicitly opt
  into Draft metadata. The public vehicle-tree endpoint requires an exact version
  ID and returns 404 for Draft or mismatched snapshots. No schema, migration,
  framework, or mutation endpoint was added. Verification: JavaScript syntax check;
  solution build with zero warnings and errors; all 346 API and 207 Admin tests pass
  (553 total), including new Published success, Draft rejection, anonymous access,
  default selection, and read-only-control coverage.

- **Batch 34 — Player progression overlay on visual tree** (status: Completed)
  Goal: connect the existing persistent player progress from Batch 22 to the shared
  visual research tree.
  Delivered:
  - Extended `IApiClient` and `ApiClient` with player auth, progress, and research calculation methods.
  - Implemented dual cookie authentication scheme (`PlayerAuth` alongside Admin default) with strict Bearer token handler request-path isolation.
  - Integrated player login, registration, and logout directly on `/Tree` via AJAX handlers and a lightweight modal, preserving the canonical read-only HTML contract (no `<form method="post">`).
  - Implemented player progress loading (`GET /api/player/progress`), intersection with the selected `ResearchTreeVersion`, and persistent ownership toggling (`PUT /api/player/progress` with optimistic concurrency handling) while preserving vehicles owned in other trees. Anonymous visitors can toggle in-memory ownership.
  - Added target vehicle selection and live progression calculation (`POST /api/research/calculate`).
  - Enhanced visual canvas with distinct card badges and accents for Owned, Target, Required Prerequisite, and Rank-Gate Filler nodes, plus active route connector highlights.
  - Added a responsive progression summary HUD drawer displaying remaining RP, remaining SL, estimated matches (with resource breakdown), rank deficits, and target reset.
  - Added full test coverage in `PlayerProgressionOverlayTests` (anonymous calculation, player login/register, cookie isolation, persistent progress intersection, out-of-tree vehicle preservation, optimistic concurrency handling, and logout).
  Verification: 0 warnings, 0 errors, all 559 tests pass across the entire solution.


- **Batch 35 — Visual editor productivity and safety** (status: Completed)
  Goal: add advanced editing conveniences only after the core WYSIWYG workflow has
  proven stable through real content maintenance.
  Delivered:
  - Client-side bounded Undo/Redo history stack in `visual-tree.js` (`undoStack`, `redoStack`, `maxHistorySize = 50`) for unsaved visual Draft mutations (drag/drop moves, inspector coordinate changes/nudges, layout toolbar nudges, column/row alignment, and vertical distribution). Reverting restores node coordinates, updates SVG connectors, recalculates layout heights, updates inspector preview fields/dirty tracking, and refreshes toolbar states.
  - Dedicated Undo and Redo toolbar buttons (`data-tree-undo`, `data-tree-redo`) with live disabled state management and contextual shortcut tooltips (`Undo (Ctrl+Z)`, `Redo (Ctrl+Y)`).
  - Context-aware keyboard shortcuts in visual draft edit mode:
    - `Ctrl+Z` / `Ctrl+Y` (or `Ctrl+Shift+Z`) for canvas undo/redo while bypassing active form inputs.
    - `Escape` to cancel active pointer dragging (restoring pre-drag coordinates and clearing preview), deactivate edge mode, or deselect entries with discard guards.
    - `Delete` / `Backspace` to open remove entry confirmation when a vehicle node is selected outside form inputs.
    - `Arrow keys` (`ArrowLeft`, `ArrowRight`, `ArrowUp`, `ArrowDown`) for fine-grained 1-cell nudging of selected nodes, preserving non-negative coordinate constraints (`TreeColumn >= 0`, `TreeRow >= 0`).
  - Visual validation warnings on nodes and connectors:
    - Highlight coordinate overlaps (`.is-coordinate-collision` with `⚠️ Overlap` badge) and out-of-bounds nodes (`.is-out-of-bounds` with `⚠️ Out of bounds` badge).
    - Highlight invalid prerequisite edges violating rank order (`prerequisite.RankNumber > dependent.RankNumber`) with `.is-validation-warning` dashed danger strokes.
    - Actionable clickable jump navigation: populated `data-tree-collision-list` and page-level `data-validation-message` hooks with `[data-jump-to-node]` buttons that switch tabs, center offending nodes in the viewport, and trigger a focus pulse animation (`.is-jump-highlighted`).
  - Strictly immutable Published snapshots: all undo/redo operations and editing shortcuts are guarded by `isEditing()`; public `/Tree` and Published admin views omit all edit controls.
  - Comprehensive unit and integration test coverage in `VisualEditorProductivityTests.cs` (CSS badges, JS history/shortcut hooks, draft toolbar buttons, published/public read-only exclusion, server validation message hooks, and Node.js state machine simulation).
  Verification: 0 warnings, 0 errors, all 567 tests pass across the entire solution (346 API + 221 Admin).

- **Batch 36 — War Thunder Scraper Integration & Stable Vehicle Wiki Metadata** (status: Completed)
  Goal: integrate the local Python War Thunder scraper with Grinding Thunder without requiring manual workbook cleanup, and extend stable vehicle identity with direct wiki metadata.
  Delivered:
  - Extended stable `Vehicle` identity with nullable `WikiUrl` (`character varying(2048)`), explicitly distinguishing stable vehicle documentation from transient, versioned tech-tree entry data. Generated and verified EF Core migration `20261017033110_AddVehicleWikiUrl`.
  - Updated API DTOs and projections (`VehicleDto`, `VehicleSummaryDto`, `VehicleTreeResponse`) and administrative endpoints (`AdminVehiclesController`) to accept, validate (<= 2048 chars, absolute URL format), persist, and expose `WikiUrl`.
  - Extended canonical tabular import schema (`TabularDataParser` and `TechTreeImportService`) with optional `ImageUrl` and `WikiUrl` columns. Defined and implemented safe non-destructive metadata update policy: auto-created vehicles persist incoming metadata; existing vehicles update nonblank incoming metadata; blank incoming metadata never erases existing nonblank metadata; same-named vehicles in other trees remain strictly isolated.
  - Updated Admin web UI (`GrindingThunder.Admin`): added `WikiUrl` to Vehicle catalog create/edit/list views, visual tree draft creation form, draft inspector, and public `/Tree` read-only details ("View on War Thunder Wiki ↗" external link, safely hidden when null or blank).
  - Upgraded Python scraper (`wt-scraper`): added deterministic Roman numeral rank mapping (I..X -> 1..10), RP/SL cost normalization with N/A placeholder logging, acquisition type mapping with safe fallback to `Hidden` for unknown statuses, globally collision-free placeholder coordinates, and multi-sheet Excel export (`Import` as Sheet 1, `Review Notes` as Sheet 2, `Source Reference` as Sheet 3) alongside canonical CSV.
  - Created 10 Python unit tests in `test_scraper.py` and comprehensive C# integration tests in `Batch36ScraperIntegrationTests.cs` verifying entity storage, migration safety, CRUD operations, public projections, import auto-creation/update/preservation policies, tree scoping isolation, and full import preview validation of representative Israel Aviation fixtures.
  Verification: 0 warnings, 0 errors, all 574 C# tests pass across the solution (353 API + 221 Admin) and all 10 Python scraper tests pass.

- **Batch 37 — Seamless fullscreen visual editor** (status: Completed)
  Goal: make the visual research-tree editor the primary back-office editing
  workspace by eliminating full-page refreshes during ordinary Draft mutations
  and providing a focused fullscreen editing mode.

  Current problem:
  - normal Razor Page POST/redirect workflows interrupt node editing;
  - successful edits can reset zoom, pan, selection, inspector state, and editing
    context.

  Intended interaction:

  `open visual editor
    -> enter fullscreen/focus mode
    -> select vehicle
    -> edit
    -> save asynchronously
    -> update only affected node/edge
    -> continue editing without full page refresh`

  Scope:
  - add fullscreen/focus mode for the visual editor;
  - preserve zoom, pan, selected node, inspector state, active tab, and edit mode;
  - convert common Draft mutations to asynchronous requests;
  - keep Razor Pages as the page shell;
  - JavaScript posts to authenticated GrindingThunder.Admin handlers;
  - Admin handlers continue calling GrindingThunder.Api through `IApiClient`;
  - preserve antiforgery and existing role authorization;
  - show:
    - Unsaved;
    - Saving...;
    - Saved;
    - Save failed;
  - reconcile successful saves with authoritative API values;
  - keep Published versions read-only.

  Initial async mutation scope:
  - VehicleTreeEntry updates;
  - TreeColumn / TreeRow movement;
  - rank;
  - RP / SL;
  - acquisition type;
  - folder parent;
  - prerequisite add/remove;
  - add/remove VehicleTreeEntry where practical;
  - rank quota updates where practical.

  Out of scope:
  - WebSockets;
  - collaborative editing;
  - auto-save on every keystroke;
  - replacing Razor Pages with React/Blazor;
  - changing domain/version semantics.

  Dependency: Batch 36.

  Result:
  - added a full-window focus overlay that preserves the live canvas, viewport,
    selection, inspector, active mode, and tab state, with an in-workspace exit
    control that restores the surrounding page shell;
  - added antiforgery-protected JSON Razor Page handlers for visual entry saves,
    multi-entry layout saves, prerequisite add/remove, and entry removal while
    retaining every existing redirect-based form handler as a fallback;
  - reconciled accepted entry, coordinate, folder, rank-band, and connector state
    in the existing vanilla JavaScript editor without a page reload;
  - added explicit Unsaved, Saving, Saved, and Save failed inspector states and
    extended undo/redo with a full pre-save entry memento;
  - preserved Editor/Admin mutation rules, Publisher/anonymous rejection,
    Published snapshot immutability, and the `IApiClient` boundary.

  Verification: 0 warnings, 0 errors, all 582 C# tests pass across the solution
  (353 API + 229 Admin).


- **Batch 38 — Human-friendly visual coordinates** (status: Completed)
  Goal: present layout coordinates as one-based values to administrators while
  preserving the existing zero-based persistence model.

  Product rule:

  `displayed coordinate = persisted coordinate + 1`

  Example:
  - persisted: Column 0 / Row 0
  - admin sees: Column 1 / Row 1

  Scope:
  - show one-based values in:
    - inspector;
    - coordinate badges;
    - edit grid labels;
    - drag/drop feedback;
    - nudge controls;
    - collision messages;
    - occupied-cell indicators;
  - accept one-based values in the UI;
  - translate back to zero-based values before API calls;
  - preserve current visual layout exactly.

  Persistence:
  - do not migrate existing TreeColumn / TreeRow values;
  - do not modify Published versions;
  - keep zero-based coordinates as internal canonical data;
  - do not silently reinterpret existing import files.

  Result:
  - updated visual card coordinate badges, accessibility labels, inspector inputs, and edit-mode grid labels to display 1-based coordinates (`col + 1`, `row + 1`);
  - added 1-based occupied-cell indicator chips and drag-and-drop ghost preview badges (`Col X, Row Y`);
  - configured `TreeColumn` and `TreeRow` inputs across inspector, single-entry modals, and quick-add forms with `[Range(1, int.MaxValue)]` and `min="1"`;
  - translated 1-based form and JSON payloads to canonical 0-based coordinates (`entered - 1`) in `OnPostUpdateVisualEntry`, `OnPostUpdateVisualEntryJson`, `OnPostUpdateLayout`, `OnPostUpdateLayoutJson`, `OnPostAddEntry`, and `OnPostCreateAndAddEntry` before dispatching to `IApiClient`;
  - preserved exact canvas positioning by retaining 0-based coordinates in CSS custom properties and DOM dataset attributes;
  - formatted coordinate collision error messages and multi-selection collision lists using 1-based numbers;
  - verified with comprehensive integration tests covering HTML badge/inspector rendering, 1-to-0 form translation to `IApiClient`, 1-based collision detection, and multi-selection layout translation.

  Verification: 0 warnings, 0 errors, all 586 C# tests pass across the solution
  (353 API + 233 Admin).


- **Batch 38.1 — Rank-scoped layout collision validation** (status: Completed)
  Goal: align layout collision rules with the rank-banded visual tree model.

  Rule:
  - TreeColumn and TreeRow are local coordinates inside one TreeRank.
  - Two root entries may share the same TreeColumn/TreeRow when they belong to
    different TreeRanks.
  - Two root entries in the same TreeRank may not share the same TreeColumn/TreeRow.

  Scope:
  - update ResearchTreeVersion validation;
  - update import preview collision validation;
  - update visual editor collision detection;
  - update multi-entry layout validation;
  - update relevant tests;
  - preserve folder-child behavior;
  - no schema migration required.

  Canonical collision key:
  `(TreeRankId, TreeColumn, TreeRow)`

  During import before TreeRank IDs are resolved:
  `(RankNumber, TreeColumn, TreeRow)`

  Result:
  - updated `VersionLifecycleService.ValidateVersionAsync` to group root entries by `(TreeRankId, TreeColumn, TreeRow)` and format collision error messages with rank context (`in rank {RankNumber}`);
  - updated `TechTreeImportService.ValidateImportDataAsync` to group intra-file root rows by `(RankNumber, TreeColumn, TreeRow)` and compare imported root rows against existing draft root entries within the same rank (`collidingExisting.TreeRank.RankNumber == rootRow.RankNumber`);
  - audited `visual-tree.js` and `Version.cshtml.cs`, confirming collision detection and layout validation already consistently enforce `(TreeRankId, TreeColumn, TreeRow)` and exclude folder children;
  - added test coverage in `VersionValidationTests` and `TechTreeImportServiceTests` confirming that cross-rank root entries sharing coordinates validate cleanly while same-rank root collisions remain strictly flagged;
  - verified that no database migrations or persistence changes were introduced.

  Verification: 0 warnings, 0 errors, all 588 C# tests pass across the solution (355 API + 233 Admin).


- **Batch 39 — Player frontend foundation (`GrindingThunder.Web`)** (status: Completed)
  Goal: create a dedicated player-facing frontend separate from
  `GrindingThunder.Admin`.

  New application:

  `src/GrindingThunder.Web`

  Recommended stack:
  - Next.js;
  - React;
  - TypeScript;
  - Tailwind CSS.

  Architectural direction:

  Browser
    -> GrindingThunder.Web
    -> GrindingThunder.Api
    -> PostgreSQL

  GrindingThunder.Web must not depend on GrindingThunder.Admin or access the
  database directly.

  Admin remains:
  - ASP.NET Core Razor Pages;
  - Tabler;
  - Bootstrap 5.

  Public/player frontend becomes:
  - Next.js;
  - React;
  - TypeScript;
  - Tailwind.

  Scope:
  - create the new frontend project inside the existing monorepo;
  - establish development/build configuration;
  - define API base URL configuration;
  - create shared frontend API client utilities;
  - establish typed DTOs for the public API contracts used by the frontend;
  - create basic application shell and routing;
  - establish responsive layout and design tokens;
  - keep player and admin authentication conceptually separate;
  - prepare player session/auth handling without changing backend auth semantics;
  - provide initial routes such as:
    - `/`;
    - `/tree`;
    - `/login`;
    - `/register`;
    - `/profile` placeholder where useful.

  Reuse:
  - existing public API endpoints;
  - existing Published-version visibility rules;
  - existing player authentication/progress endpoints.

  Do not:
  - duplicate backend domain logic in Next.js;
  - call PostgreSQL directly;
  - expose admin JWT/session data to the public app;
  - migrate Admin UI into Next.js;
  - replace GrindingThunder.Admin.

  Dependency: Batch 38.

  Result:
  - added a standalone Next.js App Router application using React 19,
    TypeScript, and Tailwind CSS under `src/GrindingThunder.Web`;
  - added environment-driven API configuration (defaulting to
    `http://localhost:5034`), typed public/player DTOs, and a native-fetch API
    client with structured errors and optional player Bearer authentication;
  - added isolated browser-side player session handling plus responsive landing,
    published tree-selection, login, registration, and profile routes;
  - kept the tree route at the data-loading/manifest stage so Batch 40 remains
    responsible for the React SVG canvas and visual interaction;
  - introduced no backend, Admin, persistence, or migration changes.

  Verification: `npm run build` and `npm run typecheck` pass with zero errors;
  the solution continues to build and all 588 C# tests pass.


- **Batch 40 — Public visual research-tree browser** (status: Completed)
  Goal: rebuild the player-facing tree experience in GrindingThunder.Web using
  React while preserving the same canonical tree data and layout semantics authored
  in the Admin editor.

  Product rule:

  `Admin-authored layout == Public player layout`

  The rendering implementation may differ, but the data must remain identical:
  - TreeRanks;
  - TreeColumn;
  - TreeRow;
  - folder relationships;
  - prerequisites;
  - acquisition types;
  - Published ResearchTreeVersion.

  Scope:
  - allow players to select:
    - Nation;
    - category / ResearchTree;
    - Published version;
  - default to the latest Published version where appropriate;
  - render:
    - rank bands;
    - sparse/non-contiguous ranks;
    - vehicle cards;
    - RP / SL;
    - acquisition styling;
    - folder/group relationships;
    - prerequisite edges;
    - images;
    - WikiUrl links;
  - support:
    - pan;
    - zoom;
    - fit-to-view;
    - node selection;
    - responsive layout;
  - show vehicle detail panel/drawer;
  - show only Published ResearchTreeVersions;
  - never expose Draft versions in normal public browsing.

  Technical direction:
  - build the visual renderer in React;
  - use existing API contracts as source of truth;
  - do not duplicate progression validation logic client-side;
  - preserve TreeColumn / TreeRow semantics;
  - keep rendering deterministic.

  Out of scope:
  - ownership/progression overlays;
  - calculator integration;
  - editing;
  - admin features.

  Dependency: Batch 39.

  Result:
  - replaced the `/tree` tabular manifest with a deterministic React research-tree
    canvas that renders every version-owned rank (including empty and sparse ranks),
    unlock quotas, acquisition-styled vehicle cards, images/fallbacks, exact logical
    coordinates, folder stacks, and explicit prerequisite/folder SVG connectors;
  - added pointer panning, toolbar and Ctrl/Command-wheel zoom, fit/reset actions,
    responsive viewport behavior, accessible node selection, and a vehicle details
    drawer with costs, prerequisites, folder context, and external Wiki links;
  - retained the publication boundary through the anonymous publication-filtered API
    plus defensive client filtering, and introduced no ownership, calculator, editing,
    backend, persistence, migration, or third-party dependency changes.

  Verification: `npm run typecheck` and `npm run build` pass; solution build succeeds
  with zero warnings and all 588 C# tests pass (355 API + 233 Admin).


- **Batch 41 — Player calculator and progression experience** (status: Completed)
  Goal: connect the existing calculator and player-progress backend to the new
  Next.js visual research tree.

  Existing backend capabilities to reuse:
  - player registration/login;
  - synchronized owned vehicles;
  - calculator preferences;
  - `POST /api/research/calculate`;
  - optimization modes;
  - RP / SL estimates;
  - rank-gate fillers;
  - prerequisite traversal;
  - explicit ResearchTreeVersion selection.

  Intended player flow:

  `choose Nation
    -> choose category
    -> choose Published tree version
    -> mark owned vehicles
    -> choose target vehicle
    -> enter average RP / match
    -> enter average net SL / match
    -> choose optimization mode
    -> calculate
    -> highlight route on visual tree
    -> show result summary`

  Scope:
  - allow anonymous users to use the calculator;
  - allow anonymous owned state to live client-side;
  - allow signed-in players to load/sync owned vehicles;
  - allow target selection directly from the visual tree;
  - expose calculator controls:
    - Average RP per match;
    - Average net SL per match;
    - optimization mode;
  - display:
    - remaining RP;
    - remaining SL;
    - RP match estimate;
    - SL match estimate;
    - combined match estimate;
    - required prerequisite vehicles;
    - rank-gate fillers;
    - rank deficits;
  - visually distinguish:
    - owned;
    - target;
    - required route;
    - rank-gate filler;
  - highlight active prerequisite route edges;
  - support reset/recalculate/change-target workflows;
  - preserve calculator state while inspecting nodes.

  Signed-in behavior:
  - load saved ownership;
  - load/save calculator preferences;
  - synchronize progress without removing ownership from unrelated trees.

  Public-data rule:
  - calculations always target one explicit Published ResearchTreeVersion.

  Dependency: Batch 40.

  Result:
  - added a page-level React progression planner around the existing visual canvas, with Researchable target selection, positive RP/net-SL inputs, all four optimization modes, and explicit calculate, recalculate, reset, and signed-in save actions;
  - added anonymous stable-vehicle ownership persistence in namespaced local storage and authenticated progress/preference loading through the existing player API;
  - preserved ownership outside the active snapshot during signed-in full-set saves by refreshing canonical server progress and replacing only the selected snapshot's visible ownership slice;
  - added owned, target, prerequisite-route, and rank-gate-filler card treatments, active calculated prerequisite edges, drawer ownership/target actions, and result summaries for RP, SL, match estimates, required vehicles, fillers, and rank deficits;
  - kept all progression rules in the backend calculator and introduced no backend, Admin, schema, migration, dependency, or global-state-library changes.

  Verification: `npm --prefix src/GrindingThunder.Web run typecheck` and
  `npm --prefix src/GrindingThunder.Web run build` pass; all 588 C# tests pass
  (355 API + 233 Admin).

- **Batch 42 — Player account, profile, and public UX polish** (status: Completed)
  Goal: complete the player-facing product experience around the new
  GrindingThunder.Web application.

  Scope:
  - polished login/register/logout flows;
  - player profile page;
  - saved progress summary;
  - saved calculator preferences;
  - clear signed-in / signed-out states;
  - preserve local calculator/ownership state through login where practical;
  - robust loading, empty, error, and retry states;
  - responsive desktop/tablet/mobile layouts;
  - mobile calculator drawer;
  - accessible keyboard navigation;
  - useful shareable URLs for:
    - Nation;
    - category;
    - Published version;
    - target vehicle where appropriate;
  - improve vehicle detail presentation using:
    - ImageUrl;
    - WikiUrl;
    - current version-specific RP / SL / rank metadata.

  Out of scope:
  - social features;
  - leaderboards;
  - subscriptions/payments;
  - modification progression;
  - new calculator algorithms.

  Dependency: Batch 41.

  Result:
  - polished player login and registration with field-level validation, safe
    `returnTo` redirects, accessible signed-in/guest navigation, and non-destructive
    anonymous ownership/preference merging into canonical account progress;
  - expanded `/profile` with player identity/account ID, last local synchronization
    state, saved calculator defaults, total ownership, and a latest-Published-snapshot
    nation/category breakdown, plus explicit local-cache and logout actions;
  - made `/tree` selection URL-driven through canonical `nation`, `category`,
    `version`, and optional `target` parameters, including Published-only fallback
    and invalid-target recovery;
  - added responsive calculator/result treatment, a mobile modal vehicle sheet,
    spatial arrow-key node navigation, focus restoration/trapping, richer direct
    prerequisite/unlock/folder detail, and robust loading, empty, error, and retry
    states;
  - introduced no backend, Admin, schema, migration, dependency, state-management
    library, or client-side progression algorithm changes.

  Verification: `npm --prefix src/GrindingThunder.Web run typecheck` and
  `npm --prefix src/GrindingThunder.Web run build` pass; all 588 C# tests pass
  (355 API + 233 Admin).


- **Batch 43 — Cross-frontend visual consistency and performance** (status: Completed)
  Goal: harden the shared visual-tree contract so Admin and player frontend remain
  consistent as both evolve.

  Scope:
  - compare Admin and Web rendering against identical Published fixtures;
  - ensure:
    - same node positions;
    - same ranks;
    - same folder relationships;
    - same prerequisite graph;
    - same acquisition semantics;
  - define explicit frontend-facing tree DTO contracts where needed;
  - reduce accidental coupling to legacy transitional projections;
  - improve large-tree rendering performance based on measurements;
  - consider virtualization/culling only if real profiling justifies it;
  - add visual regression fixtures where practical;
  - ensure public mobile rendering remains usable for large trees.

  Do not:
  - merge Admin and Web applications;
  - introduce a shared frontend framework dependency between Razor and Next.js;
  - duplicate domain rules in the frontend;
  - optimize without measurements.

  Dependency: Batches 40–42.
  Result: defined canonical strongly typed DTO `VehicleTreeResponseDto` in `src/GrindingThunder.Api/Application/Models/VehicleTreeDtos.cs` containing all 19 catalog and layout properties, replacing the anonymous projection in `VehiclesController.GetVehicleTree` while maintaining exact wire compatibility and camelCase JSON contract. Aligned Admin models (`PublicVehicleTreeEntryDto`, `VehicleTreeResponseDto`) and Web TypeScript interfaces (`src/GrindingThunder.Web/lib/api/types.ts`). Standardized relative row positioning across frontends: Web's `tree-layout.ts` computes `minRowByRankId` and normalizes `rowOffset = treeRow - minRow`, matching Admin's `VisualTreeMapper` `RowOffset = node.TreeRow - minimumRow` and preventing multi-row blank gaps on non-contiguous or sparse row assignments. Aligned defensive folder parent resolution (`isFolderParent || folderParentIds.has(entry.id)`), identical 6-type acquisition styling, Roman numeral rank labels, and rank unlock quota displays. Optimized Web rendering performance without DOM virtualization: precomputed SVG connector bezier paths in `tree-layout.ts` and extracted memoized `TreeNodeCard` (`React.memo`) with stable callbacks in `research-tree-canvas.tsx` to eliminate re-renders across 100+ vehicle nodes during viewport panning or selection changes. Optimized Admin `visual-tree.js` `drawConnectors`: cached stage rectangle and per-node bounding client rects in a single pass to eliminate forced reflow / layout thrashing over dense prerequisite checks. Preserved responsive mobile viewport layouts with touch-friendly bottom drawer and panning. Added automated contract test `VehicleTreeContractTests.cs` in `GrindingThunder.Api.Tests` (verifying strongly typed returns, all 19 camelCase wire properties, acquisition types, and folder/prerequisite serialization) and consistency tests `VisualTreeConsistencyTests.cs` in `GrindingThunder.Admin.Tests` (verifying sparse row offset normalization, folder parent resolution, Draft vs Public equivalence, and 200-vehicle large-tree mapping performance in < 50ms).
  Verification: `dotnet build` succeeds with 0 warnings/errors; `dotnet test` passes all 593 tests (356 API + 237 Admin); `npm --prefix src/GrindingThunder.Web run typecheck` passes; `npm --prefix src/GrindingThunder.Web run build` passes.

- **Batch 44 — Production Docker deployment** (status: Completed)
  Goal: make Grinding Thunder deployable to a single Linux VPS using Docker
  Compose behind an existing host-managed Nginx reverse proxy.

  Production topology:

  Native Nginx
    -> GrindingThunder.Web
    -> GrindingThunder.Admin
    -> GrindingThunder.Api

  Docker network:
    Web
    Admin
    Api
    PostgreSQL

  Scope:
  - add production Dockerfile for GrindingThunder.Admin;
  - add production multi-stage Dockerfile for GrindingThunder.Web;
  - review/harden existing GrindingThunder.Api Dockerfile;
  - configure Next.js standalone production output;
  - add root `docker-compose.prod.yml`;
  - run PostgreSQL as a Docker Compose service;
  - persist PostgreSQL data in a named Docker volume;
  - do not expose PostgreSQL publicly;
  - expose Web/API/Admin only through localhost-bound host ports for Nginx;
  - add `.env.example` containing required production variable names without secrets;
  - keep actual `.env` gitignored;
  - move production database credentials, JWT signing key, bootstrap admin
    credentials, and similar secrets to environment variables;
  - make API CORS origins configuration-driven;
  - configure ASP.NET Core forwarded headers for Nginx TLS termination;
  - configure production cookie/security behavior appropriately;
  - add container health checks where practical;
  - define an explicit production EF Core migration procedure;
  - do not silently run destructive migrations on every container restart;
  - add PostgreSQL backup/restore documentation using `pg_dump`;
  - add example Nginx reverse-proxy configuration for:
    - `grindthunder.mydomain.com` -> Web
    - `api.grindthunder.mydomain.com` -> API
    - `admin.grindthunder.mydomain.com` -> Admin
  - document TLS/Certbot integration with the existing native Nginx;
  - document first deployment, normal update deployment, logs, restart, rollback,
    database backup, and restore procedures.

  PostgreSQL:
  - run PostgreSQL in Docker;
  - use a named persistent volume;
  - database is reachable only through the private Compose network;
  - use a strong externally supplied password;
  - maintain regular off-container backups.

  Redis:
  - do not add Redis in this batch;
  - add it later only if a concrete feature requires distributed caching,
    queues, rate-limit state, sessions, or similar shared ephemeral state.

  Out of scope:
  - Kubernetes;
  - Dockerizing the host Nginx;
  - Redis without a current requirement;
  - CI/CD deployment automation;
  - managed cloud databases;
  - multi-server/high-availability deployment.

  Dependency: Batch 43.

  Result: added hardened multi-stage, non-root production images for API, Admin, and standalone Next.js Web; a four-service production Compose topology with localhost-only application ports, private PostgreSQL 17, persistent database and Admin data-protection volumes, dependency health checks, and externalized production configuration; forwarded-header, cookie, CORS, health, and API-client hosting configuration; an explicit one-shot `--migrate` API mode that keeps production schema changes out of normal startup; native Nginx examples for all three public hostnames; and a production runbook covering prerequisites, Certbot TLS, initial bootstrap, routine deployment, rollback, logs, `pg_dump`, and `pg_restore`. Added anonymous health endpoint integration tests for API and Admin.
  Verification: `dotnet build GrindingThunder.sln --no-restore` succeeds with 0 warnings/errors; `dotnet test GrindingThunder.sln --no-build` passes all 595 tests (357 API + 238 Admin); `npm --prefix src/GrindingThunder.Web run typecheck` passes; `npm --prefix src/GrindingThunder.Web run build` passes and emits `.next/standalone/server.js`; `docker compose --env-file .env.example -f docker-compose.prod.yml config --quiet` validates successfully.

## Later

- Vehicle modification progression
- Advanced War Thunder economy/RP mechanics
- Audit/history improvements for admin operations
- deployment/CDN/image optimization for GrindingThunder.Web
- performance/caching only when measurements justify it
