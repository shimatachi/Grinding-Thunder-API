# Product

## Purpose

Plan the War Thunder vehicles, Research Points (RP), Silver Lions (SL), and estimated matches needed to reach a target vehicle. The intended workflow is to choose a nation and vehicle tree, mark vehicles already researched or owned, choose a target, and see the remaining path and cost.

## CURRENT — implemented backend behavior

- The API lists nations with their research trees, vehicle types, versions, and version-owned ranks. Vehicle entries can be filtered by nation and optional vehicle-type name. Development seed data contains four nations with Ground research trees, a `dev-sample` published version for each seeded tree, and a small USA Ground vehicle sample. This does not establish support for all nations or trees.
- A calculation selects one research-tree version and accepts a target vehicle, IDs treated as already unlocked, optional player-selected filler targets, average RP per match, average net SL per match, and an optional route optimization mode (`LeastRp` by default, `LeastSl`, `FewestVehicles`, or `FewestMatches`). Both averages must be greater than zero. Target, owned, and filler IDs must belong to the selected version. It follows that version's explicit prerequisite links, includes filler prerequisite lines, and totals the selected version's entry RP and vehicle-purchase SL costs.
- The result exposes remaining RP and SL per required vehicle, total remaining RP and SL, separate ceiling-based RP and SL match estimates, and one combined estimate equal to the greater resource estimate. RP and SL are treated as earned concurrently; research modifiers and other economy costs are not simulated.
- When a rank below the target has a quota that is not met by the mandatory prerequisite line, already-unlocked vehicles, and any explicit fillers, the calculator automatically selects eligible researchable vehicles in that rank according to the chosen optimization mode (`LeastRp` minimizes added RP; `LeastSl` minimizes vehicle purchase SL; `FewestVehicles` minimizes the count of added filler vehicles including transitive prerequisites; `FewestMatches` minimizes concurrent match requirements `max(ceil(RP/avgRP), ceil(SL/avgSL))`). Deterministic tie-breaking ends on vehicle name. Each auto-selected vehicle and its transitive prerequisite chain are gathered and marked `IsRankGateFiller = true`. Evaluation continues through all prerequisite ranks; any remaining deficit that cannot be resolved by available candidates is reported in `RankDeficits`. An already-unlocked vehicle is a traversal boundary, so prerequisites behind it are not charged again.
- Administrative draft editing, version lifecycle management, pre-publication validation, historical version diffing, and tabular tech-tree data imports are fully implemented:
  - Role-based administration (`Editor`, `Publisher`, `Admin`) via JWT Bearer authentication protects all mutation endpoints while keeping public calculation endpoints anonymous.
  - Secured administrative CRUD APIs manage catalog entities (nations, vehicle types, vehicles, game updates, research trees) and draft version components (ranks, entries, layout coordinates, folder parent groupings, and prerequisite edges).
  - Two-phase tabular imports (`POST /api/admin/drafts/{versionId}/import/preview` and `POST /api/admin/drafts/{versionId}/import`) allow administrators to import tech-tree data from CSV or XLSX into Draft versions, featuring non-mutating preview validation and atomic transactional commit.
  - Version lifecycle services (`IVersionLifecycleService`) allow cloning published snapshots into editable Drafts, validating version integrity prior to publication, publishing immutable historical snapshots, and computing structural/economic diffs between versions. Published version immutability is strictly enforced at the persistence layer.
- No saved player inventory, user-facing client web UI, or automated version selection is implemented in this repository.

## TARGET — intended user behavior

- Choose a nation and one of five conceptual vehicle trees: Aviation, Ground, Helicopter, Coastal Fleet, or Bluewater Fleet.
- Intended supported nations are USA, Germany, USSR, Great Britain, Japan, China, Italy, France, Sweden, and Israel.
- Select already researched or owned vehicles and a target vehicle. Show the required prerequisite vehicles and any rank unlock requirements, without charging for already-completed prerequisite history.
- Show total RP and total SL still required. Estimate matches from the player's average RP per match and average **net** SL per match. Net SL means SL remaining after normal match expenses from the player's perspective. Nonpositive averages are rejected rather than clamped.

## FUTURE — direction without detailed rules yet

- Back-office web UI for administrative CRUD, version workflows, and tabular import preview/commit.
- Player accounts and synchronized owned-vehicle progress across sessions and devices.
- Vehicle modification progression (a separate progression system out of scope for the initial vehicle research planner).
- Advanced War Thunder economy and RP mechanics (premium account modifiers, talisman bonuses, vehicle research efficiency penalties).
- Assisted scraping and data-conversion tools to transform external game data into the application's canonical import schema.
