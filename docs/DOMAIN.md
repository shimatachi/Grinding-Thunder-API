# Domain

## Principles

A `Vehicle` is the stable identity of a War Thunder vehicle. Update-dependent values belong to versioned tech-tree data rather than replacing that identity. Published tree versions are immutable historical snapshots. Progression is a directed graph of explicit prerequisites; visual row and column placement does not define dependencies. Rank unlock requirements are data-driven.

## TARGET — conceptual model and invariants

- `Nation` identifies a country independently of vehicle-tree type. `VehicleType` identifies Aviation, Ground, Helicopter, Coastal Fleet, or Bluewater Fleet. A `ResearchTree` is one nation plus one vehicle type.
- `GameUpdate` identifies a War Thunder update. A `ResearchTreeVersion` is a snapshot of one research tree for an update. Draft versions are editable; publishing creates the historical version players can select. Published versions should be immutable. Corrections should eventually use a new version or revision rather than silently rewriting history. `GameUpdate`, `ResearchTreeVersion`, and their version-owned vehicle entries, ranks, prerequisites, and layout data are implemented. Detailed publication rules and the publishing workflow remain open.
- `Vehicle` retains stable identity across versions. A versioned vehicle placement/state records its membership in a tree version and its RP cost, SL cost, rank, visual position, research availability, and folder/group placement. Changes to these values create new version data; they do not rewrite published history.
- Rank and rank-gate configuration belong to the tree version. The required count or other rank rule must come from data, not code constants. The exact eligibility and counting rules need validation against the game.
- A versioned prerequisite edge explicitly links a vehicle placement to a prerequisite in the same snapshot. Traversing edges toward prerequisites finds required vehicles. Shared prerequisites should be counted once. An already-owned vehicle that is required on the route is a traversal boundary: do not automatically charge historical prerequisites behind it again.
- Visual tree position and folder/group placement describe presentation. Neither implies a progression edge. The progression graph must be valid for a version; detailed import-time validation rules remain to be designed.

## CURRENT — initial match estimate

RP matches = ceiling(total remaining RP / average RP per match). SL matches = ceiling(total remaining vehicle-purchase SL / average net SL per match). Because RP and SL are earned concurrently, the overall estimate is the greater of these two values. Both averages must be greater than zero. A zero remaining cost produces zero matches for that resource. This is an initial estimation rule, not a complete simulation of War Thunder economy mechanics.

## FUTURE — acquisition rules

Acquisition categories may include normal researchable, premium, squadron, event, pack/marketplace, and hidden vehicles. Their route eligibility, costs, and ownership rules need product decisions before encoding them.

## Current model gaps

`Nation.Type` was replaced by the `Nation`/`VehicleType`/`ResearchTree` model. `Vehicle` now contains stable identity and presentation metadata only (`Id`, `Name`, and `ImageUrl`); its tree memberships are represented by `TreeEntries`. A `VehicleTreeEntry` is unique per `ResearchTreeVersion` + `Vehicle` and owns version-specific RP cost, SL cost, rank association, `TreeRow`, `TreeColumn`, and optional folder membership. Folder membership links an entry to a parent entry in the same version. Parent status is derived from whether an entry has folder children; it is not stored as a second source of truth. Folder relationships and coordinates are presentation/grouping metadata and never imply prerequisite edges.

`TreeRank` owns version-specific rank numbers and rank-gate requirements. `VehiclePrerequisite` links two versioned entries; composite foreign keys make the database reject cross-version prerequisite edges. Folder membership uses the same version-carrying composite-FK approach, so the database also rejects cross-version folder relationships. Folder layout never affects calculation.

Every calculator request selects one `ResearchTreeVersionId`. The target, every owned vehicle ID, and every explicit filler target ID must have an entry in that version. Unknown versions fail distinctly from invalid vehicle membership. The calculator reads RP/SL costs, ranks, rank gates, and prerequisite edges only from the selected snapshot; it never infers or falls back to another version. The same deduplicated remaining-entry set supplies both cost totals. Owned vehicles remain traversal boundaries, shared prerequisite ancestors are counted once, and traversal protects against cycles. Flat vehicle-list projections remain transitional: flat values are emitted only for a vehicle with exactly one entry, while entry-based projections include their version ID. No publishing workflow exists yet.
