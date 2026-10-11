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

## Planned

- **Batch 13 — Version validation and historical diff** (status: Planned)
  Goal: validate graph/version integrity before publication and compare versions for
  added/removed vehicles, RP/SL/rank/prerequisite/layout changes.
  Dependency: Batch 12.

- **Batch 14 — Admin authentication and authorization foundation** (status: Planned)
  Goal: introduce administrator authentication and Editor/Publisher/Admin permissions
  before exposing substantial mutation workflows.
  Dependency: Batch 12.

- **Batch 15 — Admin CRUD/workflow APIs** (status: Planned)
  Goal: manage nations, vehicle types, vehicles, updates, drafts, ranks, entries,
  prerequisites, layout, validation and publication through secured APIs.
  Dependency: Batches 12–14.

- **Batch 16 — Admin CSV/XLSX import workflow** (status: Planned)
  Goal: define a strict application-owned import schema, validate and preview uploaded
  data, and import only into Draft ResearchTreeVersions.
  Dependency: Batches 13–15.
  Note: scraped/source files such as the Israel Aviation CSV are input/reference data,
  not the application's canonical import format.

## Later

- Back-office web UI for the admin APIs
- Player accounts and synchronized owned-vehicle progress
- Vehicle modification progression
- Advanced War Thunder economy/RP mechanics
- Assisted scraping/data-conversion tools
- Performance/caching only when measurements justify it
