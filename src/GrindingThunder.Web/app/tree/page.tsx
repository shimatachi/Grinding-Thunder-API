"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api/client";
import { formatNumber } from "@/lib/format";
import type { NationSummary, VehicleTreeEntry } from "@/lib/api/types";

function messageFrom(error: unknown): string {
  return error instanceof Error ? error.message : "The API request failed.";
}

export default function TreePage() {
  const [nations, setNations] = useState<NationSummary[]>([]);
  const [nationId, setNationId] = useState("");
  const [treeId, setTreeId] = useState("");
  const [versionId, setVersionId] = useState("");
  const [entries, setEntries] = useState<VehicleTreeEntry[]>([]);
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [treeLoading, setTreeLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const nation = useMemo(() => nations.find((item) => item.id === nationId), [nations, nationId]);
  const tree = useMemo(() => nation?.researchTrees.find((item) => item.id === treeId), [nation, treeId]);
  const version = useMemo(() => tree?.versions.find((item) => item.id === versionId), [tree, versionId]);
  const rankById = useMemo(
    () => new Map(version?.treeRanks.map((rank) => [rank.id, rank.rankNumber]) ?? []),
    [version],
  );

  useEffect(() => {
    const controller = new AbortController();
    api.getNations(controller.signal)
      .then((catalog) => {
        setNations(catalog);
        const firstNation = catalog[0];
        const firstTree = firstNation?.researchTrees[0];
        const firstVersion = firstTree?.versions[0];
        setNationId(firstNation?.id ?? "");
        setTreeId(firstTree?.id ?? "");
        setVersionId(firstVersion?.id ?? "");
      })
      .catch((requestError) => {
        if (!controller.signal.aborted) setError(messageFrom(requestError));
      })
      .finally(() => {
        if (!controller.signal.aborted) setCatalogLoading(false);
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!nation || !tree || !version) {
      setEntries([]);
      return;
    }

    const controller = new AbortController();
    setTreeLoading(true);
    setError(null);
    api.getVehicleTree(
      {
        nationId: nation.id,
        category: tree.vehicleTypeName,
        researchTreeVersionId: version.id,
      },
      controller.signal,
    )
      .then(setEntries)
      .catch((requestError) => {
        if (!controller.signal.aborted) setError(messageFrom(requestError));
      })
      .finally(() => {
        if (!controller.signal.aborted) setTreeLoading(false);
      });
    return () => controller.abort();
  }, [nation, tree, version]);

  function changeNation(nextNationId: string) {
    const nextNation = nations.find((item) => item.id === nextNationId);
    const nextTree = nextNation?.researchTrees[0];
    setNationId(nextNationId);
    setTreeId(nextTree?.id ?? "");
    setVersionId(nextTree?.versions[0]?.id ?? "");
  }

  function changeTree(nextTreeId: string) {
    const nextTree = nation?.researchTrees.find((item) => item.id === nextTreeId);
    setTreeId(nextTreeId);
    setVersionId(nextTree?.versions[0]?.id ?? "");
  }

  return (
    <div className="page-shell py-12 sm:py-16">
      <div className="flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
        <div>
          <p className="eyebrow">Published intelligence</p>
          <h1 className="mt-3 text-4xl font-black uppercase tracking-tight sm:text-6xl">Tech-tree browser</h1>
          <p className="mt-4 max-w-2xl text-steel">
            Choose an exact published snapshot. The visual research canvas arrives in Batch 40; this foundation already verifies and loads the canonical API data.
          </p>
        </div>
        {version && (
          <div className="border-l-2 border-signal pl-4">
            <p className="text-xs uppercase tracking-widest text-steel">Active snapshot</p>
            <p className="mt-1 font-mono text-lg font-bold text-white">{version.updateVersion}</p>
          </div>
        )}
      </div>

      <section className="panel mt-10 grid gap-5 p-5 sm:grid-cols-3 sm:p-7" aria-label="Tree selection">
        <label>
          <span className="field-label">Nation</span>
          <select className="field" value={nationId} onChange={(event) => changeNation(event.target.value)} disabled={catalogLoading || nations.length === 0}>
            {nations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>
          <span className="field-label">Category</span>
          <select className="field" value={treeId} onChange={(event) => changeTree(event.target.value)} disabled={!nation}>
            {nation?.researchTrees.map((item) => <option key={item.id} value={item.id}>{item.vehicleTypeName}</option>)}
          </select>
        </label>
        <label>
          <span className="field-label">Published version</span>
          <select className="field" value={versionId} onChange={(event) => setVersionId(event.target.value)} disabled={!tree}>
            {tree?.versions.map((item) => <option key={item.id} value={item.id}>{item.updateVersion}</option>)}
          </select>
        </label>
      </section>

      {error && <p className="status-error mt-6" role="alert">{error}</p>}

      {!catalogLoading && nations.length === 0 && !error && (
        <div className="panel mt-6 p-8 text-center text-steel">No published research-tree snapshots are available yet.</div>
      )}

      <section className="panel mt-6 min-h-[25rem] overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 px-5 py-4 sm:px-7">
          <div>
            <p className="text-xs font-bold uppercase tracking-widest text-steel">Snapshot manifest</p>
            <h2 className="mt-1 text-lg font-bold">{nation?.name ?? "Nation"} · {tree?.vehicleTypeName ?? "Category"}</h2>
          </div>
          <div className="flex gap-5 text-sm text-steel">
            <span><strong className="text-white">{entries.length}</strong> vehicles</span>
            <span><strong className="text-white">{version?.treeRanks.length ?? 0}</strong> ranks</span>
          </div>
        </div>

        {catalogLoading || treeLoading ? (
          <div className="grid min-h-80 place-items-center text-sm uppercase tracking-[0.2em] text-steel">Loading published data…</div>
        ) : entries.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[46rem] text-left text-sm">
              <thead className="bg-white/[0.03] text-xs uppercase tracking-wider text-steel">
                <tr><th className="px-6 py-4">Vehicle</th><th className="px-4 py-4">Rank</th><th className="px-4 py-4">RP</th><th className="px-4 py-4">SL</th><th className="px-4 py-4">Grid</th><th className="px-6 py-4 text-right">Links</th></tr>
              </thead>
              <tbody className="divide-y divide-white/10">
                {entries
                  .slice()
                  .sort((a, b) => (rankById.get(a.treeRankId) ?? 0) - (rankById.get(b.treeRankId) ?? 0) || a.treeColumn - b.treeColumn || a.treeRow - b.treeRow)
                  .map((entry) => (
                    <tr key={entry.id} className="hover:bg-white/[0.025]">
                      <td className="px-6 py-4 font-semibold text-white">{entry.name}</td>
                      <td className="px-4 py-4 text-steel">{rankById.get(entry.treeRankId) ?? "—"}</td>
                      <td className="px-4 py-4 font-mono text-steel">{formatNumber(entry.rpCost)}</td>
                      <td className="px-4 py-4 font-mono text-steel">{formatNumber(entry.slCost)}</td>
                      <td className="px-4 py-4 font-mono text-steel">{entry.treeColumn}, {entry.treeRow}</td>
                      <td className="px-6 py-4 text-right font-mono text-xs text-steel">{entry.prerequisiteIds.length} prereq.</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        ) : version && !error ? (
          <div className="grid min-h-80 place-items-center text-steel">This published snapshot contains no vehicle entries.</div>
        ) : null}
      </section>
    </div>
  );
}
