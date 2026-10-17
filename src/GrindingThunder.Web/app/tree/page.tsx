"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api/client";
import {
  ResearchTreeVersionStatus,
  VehicleAcquisitionType,
  type NationSummary,
  type VehicleTreeEntry,
} from "@/lib/api/types";
import { ResearchPlanner } from "./research-planner";

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

function slugify(value: string): string {
  return value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

export default function TreePage() {
  return (
    <Suspense fallback={<TreeLoading label="Loading published catalog…" />}>
      <TreePageContent />
    </Suspense>
  );
}

function TreePageContent() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [nations, setNations] = useState<NationSummary[]>([]);
  const [entries, setEntries] = useState<VehicleTreeEntry[]>([]);
  const [loadedVersionId, setLoadedVersionId] = useState<string | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [treeLoading, setTreeLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [treeError, setTreeError] = useState<string | null>(null);
  const [catalogRetry, setCatalogRetry] = useState(0);
  const [treeRetry, setTreeRetry] = useState(0);

  const nationParam = searchParams.get("nation") ?? "";
  const categoryParam = searchParams.get("category") ?? "";
  const versionParam = searchParams.get("version") ?? "";
  const targetParam = searchParams.get("target") ?? "";

  const nation = useMemo(() => nations.find((item) =>
    item.id.toLowerCase() === nationParam.toLowerCase() || slugify(item.name) === nationParam.toLowerCase(),
  ) ?? nations[0], [nationParam, nations]);
  const tree = useMemo(() => nation?.researchTrees.find((item) =>
    item.id.toLowerCase() === categoryParam.toLowerCase() || item.vehicleTypeName.toLowerCase() === categoryParam.toLowerCase(),
  ) ?? nation?.researchTrees[0], [categoryParam, nation]);
  const version = useMemo(() => tree?.versions.find((item) =>
    item.id.toLowerCase() === versionParam.toLowerCase(),
  ) ?? tree?.versions[0], [tree, versionParam]);

  const validTarget = loadedVersionId === version?.id
    ? entries.find((entry) =>
        entry.id.toLowerCase() === targetParam.toLowerCase() &&
        entry.acquisitionType === VehicleAcquisitionType.Researchable,
      ) ?? null
    : null;

  const replaceQuery = useCallback((values: {
    nation: NationSummary;
    tree: NationSummary["researchTrees"][number];
    versionId: string;
    targetId?: string | null;
  }) => {
    const next = new URLSearchParams();
    next.set("nation", slugify(values.nation.name));
    next.set("category", values.tree.vehicleTypeName);
    next.set("version", values.versionId);
    if (values.targetId) next.set("target", values.targetId);
    const nextQuery = next.toString();
    if (nextQuery !== searchParams.toString()) router.replace(`${pathname}?${nextQuery}`, { scroll: false });
  }, [pathname, router, searchParams]);

  useEffect(() => {
    const controller = new AbortController();
    setCatalogLoading(true);
    setCatalogError(null);
    api.getNations(controller.signal)
      .then((catalog) => setNations(onlyPublished(catalog)))
      .catch((requestError) => {
        if (!controller.signal.aborted) setCatalogError(messageFrom(requestError));
      })
      .finally(() => {
        if (!controller.signal.aborted) setCatalogLoading(false);
      });
    return () => controller.abort();
  }, [catalogRetry]);

  useEffect(() => {
    if (!nation || !tree || !version || version.status !== ResearchTreeVersionStatus.Published) {
      setEntries([]);
      setLoadedVersionId(null);
      return;
    }

    const controller = new AbortController();
    setTreeLoading(true);
    setTreeError(null);
    setEntries([]);
    setLoadedVersionId(null);
    api.getVehicleTree({
      nationId: nation.id,
      category: tree.vehicleTypeName,
      researchTreeVersionId: version.id,
    }, controller.signal)
      .then((nextEntries) => {
        setEntries(nextEntries);
        setLoadedVersionId(version.id);
      })
      .catch((requestError) => {
        if (!controller.signal.aborted) setTreeError(messageFrom(requestError));
      })
      .finally(() => {
        if (!controller.signal.aborted) setTreeLoading(false);
      });
    return () => controller.abort();
  }, [nation, tree, treeRetry, version]);

  useEffect(() => {
    if (catalogLoading || !nation || !tree || !version) return;
    const targetId = loadedVersionId === version.id ? validTarget?.id ?? null : targetParam || null;
    replaceQuery({ nation, tree, versionId: version.id, targetId });
  }, [catalogLoading, loadedVersionId, nation, replaceQuery, targetParam, tree, validTarget?.id, version]);

  function changeNation(nextNationId: string) {
    const nextNation = nations.find((item) => item.id === nextNationId);
    const nextTree = nextNation?.researchTrees[0];
    const nextVersion = nextTree?.versions[0];
    if (nextNation && nextTree && nextVersion) replaceQuery({ nation: nextNation, tree: nextTree, versionId: nextVersion.id });
  }

  function changeTree(nextTreeId: string) {
    const nextTree = nation?.researchTrees.find((item) => item.id === nextTreeId);
    const nextVersion = nextTree?.versions[0];
    if (nation && nextTree && nextVersion) replaceQuery({ nation, tree: nextTree, versionId: nextVersion.id });
  }

  function changeVersion(nextVersionId: string) {
    if (nation && tree && tree.versions.some((item) => item.id === nextVersionId)) {
      replaceQuery({ nation, tree, versionId: nextVersionId });
    }
  }

  function changeTarget(nextTargetId: string | null) {
    if (nation && tree && version) replaceQuery({ nation, tree, versionId: version.id, targetId: nextTargetId });
  }

  return (
    <div className="page-shell py-8 sm:py-14">
      <div className="flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
        <div>
          <p className="eyebrow">Published research intelligence</p>
          <h1 className="mt-3 text-4xl font-black uppercase tracking-tight sm:text-6xl">Tech-tree browser</h1>
          <p className="mt-4 max-w-2xl text-steel">Explore an exact published snapshot, mark your ownership, and calculate the backend-approved route to a target vehicle.</p>
        </div>
        {version && <div className="border-l-2 border-signal pl-4"><p className="text-xs uppercase tracking-widest text-steel">Active snapshot</p><p className="mt-1 font-mono text-lg font-bold text-white">{version.updateVersion}</p><p className="mt-1 text-xs uppercase tracking-wider text-emerald-300">Published</p></div>}
      </div>

      <section className="panel mt-9 grid gap-5 p-5 sm:grid-cols-3 sm:p-7" aria-label="Tree selection">
        <label><span className="field-label">Nation</span><select className="field" aria-label="Select nation" value={nation?.id ?? ""} onChange={(event) => changeNation(event.target.value)} disabled={catalogLoading || nations.length === 0}>{nations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label><span className="field-label">Category</span><select className="field" aria-label="Select vehicle category" value={tree?.id ?? ""} onChange={(event) => changeTree(event.target.value)} disabled={!nation}>{nation?.researchTrees.map((item) => <option key={item.id} value={item.id}>{item.vehicleTypeName}</option>)}</select></label>
        <label><span className="field-label">Published version</span><select className="field" aria-label="Select published version" value={version?.id ?? ""} onChange={(event) => changeVersion(event.target.value)} disabled={!tree}>{tree?.versions.map((item) => <option key={item.id} value={item.id}>{item.updateVersion}</option>)}</select></label>
      </section>

      {catalogError && <ErrorState message={catalogError} label="Retry catalog" onRetry={() => setCatalogRetry((value) => value + 1)} />}
      {treeError && <ErrorState message={treeError} label="Retry snapshot" onRetry={() => setTreeRetry((value) => value + 1)} />}

      {!catalogLoading && nations.length === 0 && !catalogError && <div className="empty-state panel mt-6 p-8"><h2>No published snapshots</h2><p>There are no player-visible research-tree versions available yet. Try again after a snapshot is published.</p><button type="button" className="button-secondary mt-5" onClick={() => setCatalogRetry((value) => value + 1)}>Check again</button></div>}

      {catalogLoading || treeLoading ? (
        <section className="panel mt-6" aria-label="Visual research tree"><TreeLoading label={catalogLoading ? "Loading published catalog…" : "Loading published snapshot…"} /></section>
      ) : version && nation && tree && !catalogError && !treeError ? (
        <ResearchPlanner
          ranks={version.treeRanks}
          entries={entries}
          snapshotKey={version.id}
          nationName={nation.name}
          categoryName={tree.vehicleTypeName}
          targetVehicleId={validTarget?.id ?? null}
          onTargetVehicleChange={changeTarget}
        />
      ) : null}
    </div>
  );
}

function TreeLoading({ label }: { label: string }) {
  return <div className="tree-loading" role="status"><span />{label}</div>;
}

function ErrorState({ message, label, onRetry }: { message: string; label: string; onRetry: () => void }) {
  return <div className="status-error mt-6 flex flex-col justify-between gap-3 sm:flex-row sm:items-center" role="alert"><span>{message}</span><button type="button" className="button-secondary" onClick={onRetry}>{label}</button></div>;
}
