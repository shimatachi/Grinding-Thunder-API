"use client";

import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
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
  [VehicleAcquisitionType.Researchable]: { label: "Researchable", color: "#63b3ed" },
  [VehicleAcquisitionType.Premium]: { label: "Premium", color: "#f3c34d" },
  [VehicleAcquisitionType.Squadron]: { label: "Squadron", color: "#5ed184" },
  [VehicleAcquisitionType.Event]: { label: "Event", color: "#4dabf7" },
  [VehicleAcquisitionType.Pack]: { label: "Pack", color: "#cf72e8" },
  [VehicleAcquisitionType.Hidden]: { label: "Hidden", color: "#8c98a6" },
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
  const viewportRef = useRef<HTMLDivElement>(null);
  const panRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    scrollLeft: number;
    scrollTop: number;
  } | null>(null);

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
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setSelectedId(null);
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [selected]);

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
    <div className="tree-workspace">
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
          <button type="button" onClick={fitToView}><FitIcon /><span>Fit</span></button>
          <button type="button" onClick={resetView}><ResetIcon /><span>Reset</span></button>
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
                      Unlock {rank.requiredVehiclesUnlocked} vehicles to open this rank
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
              {layout.edges.map((edge) => {
                const touchesSelection = selectedId === edge.fromId || selectedId === edge.toId;
                const connectsCalculatedPath = edge.kind === "prerequisite" &&
                  activePathIds.has(edge.toId) &&
                  (activePathIds.has(edge.fromId) || ownedVehicleIds.has(edge.fromId));
                return (
                  <path
                    key={`${edge.kind}:${edge.fromId}:${edge.toId}`}
                    d={connectorPath(edge, layout.nodesById)}
                    className={`tree-edge tree-edge-${edge.kind} ${touchesSelection ? "tree-edge-active" : ""} ${connectsCalculatedPath ? "tree-edge-route" : ""}`}
                  />
                );
              })}
            </svg>

            {layout.nodes.map((node) => {
              const acquisition = acquisitionPresentation[node.acquisitionType] ?? {
                label: "Unknown",
                color: "#8c98a6",
              };
              const selectedNode = selectedId === node.id;
              const relatedNode = selectedRelatedIds.has(node.id);
              const ownedNode = ownedVehicleIds.has(node.id);
              const targetNode = targetVehicleId === node.id;
              const requiredVehicle = requiredVehicles.get(node.id);
              const fillerNode = requiredVehicle?.isRankGateFiller === true;
              const nodeStyle = {
                left: node.x,
                top: node.y,
                width: NODE_WIDTH,
                height: NODE_HEIGHT,
                zIndex: 10 + node.stackIndex,
                "--acquisition-accent": acquisition.color,
              } as CSSProperties;

              return (
                <button
                  key={node.id}
                  type="button"
                  data-tree-node
                  className={`tree-node ${selectedNode ? "tree-node-selected" : ""} ${relatedNode ? "tree-node-related" : ""} ${ownedNode ? "tree-node-owned" : ""} ${targetNode ? "tree-node-target" : ""} ${requiredVehicle ? "tree-node-required" : ""} ${fillerNode ? "tree-node-filler" : ""} ${node.folderParentId ? "tree-node-folder-child" : ""} ${node.acquisitionType === VehicleAcquisitionType.Hidden ? "tree-node-hidden" : ""}`}
                  style={nodeStyle}
                  onClick={() => setSelectedId(node.id)}
                  onDoubleClick={() => {
                    if (node.acquisitionType === VehicleAcquisitionType.Researchable) onSelectTarget(node.id);
                  }}
                  aria-pressed={selectedNode}
                  aria-label={`${node.name}, Rank ${node.rankLabel}, ${acquisition.label}, ${formatNumber(node.rpCost)} RP, ${formatNumber(node.slCost)} SL${node.acquisitionType === VehicleAcquisitionType.Researchable ? ", double-click to set as calculator target" : ""}`}
                >
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
                    {(node.isFolderParent || node.folderParentId) && (
                      <span className="tree-folder-badge">{node.isFolderParent ? "Folder" : "Folder member"}</span>
                    )}
                    {(targetNode || fillerNode || requiredVehicle || ownedNode) && (
                      <span className={`tree-progression-badge ${targetNode ? "tree-progression-target" : fillerNode ? "tree-progression-filler" : requiredVehicle ? "tree-progression-required" : "tree-progression-owned"}`}>
                        {targetNode ? "Target" : fillerNode ? "Filler" : requiredVehicle ? "Required" : "✓ Owned"}
                      </span>
                    )}
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      </div>

      <p className="tree-viewport-help">Drag empty space to pan · Ctrl/⌘ + wheel to zoom · Select for details · Double-click a Researchable vehicle to set the target</p>

      {selected && (
        <aside className="tree-detail-drawer" role="dialog" aria-modal="false" aria-labelledby="tree-detail-title">
          <div className="tree-detail-hero">
            <VehicleImage src={selected.imageUrl} name={selected.name} />
            <button type="button" className="tree-detail-close" onClick={() => setSelectedId(null)} aria-label="Close vehicle details">
              <CloseIcon />
            </button>
          </div>
          <div className="tree-detail-body">
            <p className="eyebrow">Rank {selected.rankLabel} vehicle</p>
            <h3 id="tree-detail-title">{selected.name}</h3>
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
              <h4>Prerequisites</h4>
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
              <a className="button-primary mt-6 w-full" href={selectedWikiUrl} target="_blank" rel="noreferrer">
                Open War Thunder Wiki ↗
              </a>
            ) : (
              <p className="mt-6 border-t border-white/10 pt-5 text-sm text-steel">No War Thunder Wiki link is available for this vehicle.</p>
            )}
          </div>
        </aside>
      )}
    </div>
  );
}
