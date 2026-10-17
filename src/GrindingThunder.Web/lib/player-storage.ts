import type {
  PlayerCalculatorPreferences,
  ResearchOptimizationMode,
} from "@/lib/api/types";

const ANONYMOUS_OWNED_KEY = "grinding-thunder.anonymous-owned-vehicles";
const ANONYMOUS_PREFERENCES_KEY = "grinding-thunder.anonymous-calculator-preferences";
const PROGRESS_SYNC_KEY = "grinding-thunder.progress-sync";

const optimizationModes: ResearchOptimizationMode[] = [
  "LeastRp",
  "LeastSl",
  "FewestVehicles",
  "FewestMatches",
];

export interface ProgressSyncState {
  status: "synced" | "error";
  at: string;
  message?: string;
}

function hasStorage(): boolean {
  return typeof window !== "undefined" && typeof window.localStorage !== "undefined";
}

function positiveInteger(value: unknown): number | null {
  return typeof value === "number" && Number.isInteger(value) && value > 0
    ? value
    : null;
}

export function loadAnonymousOwnedIds(): Set<string> {
  if (!hasStorage()) return new Set();
  try {
    const value = JSON.parse(localStorage.getItem(ANONYMOUS_OWNED_KEY) ?? "[]") as unknown;
    return Array.isArray(value)
      ? new Set(
          value
            .filter((id): id is string => typeof id === "string")
            .map((id) => id.toLowerCase()),
        )
      : new Set();
  } catch {
    return new Set();
  }
}

export function saveAnonymousOwnedIds(ids: Iterable<string>): void {
  if (!hasStorage()) return;
  localStorage.setItem(
    ANONYMOUS_OWNED_KEY,
    JSON.stringify([...new Set([...ids].map((id) => id.toLowerCase()))].sort()),
  );
}

export function loadAnonymousPreferences(): PlayerCalculatorPreferences | null {
  if (!hasStorage()) return null;
  try {
    const value = JSON.parse(
      localStorage.getItem(ANONYMOUS_PREFERENCES_KEY) ?? "null",
    ) as Partial<PlayerCalculatorPreferences> | null;
    if (!value || !optimizationModes.includes(value.optimizationMode as ResearchOptimizationMode)) {
      return null;
    }

    const averageRpPerMatch = positiveInteger(value.averageRpPerMatch);
    const averageNetSlPerMatch = positiveInteger(value.averageNetSlPerMatch);
    if (averageRpPerMatch === null || averageNetSlPerMatch === null) return null;

    return {
      optimizationMode: value.optimizationMode as ResearchOptimizationMode,
      averageRpPerMatch,
      averageNetSlPerMatch,
    };
  } catch {
    return null;
  }
}

export function saveAnonymousPreferences(preferences: PlayerCalculatorPreferences): void {
  if (!hasStorage()) return;
  localStorage.setItem(ANONYMOUS_PREFERENCES_KEY, JSON.stringify(preferences));
}

export function hasAnonymousProgress(): boolean {
  return loadAnonymousOwnedIds().size > 0 || loadAnonymousPreferences() !== null;
}

export function clearAnonymousProgress(): void {
  if (!hasStorage()) return;
  localStorage.removeItem(ANONYMOUS_OWNED_KEY);
  localStorage.removeItem(ANONYMOUS_PREFERENCES_KEY);
}

export function loadProgressSyncState(): ProgressSyncState | null {
  if (!hasStorage()) return null;
  try {
    const value = JSON.parse(localStorage.getItem(PROGRESS_SYNC_KEY) ?? "null") as ProgressSyncState | null;
    return value && (value.status === "synced" || value.status === "error") && typeof value.at === "string"
      ? value
      : null;
  } catch {
    return null;
  }
}

export function recordProgressSync(status: ProgressSyncState["status"], message?: string): ProgressSyncState {
  const value: ProgressSyncState = {
    status,
    at: new Date().toISOString(),
    ...(message ? { message } : {}),
  };
  if (hasStorage()) localStorage.setItem(PROGRESS_SYNC_KEY, JSON.stringify(value));
  return value;
}

export function clearPlayerLocalCache(): void {
  clearAnonymousProgress();
  if (hasStorage()) localStorage.removeItem(PROGRESS_SYNC_KEY);
}
