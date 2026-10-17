"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useSession } from "@/components/session-provider";
import { api } from "@/lib/api/client";
import { formatNumber, formatOptimizationMode } from "@/lib/format";
import type { PlayerProgress } from "@/lib/api/types";

export default function ProfilePage() {
  const { status, player, token, logout } = useSession();
  const [progress, setProgress] = useState<PlayerProgress | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token || status !== "authenticated") return;
    const controller = new AbortController();
    api.getPlayerProgress(token, controller.signal)
      .then(setProgress)
      .catch((requestError) => {
        if (!controller.signal.aborted) setError(requestError instanceof Error ? requestError.message : "Progress could not be loaded.");
      });
    return () => controller.abort();
  }, [status, token]);

  if (status === "loading") {
    return <div className="page-shell grid min-h-[30rem] place-items-center text-steel">Loading player profile…</div>;
  }

  if (!player || !token) {
    return (
      <div className="page-shell grid min-h-[calc(100vh-8rem)] place-items-center py-14">
        <section className="panel max-w-lg p-8 text-center sm:p-12">
          <p className="eyebrow">Authentication required</p>
          <h1 className="mt-3 text-3xl font-black uppercase">Your profile is standing by</h1>
          <p className="mt-4 leading-7 text-steel">Log in with a player account to see synchronized ownership and calculator preferences.</p>
          <div className="mt-7 flex justify-center gap-3"><Link href="/login" className="button-primary">Log in</Link><Link href="/register" className="button-secondary">Create account</Link></div>
        </section>
      </div>
    );
  }

  return (
    <div className="page-shell py-12 sm:py-16">
      <div className="flex flex-col justify-between gap-5 sm:flex-row sm:items-end">
        <div><p className="eyebrow">Player profile</p><h1 className="mt-3 text-4xl font-black uppercase tracking-tight sm:text-6xl">{player.username}</h1><p className="mt-3 text-steel">{player.email}</p></div>
        <button type="button" className="button-secondary self-start" onClick={logout}>Log out</button>
      </div>

      {error && <p className="status-error mt-8" role="alert">{error}</p>}

      <div className="mt-10 grid gap-6 lg:grid-cols-3">
        <section className="panel p-7 lg:col-span-1">
          <p className="field-label">Owned vehicles</p>
          <p className="mt-5 text-5xl font-black text-white">{progress ? formatNumber(progress.unlockedVehicleIds.length) : "—"}</p>
          <p className="mt-3 text-sm leading-6 text-steel">Stable vehicle ownership synchronized across published snapshots.</p>
        </section>

        <section className="panel p-7 lg:col-span-2">
          <div className="flex items-start justify-between gap-4"><div><p className="field-label">Calculator preferences</p><h2 className="mt-2 text-xl font-bold">Planning defaults</h2></div><span className="border border-signal/30 bg-signal/10 px-3 py-1 text-xs font-bold uppercase tracking-wider text-signal">Batch 41 controls</span></div>
          <dl className="mt-7 grid gap-px bg-white/10 sm:grid-cols-3">
            <div className="bg-panel p-4"><dt className="text-xs uppercase tracking-wider text-steel">Optimization</dt><dd className="mt-2 font-semibold">{progress ? formatOptimizationMode(progress.preferences.optimizationMode) : "—"}</dd></div>
            <div className="bg-panel p-4"><dt className="text-xs uppercase tracking-wider text-steel">Average RP</dt><dd className="mt-2 font-mono font-semibold">{progress?.preferences.averageRpPerMatch ? formatNumber(progress.preferences.averageRpPerMatch) : "Not set"}</dd></div>
            <div className="bg-panel p-4"><dt className="text-xs uppercase tracking-wider text-steel">Average net SL</dt><dd className="mt-2 font-mono font-semibold">{progress?.preferences.averageNetSlPerMatch ? formatNumber(progress.preferences.averageNetSlPerMatch) : "Not set"}</dd></div>
          </dl>
          <p className="mt-5 text-sm leading-6 text-steel">Editing these preferences and applying synchronized ownership to calculations is intentionally deferred to the calculator experience.</p>
        </section>
      </div>

      <div className="mt-6 panel flex flex-col justify-between gap-5 p-7 sm:flex-row sm:items-center"><div><h2 className="text-lg font-bold">Ready to inspect a line?</h2><p className="mt-1 text-sm text-steel">Browse the latest published snapshots from the API.</p></div><Link href="/tree" className="button-primary">Open tech tree</Link></div>
    </div>
  );
}
