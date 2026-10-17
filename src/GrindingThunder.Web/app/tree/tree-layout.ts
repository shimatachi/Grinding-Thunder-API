import type {
  TreeRankSummary,
  VehicleTreeEntry,
} from "@/lib/api/types";

export const NODE_WIDTH = 224;
export const NODE_HEIGHT = 166;
export const COLUMN_STEP = 272;
export const ROW_STEP = 194;

const STAGE_MIN_WIDTH = 980;
const GRID_ORIGIN_X = 238;
const BAND_NODE_TOP = 34;
const BAND_BOTTOM_PADDING = 42;
const EMPTY_BAND_HEIGHT = 238;
const STACK_X_OFFSET = 14;
const STACK_Y_OFFSET = 12;

export interface TreeLayoutRank extends TreeRankSummary {
  top: number;
  height: number;
  label: string;
  entryCount: number;
}

export interface TreeLayoutNode extends VehicleTreeEntry {
  x: number;
  y: number;
  stackIndex: number;
  rankNumber: number;
  rankLabel: string;
}

export type TreeEdgeKind = "prerequisite" | "folder";

export interface TreeLayoutEdge {
  fromId: string;
  toId: string;
  kind: TreeEdgeKind;
}

export interface TreeLayout {
  width: number;
  height: number;
  ranks: TreeLayoutRank[];
  nodes: TreeLayoutNode[];
  edges: TreeLayoutEdge[];
  nodesById: Map<string, TreeLayoutNode>;
}

export function formatRankLabel(rankNumber: number): string {
  if (rankNumber <= 0 || rankNumber > 3999) return String(rankNumber);

  const numerals: Array<[number, string]> = [
    [1000, "M"], [900, "CM"], [500, "D"], [400, "CD"],
    [100, "C"], [90, "XC"], [50, "L"], [40, "XL"],
    [10, "X"], [9, "IX"], [5, "V"], [4, "IV"], [1, "I"],
  ];
  let remaining = rankNumber;
  let result = "";
  for (const [value, numeral] of numerals) {
    while (remaining >= value) {
      result += numeral;
      remaining -= value;
    }
  }
  return result;
}

export function createTreeLayout(
  ranks: TreeRankSummary[],
  entries: VehicleTreeEntry[],
): TreeLayout {
  const orderedRanks = [...ranks].sort((left, right) => left.rankNumber - right.rankNumber);
  const rankById = new Map(orderedRanks.map((rank) => [rank.id, rank]));
  const validEntries = entries.filter((entry) => rankById.has(entry.treeRankId));

  const stackIndexById = new Map<string, number>();
  const coordinateGroups = new Map<string, VehicleTreeEntry[]>();
  for (const entry of validEntries) {
    const key = `${entry.treeRankId}:${entry.treeColumn}:${entry.treeRow}`;
    const group = coordinateGroups.get(key) ?? [];
    group.push(entry);
    coordinateGroups.set(key, group);
  }
  for (const group of coordinateGroups.values()) {
    group
      .sort((left, right) =>
        Number(Boolean(left.folderParentId)) - Number(Boolean(right.folderParentId)) ||
        left.name.localeCompare(right.name),
      )
      .forEach((entry, index) => stackIndexById.set(entry.id, index));
  }

  const layoutRanks: TreeLayoutRank[] = [];
  let bandTop = 0;
  for (const rank of orderedRanks) {
    const rankEntries = validEntries.filter((entry) => entry.treeRankId === rank.id);
    const greatestBottom = rankEntries.reduce((maximum, entry) => {
      const stackIndex = stackIndexById.get(entry.id) ?? 0;
      return Math.max(
        maximum,
        BAND_NODE_TOP + (entry.treeRow * ROW_STEP) + (stackIndex * STACK_Y_OFFSET) + NODE_HEIGHT,
      );
    }, 0);
    const height = Math.max(EMPTY_BAND_HEIGHT, greatestBottom + BAND_BOTTOM_PADDING);
    layoutRanks.push({
      ...rank,
      top: bandTop,
      height,
      label: formatRankLabel(rank.rankNumber),
      entryCount: rankEntries.length,
    });
    bandTop += height;
  }

  const layoutRankById = new Map(layoutRanks.map((rank) => [rank.id, rank]));
  const nodes = validEntries
    .map((entry): TreeLayoutNode => {
      const rank = layoutRankById.get(entry.treeRankId)!;
      const stackIndex = stackIndexById.get(entry.id) ?? 0;
      return {
        ...entry,
        x: GRID_ORIGIN_X + (entry.treeColumn * COLUMN_STEP) + (stackIndex * STACK_X_OFFSET),
        y: rank.top + BAND_NODE_TOP + (entry.treeRow * ROW_STEP) + (stackIndex * STACK_Y_OFFSET),
        stackIndex,
        rankNumber: rank.rankNumber,
        rankLabel: rank.label,
      };
    })
    .sort((left, right) =>
      left.rankNumber - right.rankNumber ||
      left.treeRow - right.treeRow ||
      left.treeColumn - right.treeColumn ||
      left.stackIndex - right.stackIndex ||
      left.name.localeCompare(right.name),
    );
  const nodesById = new Map(nodes.map((node) => [node.id, node]));

  const edgeKeys = new Set<string>();
  const edges: TreeLayoutEdge[] = [];
  const addEdge = (fromId: string, toId: string, kind: TreeEdgeKind) => {
    if (!nodesById.has(fromId) || !nodesById.has(toId)) return;
    const key = `${kind}:${fromId}:${toId}`;
    if (edgeKeys.has(key)) return;
    edgeKeys.add(key);
    edges.push({ fromId, toId, kind });
  };

  for (const node of nodes) {
    if (node.folderParentId) addEdge(node.folderParentId, node.id, "folder");
    for (const prerequisiteId of node.prerequisiteIds) {
      addEdge(prerequisiteId, node.id, "prerequisite");
    }
  }

  const greatestRight = nodes.reduce(
    (maximum, node) => Math.max(maximum, node.x + NODE_WIDTH),
    GRID_ORIGIN_X,
  );

  return {
    width: Math.max(STAGE_MIN_WIDTH, greatestRight + 56),
    height: Math.max(EMPTY_BAND_HEIGHT, bandTop),
    ranks: layoutRanks,
    nodes,
    edges: edges.sort((left, right) =>
      left.kind.localeCompare(right.kind) ||
      left.fromId.localeCompare(right.fromId) ||
      left.toId.localeCompare(right.toId),
    ),
    nodesById,
  };
}

export function connectorPath(
  edge: TreeLayoutEdge,
  nodesById: Map<string, TreeLayoutNode>,
): string {
  const source = nodesById.get(edge.fromId);
  const target = nodesById.get(edge.toId);
  if (!source || !target) return "";

  const sourceCenter = { x: source.x + (NODE_WIDTH / 2), y: source.y + (NODE_HEIGHT / 2) };
  const targetCenter = { x: target.x + (NODE_WIDTH / 2), y: target.y + (NODE_HEIGHT / 2) };
  const dx = targetCenter.x - sourceCenter.x;
  const dy = targetCenter.y - sourceCenter.y;
  const gutter = 28;

  if (edge.kind === "folder" || Math.abs(dy) < ROW_STEP * 0.55) {
    const direction = dx >= 0 ? 1 : -1;
    const startX = direction > 0 ? source.x + NODE_WIDTH : source.x;
    const endX = direction > 0 ? target.x : target.x + NODE_WIDTH;
    const exitX = startX + (direction * gutter);
    const enterX = endX - (direction * gutter);
    const middleX = exitX + ((enterX - exitX) / 2);
    return `M ${startX} ${sourceCenter.y} L ${exitX} ${sourceCenter.y} C ${middleX} ${sourceCenter.y}, ${middleX} ${targetCenter.y}, ${enterX} ${targetCenter.y} L ${endX} ${targetCenter.y}`;
  }

  const direction = dy >= 0 ? 1 : -1;
  const anchorShift = Math.max(-NODE_WIDTH * 0.28, Math.min(NODE_WIDTH * 0.28, dx * 0.12));
  const startX = sourceCenter.x + anchorShift;
  const endX = targetCenter.x - anchorShift;
  const startY = direction > 0 ? source.y + NODE_HEIGHT : source.y;
  const endY = direction > 0 ? target.y : target.y + NODE_HEIGHT;
  const exitY = startY + (direction * gutter);
  const enterY = endY - (direction * gutter);
  const middleY = exitY + ((enterY - exitY) / 2);
  return `M ${startX} ${startY} L ${startX} ${exitY} C ${startX} ${middleY}, ${endX} ${middleY}, ${endX} ${enterY} L ${endX} ${endY}`;
}
