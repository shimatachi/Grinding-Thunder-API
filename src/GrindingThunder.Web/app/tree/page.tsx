"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api/client";
import {
  ResearchTreeVersionStatus,
  type NationSummary,
  type VehicleTreeEntry,
} from "@/lib/api/types";
import { ResearchTreeCanvas } from "./research-tree-canvas";

function messageFrom(error: unknown): string {
  return error instanceof Error ? error.message : "The API request failed.";
}

function onlyPublished(catalog: NationSummary[]): NationSummary[] {
  return catalog
    .map((nation) => ({
      ...nation,
      researchTrees: nation.researchTrees
        .map((tree) => ({
          ...tree,
          versions: tree.versions.filter(
            (version) => version.status === ResearchTreeVersionStatus.Published,
          ),
        }))
        .filter((tree) => tree.versions.length > 0),
    }))
    .filter((nation) => nation.researchTrees.length > 0);
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

  const nation = useMemo(
    () => nations.find((item) => item.id === nationId),
    [nations, nationId],
  );
  const tree = useMemo(
    () => nation?.researchTrees.find((item) => item.id === treeId),
    [nation, treeId],
  );
  const version = useMemo(
    () => tree?.versions.find((item) => item.id === versionId),
    [tree, versionId],
  );

  useEffect(() => {
    const controller = new AbortController();
    api.getNations(controller.signal)
      .then((catalog) => {
        const publishedCatalog = onlyPublished(catalog);
        setNations(publishedCatalog);
        const firstNation = publishedCatalog[0];
        const firstTree = firstNation?.researchTrees[0];
        const latestPublishedVersion = firstTree?.versions[0];
        setNationId(firstNation?.id ?? "");
        setTreeId(firstTree?.id ?? "");
        setVersionId(latestPublishedVersion?.id ?? "");
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
    if (!nation || !tree || !version || version.status !== ResearchTreeVersionStatus.Published) {
      setEntries([]);
      return;
    }

    const controller = new AbortController();
    setTreeLoading(true);
    setEntries([]);
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
    <div className="page-shell py-10 sm:py-14">
      <div className="flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
        <div>
          <p className="eyebrow">Published research intelligence</p>
          <h1 className="mt-3 text-4xl font-black uppercase tracking-tight sm:text-6xl">Tech-tree browser</h1>
          <p className="mt-4 max-w-2xl text-steel">
            Explore the exact layout, rank gates, folders, and prerequisite lines authored for a published War Thunder snapshot.
          </p>
        </div>
        {version && (
          <div className="border-l-2 border-signal pl-4">
            <p className="text-xs uppercase tracking-widest text-steel">Active snapshot</p>
            <p className="mt-1 font-mono text-lg font-bold text-white">{version.updateVersion}</p>
            <p className="mt-1 text-xs uppercase tracking-wider text-emerald-300">Published</p>
          </div>
        )}
      </div>

      <section className="panel mt-9 grid gap-5 p-5 sm:grid-cols-3 sm:p-7" aria-label="Tree selection">
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

      <section className="panel mt-6 overflow-hidden" aria-label="Visual research tree">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 px-5 py-4 sm:px-7">
          <div>
            <p className="text-xs font-bold uppercase tracking-widest text-steel">Visual research tree</p>
            <h2 className="mt-1 text-lg font-bold">{nation?.name ?? "Nation"} · {tree?.vehicleTypeName ?? "Category"}</h2>
          </div>
          <div className="flex gap-5 text-sm text-steel">
            <span><strong className="text-white">{entries.length}</strong> vehicles</span>
            <span><strong className="text-white">{version?.treeRanks.length ?? 0}</strong> ranks</span>
          </div>
        </div>

        {catalogLoading || treeLoading ? (
          <div className="tree-loading">
            <span />
            Loading published snapshot…
          </div>
        ) : version && !error ? (
          <ResearchTreeCanvas
            ranks={version.treeRanks}
            entries={entries}
            snapshotKey={version.id}
          />
        ) : null}
      </section>
    </div>
  );
}
