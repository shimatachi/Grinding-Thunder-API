import { test, describe } from "node:test";
import assert from "node:assert/strict";
import {
  isEditableElement,
  shouldSuppressShortcut,
} from "../app/tree/tree-shortcuts.ts";
import {
  parseThemePreference,
  resolveTheme,
} from "../lib/theme.ts";
import {
  createTreeLayout,
  type TreeLayoutNode,
} from "../app/tree/tree-layout.ts";
import {
  VehicleAcquisitionType,
  type TreeRankSummary,
  type VehicleTreeEntry,
} from "../lib/api/types.ts";

describe("Keyboard shortcut suppression", () => {
  test("suppresses shortcuts for input, textarea, and select elements", () => {
    assert.equal(isEditableElement({ tagName: "input" } as unknown as EventTarget), true);
    assert.equal(isEditableElement({ tagName: "INPUT" } as unknown as EventTarget), true);
    assert.equal(isEditableElement({ tagName: "textarea" } as unknown as EventTarget), true);
    assert.equal(isEditableElement({ tagName: "select" } as unknown as EventTarget), true);
  });

  test("suppresses shortcuts for contenteditable elements", () => {
    assert.equal(
      isEditableElement({ tagName: "div", isContentEditable: true } as unknown as EventTarget),
      true
    );
  });

  test("suppresses shortcuts when closest ancestor is editable", () => {
    const mockElement = {
      tagName: "span",
      isContentEditable: false,
      closest: (selector: string) => {
        if (selector.includes("input")) return { tagName: "input" };
        return null;
      },
    };
    assert.equal(isEditableElement(mockElement as unknown as EventTarget), true);
  });

  test("does not suppress shortcuts for normal interactive buttons or null targets", () => {
    assert.equal(isEditableElement(null), false);
    assert.equal(
      isEditableElement({ tagName: "button", isContentEditable: false, closest: () => null } as unknown as EventTarget),
      false
    );
    assert.equal(
      isEditableElement({ tagName: "div", isContentEditable: false, closest: () => null } as unknown as EventTarget),
      false
    );
  });

  test("suppresses shortcuts when modifier keys are pressed", () => {
    assert.equal(shouldSuppressShortcut({ ctrlKey: true }), true);
    assert.equal(shouldSuppressShortcut({ metaKey: true }), true);
    assert.equal(shouldSuppressShortcut({ altKey: true }), true);
  });

  test("allows shortcuts when target is non-editable and no modifiers are held", () => {
    const nonEditable = { tagName: "button", isContentEditable: false, closest: () => null };
    assert.equal(
      shouldSuppressShortcut({ target: nonEditable as unknown as EventTarget, ctrlKey: false, metaKey: false, altKey: false }),
      false
    );
    assert.equal(
      shouldSuppressShortcut({ target: null, ctrlKey: false, metaKey: false, altKey: false }),
      false
    );
  });
});

describe("Theme preference resolution", () => {
  test("parses valid theme preferences accurately", () => {
    assert.equal(parseThemePreference("light"), "light");
    assert.equal(parseThemePreference("dark"), "dark");
    assert.equal(parseThemePreference("system"), "system");
  });

  test("falls back to system for null, undefined, and unrecognized values", () => {
    assert.equal(parseThemePreference(null), "system");
    assert.equal(parseThemePreference(undefined), "system");
    assert.equal(parseThemePreference(""), "system");
    assert.equal(parseThemePreference("high-contrast"), "system");
    assert.equal(parseThemePreference("solarized"), "system");
  });

  test("resolves explicit theme preferences regardless of system color scheme", () => {
    assert.equal(resolveTheme("light", false), "light");
    assert.equal(resolveTheme("light", true), "light");
    assert.equal(resolveTheme("dark", false), "dark");
    assert.equal(resolveTheme("dark", true), "dark");
  });

  test("resolves system preference dynamically based on prefers-color-scheme", () => {
    assert.equal(resolveTheme("system", true), "light");
    assert.equal(resolveTheme("system", false), "dark");
  });
});

describe("Folder layout derivation and coordinate immutability", () => {
  const mockRanks: TreeRankSummary[] = [
    {
      id: "rank-1",
      rankNumber: 1,
      requiredVehiclesUnlocked: 3,
    },
  ];

  const mockEntries: VehicleTreeEntry[] = [
    {
      id: "vautour-iia",
      name: "Vautour IIA",
      nationId: "nation-israel",
      nationName: "Israel",
      researchTreeId: "tree-israel-aviation",
      vehicleTypeId: "type-aviation",
      vehicleTypeName: "Aviation",
      researchTreeVersionId: "ver-1",
      treeRankId: "rank-1",
      treeColumn: 1,
      treeRow: 1,
      isFolderParent: true,
      folderParentId: null,
      acquisitionType: VehicleAcquisitionType.Researchable,
      rpCost: 89000,
      slCost: 260000,
      imageUrl: null,
      wikiUrl: null,
      prerequisiteIds: [],
    },
    {
      id: "vautour-iin",
      name: "Vautour IIN",
      nationId: "nation-israel",
      nationName: "Israel",
      researchTreeId: "tree-israel-aviation",
      vehicleTypeId: "type-aviation",
      vehicleTypeName: "Aviation",
      researchTreeVersionId: "ver-1",
      treeRankId: "rank-1",
      treeColumn: 1,
      treeRow: 2,
      isFolderParent: false,
      folderParentId: "vautour-iia",
      acquisitionType: VehicleAcquisitionType.Researchable,
      rpCost: 89000,
      slCost: 260000,
      imageUrl: null,
      wikiUrl: null,
      prerequisiteIds: [],
    },
    {
      id: "mirage-iiic",
      name: "Mirage IIIC",
      nationId: "nation-israel",
      nationName: "Israel",
      researchTreeId: "tree-israel-aviation",
      vehicleTypeId: "type-aviation",
      vehicleTypeName: "Aviation",
      researchTreeVersionId: "ver-1",
      treeRankId: "rank-1",
      treeColumn: 1,
      treeRow: 3,
      isFolderParent: false,
      folderParentId: null,
      acquisitionType: VehicleAcquisitionType.Researchable,
      rpCost: 170000,
      slCost: 450000,
      imageUrl: null,
      wikiUrl: null,
      prerequisiteIds: ["vautour-iia"],
    },
  ];

  test("populates folder child list and member count on parent without mutating coordinates", () => {
    const layout = createTreeLayout(mockRanks, mockEntries);

    const parentNode = layout.nodesById.get("vautour-iia");
    const childNode = layout.nodesById.get("vautour-iin");
    const followerNode = layout.nodesById.get("mirage-iiic");

    assert.ok(parentNode, "Parent node exists in layout");
    assert.ok(childNode, "Child node exists in layout");
    assert.ok(followerNode, "Follower node exists in layout");

    assert.equal(parentNode.isFolderParent, true);
    assert.deepEqual(parentNode.folderChildIds, ["vautour-iin"]);
    assert.equal(parentNode.folderMemberCount, 2);

    assert.equal(childNode.folderParentId, "vautour-iia");
    assert.equal(childNode.treeColumn, 1);
    assert.equal(childNode.treeRow, 2);

    assert.equal(parentNode.treeColumn, 1);
    assert.equal(parentNode.treeRow, 1);
  });

  test("collapsed state excludes folder members from visible nodes while preserving canonical coordinates", () => {
    const layout = createTreeLayout(mockRanks, mockEntries);
    const expandedFolderIds = new Set<string>(); // Collapsed

    const visibleNodes = layout.nodes.filter((node: TreeLayoutNode) => {
      if (!node.folderParentId) return true;
      return expandedFolderIds.has(node.folderParentId);
    });

    const visibleIds = visibleNodes.map((n) => n.id);
    assert.ok(visibleIds.includes("vautour-iia"), "Parent is visible when collapsed");
    assert.ok(visibleIds.includes("mirage-iiic"), "Follower is visible when collapsed");
    assert.equal(visibleIds.includes("vautour-iin"), false, "Child is hidden when collapsed");

    // Parent coordinates remain identical
    const parentInCollapsed = visibleNodes.find((n) => n.id === "vautour-iia");
    assert.equal(parentInCollapsed?.x, layout.nodesById.get("vautour-iia")?.x);
    assert.equal(parentInCollapsed?.y, layout.nodesById.get("vautour-iia")?.y);
  });

  test("expanded state includes folder members with exact persisted canonical coordinates", () => {
    const layout = createTreeLayout(mockRanks, mockEntries);
    const expandedFolderIds = new Set<string>(["vautour-iia"]); // Expanded

    const visibleNodes = layout.nodes.filter((node: TreeLayoutNode) => {
      if (!node.folderParentId) return true;
      return expandedFolderIds.has(node.folderParentId);
    });

    const visibleIds = visibleNodes.map((n) => n.id);
    assert.ok(visibleIds.includes("vautour-iia"), "Parent is visible when expanded");
    assert.ok(visibleIds.includes("vautour-iin"), "Child is visible when expanded");
    assert.ok(visibleIds.includes("mirage-iiic"), "Follower is visible when expanded");

    const childInExpanded = visibleNodes.find((n) => n.id === "vautour-iin");
    const canonicalChild = layout.nodesById.get("vautour-iin");

    assert.equal(childInExpanded?.x, canonicalChild?.x);
    assert.equal(childInExpanded?.y, canonicalChild?.y);
    assert.equal(childInExpanded?.treeColumn, canonicalChild?.treeColumn);
    assert.equal(childInExpanded?.treeRow, canonicalChild?.treeRow);
  });

  test("connector lines to hidden folder members are omitted when collapsed", () => {
    const layout = createTreeLayout(mockRanks, mockEntries);
    const expandedFolderIds = new Set<string>(); // Collapsed

    const visibleNodes = layout.nodes.filter((node: TreeLayoutNode) => {
      if (!node.folderParentId) return true;
      return expandedFolderIds.has(node.folderParentId);
    });
    const visibleNodeIds = new Set(visibleNodes.map((n) => n.id));

    const visibleEdges = layout.edges.filter(
      (edge) => visibleNodeIds.has(edge.fromId) && visibleNodeIds.has(edge.toId)
    );

    // Prerequisite from vautour-iia to mirage-iiic is visible
    assert.ok(
      visibleEdges.some((e) => e.fromId === "vautour-iia" && e.toId === "mirage-iiic"),
      "Edge from parent to follower is visible"
    );

    // No edges to or from the collapsed child exist in visibleEdges
    assert.equal(
      visibleEdges.some((e) => e.fromId === "vautour-iin" || e.toId === "vautour-iin"),
      false,
      "No broken edges point to or from collapsed child"
    );
  });
});
