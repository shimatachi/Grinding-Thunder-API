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

## 2026 — Batch 16.5: repository reorganization (src / tests layout)

- Reorganized repository from a root-level API project into standard `src/` and `tests/` solution structure:
  - Moved API project source files, configurations, and Dockerfile to `src/GrindingThunder.Api/`.
  - Moved test project to `tests/GrindingThunder.Api.Tests/`.
  - Renamed root solution from `GrindingThunder.Api.sln` to `GrindingThunder.sln` and updated relative project paths.
  - Updated test project reference from `..\GrindingThunder.Api.csproj` to `..\..\src\GrindingThunder.Api\GrindingThunder.Api.csproj`.
  - Removed artificial compile and content exclusions (`GrindingThunder.Api.Tests\**\*`) from `GrindingThunder.Api.csproj` that were previously necessary when tests were nested inside the API project directory.
- Preserved Docker packaging behavior while updating to root build context:
  - Relocated `Dockerfile` beside the API project under `src/GrindingThunder.Api/Dockerfile`.
  - Updated container COPY and build instructions to reference `src/GrindingThunder.Api/` relative to repository root build context (`docker build -f src/GrindingThunder.Api/Dockerfile -t grinding-thunder-api .`).
- Maintained exact application and domain behavior:
  - Preserved namespaces (`GrindingThunder.Api...`), UserSecretsId, launch profile ports, migrations history, seed behavior, and zero schema changes.

## 2026 — Batch 17: admin back-office foundation (Razor Pages client)

- Implemented the back-office as an isolated ASP.NET Core Razor Pages project under `src/GrindingThunder.Admin` (port 5035) communicating with `GrindingThunder.Api` (port 5034) exclusively over HTTP via a typed `ApiClient`.
- Zero database or EF Core dependencies in the admin project: the back office treats the API as its single source of truth, avoiding dual-context synchronization issues and maintaining strict domain separation.
- Cookie-to-JWT bridge architecture:
  - Web users authenticate via `/Login` submitting credentials to the existing `POST /api/auth/login` endpoint.
  - On authentication success, the server establishes a secure ASP.NET Core cookie session (`GrindingThunder.Admin.Session`) containing user claims (Username, Roles) and securely stores the JWT Bearer token in the authentication ticket.
  - A custom `BearerTokenHandler` (`DelegatingHandler`) injected into `IHttpClientFactory` retrieves the token from the active `HttpContext` and automatically attaches `Authorization: Bearer <token>` to outbound requests to secured API endpoints.
- Provided modern responsive layout, user profile badge rendering (`Admin`, `Publisher`, `Editor`), live connectivity probe to `GET /api/auth/me`, sign-out workflow clearing cookies, and placeholder pages for Batches 18–20.
- Added comprehensive unit and integration test coverage under `tests/GrindingThunder.Admin.Tests` using `WebApplicationFactory` and mock backend message handlers, validating cookie creation, unauthenticated redirects, error feedback, and Bearer token propagation without requiring external service dependencies.

## Log format

## 2026 — Batch 18: Tabler-based admin catalog management

- Adopted Tabler through pinned CDN-hosted CSS and JavaScript rather than vendored assets. This keeps the Razor Pages client free of Node/npm and new package dependencies and makes the UI kit easy to update, with the tradeoff that full styling and Tabler JavaScript enhancements require CDN availability in the browser.
- Kept catalog behavior behind the existing typed HTTP client and cookie-to-JWT bridge. The admin project mirrors existing API DTOs and status semantics but contains no EF Core or database access; Batch 18 made zero API, domain, persistence, or migration changes.
- Reflected the existing API boundary instead of simulating unsupported behavior: research trees can be listed, created, inspected for deletion, and deleted, but cannot be edited because `AdminResearchTreesController` exposes no update endpoint.

Append new entries at the end. Keep each entry to the decisions and tradeoffs that are not obvious from the code; do not duplicate content that belongs in DOMAIN.md, DATABASE.md, or ARCHITECTURE.md.

## 2026 — Batch 19: research-tree version editor closure

- Retained server-rendered Razor forms and tables with numeric row/column editing. Existing HTTP endpoints support these bounded editing tasks without adding Blazor state management or a JavaScript graph component. The tradeoff is page reloads and less visual interaction; reconsider a graph editor when actual editing needs justify the complexity. Layout coordinates and explicit prerequisite relationships stay separate.
- Matched Batch 14 supervisory role semantics through small local predicates and entry-page role authorization, verified with single-role accounts. Native browser confirmation protects destructive submissions without a dependency. The API remains the authority for permissions and immutable published snapshots.
- Supported Publisher read-only browsing via public projections without weakening backend security: the real backend endpoints (`/api/admin/research-trees`, `/api/admin/game-updates`, `/api/admin/research-tree-versions/{id}`, `/api/admin/drafts/{id}`) require Editor or Admin. Rather than weakening `RequireEditorRole` or broadening API permissions to fit UI shortcuts, Publisher-only sessions on `/Trees` and `/Trees/Version/{id}` query public exact-version projections (`GET /api/nations` and `GET /api/vehicles/tree`) filtered strictly by `ResearchTreeVersionId`. Publisher views hide all mutation controls and reject direct POST requests before sending outbound HTTP calls. Aligned `TestMockApiHandler` to enforce authentic backend role checks on `/api/admin/` routes (returning 403 Forbidden for insufficient roles), eliminating test harness permissiveness that previously masked authorization mismatches.
- Kept publishing out of the Batch 19 admin client: Batch 20 owns import, validation, diff, and publish confirmation/workflow UI. Existing backend endpoints and the HTTP-only cookie-to-JWT client boundary remain unchanged; this batch required no domain or persistence changes.
## 2026 — Batch 20: import and publication controls

- Kept import as a two-step server-rendered form. A valid preview stores the uploaded bytes for 15 minutes in the Admin process, bound to the signed-in username and draft version, so commit sends exactly the reviewed file and vehicle-creation option. This avoids a client-side build step and makes the flow simple, with the tradeoff that a restart or expired cache entry requires a new preview. The API revalidates the file at commit and remains authoritative.
- Used the public validation and diff endpoints for every authenticated role. Version comparison checks both selected IDs against the same tree before calling the API. Publication is limited locally to Publisher/Admin and requires explicit acknowledgement, while the secured API retains final role, draft-state, and validation enforcement.

## 2026 — Batch 21: persistent administrator accounts

- Chose a minimal custom administrator identity model (`AdminAccounts` and `AdminAccountRoles`) over ASP.NET Core Identity. Why: ASP.NET Core Identity introduces considerable schema bloat (multiple tables for logins, user tokens, role claims, etc.) and complex service abstractions that do not align with our lightweight JWT architecture. A focused entity model with physical check constraints (`"Role" IN ('Admin', 'Publisher', 'Editor')`) and normalized username uniqueness delivers exact least-privilege security without superfluous dependencies or abstractions.
- Hardened first-administrator bootstrap (`AdminAccountBootstrap`): bootstrap from external configuration (`AdminBootstrap:Username` / `AdminBootstrap:Password`) creates the initial account only when no Admin-role account exists in the database. It enforces the same username length (<= 64) and password strength (>= 8 characters) as managed accounts, never logs credentials or hashes, fails fast on partial or invalid configuration, never overwrites or re-enables an existing admin account, and safely recovers only from verified unique index collisions arising from concurrent bootstrap races.
- Token invalidation via `SecurityStamp`: instead of managing distributed token revocation blocklists or stateful server session caches, each administrator account owns an integer `SecurityStamp` included in JWT claims. On authenticated requests and profile probes, the claim is verified against the database. Any credential mutation (password replacement, role changes, deactivation) increments the stamp, instantly invalidating active tokens.
- Last-Admin invariant: mutations inside `AdminAccountService` prevent demoting or deactivating the last active Admin account, guaranteeing the system remains manageable. Operations run inside serializable transactions (or join existing ambient transactions) to avoid race conditions.

## 2026 — Batch 22: player accounts and synchronized progress

- Separated player and administrator identities and JWT audiences: player accounts live in `PlayerAccounts`, completely decoupled from `AdminAccounts`. Player JWT tokens target a dedicated audience (`PlayerAudience = "GrindingThunder.Player"`) and policy (`RequirePlayerPolicy`), preventing token confusion, cross-boundary spoofing, or administrative privilege escalation.
- Stable Vehicle IDs for saved ownership: player progress records unlocked vehicles via `PlayerUnlockedVehicle(PlayerAccountId, VehicleId)` referencing stable `Vehicle` identity, not transient `VehicleTreeEntry` rows. Why: War Thunder player vehicle ownership is an invariant achievement that persists across game updates, even as vehicles change ranks, costs, or positions across versioned snapshots.
- Decoupled calculation contract: preserved the anonymous, stateless nature of the progression calculator (`POST /api/research/calculate`). Saved player progress is never automatically injected into calculations. Clients explicitly intersect the player's saved vehicle ownership with the entries in the selected `ResearchTreeVersionId` and provide `unlockedVehicleIds`.
- Atomic full-set progress replacement with optimistic concurrency: player progression updates (`PUT /api/player/progress`) replace the full set of unlocked vehicle IDs and calculator preferences in an atomic transaction. `ProgressStamp` is an internal EF Core concurrency token (`IsConcurrencyToken()`) that rejects overlapping in-flight saves with HTTP 409 Conflict; callers do not pass `progressStamp` in the JSON request body.

## 2026 — Batch 23: tree-scoped vehicle ownership

- Vehicle is stable identity within one ResearchTree. Update-specific progression state remains in VehicleTreeEntry. The former global catalog could reuse a same-named vehicle across nations/categories during imports and allowed cross-tree entries. Owning identity at the tree level prevents that conflation while preserving stable player-owned IDs across updates.
- Enforced equality with a shared `VehicleTreeEntry.ResearchTreeId` and composite FKs to vehicle and version alternate keys. This adds a redundant tree column and indexes, but gives ordinary database constraints enough information to reject cross-tree writes without custom triggers or controller-only guarantees. Existing draft deletion behavior remains; tree/vehicle deletion is restricted where historical or player data exists. Vehicle creation requires ownership, and editing does not reparent identity.
- Replaced controller-only global duplicate checks with tree-scoped checks plus a unique database index over a generated `lower(trim(Name))` column. Same-named vehicles in different trees are separate stable identities. The generated value avoids drift from direct SQL or concurrent writes without an extension/dependency.
- Chose transactional fail-fast backfill instead of automatically splitting multi-tree identities or assigning entryless vehicles. Only a single inferred tree is safe; guard errors report bounded examples for explicit resolution. Player ownership is never deleted, duplicated, or reassigned. Down refuses to discard entryless ownership mappings.
- Kept Admin changes to contract compatibility. Batch 24 remains responsible for Nation -> Category -> Vehicle navigation and workflow.

## 2026 — Batch 24: contextual admin navigation

- Reorganized the existing Razor Pages around Nation -> ResearchTree/category -> Vehicles and Versions. VehicleType remains the master category concept; enabling a category creates the existing Nation/VehicleType combination through the existing API. No new domain entity or schema is needed.
- Used verified route ownership for vehicle creation and checked the full nation/tree/vehicle relationship before editing or deleting. The shared page-model base centralizes these checks across the four vehicle screens. Stable identity input now contains only name/image, while version-dependent progression remains in the existing editor.
- Added one bounded read endpoint, `GET /api/admin/vehicles/tree/{researchTreeId}`, for Admin/Editor/Publisher. The existing public entry projection omits unversioned vehicles, and its global alternative loads unrelated snapshot state. A filtered stable-identity DTO projection supports complete Publisher browsing without granting global catalog or mutation access. Other role policies, including Publisher/Admin publication and Admin-only deletion, remain intact.
- Kept Dashboard session/connection details and existing version lifecycle behavior. Administration groups master reference data and accounts, and existing catalog/account URLs remain where practical. Vehicle routes now require nation/tree context; old version query bookmarks still work. Using the existing public nation/version projection avoids another tree-read endpoint, with the tradeoff that hierarchy navigation currently fetches the complete nation/version list.

## 2026 — Batch 25: shared visual research-tree viewer

- Introduced one presentation-only `VisualResearchTreeViewModel` and mapper for both secured Draft DTOs and public snapshot DTOs. This keeps the renderer reusable without coupling it to controllers, EF Core, or one API authorization path. Draft entry IDs and public stable vehicle IDs remain input-specific node identities, while the visual contract normalizes ranks, nodes, prerequisite edges, and folder edges.
- Rendered ranks as ordered bands from the actual version-owned `TreeRank` records. Rank numbers are labels, not array indexes, so sparse trees (including trees beginning above Rank I) need no synthetic ranks. Vehicle cards retain their persisted `TreeColumn` and `TreeRow`; row values are normalized only within a displayed rank band to avoid meaningless leading canvas whitespace, while the original coordinates remain available on each rendered node.
- Used server-rendered Razor cards and lightweight local vanilla JavaScript with an SVG overlay. Connector paths are calculated from rendered node geometry: solid arrowed paths represent explicit prerequisite progression and dashed amber paths represent folder/group membership. Visual proximity never creates a progression edge. This avoids a graph library, npm pipeline, SPA state model, or new dependency; the tradeoff is that connector routing is deliberately simple and will need extension if later editing batches require collision-aware routing.
- Kept the initial Batch 25 viewer read-only and the existing tables/forms in a `Table / Details` tab. Scroll overflow plus transform-based zoom, fit/reset controls, Ctrl-wheel zoom, and pointer panning make large trees navigable without changing canonical layout persistence. Batches 26 and 27 subsequently layered node and prerequisite editing onto this shared renderer.

## 2026 — Batch 26: visual vehicle-entry editor

- Extended the existing Razor/SVG renderer with a page-owned workspace model and local vanilla JavaScript instead of introducing SPA state, a graph library, npm tooling, or a new API. JavaScript owns only transient selection and drag previews; every save remains an antiforgery-protected Razor Page post through `IApiClient`, so the API stays authoritative for authorization and domain validation.
- Kept layout movement structured: dragging and arrow nudges snap directly to integer `TreeColumn` / `TreeRow` values in the selected rank band. Pixel positions are derived presentation state and are never submitted or persisted. Rank changes are saved through the inspector and re-rendered by the server rather than attempting complex client-side cross-band state reconciliation.
- Made edit capability an explicit intersection of Draft status and Editor/Admin role. The workspace does not merely disable controls for Published or Publisher-only views; it omits the inspector, addition/removal forms, and edit toggle entirely. Direct handler posts repeat role and Draft-status checks before any mutation call. The existing Table / Details forms remain a fallback and share the same API boundary.
- Contextual vehicle creation composes the existing stable-Vehicle creation endpoint with the draft-entry endpoint. The page validates name, rank, costs, coordinates, acquisition type, and folder membership before creating the stable identity. Because these are two existing HTTP operations without a transaction spanning them, entry placement can still fail after identity creation; the UI reports that partial outcome explicitly rather than hiding it or adding a backend orchestration endpoint outside this batch.

## 2026 — Batch 27: visual prerequisite editor

- Kept movement and prerequisite editing as mutually exclusive canvas modes. A prerequisite operation explicitly selects the dependent first and the required entry second, then displays a pending directed connector until the administrator submits the existing antiforgery-protected Razor form. Node movement remains isolated to integer layout fields and cannot implicitly create, delete, or redirect progression edges.
- Reused the existing SVG connector geometry and added a wide transparent hit path for each prerequisite connector. This makes a thin visible path practical to select without changing the visual graph, while folder connectors remain non-interactive. Selection and pending state are transient CSS/JavaScript state; persisted endpoints always use the rendered Draft `VehicleTreeEntryId` values.
- Kept the API as the authoritative validation boundary and the server-rendered redirect/reload workflow as the persistence model. Failed mutations return a workspace banner and restore prerequisite mode plus its endpoints, while successful mutations reload canonical server data. This avoids duplicating graph, rank, acquisition, Draft-status, or version-isolation rules in JavaScript and adds no graph library, SPA state layer, API endpoint, or dependency.

## 2026 — Batch 28: Game Update release workspace

- Built the release view as a Razor Page orchestration workspace over existing lifecycle endpoints. Aggregate status and per-tree actions are derived read models, not new persisted release state, so independently published immutable snapshots remain the source of truth and there is no bulk-publish transaction.
- Added `GameUpdateId` to the backward-compatible public nation/version projection. Matching by stable ID avoids coupling coordination to formatted update-version text. Clone and default diff baselines are selected only from Published versions already nested under the same ResearchTree; posted IDs are checked against that reconstructed relationship before an API mutation.
- Granted Publisher the narrow read needed for one Game Update's metadata while retaining Editor/Admin catalog listing and mutation boundaries. The workspace hides Editor actions from Publisher and publish actions from Editor, but the API remains authoritative for all role, validation, Draft-state, and immutability rules.

## 2026 — Batch 29: visual tree layout and editor polish

- Kept `TreeColumn` and `TreeRow` as the only layout values submitted to the server. The shared renderer now maps each logical cell to a roomier 280 by 204 pixel presentation grid, with the same values emitted by Razor, declared as CSS defaults, and consumed from workspace metadata by JavaScript. This intentionally makes large trees wider and taller in exchange for clearer card gutters and connector lanes; zoom, fit, and scrolling remain the density controls.
- Kept placement guides and live coordinate badges as edit-mode presentation state. Dragging, buttons, manual inputs, and keyboard arrows update the same integer preview and existing inspector form, but none auto-save or bypass antiforgery/API validation. Keyboard handling ignores form controls and browser modifier shortcuts; Published and Publisher-only renderings still omit all editing controls.
- Replaced center-to-center connector curves with direction-aware edge anchors, short straight gutter stubs, and cubic Bézier transitions. This improves predictable card entry/exit without adding obstacle-routing state or a graph library. The renderer reduces common card crossings but does not promise globally optimal routing when many explicit relationships share a lane.

## 2026 — Batch 30: vehicle node inspector and direct editing UX

- Structured the inspector into two explicit visual sections: read-only stable vehicle identity (displaying vehicle ID, tree context, and catalog name/image) and version-specific entry data (rank, costs, acquisition type, integer coordinates, and folder parent). This reinforces the domain boundary established in Batches 3 and 23 directly within the administrative UI.
- Implemented client-side dirty state tracking and discard guards in vanilla JavaScript. Field changes produce live DOM previews (including dynamic rank-band re-parenting and SVG connector updates) without saving on every keystroke. Modifying selection, toggling edit/edge modes, pressing Escape, or navigating away prompts the user before discarding unsaved state, restoring original node values if confirmed.
- Kept the existing HTTP API and Razor Page handler boundary authoritative: form submission posts through `UpdateVisualEntry`, preserving antiforgery validation, Draft status verification, and backend business logic. Published snapshots and Publisher-only sessions omit inspector controls entirely.

## 2026 — Batch 31: rank-band and research-tree visual styling

- Kept rank-band construction tied strictly to the ordered `TreeRank` projection. Roman numerals are presentation labels only, and unsupported numeral ranges fall back to the persisted integer; missing rank numbers never produce synthetic bands. Sticky headers improve horizontal navigation, while the extra band padding and full-width separators intentionally trade some vertical density for clearer progression tiers.
- Represented acquisition types through semantic CSS classes on both cards and badges, with the inspector's unsaved preview maintaining those classes client-side. The accents communicate acquisition source only: they do not affect prerequisites, rank gates, calculation eligibility, or persistence. Rank is shown because it exists in the visual model; no battle-rating value is invented where the current data contract has none.
- Increased connector contrast through brighter strokes and subtle shadows without changing anchor calculations, gutter stubs, cubic Bézier routing, transparent hit targets, or edge-editing state. This preserves Batch 29/30 interaction behavior and avoids introducing a graph library or a second layout model.

## 2026 — Batch 32: visual layout assistance tools

- Kept layout assistance in the existing Razor and vanilla JavaScript editor. A bounded selection of at most 12 entries from one rank can be moved, aligned by row or column, or distributed vertically. Limiting a selection to one rank avoids hidden rank changes and keeps each operation within one rank band's coordinate space.
- Kept `TreeColumn` and `TreeRow` as the only persisted layout values. The snap toggle changes drag feedback only; releasing a card still resolves to non-negative integer coordinates. Occupied-cell guides, potential drop cells, selection outlines, collision warnings, and folder relationship emphasis are derived client presentation state.
- Reused the existing per-entry coordinate endpoint rather than adding a bulk API contract. The Razor Page handler validates draft membership, selection bounds, same-rank scope, and the complete proposed root-cell layout before issuing coordinate updates. This keeps the backend contract stable; the tradeoff is that several coordinate calls are not one database transaction, although all deterministic validation occurs before the first call.
- Preserved the domain collision rule: only root entries must occupy distinct `(TreeColumn, TreeRow)` cells within a rank. Folder children may share a logical cell with their group, and layout feedback never creates or changes prerequisite edges.

## 2026 — Batch 33: shared public research-tree experience

- Made publication filtering an API invariant rather than a client convention. Anonymous `GET /api/nations` returns only Published snapshots, while authenticated administrator callers must explicitly request `includeDrafts=true` for back-office discovery. `GET /api/vehicles/tree` now requires one exact `researchTreeVersionId` and returns 404 for Draft, missing, or mismatched nation/category versions. This prevents a public client from downloading Draft data and merely hiding it after the fact.
- Added `/Tree` as an explicitly anonymous Razor Page inside the existing HTTP-only web project instead of creating another frontend or bypassing the API. Its cascading selectors are reconstructed from the publication-filtered hierarchy, default to the first/latest Published options, and send an exact version ID for entry loading. The page defines GET behavior only and has no mutation handlers.
- Reused the canonical `VisualResearchTreeViewModel`, `VisualTreeMapper.FromPublic`, `_VisualTree.cshtml`, `visual-tree.js`, CSS, and SVG routing. Read-only selection is a small presentation state layered onto the same cards and exposes identity plus version-specific rank/cost/acquisition/layout/folder details. The tradeoff is that the current public browser lives in the project historically named `GrindingThunder.Admin`; authorization conventions and API boundaries, rather than the assembly name, keep it independent from administrator authentication.

## 2026 — Batch 34: player progression overlay on shared visual tree

- Isolated player and administrator authentication schemes. The ASP.NET Core web host configures a distinct `"PlayerAuth"` cookie scheme (`GrindingThunder.Player.Session`) alongside the default admin session scheme. `BearerTokenHandler` selectively attaches player JWTs to `/api/player/*` requests and admin JWTs to back-office endpoints, preventing privilege leakage or token collision between player accounts and back-office administrators.
- Preserved the public tree read-only HTML contract. To prevent violating the canonical read-only view contract (and avoid introducing full-page post reloads or CSRF token requirements for anonymous visitors), player authentication, progress synchronization, and research calculations communicate through AJAX fetch calls to dedicated Razor Page handlers. The visual tree remains free of standard `<form method="post">` mutation controls.
- Maintained stable `VehicleId` ownership and preserved out-of-tree progress. Player progress tracks stable `VehicleId` across the entire account. The client loads the full set of unlocked vehicle IDs and intersects it with the entries of the selected `ResearchTreeVersion` for visual badges and calculation requests. When toggling ownership on `/Tree`, the full set is submitted to `PUT /api/player/progress`, ensuring vehicles owned in other trees or versions are never wiped out. Concurrency conflicts (409) reload canonical server progress and re-synchronize cleanly.
- Reused canonical visual tree rendering for progression overlays. Progression states (owned, target, required prerequisite, rank-gate filler) and active route edges are represented as lightweight CSS classes and SVG connector accents over the existing shared renderer. A collapsible bottom HUD drawer displays remaining RP, remaining SL, estimated matches, and rank deficits directly over the canvas without introducing SPA frameworks or third-party graph dependencies.

## 2026 — Batch 35: visual editor productivity and safety

- Implemented an in-memory delta memento history stack (`undoStack`, `redoStack`) in vanilla JavaScript instead of round-tripping fine-grained mutations to the database. Capturing `{ from: { column, row, rankId }, to: { column, row, rankId } }` allows administrators to freely drag, align, nudge, and distribute nodes with instant reversibility while keeping backend persistence atomic and antiforgery-validated. Persisted changes remain committed strictly when the user submits the existing Razor form.
- Applied context-aware keyboard event routing. Canvas shortcuts (`Ctrl+Z`, `Ctrl+Y`, `Escape`, `Delete`/`Backspace`, and arrow keys) check `event.target.matches("input, select, textarea")` to avoid intercepting native form input manipulations (such as typing values or cursor movements). Arrow keys enforce non-negative coordinate boundaries (`TreeColumn >= 0`, `TreeRow >= 0`) before mutating state, preventing out-of-bounds positioning.
- Integrated spatial visual validation and clickable jump navigation. Rather than relying solely on text-based error lists, root-cell collisions and out-of-bounds nodes are visually flagged on the canvas with distinct CSS badges (`⚠️ Overlap`, `⚠️ Out of bounds`), and invalid prerequisite edges are highlighted with dashed warning strokes. Offending entries are linked via `[data-jump-to-node]` buttons that switch view tabs, center the node in the viewport, and trigger a temporary highlight pulse, bridging tabular validation reports with spatial WYSIWYG editing.
- Preserved strict Published snapshot immutability. All undo/redo functionality and editing shortcuts are guarded by `isEditing()`, which requires active edit mode on an editable draft. Public tree views and Published admin snapshots completely exclude undo/redo and layout mutation controls from the DOM and event listeners.


