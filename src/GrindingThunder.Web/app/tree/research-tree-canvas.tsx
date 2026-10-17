"use client";

import {
  memo,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  type WheelEvent as ReactWheelEvent,
} from "react";
import { formatNumber } from "@/lib/format";
import {
  VehicleAcquisitionType,
  type ResearchCalculationResult,
  type TreeRankSummary,
  type VehicleTreeEntry,
} from "@/lib/api/types";
import {
  createTreeLayout,
  connectorPath,
  NODE_HEIGHT,
  NODE_WIDTH,
  type TreeLayoutNode,
} from "./tree-layout";

const MIN_SCALE = 0.12;
const MAX_SCALE = 1.75;

const acquisitionPresentation: Record<number, { label: string; color: string }> = {
  [VehicleAcquisitionType.Researchable]: { label: "Researchable", color: "var(--acquisition-researchable, #63b3ed)" },
  [VehicleAcquisitionType.Premium]: { label: "Premium", color: "var(--acquisition-premium, #f3c34d)" },
  [VehicleAcquisitionType.Squadron]: { label: "Squadron", color: "var(--acquisition-squadron, #5ed184)" },
  [VehicleAcquisitionType.Event]: { label: "Event", color: "var(--acquisition-event, #4dabf7)" },
  [VehicleAcquisitionType.Pack]: { label: "Pack", color: "var(--acquisition-pack, #cf72e8)" },
  [VehicleAcquisitionType.Hidden]: { label: "Hidden", color: "var(--acquisition-hidden, #8c98a6)" },
};

interface ResearchTreeCanvasProps {
  ranks: TreeRankSummary[];
  entries: VehicleTreeEntry[];
  snapshotKey: string;
  ownedVehicleIds: ReadonlySet<string>;
  targetVehicleId: string | null;
  calculationResult: ResearchCalculationResult | null;
  onToggleOwned: (vehicleId: string) => void;
  onSelectTarget: (vehicleId: string) => void;
}

function clampScale(value: number): number {
  return Math.min(MAX_SCALE, Math.max(MIN_SCALE, value));
}

function externalHttpUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? url.toString() : null;
  } catch {
    return null;
  }
}

function VehicleImage({ src, name, compact = false }: { src: string | null; name: string; compact?: boolean }) {
  const [failed, setFailed] = useState(false);

  useEffect(() => setFailed(false), [src]);

  if (!src || failed) {
    return (
      <div className="tree-image-fallback" aria-label={`No image available for ${name}`}>
        <svg viewBox="0 0 64 40" aria-hidden="true">
          <path d="M9 26h7l5-9h23l6 9h5v7H9z" />
          <path d="M27 11h14l4 6H23zM14 33h38" />
          <circle cx="19" cy="33" r="4" />
          <circle cx="45" cy="33" r="4" />
        </svg>
        {!compact && <span>Image unavailable</span>}
      </div>
    );
  }

  return <img src={src} alt={name} loading="lazy" onError={() => setFailed(true)} />;
}

interface TreeNodeCardProps {
  node: TreeLayoutNode;
  selectedNode: boolean;
  relatedNode: boolean;
  ownedNode: boolean;
  targetNode: boolean;
  requiredVehicle: boolean;
  fillerNode: boolean;
  isFolderExpanded: boolean;
  onSetRef: (id: string, el: HTMLButtonElement | null) => void;
  onSelect: (id: string) => void;
  onNavigate: (event: ReactKeyboardEvent<HTMLButtonElement>, node: TreeLayoutNode) => void;
  onSelectTarget: (id: string) => void;
  onToggleOwned: (id: string) => void;
  onToggleFolder: (folderParentId: string, event?: React.MouseEvent) => void;
}

const TreeNodeCard = memo(function TreeNodeCard({
  node,
  selectedNode,
  relatedNode,
  ownedNode,
  targetNode,
  requiredVehicle,
  fillerNode,
  isFolderExpanded,
  onSetRef,
  onSelect,
  onNavigate,
  onSelectTarget,
  onToggleOwned,
  onToggleFolder,
}: TreeNodeCardProps) {
  const acquisition = acquisitionPresentation[node.acquisitionType] ?? {
    label: "Unknown",
    color: "#8c98a6",
  };
  const nodeStyle = {
    left: node.x,
    top: node.y,
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    zIndex: 10 + node.stackIndex,
    "--acquisition-accent": acquisition.color,
  } as CSSProperties;

  const folderClass = node.isFolderParent
    ? isFolderExpanded
      ? "tree-node-folder-parent tree-node-folder-expanded"
      : "tree-node-folder-parent tree-node-folder-collapsed"
    : node.folderParentId
      ? "tree-node-folder-child"
      : "";

  return (
    <button
      ref={(element) => onSetRef(node.id, element)}
      type="button"
      data-tree-node
      data-tree-node-id={node.id}
      className={`tree-node ${selectedNode ? "tree-node-selected" : ""} ${relatedNode ? "tree-node-related" : ""} ${ownedNode ? "tree-node-owned" : ""} ${targetNode ? "tree-node-target" : ""} ${requiredVehicle ? "tree-node-required" : ""} ${fillerNode ? "tree-node-filler" : ""} ${folderClass} ${node.acquisitionType === VehicleAcquisitionType.Hidden ? "tree-node-hidden" : ""}`}
      style={nodeStyle}
      onClick={() => onSelect(node.id)}
      onKeyDown={(event) => onNavigate(event, node)}
      onDoubleClick={() => {
        if (node.acquisitionType === VehicleAcquisitionType.Researchable) onSelectTarget(node.id);
      }}
      aria-pressed={selectedNode}
      aria-label={`${node.name}, Rank ${node.rankLabel}, ${acquisition.label}, ${formatNumber(node.rpCost)} RP, ${formatNumber(node.slCost)} SL${node.acquisitionType === VehicleAcquisitionType.Researchable ? ", double-click or press T to set as calculator target, press O to toggle owned" : ""}`}
    >
      <div className="tree-node-quick-actions" onClick={(e) => e.stopPropagation()}>
        <button
          type="button"
          className={`tree-node-quick-btn tree-node-quick-owned ${ownedNode ? "is-active" : ""}`}
          title={ownedNode ? "Mark as unowned" : "Mark as owned (researched)"}
          aria-label={`${ownedNode ? "Unmark" : "Mark"} ${node.name} as owned`}
          onClick={(e) => {
            e.stopPropagation();
            onToggleOwned(node.id);
          }}
        >
          ✓
        </button>
        {node.acquisitionType === VehicleAcquisitionType.Researchable && (
          <button
            type="button"
            className={`tree-node-quick-btn tree-node-quick-target ${targetNode ? "is-active" : ""}`}
            title={targetNode ? "Clear target" : "Set as target vehicle"}
            aria-label={`${targetNode ? "Clear" : "Set"} ${node.name} as target`}
            onClick={(e) => {
              e.stopPropagation();
              onSelectTarget(node.id);
            }}
          >
            🎯
          </button>
        )}
      </div>

      <span className="tree-node-image"><VehicleImage src={node.imageUrl} name={node.name} compact /></span>
      <span className="tree-node-content">
        <span className="tree-node-name" title={node.name}>{node.name}</span>
        <span className="tree-node-meta">
          <span>Rank {node.rankLabel}</span>
          <span className="tree-acquisition-badge">{acquisition.label}</span>
        </span>
        <span className="tree-node-costs">
          <span><small>RP</small>{formatNumber(node.rpCost)}</span>
          <span><small>SL</small>{formatNumber(node.slCost)}</span>
        </span>
        {node.isFolderParent ? (
          <button
            type="button"
            className="tree-folder-toggle"
            aria-expanded={isFolderExpanded}
            aria-label={isFolderExpanded ? `Collapse folder (${node.folderMemberCount} vehicles)` : `Expand folder (${node.folderMemberCount} vehicles)`}
            title={`Toggle folder (${node.folderMemberCount} vehicles)`}
            onClick={(e) => onToggleFolder(node.id, e)}
          >
            <span className="tree-folder-toggle-icon" aria-hidden="true">📁</span>
            <span className="tree-folder-badge">Folder</span>
            <span className="tree-folder-toggle-count">{node.folderMemberCount}</span>
            <span className="tree-folder-toggle-chevron" aria-hidden="true">{isFolderExpanded ? "▴" : "▾"}</span>
          </button>
        ) : node.folderParentId ? (
          <span className="tree-folder-badge">Folder member</span>
        ) : null}
        {(targetNode || fillerNode || requiredVehicle || ownedNode) && (
          <span className={`tree-progression-badge ${targetNode ? "tree-progression-target" : fillerNode ? "tree-progression-filler" : requiredVehicle ? "tree-progression-required" : "tree-progression-owned"}`}>
            {targetNode ? "Target" : fillerNode ? "Filler" : requiredVehicle ? "Required" : "✓ Owned"}
          </span>
        )}
      </span>
    </button>
  );
});

function ZoomOutIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M5 10h10" /></svg>;
}

function ZoomInIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 5v10M5 10h10" /></svg>;
}

function FitIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M7 3H3v4M13 3h4v4M7 17H3v-4M13 17h4v-4" /></svg>;
}

function ResetIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M4 7V3m0 0h4M4 3l3 3a6 6 0 1 1-2 7" /></svg>;
}

function CloseIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="m5 5 10 10M15 5 5 15" /></svg>;
}

function FocusIcon({ isFullscreen }: { isFullscreen: boolean }) {
  if (isFullscreen) {
    return (
      <svg viewBox="0 0 20 20" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6">
        <path d="M4 8V4h4M12 4h4v4M16 12v4h-4M8 16H4v-4" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M7 3H3v4M13 3h4v4M7 17H3v-4M13 17h4v-4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

import {
  isEditableElement,
  shouldSuppressShortcut,
} from "./tree-shortcuts";

export {
  isEditableElement,
  shouldSuppressShortcut,
};

export function ResearchTreeCanvas({
  ranks,
  entries,
  snapshotKey,
  ownedVehicleIds,
  targetVehicleId,
  calculationResult,
  onToggleOwned,
  onSelectTarget,
}: ResearchTreeCanvasProps) {
  const layout = useMemo(() => createTreeLayout(ranks, entries), [ranks, entries]);
  const [scale, setScale] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [expandedFolderIds, setExpandedFolderIds] = useState<Set<string>>(new Set());

  const workspaceRef = useRef<HTMLDivElement>(null);
  const viewportRef = useRef<HTMLDivElement>(null);
  const drawerRef = useRef<HTMLElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const nodeRefs = useRef(new Map<string, HTMLButtonElement>());
  const panRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    scrollLeft: number;
    scrollTop: number;
  } | null>(null);

  // Automatically expand folder if target vehicle is a folder child
  useEffect(() => {
    if (targetVehicleId) {
      const targetNode = layout.nodesById.get(targetVehicleId);
      if (targetNode?.folderParentId) {
        setExpandedFolderIds((prev) => {
          if (prev.has(targetNode.folderParentId!)) return prev;
          const next = new Set(prev);
          next.add(targetNode.folderParentId!);
          return next;
        });
      }
    }
  }, [targetVehicleId, layout.nodesById]);

  // Automatically expand folder if inspected vehicle is a folder child
  useEffect(() => {
    if (selectedId) {
      const selectedNode = layout.nodesById.get(selectedId);
      if (selectedNode?.folderParentId) {
        setExpandedFolderIds((prev) => {
          if (prev.has(selectedNode.folderParentId!)) return prev;
          const next = new Set(prev);
          next.add(selectedNode.folderParentId!);
          return next;
        });
      }
    }
  }, [selectedId, layout.nodesById]);

  const handleToggleFolder = useCallback((folderParentId: string, event?: React.MouseEvent) => {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    setExpandedFolderIds((prev) => {
      const next = new Set(prev);
      if (next.has(folderParentId)) {
        next.delete(folderParentId);
      } else {
        next.add(folderParentId);
      }
      return next;
    });
  }, []);

  const visibleNodes = useMemo(() => {
    return layout.nodes.filter(
      (node) => !node.folderParentId || expandedFolderIds.has(node.folderParentId),
    );
  }, [layout.nodes, expandedFolderIds]);

  const visibleNodeIds = useMemo(() => {
    return new Set(visibleNodes.map((n) => n.id));
  }, [visibleNodes]);

  const visibleEdges = useMemo(() => {
    return layout.edges.filter(
      (edge) => visibleNodeIds.has(edge.fromId) && visibleNodeIds.has(edge.toId),
    );
  }, [layout.edges, visibleNodeIds]);

  const toggleFullscreen = useCallback(async () => {
    const container = workspaceRef.current;
    if (!container) return;

    if (document.fullscreenElement) {
      try {
        await document.exitFullscreen();
      } catch {
        setIsFullscreen(false);
      }
    } else if (container.requestFullscreen) {
      try {
        await container.requestFullscreen();
      } catch {
        setIsFullscreen((prev) => !prev);
      }
    } else {
      setIsFullscreen((prev) => !prev);
    }
  }, []);

  const exitFullscreen = useCallback(async () => {
    if (document.fullscreenElement) {
      try {
        await document.exitFullscreen();
      } catch {
        setIsFullscreen(false);
      }
    } else {
      setIsFullscreen(false);
    }
  }, []);

  useEffect(() => {
    const onFullscreenChange = () => {
      setIsFullscreen(Boolean(document.fullscreenElement));
    };
    document.addEventListener("fullscreenchange", onFullscreenChange);
    return () => document.removeEventListener("fullscreenchange", onFullscreenChange);
  }, []);

  const selected = selectedId ? layout.nodesById.get(selectedId) ?? null : null;
  const selectedPrerequisites = selected
    ? selected.prerequisiteIds
        .map((id) => layout.nodesById.get(id))
        .filter((node): node is TreeLayoutNode => Boolean(node))
        .sort((left, right) => left.name.localeCompare(right.name))
    : [];
  const selectedFolderParent = selected?.folderParentId
    ? layout.nodesById.get(selected.folderParentId) ?? null
    : null;
  const selectedFolderChildren = selected
    ? layout.nodes
        .filter((node) => node.folderParentId === selected.id)
        .sort((left, right) => left.name.localeCompare(right.name))
    : [];
  const selectedUnlocks = selected
    ? layout.nodes
        .filter((node) => node.prerequisiteIds.includes(selected.id))
        .sort((left, right) => left.rankNumber - right.rankNumber || left.name.localeCompare(right.name))
    : [];
  const selectedWikiUrl = externalHttpUrl(selected?.wikiUrl);
  const requiredVehicles = useMemo(
    () => new Map(calculationResult?.requiredVehicles.map((vehicle) => [vehicle.vehicleId, vehicle]) ?? []),
    [calculationResult],
  );
  const activePathIds = useMemo(() => {
    const ids = new Set(requiredVehicles.keys());
    if (targetVehicleId) ids.add(targetVehicleId);
    return ids;
  }, [requiredVehicles, targetVehicleId]);

  useEffect(() => {
    setSelectedId(null);
    setScale(1);
    viewportRef.current?.scrollTo({ left: 0, top: 0 });
  }, [snapshotKey]);

  useEffect(() => {
    if (!selected) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    closeButtonRef.current?.focus();
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") closeDrawer();
      if (event.key !== "Tab" || !drawerRef.current) return;
      const focusable = [...drawerRef.current.querySelectorAll<HTMLElement>(
        "button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])",
      )];
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [selected?.id]);

  function closeDrawer() {
    const previousId = selectedId;
    setSelectedId(null);
    requestAnimationFrame(() => {
      if (previousId) nodeRefs.current.get(previousId)?.focus();
    });
  }

  const getActiveVehicleId = useCallback((): string | null => {
    if (selectedId) return selectedId;
    const activeEl = document.activeElement;
    if (activeEl instanceof HTMLElement) {
      const nodeId = activeEl.getAttribute("data-tree-node-id");
      if (nodeId) return nodeId;
      const closestNode = activeEl.closest<HTMLElement>("[data-tree-node-id]");
      if (closestNode) return closestNode.getAttribute("data-tree-node-id");
    }
    return null;
  }, [selectedId]);

  useEffect(() => {
    const handleGlobalKeyDown = (event: KeyboardEvent) => {
      if (shouldSuppressShortcut(event)) return;

      const key = event.key.toUpperCase();

      if (event.key === "Escape") {
        if (selectedId) {
          event.preventDefault();
          closeDrawer();
        } else if (isFullscreen) {
          event.preventDefault();
          void exitFullscreen();
        }
        return;
      }

      if (key === "F") {
        event.preventDefault();
        void toggleFullscreen();
        return;
      }

      if (key === "O") {
        const activeId = getActiveVehicleId();
        if (activeId) {
          event.preventDefault();
          onToggleOwned(activeId);
        }
        return;
      }

      if (key === "T") {
        const activeId = getActiveVehicleId();
        if (activeId) {
          const candidate = layout.nodesById.get(activeId);
          if (candidate?.acquisitionType === VehicleAcquisitionType.Researchable) {
            event.preventDefault();
            onSelectTarget(activeId);
          }
        }
        return;
      }
    };

    document.addEventListener("keydown", handleGlobalKeyDown);
    return () => document.removeEventListener("keydown", handleGlobalKeyDown);
  }, [selectedId, isFullscreen, exitFullscreen, toggleFullscreen, getActiveVehicleId, onToggleOwned, onSelectTarget, layout.nodesById]);

  const navigateNodes = useCallback((event: ReactKeyboardEvent<HTMLButtonElement>, node: TreeLayoutNode) => {
    if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
    event.preventDefault();
    const horizontal = event.key === "ArrowLeft" || event.key === "ArrowRight";
    const direction = event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 1;
    const candidates = visibleNodes.filter((candidate) => {
      if (candidate.id === node.id) return false;
      const delta = horizontal ? candidate.x - node.x : candidate.y - node.y;
      return Math.sign(delta) === direction;
    });
    candidates.sort((left, right) => {
      const leftPrimary = Math.abs((horizontal ? left.x : left.y) - (horizontal ? node.x : node.y));
      const rightPrimary = Math.abs((horizontal ? right.x : right.y) - (horizontal ? node.x : node.y));
      const leftSecondary = Math.abs((horizontal ? left.y : left.x) - (horizontal ? node.y : node.x));
      const rightSecondary = Math.abs((horizontal ? right.y : right.x) - (horizontal ? node.y : node.x));
      return leftPrimary - rightPrimary || leftSecondary - rightSecondary || left.name.localeCompare(right.name);
    });
    const next = candidates[0];
    if (next) nodeRefs.current.get(next.id)?.focus();
  }, [visibleNodes]);

  const handleSetRef = useCallback((id: string, el: HTMLButtonElement | null) => {
    if (el) nodeRefs.current.set(id, el);
    else nodeRefs.current.delete(id);
  }, []);
  const handleSelect = useCallback((id: string) => setSelectedId(id), []);
  const handleSelectTarget = useCallback((id: string) => onSelectTarget(id), [onSelectTarget]);

  function zoomTo(value: number) {
    const viewport = viewportRef.current;
    const nextScale = clampScale(value);
    if (!viewport || nextScale === scale) return;

    const contentCenterX = (viewport.scrollLeft + (viewport.clientWidth / 2)) / scale;
    const contentCenterY = (viewport.scrollTop + (viewport.clientHeight / 2)) / scale;
    setScale(nextScale);
    requestAnimationFrame(() => {
      viewport.scrollTo({
        left: (contentCenterX * nextScale) - (viewport.clientWidth / 2),
        top: (contentCenterY * nextScale) - (viewport.clientHeight / 2),
      });
    });
  }

  function fitToView() {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const horizontal = (viewport.clientWidth - 32) / layout.width;
    const vertical = (viewport.clientHeight - 32) / layout.height;
    setScale(clampScale(Math.min(horizontal, vertical, 1)));
    requestAnimationFrame(() => viewport.scrollTo({ left: 0, top: 0, behavior: "smooth" }));
  }

  function resetView() {
    setScale(1);
    requestAnimationFrame(() => viewportRef.current?.scrollTo({ left: 0, top: 0, behavior: "smooth" }));
  }

  function startPan(event: ReactPointerEvent<HTMLDivElement>) {
    if (event.button !== 0 || (event.target as Element).closest("[data-tree-node]")) return;
    const viewport = event.currentTarget;
    panRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      scrollLeft: viewport.scrollLeft,
      scrollTop: viewport.scrollTop,
    };
    viewport.setPointerCapture(event.pointerId);
    viewport.dataset.panning = "true";
  }

  function continuePan(event: ReactPointerEvent<HTMLDivElement>) {
    const pan = panRef.current;
    if (!pan || pan.pointerId !== event.pointerId) return;
    event.currentTarget.scrollLeft = pan.scrollLeft - (event.clientX - pan.startX);
    event.currentTarget.scrollTop = pan.scrollTop - (event.clientY - pan.startY);
  }

  function stopPan(event: ReactPointerEvent<HTMLDivElement>) {
    const pan = panRef.current;
    if (!pan || pan.pointerId !== event.pointerId) return;
    panRef.current = null;
    delete event.currentTarget.dataset.panning;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  }

  function handleWheel(event: ReactWheelEvent<HTMLDivElement>) {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    zoomTo(scale + (event.deltaY < 0 ? 0.08 : -0.08));
  }

  const selectedRelatedIds = useMemo(() => {
    if (!selected) return new Set<string>();
    return new Set([
      ...selected.prerequisiteIds,
      ...(selected.folderParentId ? [selected.folderParentId] : []),
      ...layout.nodes.filter((node) => node.folderParentId === selected.id).map((node) => node.id),
    ]);
  }, [layout.nodes, selected]);

  return (
    <div
      ref={workspaceRef}
      className={`tree-workspace ${isFullscreen ? "tree-workspace-fullscreen" : ""}`}
    >
      <div className="tree-toolbar">
        <div className="tree-legend" aria-label="Tree relationship legend">
          <span><i className="tree-legend-line tree-legend-prerequisite" />Prerequisite</span>
          <span><i className="tree-legend-line tree-legend-folder" />Folder group</span>
          <span className="hidden text-slate-500 lg:inline">Layout coordinates come from the published snapshot</span>
        </div>
        <div className="tree-viewport-controls" aria-label="Tree viewport controls">
          <button type="button" onClick={() => zoomTo(scale - 0.1)} aria-label="Zoom out"><ZoomOutIcon /></button>
          <output aria-live="polite">{Math.round(scale * 100)}%</output>
          <button type="button" onClick={() => zoomTo(scale + 0.1)} aria-label="Zoom in"><ZoomInIcon /></button>
          <button type="button" onClick={fitToView} aria-label="Fit the entire research tree in the viewport"><FitIcon /><span>Fit</span></button>
          <button type="button" onClick={resetView} aria-label="Reset research tree zoom and position"><ResetIcon /><span>Reset</span></button>
          <button
            type="button"
            onClick={toggleFullscreen}
            aria-pressed={isFullscreen}
            aria-label={isFullscreen ? "Exit fullscreen focus mode (F)" : "Enter fullscreen focus mode (F)"}
            title={isFullscreen ? "Exit focus (F)" : "Focus mode (F)"}
          >
            <FocusIcon isFullscreen={isFullscreen} />
            <span>{isFullscreen ? "Exit" : "Focus"}</span>
          </button>
        </div>
      </div>

      <div
        ref={viewportRef}
        className="tree-viewport"
        tabIndex={0}
        aria-label="Interactive visual research tree. Drag empty space to pan and hold Control while scrolling to zoom."
        onPointerDown={startPan}
        onPointerMove={continuePan}
        onPointerUp={stopPan}
        onPointerCancel={stopPan}
        onWheel={handleWheel}
      >
        <div
          className="tree-surface"
          style={{ width: layout.width * scale, height: layout.height * scale }}
        >
          <div
            className="tree-stage"
            style={{
              width: layout.width,
              height: layout.height,
              transform: `scale(${scale})`,
            }}
          >
            {layout.ranks.map((rank, index) => (
              <section
                key={rank.id}
                className={`tree-rank-band ${index % 2 === 0 ? "tree-rank-even" : "tree-rank-odd"}`}
                style={{ top: rank.top, height: rank.height }}
                aria-label={`Rank ${rank.label}`}
              >
                <header className="tree-rank-header">
                  <span>Research tier</span>
                  <strong><small>Rank</small>{rank.label}</strong>
                  <p>{rank.entryCount} {rank.entryCount === 1 ? "vehicle" : "vehicles"}</p>
                  {rank.requiredVehiclesUnlocked > 0 && (
                    <p className="tree-rank-quota">
                      Requires <strong>{rank.requiredVehiclesUnlocked}</strong> vehicles to unlock Rank {rank.label}
                    </p>
                  )}
                </header>
                {rank.entryCount === 0 && <p className="tree-empty-rank">No entries in this published rank</p>}
              </section>
            ))}

            <svg
              className="tree-connectors"
              viewBox={`0 0 ${layout.width} ${layout.height}`}
              width={layout.width}
              height={layout.height}
              aria-hidden="true"
            >
              <defs>
                <marker id="tree-prerequisite-arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="strokeWidth">
                  <path d="M 0 0 L 8 4 L 0 8 z" />
                </marker>
                <marker id="tree-route-arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="strokeWidth">
                  <path d="M 0 0 L 8 4 L 0 8 z" fill="#f4c14f" />
                </marker>
              </defs>
              {visibleEdges.map((edge) => {
                const touchesSelection = selectedId === edge.fromId || selectedId === edge.toId;
                const connectsCalculatedPath = edge.kind === "prerequisite" &&
                  activePathIds.has(edge.toId) &&
                  (activePathIds.has(edge.fromId) || ownedVehicleIds.has(edge.fromId));
                return (
                  <path
                    key={`${edge.kind}:${edge.fromId}:${edge.toId}`}
                    d={edge.path}
                    className={`tree-edge tree-edge-${edge.kind} ${touchesSelection ? "tree-edge-active" : ""} ${connectsCalculatedPath ? "tree-edge-route" : ""}`}
                  />
                );
              })}
            </svg>

            {visibleNodes.map((node) => {
              const selectedNode = selectedId === node.id;
              const relatedNode = selectedRelatedIds.has(node.id);
              const ownedNode = ownedVehicleIds.has(node.id);
              const targetNode = targetVehicleId === node.id;
              const requiredVehicle = requiredVehicles.get(node.id);
              const fillerNode = requiredVehicle?.isRankGateFiller === true;
              const isFolderExpanded = expandedFolderIds.has(node.id);

              return (
                <TreeNodeCard
                  key={node.id}
                  node={node}
                  selectedNode={selectedNode}
                  relatedNode={relatedNode}
                  ownedNode={ownedNode}
                  targetNode={targetNode}
                  requiredVehicle={Boolean(requiredVehicle)}
                  fillerNode={Boolean(fillerNode)}
                  isFolderExpanded={isFolderExpanded}
                  onSetRef={handleSetRef}
                  onSelect={handleSelect}
                  onNavigate={navigateNodes}
                  onSelectTarget={handleSelectTarget}
                  onToggleOwned={onToggleOwned}
                  onToggleFolder={handleToggleFolder}
                />
              );
            })}
          </div>
        </div>
      </div>

      <p className="tree-viewport-help">Drag empty space to pan · Ctrl/⌘ + wheel to zoom · Click to inspect · Double-click or T to set target · O to toggle owned · F to toggle focus</p>

      {selected && (
        <>
        <button type="button" className="tree-detail-backdrop" onClick={closeDrawer} aria-label="Close vehicle details" />
        <aside ref={drawerRef} className="tree-detail-drawer" role="dialog" aria-modal="true" aria-labelledby="tree-detail-title" aria-describedby="tree-detail-description">
          <div className="tree-detail-hero">
            <VehicleImage src={selected.imageUrl} name={selected.name} />
            <button ref={closeButtonRef} type="button" className="tree-detail-close" onClick={closeDrawer} aria-label="Close vehicle details">
              <CloseIcon />
            </button>
          </div>
          <div className="tree-detail-body">
            <p className="eyebrow">Rank {selected.rankLabel} vehicle</p>
            <h3 id="tree-detail-title">{selected.name}</h3>
            <p id="tree-detail-description" className="sr-only">Vehicle costs, ownership actions, prerequisites, folder grouping, and Wiki link.</p>
            <div className="tree-detail-badges">
              <span style={{ borderColor: acquisitionPresentation[selected.acquisitionType]?.color }}>
                {acquisitionPresentation[selected.acquisitionType]?.label ?? "Unknown"}
              </span>
              {selected.isFolderParent && <span>Folder parent</span>}
              {selected.folderParentId && <span>Folder member</span>}
              {ownedVehicleIds.has(selected.id) && <span className="tree-detail-owned">✓ Owned</span>}
              {targetVehicleId === selected.id && <span className="tree-detail-target">Target</span>}
              {requiredVehicles.get(selected.id)?.isRankGateFiller && <span className="tree-detail-filler">Rank-gate filler</span>}
              {requiredVehicles.has(selected.id) && !requiredVehicles.get(selected.id)?.isRankGateFiller && <span className="tree-detail-required">Required route</span>}
            </div>

            <dl className="tree-detail-stats">
              <div><dt>Research cost</dt><dd>{formatNumber(selected.rpCost)} RP</dd></div>
              <div><dt>Purchase cost</dt><dd>{formatNumber(selected.slCost)} SL</dd></div>
              <div><dt>Layout cell</dt><dd>C{selected.treeColumn + 1} · R{selected.treeRow + 1}</dd></div>
            </dl>

            <div className="tree-detail-actions">
              <button type="button" className="button-secondary" onClick={() => onToggleOwned(selected.id)}>
                {ownedVehicleIds.has(selected.id) ? "Mark as not owned" : "Mark as owned"}
              </button>
              <button
                type="button"
                className="button-primary"
                disabled={selected.acquisitionType !== VehicleAcquisitionType.Researchable}
                onClick={() => onSelectTarget(selected.id)}
              >
                {targetVehicleId === selected.id ? "Target selected" : "Set calculator target"}
              </button>
              {selected.acquisitionType !== VehicleAcquisitionType.Researchable && (
                <p>Only Researchable vehicles can be calculator targets.</p>
              )}
            </div>

            <section className="tree-detail-section">
              <h4>Direct prerequisites</h4>
              {selectedPrerequisites.length > 0 ? (
                <div className="tree-detail-links">
                  {selectedPrerequisites.map((node) => (
                    <button type="button" key={node.id} onClick={() => setSelectedId(node.id)}>
                      <span>{node.name}</span><small>Rank {node.rankLabel}</small>
                    </button>
                  ))}
                </div>
              ) : <p>No prerequisite vehicles.</p>}
            </section>

            {selectedUnlocks.length > 0 && (
              <section className="tree-detail-section">
                <h4>Unlocks next</h4>
                <div className="tree-detail-links">
                  {selectedUnlocks.map((node) => (
                    <button type="button" key={node.id} onClick={() => setSelectedId(node.id)}>
                      <span>{node.name}</span><small>Rank {node.rankLabel}</small>
                    </button>
                  ))}
                </div>
              </section>
            )}

            {(selectedFolderParent || selectedFolderChildren.length > 0) && (
              <section className="tree-detail-section">
                <h4>Folder group</h4>
                <div className="tree-detail-links">
                  {selectedFolderParent && (
                    <button type="button" onClick={() => setSelectedId(selectedFolderParent.id)}>
                      <span>{selectedFolderParent.name}</span><small>Parent</small>
                    </button>
                  )}
                  {selectedFolderChildren.map((node) => (
                    <button type="button" key={node.id} onClick={() => setSelectedId(node.id)}>
                      <span>{node.name}</span><small>Member</small>
                    </button>
                  ))}
                </div>
              </section>
            )}

            {selectedWikiUrl ? (
              <a className="button-primary mt-6 w-full" href={selectedWikiUrl} target="_blank" rel="noopener noreferrer">
                Open War Thunder Wiki ↗
              </a>
            ) : (
              <p className="mt-6 border-t border-white/10 pt-5 text-sm text-steel">No War Thunder Wiki link is available for this vehicle.</p>
            )}
          </div>
        </aside>
        </>
      )}
    </div>
  );
}
