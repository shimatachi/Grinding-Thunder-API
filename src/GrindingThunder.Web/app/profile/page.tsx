"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { useSession } from "@/components/session-provider";
import { api } from "@/lib/api/client";
import { formatNumber, formatOptimizationMode } from "@/lib/format";
import {
  clearPlayerLocalCache,
  loadProgressSyncState,
  recordProgressSync,
  type ProgressSyncState,
} from "@/lib/player-storage";
import {
  ResearchTreeVersionStatus,
  type PlayerProgress,
} from "@/lib/api/types";

interface CategoryBreakdown {
  category: string;
  count: number;
}

interface NationBreakdown {
  nation: string;
  count: number;
  categories: CategoryBreakdown[];
}

interface ProgressSummary {
  nations: NationBreakdown[];
  mappedOwnedCount: number;
  unavailableSnapshots: number;
}

function messageFrom(error: unknown): string {
  return error instanceof Error ? error.message : "Progress could not be loaded.";
}

function formatSyncTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "Unknown"
    : new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}

export default function ProfilePage() {
  const { status, player, token, logout } = useSession();
  const [progress, setProgress] = useState<PlayerProgress | null>(null);
  const [summary, setSummary] = useState<ProgressSummary | null>(null);
  const [syncState, setSyncState] = useState<ProgressSyncState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [retryKey, setRetryKey] = useState(0);
  const [cacheMessage, setCacheMessage] = useState<string | null>(null);

  const loadProfile = useCallback(async (activeToken: string, signal: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const [nextProgress, catalog] = await Promise.all([
        api.getPlayerProgress(activeToken, signal),
        api.getNations(signal),
      ]);
      if (signal.aborted) return;
      setProgress(nextProgress);
      const nextSyncState = recordProgressSync("synced");
      setSyncState(nextSyncState);

      const ownedIds = new Set(nextProgress.unlockedVehicleIds.map((id) => id.toLowerCase()));
      const snapshots = catalog.flatMap((nation) => nation.researchTrees.flatMap((tree) => {
        const version = tree.versions.find((item) => item.status === ResearchTreeVersionStatus.Published);
        return version ? [{ nation, tree, version }] : [];
      }));
      const results = await Promise.allSettled(snapshots.map(async ({ nation, tree, version }) => ({
        nation: nation.name,
        category: tree.vehicleTypeName,
        entries: await api.getVehicleTree({
          nationId: nation.id,
          category: tree.vehicleTypeName,
          researchTreeVersionId: version.id,
        }, signal),
      })));
      if (signal.aborted) return;

      const mappedOwnedIds = new Set<string>();
      const byNation = new Map<string, Map<string, number>>();
      for (const result of results) {
        if (result.status !== "fulfilled") continue;
        const count = result.value.entries.reduce((total, entry) => {
          if (!ownedIds.has(entry.id.toLowerCase())) return total;
          mappedOwnedIds.add(entry.id.toLowerCase());
          return total + 1;
        }, 0);
        if (count === 0) continue;
        const categories = byNation.get(result.value.nation) ?? new Map<string, number>();
        categories.set(result.value.category, count);
        byNation.set(result.value.nation, categories);
      }

      const nations = [...byNation.entries()].map(([nation, categories]) => ({
        nation,
        count: [...categories.values()].reduce((total, count) => total + count, 0),
        categories: [...categories.entries()]
          .map(([category, count]) => ({ category, count }))
          .sort((left, right) => left.category.localeCompare(right.category)),
      })).sort((left, right) => right.count - left.count || left.nation.localeCompare(right.nation));

      setSummary({
        nations,
        mappedOwnedCount: mappedOwnedIds.size,
        unavailableSnapshots: results.filter((result) => result.status === "rejected").length,
      });
    } catch (requestError) {
      if (signal.aborted) return;
      const message = messageFrom(requestError);
      setError(message);
      setSyncState(recordProgressSync("error", message));
    } finally {
      if (!signal.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    setSyncState(loadProgressSyncState());
    if (!token || status !== "authenticated") return;
    const controller = new AbortController();
    void loadProfile(token, controller.signal);
    return () => controller.abort();
  }, [loadProfile, retryKey, status, token]);

  if (status === "loading") {
    return <div className="page-shell grid min-h-[30rem] place-items-center text-steel" role="status">Loading player profile…</div>;
  }

  if (!player || !token) {
    return (
      <div className="page-shell grid min-h-[calc(100vh-8rem)] place-items-center py-14">
        <section className="panel max-w-lg p-8 text-center sm:p-12">
          <p className="eyebrow">Authentication required</p>
          <h1 className="mt-3 text-3xl font-black uppercase">Your profile is standing by</h1>
          <p className="mt-4 leading-7 text-steel">Log in with a player account to see synchronized ownership and calculator preferences.</p>
          <div className="mt-7 flex flex-wrap justify-center gap-3"><Link href="/login?returnTo=/profile" className="button-primary">Log in</Link><Link href="/register?returnTo=/profile" className="button-secondary">Create account</Link></div>
        </section>
      </div>
    );
  }

  const unmappedOwnedCount = progress && summary
    ? Math.max(0, progress.unlockedVehicleIds.length - summary.mappedOwnedCount)
    : 0;

  return (
    <div className="page-shell py-10 sm:py-16">
      <div className="flex flex-col justify-between gap-5 sm:flex-row sm:items-end">
        <div><p className="eyebrow">Player profile</p><h1 className="mt-3 text-4xl font-black uppercase tracking-tight sm:text-6xl">{player.username}</h1><p className="mt-3 text-steel">{player.email}</p></div>
        <span className="sync-badge" data-status={syncState?.status ?? "unknown"}>
          <span aria-hidden="true" />
          {syncState?.status === "synced" ? "Synchronized" : syncState?.status === "error" ? "Sync interrupted" : "Not synchronized yet"}
        </span>
      </div>

      {error && (
        <div className="status-error mt-8 flex flex-col justify-between gap-3 sm:flex-row sm:items-center" role="alert">
          <span>{error}</span>
          <button type="button" className="button-secondary" onClick={() => setRetryKey((value) => value + 1)}>Retry profile</button>
        </div>
      )}

      <div className="mt-10 grid gap-6 lg:grid-cols-3">
        <section className="panel p-7 lg:col-span-1" aria-labelledby="account-details-title">
          <p className="field-label">Account identity</p>
          <h2 id="account-details-title" className="mt-2 text-xl font-bold">{player.username}</h2>
          <dl className="profile-details mt-6">
            <div><dt>Email</dt><dd>{player.email}</dd></div>
            <div><dt>Account ID</dt><dd className="break-all font-mono text-xs">{player.playerAccountId}</dd></div>
            <div><dt>Last progress sync</dt><dd>{syncState ? formatSyncTime(syncState.at) : "No local sync record"}</dd></div>
          </dl>
        </section>

        <section className="panel p-7 lg:col-span-2" aria-labelledby="preferences-title">
          <div className="flex items-start justify-between gap-4"><div><p className="field-label">Calculator preferences</p><h2 id="preferences-title" className="mt-2 text-xl font-bold">Planning defaults</h2></div>{loading && <span className="text-xs uppercase tracking-wider text-steel" role="status">Refreshing…</span>}</div>
          <dl className="mt-7 grid gap-px bg-white/10 sm:grid-cols-3">
            <div className="bg-panel p-4"><dt className="text-xs uppercase tracking-wider text-steel">Optimization</dt><dd className="mt-2 font-semibold">{progress ? formatOptimizationMode(progress.preferences.optimizationMode) : "—"}</dd></div>
            <div className="bg-panel p-4"><dt className="text-xs uppercase tracking-wider text-steel">Average RP</dt><dd className="mt-2 font-mono font-semibold">{progress?.preferences.averageRpPerMatch ? formatNumber(progress.preferences.averageRpPerMatch) : "Not set"}</dd></div>
            <div className="bg-panel p-4"><dt className="text-xs uppercase tracking-wider text-steel">Average net SL</dt><dd className="mt-2 font-mono font-semibold">{progress?.preferences.averageNetSlPerMatch ? formatNumber(progress.preferences.averageNetSlPerMatch) : "Not set"}</dd></div>
          </dl>
          <p className="mt-5 text-sm leading-6 text-steel">Change these values in the tech-tree planner, then save progress to make them your account defaults.</p>
        </section>
      </div>

      <section className="panel mt-6 p-7" aria-labelledby="ownership-title">
        <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
          <div><p className="field-label">Saved progress</p><h2 id="ownership-title" className="text-2xl font-black uppercase">Owned vehicles</h2></div>
          <p className="font-mono text-4xl font-black text-white">{progress ? formatNumber(progress.unlockedVehicleIds.length) : "—"}</p>
        </div>

        {summary?.unavailableSnapshots ? <p className="status-warning mt-5" role="status">{summary.unavailableSnapshots} published snapshot{summary.unavailableSnapshots === 1 ? " was" : "s were"} unavailable, so this breakdown may be incomplete.</p> : null}

        {loading && !summary ? (
          <div className="profile-loading mt-7" role="status">Building nation and category breakdown…</div>
        ) : summary && summary.nations.length > 0 ? (
          <div className="ownership-breakdown mt-7">
            {summary.nations.map((nation) => (
              <article key={nation.nation}>
                <header><h3>{nation.nation}</h3><strong>{formatNumber(nation.count)}</strong></header>
                <ul>{nation.categories.map((category) => <li key={category.category}><span>{category.category}</span><strong>{formatNumber(category.count)}</strong></li>)}</ul>
              </article>
            ))}
          </div>
        ) : progress && progress.unlockedVehicleIds.length === 0 ? (
          <div className="empty-state mt-7"><h3>No saved vehicles yet</h3><p>Open a published tree, mark vehicles as owned, and save your progress.</p></div>
        ) : null}

        {unmappedOwnedCount > 0 && <p className="mt-5 text-sm text-steel">{formatNumber(unmappedOwnedCount)} owned vehicle{unmappedOwnedCount === 1 ? " is" : "s are"} retained in your account but not present in the latest available published snapshots.</p>}
      </section>

      <section className="panel mt-6 flex flex-col justify-between gap-6 p-7 lg:flex-row lg:items-center" aria-labelledby="account-actions-title">
        <div><p className="field-label">Account actions</p><h2 id="account-actions-title" className="text-lg font-bold">Session and device data</h2><p className="mt-2 max-w-2xl text-sm leading-6 text-steel">Clearing local cache removes anonymous ownership, local calculator defaults, and this device's sync timestamp. It does not delete synchronized account progress or sign you out.</p>{cacheMessage && <p className="mt-3 text-sm text-emerald-200" role="status">{cacheMessage}</p>}</div>
        <div className="flex flex-wrap gap-3 lg:justify-end">
          <button type="button" className="button-secondary" onClick={() => {
            if (!window.confirm("Clear Grinding Thunder's local anonymous progress and sync record from this browser?")) return;
            clearPlayerLocalCache();
            setSyncState(null);
            setCacheMessage("Local cache cleared. Your synchronized account progress is unchanged.");
          }}>Clear local cache</button>
          <button type="button" className="button-secondary" onClick={logout}>Log out</button>
          <Link href="/tree" className="button-primary">Open tech tree</Link>
        </div>
      </section>
    </div>
  );
}
