"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useSession } from "@/components/session-provider";
import { api } from "@/lib/api/client";
import { formatNumber, formatOptimizationMode } from "@/lib/format";
import {
  VehicleAcquisitionType,
  type PlayerCalculatorPreferences,
  type ResearchCalculationResult,
  type ResearchOptimizationMode,
  type TreeRankSummary,
  type VehicleTreeEntry,
} from "@/lib/api/types";
import { ResearchTreeCanvas } from "./research-tree-canvas";

const LOCAL_OWNED_KEY = "grinding-thunder.anonymous-owned-vehicles";
const DEFAULT_RP_PER_MATCH = "5000";
const DEFAULT_SL_PER_MATCH = "20000";

const optimizationModes: ResearchOptimizationMode[] = [
  "LeastRp",
  "LeastSl",
  "FewestVehicles",
  "FewestMatches",
];

interface ResearchPlannerProps {
  ranks: TreeRankSummary[];
  entries: VehicleTreeEntry[];
  snapshotKey: string;
  nationName: string;
  categoryName: string;
}

function messageFrom(error: unknown): string {
  return error instanceof Error ? error.message : "The request failed.";
}

function normalizedIds(ids: string[]): Set<string> {
  return new Set(ids.map((id) => id.toLowerCase()));
}

function loadAnonymousOwnedIds(): Set<string> {
  try {
    const value = JSON.parse(localStorage.getItem(LOCAL_OWNED_KEY) ?? "[]") as unknown;
    return Array.isArray(value)
      ? normalizedIds(value.filter((id): id is string => typeof id === "string"))
      : new Set<string>();
  } catch {
    return new Set<string>();
  }
}

function positiveInteger(value: string): number | null {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 && parsed <= 2_147_483_647
    ? parsed
    : null;
}

export function ResearchPlanner({
  ranks,
  entries,
  snapshotKey,
  nationName,
  categoryName,
}: ResearchPlannerProps) {
  const { status, token, player } = useSession();
  const [allOwnedVehicleIds, setAllOwnedVehicleIds] = useState<Set<string>>(new Set());
  const [ownershipLoaded, setOwnershipLoaded] = useState(false);
  const [serverProgressLoaded, setServerProgressLoaded] = useState(false);
  const [targetVehicleId, setTargetVehicleId] = useState<string | null>(null);
  const [averageRpPerMatch, setAverageRpPerMatch] = useState(DEFAULT_RP_PER_MATCH);
  const [averageNetSlPerMatch, setAverageNetSlPerMatch] = useState(DEFAULT_SL_PER_MATCH);
  const [optimizationMode, setOptimizationMode] = useState<ResearchOptimizationMode>("LeastRp");
  const [calculationResult, setCalculationResult] = useState<ResearchCalculationResult | null>(null);
  const [calculationError, setCalculationError] = useState<string | null>(null);
  const [progressError, setProgressError] = useState<string | null>(null);
  const [progressMessage, setProgressMessage] = useState<string | null>(null);
  const [calculating, setCalculating] = useState(false);
  const [saving, setSaving] = useState(false);
  const calculationSequence = useRef(0);

  const entryIds = useMemo(
    () => new Set(entries.map((entry) => entry.id.toLowerCase())),
    [entries],
  );
  const activeOwnedVehicleIds = useMemo(
    () => new Set([...allOwnedVehicleIds].filter((id) => entryIds.has(id))),
    [allOwnedVehicleIds, entryIds],
  );
  const researchableEntries = useMemo(
    () => entries
      .filter((entry) => entry.acquisitionType === VehicleAcquisitionType.Researchable)
      .sort((left, right) => left.name.localeCompare(right.name)),
    [entries],
  );
  const target = targetVehicleId
    ? entries.find((entry) => entry.id === targetVehicleId) ?? null
    : null;
  const prerequisiteVehicles = calculationResult?.requiredVehicles.filter(
    (vehicle) => !vehicle.isRankGateFiller,
  ) ?? [];
  const fillerVehicles = calculationResult?.requiredVehicles.filter(
    (vehicle) => vehicle.isRankGateFiller,
  ) ?? [];

  useEffect(() => {
    if (status === "loading") return;

    const controller = new AbortController();
    setOwnershipLoaded(false);
    setServerProgressLoaded(false);
    setProgressError(null);
    setProgressMessage(null);

    if (status === "authenticated" && token) {
      api.getPlayerProgress(token, controller.signal)
        .then((progress) => {
          setAllOwnedVehicleIds(normalizedIds(progress.unlockedVehicleIds));
          setOptimizationMode(progress.preferences.optimizationMode);
          setAverageRpPerMatch(
            progress.preferences.averageRpPerMatch?.toString() ?? DEFAULT_RP_PER_MATCH,
          );
          setAverageNetSlPerMatch(
            progress.preferences.averageNetSlPerMatch?.toString() ?? DEFAULT_SL_PER_MATCH,
          );
          setServerProgressLoaded(true);
        })
        .catch((error) => {
          if (!controller.signal.aborted) setProgressError(messageFrom(error));
        })
        .finally(() => {
          if (!controller.signal.aborted) setOwnershipLoaded(true);
        });
    } else {
      setAllOwnedVehicleIds(loadAnonymousOwnedIds());
      setOptimizationMode("LeastRp");
      setAverageRpPerMatch(DEFAULT_RP_PER_MATCH);
      setAverageNetSlPerMatch(DEFAULT_SL_PER_MATCH);
      setOwnershipLoaded(true);
    }

    return () => controller.abort();
  }, [status, token]);

  useEffect(() => {
    if (status !== "anonymous" || !ownershipLoaded) return;
    localStorage.setItem(LOCAL_OWNED_KEY, JSON.stringify([...allOwnedVehicleIds].sort()));
  }, [allOwnedVehicleIds, ownershipLoaded, status]);

  useEffect(() => {
    calculationSequence.current += 1;
    setTargetVehicleId(null);
    setCalculationResult(null);
    setCalculationError(null);
    setCalculating(false);
  }, [snapshotKey]);

  function setTarget(vehicleId: string) {
    const entry = entries.find((candidate) => candidate.id === vehicleId);
    if (!entry || entry.acquisitionType !== VehicleAcquisitionType.Researchable) return;
    setTargetVehicleId(vehicleId);
    setCalculationResult(null);
    setCalculationError(null);
  }

  function toggleOwned(vehicleId: string) {
    const normalizedId = vehicleId.toLowerCase();
    const isBecomingOwned = !allOwnedVehicleIds.has(normalizedId);
    setAllOwnedVehicleIds((current) => {
      const next = new Set(current);
      if (next.has(normalizedId)) next.delete(normalizedId);
      else next.add(normalizedId);
      return next;
    });
    setProgressMessage(null);
    setProgressError(null);
    if (isBecomingOwned && targetVehicleId?.toLowerCase() === normalizedId) {
      setTargetVehicleId(null);
    }
    setCalculationResult(null);
  }

  function readPreferences(): PlayerCalculatorPreferences | null {
    const rp = positiveInteger(averageRpPerMatch);
    const sl = positiveInteger(averageNetSlPerMatch);
    if (rp === null || sl === null) {
      setCalculationError("Average RP and average net SL must both be positive whole numbers.");
      return null;
    }
    return {
      optimizationMode,
      averageRpPerMatch: rp,
      averageNetSlPerMatch: sl,
    };
  }

  async function calculate() {
    const preferences = readPreferences();
    if (!targetVehicleId) {
      setCalculationError("Choose a Researchable target vehicle first.");
      return;
    }
    if (!preferences) return;

    const sequence = ++calculationSequence.current;
    setCalculating(true);
    setCalculationError(null);
    try {
      const result = await api.calculateResearch({
        researchTreeVersionId: snapshotKey,
        targetVehicleId,
        unlockedVehicleIds: [...activeOwnedVehicleIds],
        fillerTargetIds: [],
        averageRpPerMatch: preferences.averageRpPerMatch!,
        averageNetSlPerMatch: preferences.averageNetSlPerMatch!,
        optimizationMode,
      });
      if (sequence === calculationSequence.current) setCalculationResult(result);
    } catch (error) {
      if (sequence === calculationSequence.current) setCalculationError(messageFrom(error));
    } finally {
      if (sequence === calculationSequence.current) setCalculating(false);
    }
  }

  async function saveProgress() {
    if (!token || status !== "authenticated") return;
    const preferences = readPreferences();
    if (!preferences) return;

    setSaving(true);
    setProgressError(null);
    setProgressMessage(null);
    try {
      // PUT is a full-set replacement. Refresh first, then replace only this
      // tree's slice so ownership in every other nation/category is preserved.
      const currentServerProgress = await api.getPlayerProgress(token);
      const mergedIds = new Set(
        currentServerProgress.unlockedVehicleIds
          .map((id) => id.toLowerCase())
          .filter((id) => !entryIds.has(id)),
      );
      activeOwnedVehicleIds.forEach((id) => mergedIds.add(id));
      const saved = await api.updatePlayerProgress(token, {
        unlockedVehicleIds: [...mergedIds],
        preferences,
      });
      setAllOwnedVehicleIds(normalizedIds(saved.unlockedVehicleIds));
      setProgressMessage("Ownership and calculator preferences are synchronized.");
    } catch (error) {
      setProgressError(messageFrom(error));
    } finally {
      setSaving(false);
    }
  }

  function resetCalculator() {
    calculationSequence.current += 1;
    setTargetVehicleId(null);
    setCalculationResult(null);
    setCalculationError(null);
    setCalculating(false);
  }

  return (
    <>
      <section className="panel mt-6 p-5 sm:p-7" aria-labelledby="calculator-title">
        <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-start">
          <div>
            <p className="eyebrow">Progression planner</p>
            <h2 id="calculator-title" className="mt-2 text-2xl font-black uppercase">Calculate your route</h2>
            <p className="mt-2 text-sm text-steel">
              {status === "authenticated"
                ? `Using ${player?.username ?? "your"} synchronized progress.`
                : "Anonymous ownership is saved only in this browser."}
            </p>
          </div>
          <div className="text-sm text-steel">
            <strong className="text-white">{activeOwnedVehicleIds.size}</strong> owned in this tree
          </div>
        </div>

        <div className="mt-6 grid gap-4 lg:grid-cols-4">
          <label>
            <span className="field-label">Target vehicle</span>
            <select
              className="field"
              value={targetVehicleId ?? ""}
              onChange={(event) => event.target.value ? setTarget(event.target.value) : resetCalculator()}
            >
              <option value="">Choose a Researchable vehicle</option>
              {researchableEntries.map((entry) => (
                <option key={entry.id} value={entry.id}>{entry.name}</option>
              ))}
            </select>
          </label>
          <label>
            <span className="field-label">Average RP / match</span>
            <input className="field" type="number" min="1" step="1" inputMode="numeric" value={averageRpPerMatch} onChange={(event) => { setAverageRpPerMatch(event.target.value); setProgressMessage(null); }} />
          </label>
          <label>
            <span className="field-label">Average net SL / match</span>
            <input className="field" type="number" min="1" step="1" inputMode="numeric" value={averageNetSlPerMatch} onChange={(event) => { setAverageNetSlPerMatch(event.target.value); setProgressMessage(null); }} />
          </label>
          <label>
            <span className="field-label">Optimize route for</span>
            <select className="field" value={optimizationMode} onChange={(event) => { setOptimizationMode(event.target.value as ResearchOptimizationMode); setProgressMessage(null); }}>
              {optimizationModes.map((mode) => <option key={mode} value={mode}>{formatOptimizationMode(mode)}</option>)}
            </select>
          </label>
        </div>

        <div className="mt-5 flex flex-wrap gap-3">
          <button type="button" className="button-primary" disabled={calculating || !targetVehicleId} onClick={() => void calculate()}>
            {calculating ? "Calculating…" : calculationResult ? "Recalculate" : "Calculate"}
          </button>
          <button type="button" className="button-secondary" onClick={resetCalculator} disabled={!targetVehicleId && !calculationResult}>Reset</button>
          {status === "authenticated" && (
            <button type="button" className="button-secondary" disabled={saving || !ownershipLoaded || !serverProgressLoaded} onClick={() => void saveProgress()}>
              {saving ? "Saving…" : "Save progress"}
            </button>
          )}
        </div>

        {progressError && <p className="status-error mt-5" role="alert">Progress: {progressError}</p>}
        {calculationError && <p className="status-error mt-5" role="alert">Calculator: {calculationError}</p>}
        {progressMessage && <p className="status-success mt-5" role="status">{progressMessage}</p>}
      </section>

      <section className="panel mt-6 overflow-hidden" aria-label="Visual research tree">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 px-5 py-4 sm:px-7">
          <div>
            <p className="text-xs font-bold uppercase tracking-widest text-steel">Visual research tree</p>
            <h2 className="mt-1 text-lg font-bold">{nationName} · {categoryName}</h2>
          </div>
          <div className="flex gap-5 text-sm text-steel">
            <span><strong className="text-white">{entries.length}</strong> vehicles</span>
            <span><strong className="text-white">{ranks.length}</strong> ranks</span>
          </div>
        </div>

        <ResearchTreeCanvas
          ranks={ranks}
          entries={entries}
          snapshotKey={snapshotKey}
          ownedVehicleIds={activeOwnedVehicleIds}
          targetVehicleId={targetVehicleId}
          calculationResult={calculationResult}
          onToggleOwned={toggleOwned}
          onSelectTarget={setTarget}
        />
      </section>

      {calculationResult && (
        <section className="panel mt-6 p-5 sm:p-7" aria-labelledby="results-title">
          <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-end">
            <div>
              <p className="eyebrow">Calculated progression</p>
              <h2 id="results-title" className="mt-2 text-2xl font-black uppercase">Route to {target?.name ?? "target"}</h2>
            </div>
            <p className="text-sm text-steel">Optimized for {formatOptimizationMode(optimizationMode)}</p>
          </div>

          <dl className="calculator-summary mt-6">
            <div><dt>Remaining RP</dt><dd>{formatNumber(calculationResult.totalRpRequired)}</dd></div>
            <div><dt>Remaining SL</dt><dd>{formatNumber(calculationResult.totalSlRequired)}</dd></div>
            <div><dt>RP matches</dt><dd>{formatNumber(calculationResult.rpEstimatedMatches)}</dd></div>
            <div><dt>SL matches</dt><dd>{formatNumber(calculationResult.slEstimatedMatches)}</dd></div>
            <div className="calculator-summary-primary"><dt>Concurrent ceiling</dt><dd>{formatNumber(calculationResult.estimatedMatches)} matches</dd></div>
          </dl>

          <div className="mt-7 grid gap-6 lg:grid-cols-2">
            <ResultVehicleList title="Target and prerequisite line" vehicles={prerequisiteVehicles} empty="No unowned target or prerequisite vehicles remain." />
            <ResultVehicleList title="Rank-gate fillers" vehicles={fillerVehicles} empty="No additional rank-gate fillers are required." />
          </div>

          <div className={`mt-6 border px-4 py-4 ${calculationResult.rankDeficits.length > 0 ? "border-amber-400/40 bg-amber-950/20" : "border-emerald-400/25 bg-emerald-950/20"}`}>
            <h3 className="text-xs font-bold uppercase tracking-widest text-white">Rank gates</h3>
            {calculationResult.rankDeficits.length > 0 ? (
              <ul className="mt-3 space-y-2 text-sm text-amber-100">
                {calculationResult.rankDeficits.map((deficit) => (
                  <li key={deficit.rankNumber}>Rank {deficit.rankNumber}: {deficit.shortfall} more vehicle{deficit.shortfall === 1 ? "" : "s"} required ({deficit.required} total).</li>
                ))}
              </ul>
            ) : <p className="mt-2 text-sm text-emerald-200">All rank gates can be satisfied by this route and your owned vehicles.</p>}
          </div>
        </section>
      )}
    </>
  );
}

function ResultVehicleList({
  title,
  vehicles,
  empty,
}: {
  title: string;
  vehicles: ResearchCalculationResult["requiredVehicles"];
  empty: string;
}) {
  return (
    <div>
      <h3 className="text-xs font-bold uppercase tracking-widest text-steel">{title} ({vehicles.length})</h3>
      {vehicles.length > 0 ? (
        <ul className="calculator-vehicle-list mt-3">
          {vehicles.map((vehicle) => (
            <li key={vehicle.vehicleId}>
              <strong>{vehicle.name}</strong>
              <span>{formatNumber(vehicle.rpRemaining)} RP · {formatNumber(vehicle.slRemaining)} SL</span>
            </li>
          ))}
        </ul>
      ) : <p className="mt-3 text-sm text-steel">{empty}</p>}
    </div>
  );
}
