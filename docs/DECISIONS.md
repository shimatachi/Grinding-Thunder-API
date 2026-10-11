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

## 2026 — Batch 9: acquisition types and progression eligibility

- Added `VehicleAcquisitionType` (`Researchable = 0`, `Premium = 1`, `Squadron = 2`, `Event = 3`, `Pack = 4`, `Hidden = 5`) to `VehicleTreeEntry` rather than `Vehicle`. Why: in War Thunder, availability and acquisition status can change between updates (e.g. vehicles becoming hidden, event vehicles returning as store packs, or vehicles moving between tech-tree and premium/event categories), so acquisition type belongs to versioned snapshot state, preserving the stable identity of `Vehicle`.
- Enforced that target and filler vehicles must have `AcquisitionType == VehicleAcquisitionType.Researchable`, rejecting non-researchable entries with `ResearchCalculationInputException`. Why: premium, pack, squadron, and event vehicles cannot be researched with standard vehicle RP progression.
- Permitted any acquisition type in `UnlockedVehicleIds` and counted owned non-researchable vehicles towards rank-gate quotas. Why: in War Thunder, purchasing or owning premium or squadron vehicles in a rank counts toward the rank unlock requirement for subsequent ranks without requiring standard tech-tree RP expenditure.
- Stored `AcquisitionType` as an integer with default `0` (`Researchable`) in the database. Why: aligns with existing enum storage patterns (e.g., `ResearchTreeVersionStatus`), and safely defaults all existing snapshot rows without data-migration overhead.

## 2026 — Batch 10: automatic rank-gate candidate selection

- Replaced the "stop at first deficit" behavior with a greedy in-place selection loop that runs for every prerequisite rank below the target. Why: a strict halt at the first rank gate forced the caller to run multiple roundtrips to discover all blocking deficits and provide fillers incrementally; a single pass that auto-selects and reports remaining shortfalls gives a complete picture in one response.
- Ordered auto-select candidates by RP cost ascending, then SL cost ascending, then vehicle name ascending. Why: RP cost is the primary game resource and the most natural minimization axis; SL cost secondary breaks RP ties sensibly; name is the stable, deterministic final tie-breaker that avoids GUID-order instability across runs. This ordering is a greedy heuristic, not a globally optimal solution — Batch 11 will expose optimization mode selection.
- Reused the existing `CollectFillerLine` traversal contract through a new `CollectAutoFillerLine` local function that writes into the shared `fillerIds`/`fillerVehicles`/`fillerVisited` collections. Why: sharing state ensures that a vehicle gathered transitively by an earlier candidate is not double-counted by a later one, and that the same de-duplication rules (unlocked boundary, mandatory-line skip-but-recurse) apply uniformly regardless of whether a vehicle was supplied explicitly or auto-selected.
- Continued evaluation through all prerequisite ranks after a deficit rather than breaking. Why: the caller can see and explain all blocking shortfalls at once; partial information forced extra server roundtrips before. Tradeoff: the response may now list multiple `RankDeficits`; callers that previously assumed at most one entry must handle a list (this is consistent with the existing schema, which is already a `List<RankDeficitDto>`).
- Explicit `FillerTargetIds` are processed first (Step 1b); auto-selection in Step 2 only runs against the remaining shortfall. Why: the player's explicit intent must be respected before any automatic substitution; the explicit fillers may be higher RP for legitimate reasons (e.g., a vehicle the player wants for gameplay purposes).
- Added no new abstractions, repositories, or interfaces. The auto-selection is a local extension of the in-memory traversal that already lives in `ResearchCalculatorService`. The read-only aggregate snapshot loaded in Step 0 already contains all entries, ranks, and prerequisites needed.

## 2026 — Batch 11: route optimization modes

- Modeled `ResearchOptimizationMode` enum (`LeastRp = 0`, `LeastSl = 1`, `FewestVehicles = 2`, `FewestMatches = 3`) in `Application/Models/ResearchDtos.cs` with default `LeastRp` on `ResearchCalculationRequest`. Why: allows callers to tailor auto-selected fillers to their in-game economic bottlenecks (RP vs SL vs match count vs vehicle count) while preserving full backward compatibility for existing requests.
- Tailored candidate ordering and evaluation to the selected mode:
  - `LeastRp`: orders candidates by `RpCost asc, SlCost asc, Name asc`, preserving Batch 10 greedy behavior.
  - `LeastSl`: orders candidates by `SlCost asc, RpCost asc, Name asc`, prioritizing vehicle purchase cost savings for Silver Lion-constrained players.
  - `FewestVehicles`: simulates the candidate's unresearched transitive prerequisite chain using a non-mutating traversal helper (`GetCandidateChain`) and orders by `Chain.Count asc, Chain.RpCost asc, Chain.SlCost asc, Name asc`. Why: prevents selecting low-RP vehicles that drag in long prerequisite lines, minimizing research clutter and vehicle count.
  - `FewestMatches`: projects the concurrent match ceiling `max(ceil((CurrentRP + AddRP) / avgRP), ceil((CurrentSL + AddSL) / avgSL))` and orders candidates by projected matches ascending, then `AddRP asc, AddSL asc, Name asc`. Why: aligns filler choices with the concurrent progression model introduced in Batch 8, picking zero- or low-cost vehicles on whichever economy resource currently bottlenecks match completion.
- Kept the implementation in-memory and lightweight using the existing read-only snapshot aggregate. Did not introduce external solver packages (e.g. Google OR-Tools), MediatR, or generic abstractions. Why: tech-tree ranks are small bounded graphs (rarely more than 10-20 candidates per rank) and greedy simulation provides fast, predictable, and deterministic results without dependency or runtime overhead.

## 2026 — Batch 12: draft / clone / publish workflow and published version immutability

- Changed deletion behavior on `ResearchTreeVersion.ResearchTree` and `ResearchTreeVersion.GameUpdate` from `DeleteBehavior.Cascade` to `DeleteBehavior.Restrict` via the `RestrictResearchTreeVersionCascadeDelete` migration. Why: as anticipated in Batch 2, once versions hold historical tech-tree data (ranks, entries, layout, prerequisites), deleting a tree or game update record must never silently wipe out historical version snapshots.
- Enforced published version immutability at the EF Core interceptor level (`ApplicationDbContext.SaveChangesAsync` and `SaveChanges`). Why: published tech-tree versions represent immutable historical snapshots; allowing modifications, deletions, or additions of ranks, entries, or prerequisites on published versions would corrupt historical player progression calculations. The only allowed modification on a version is transitioning a `Draft` version to `Published`.
- Implemented `IVersionLifecycleService` (`VersionLifecycleService`) to manage version lifecycle workflows:
  - `CloneVersionAsync`: deep-clones an existing version into an editable `Draft` version for a target `GameUpdate`. It validates target game update existence and tree-update uniqueness, deep-copies `TreeRank` rows with new IDs, deep-copies `VehicleTreeEntry` rows with remapped `TreeRankId` and `FolderParentEntryId` (same-version parent hierarchy), and deep-copies `VehiclePrerequisite` rows with remapped endpoints and `ResearchTreeVersionId`. Deep-cloning isolates the draft completely from historical versions without cross-version foreign key violations or data leakage.
  - `PublishVersionAsync`: validates that a version is in `Draft` status and transitions it to `Published`, locking it against future mutations.
  - `DeleteDraftVersionAsync`: allows deleting versions only while in `Draft` status; attempts to delete a published version are rejected.
- Avoided generic repository and UnitOfWork abstractions, keeping implementation directly with `ApplicationDbContext` and plain, maintainable C#.

## 2026 — Batch 13: version validation engine and historical diff

- Introduced a comprehensive validation engine executed prior to publication via `PublishVersionAsync` and accessible via `ValidateVersionAsync`:
  - Enforced graph acyclicity (DAG) using a 3-state (unvisited/visiting/visited) DFS cycle detection on the version's prerequisite edges. Why: cyclic prerequisite dependencies would deadlock recursive progression calculation or cause infinite loops.
  - Enforced rank progression consistency: prerequisite edges cannot point to higher ranks (`prereq.TreeRank.RankNumber <= entry.TreeRank.RankNumber`), and rank-gate quotas (`RequiredVehiclesUnlocked`) must be feasible (available eligible vehicles in preceding ranks, or the rank itself for lowest ranks, must equal or exceed the quota). Why: prevents publishing tech trees where vehicles are mathematically impossible to unlock.
  - Enforced folder hierarchy constraints: maximum 1 level of folder nesting (folder parents cannot themselves have folder parents), folder children must share the same `TreeRank` as their folder parent, and folder parent relationships cannot form circular references.
  - Enforced entity, cost, and coordinate invariants: a version must have at least one rank and one entry, RP/SL costs and coordinates must be non-negative, and root (non-folder-child) entries cannot collide on identical `(TreeColumn, TreeRow)` coordinates.
  - Enforced progression acquisition integrity: `Researchable` vehicles cannot have non-researchable entries (`Premium`, `Hidden`, `Event`, etc.) as mandatory prerequisites. Why: players cannot research non-researchable vehicles with standard tech-tree RP, so requiring one would block tech-tree progression.
- Reusable historical diff engine (`CompareVersionsAsync`):
  - Compares two versions belonging to the same research tree and reports added, removed, and modified vehicles (with detailed deltas for RP, SL, rank, acquisition type, and coordinates), added and removed prerequisite edges, and rank quota changes.
  - Generates aggregate summary statistics (`AddedVehiclesCount`, `RemovedVehiclesCount`, `ModifiedVehiclesCount`, and net RP/SL deltas) in a single pass.
  - Exposed read-only REST endpoints on `ResearchTreeVersionsController` (`/api/research-tree-versions/diff` and `/api/research-trees/{treeId}/diff`) without requiring admin authentication (deferred to Batch 14).
- Kept implementation directly in `IVersionLifecycleService` / `VersionLifecycleService` and `Application/Models`: avoiding dedicated generic diff/validation frameworks or external graph libraries. Plain, idiomatic C# with EF Core split queries preserves modular monolith simplicity.

## 2026 — Batch 14: admin authentication and authorization foundation

- Chosen standard JWT Bearer token authentication over ASP.NET Core Identity or third-party auth platforms (Auth0, Firebase). Why: ASP.NET Core Identity introduces massive database bloat (7 identity tables for users, roles, logins, claims, tokens, etc.), and third-party SaaS services introduce external runtime network dependencies and test complexity. Stateless JWT Bearer provides clean REST API authentication, works natively with SPAs/admin UIs, and runs self-contained in in-memory test harnesses.
- Granular administrative roles and policies:
  - Modeled roles `Editor`, `Publisher`, and `Admin`.
  - Modeled authorization policies `RequireEditorRole`, `RequirePublisherRole`, and `RequireAdminRole`.
  - Granted `Admin` role supervisory access across `RequireEditorRole` and `RequirePublisherRole` policies, enabling full administrative oversight while restricting single-role accounts (`Editor` cannot publish, `Publisher` cannot modify drafts or layout, neither can delete drafts or perform admin tasks).
- Avoided plaintext secrets in source control and database:
  - Implemented `IPasswordHasher` / `PasswordHasher` using PBKDF2 with HMAC-SHA256, 16-byte random salt, 100,000 iterations, and constant-time byte comparison (`CryptographicOperations.FixedTimeEquals`).
  - Stored non-sensitive JWT settings (`Issuer`, `Audience`, `TokenLifetimeMinutes`) in `appsettings.json`, and configured environment variable / User Secret overrides for signing keys (`JWT_SIGNING_KEY`).
  - Enforced that Production fails fast on missing signing keys, while Development and Testing provide deterministic, zero-configuration fallbacks and seeded development credentials (`admin`, `publisher`, `editor`).
- Preserved anonymous access on player-facing endpoints:
  - Registered `UseAuthentication()` and `UseAuthorization()` in `Program.cs`. Existing query and calculation endpoints (`GET /api/nations`, `GET /api/vehicles`, `POST /api/research/calculate`, `GET /api/research-tree-versions/diff`, `GET /api/research-tree-versions/{versionId}/validate`) remain public without authentication challenges.

## 2026 — Batch 15: admin CRUD and draft workflow APIs

- Implemented comprehensive administrative REST endpoints for catalog entities (`Nation`, `VehicleType`, `Vehicle`, `GameUpdate`, `ResearchTree`) and versioned draft management (`TreeRank`, `VehicleTreeEntry`, layout coordinates, folder parents, and `VehiclePrerequisite`).
- Enforced role-based access control policies (`RequireEditorRole`, `RequirePublisherRole`, `RequireAdminRole`) with Admin supervisory coverage.
- Guarded draft-mutating endpoints with `EnsureDraftVersionAsync`, immediately rejecting non-existent versions with 404 and published versions with 409 Conflict to uphold published version immutability.

## 2026 — Batch 16: two-phase tabular import workflow (CSV/XLSX)

- Defined an application-owned canonical tabular schema (`VehicleName`, `RankNumber`, `RpCost`, `SlCost`, `TreeColumn`, `TreeRow`, `AcquisitionType`, `FolderParentVehicleName`, `PrerequisiteVehicleNames`, `RequiredVehiclesUnlocked`). Scraped files (such as `tools/import/wt_israel_aviation.csv`) are external reference datasets, not the application's internal canonical import schema. Flexible header aliases normalize common casing and column name variations without relaxing required column invariants.
- Built a lightweight, zero-dependency parser (`TabularDataParser`) using pure .NET BCL:
  - RFC 4180-compliant CSV/TSV streaming parser with delimiter auto-detection, quote escaping, multiline cells, and whitespace trimming.
  - OpenXML XLSX parser utilizing standard `System.IO.Compression.ZipArchive` and `System.Xml.XmlReader` to extract shared strings and worksheet cells without pulling heavy third-party Excel dependencies (e.g. EPPlus or ClosedXML).
- Implemented a two-phase import engine in `ITechTreeImportService` / `TechTreeImportService`:
  - Phase 1 (Preview): non-mutating validation checking format, data types, stable vehicle catalog matching, rank definitions, coordinate collisions (intra-file and against unimported draft entries), folder nesting constraints (max 1 level, same rank, no cycles), prerequisite consistency, DAG acyclicity, and rank gate quotas. Returns a detailed preview payload with detected rows, counts of new vs updated entries, auto-created vehicles, created ranks, errors, and warnings.
  - Phase 2 (Commit): targets strictly Draft versions (rejects published versions with 409 Conflict), auto-creates catalog vehicles when enabled, ensures TreeRanks exist, upserts vehicle entries, links folder parents, and synchronizes prerequisite edges in an atomic database transaction.
- Secured REST endpoints exposed at `/api/admin/drafts/{versionId}/import/preview` and `/api/admin/drafts/{versionId}/import` accepting `multipart/form-data` under the `RequireEditorRole` policy.

## Log format

Append new entries at the end. Keep each entry to the decisions and tradeoffs that are not obvious from the code; do not duplicate content that belongs in DOMAIN.md, DATABASE.md, or ARCHITECTURE.md.


