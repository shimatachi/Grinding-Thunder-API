# Product

## Purpose

Plan the War Thunder vehicles, Research Points (RP), Silver Lions (SL), and estimated matches needed to reach a target vehicle. The intended workflow is to choose a nation and vehicle tree, mark vehicles already researched or owned, choose a target, and see the remaining path and cost.

## CURRENT — implemented backend behavior

- The API lists nations and vehicles and can filter a vehicle tree by nation and the current nation `Type`. Development seed data contains four nations marked `Ground`, with a small USA Ground vehicle sample. This does not establish support for all nations or trees.
- A calculation selects one research-tree version and accepts a target vehicle, IDs treated as already unlocked, optional player-selected filler targets, average RP per match, and average net SL per match. Both averages must be greater than zero. Target, owned, and filler IDs must belong to the selected version. It follows that version's explicit prerequisite links, includes filler prerequisite lines, and totals the selected version's entry RP and vehicle-purchase SL costs.
- The result exposes remaining RP and SL per required vehicle, total remaining RP and SL, separate ceiling-based RP and SL match estimates, and one combined estimate equal to the greater resource estimate. RP and SL are treated as earned concurrently; research modifiers and other economy costs are not simulated.
- The result reports the first unmet rank quota below the target rank. The player must select filler vehicles; the API does not choose them. An already-unlocked vehicle is a traversal boundary, so prerequisites behind it are not charged again.
- No saved player inventory, administrative editing, automatic filler selection, or automatic version selection is implemented in this repository.

## TARGET — intended user behavior

- Choose a nation and one of five conceptual vehicle trees: Aviation, Ground, Helicopter, Coastal Fleet, or Bluewater Fleet.
- Intended supported nations are USA, Germany, USSR, Great Britain, Japan, China, Italy, France, Sweden, and Israel.
- Select already researched or owned vehicles and a target vehicle. Show the required prerequisite vehicles and any rank unlock requirements, without charging for already-completed prerequisite history.
- Show total RP and total SL still required. Estimate matches from the player's average RP per match and average **net** SL per match. Net SL means SL remaining after normal match expenses from the player's perspective. Nonpositive averages are rejected rather than clamped.

## FUTURE — direction without detailed rules yet

- Automatically select a route, including vehicles needed to satisfy rank gates, instead of requiring the player to choose all fillers. Possible optimization objectives include least RP, least SL, fewest additional vehicles, and estimated fewest matches; the algorithm is not specified yet.
- Let administrators maintain War Thunder data through a conceptual publishing flow: published tree version → clone/create draft for a new War Thunder update → edit → validate → preview changes → publish. Published historical versions should not be silently mutated. Editing details and permissions remain open.
- Compare historical updates, including vehicles added or removed and changes to rank, RP, SL, or prerequisites.
- Vehicle modifications are a separate progression system and are out of scope for the initial vehicle research planner. They may be supported later.
