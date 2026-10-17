"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { api, ApiError } from "@/lib/api/client";
import type {
  PlayerLoginRequest,
  PlayerProfile,
  PlayerRegisterRequest,
  PlayerTokenResponse,
} from "@/lib/api/types";
import {
  clearAnonymousProgress,
  hasAnonymousProgress,
  loadAnonymousOwnedIds,
  loadAnonymousPreferences,
  recordProgressSync,
} from "@/lib/player-storage";

const PLAYER_TOKEN_KEY = "grinding-thunder.player-token";

type SessionStatus = "loading" | "authenticated" | "anonymous";

export interface SessionNotice {
  tone: "success" | "warning";
  message: string;
}

interface SessionContextValue {
  status: SessionStatus;
  token: string | null;
  player: PlayerProfile | null;
  notice: SessionNotice | null;
  login: (request: PlayerLoginRequest) => Promise<void>;
  register: (request: PlayerRegisterRequest) => Promise<void>;
  logout: () => void;
  refresh: () => Promise<void>;
  dismissNotice: () => void;
}

const SessionContext = createContext<SessionContextValue | null>(null);

function profileFromToken(response: PlayerTokenResponse): PlayerProfile {
  return {
    playerAccountId: response.playerAccountId,
    username: response.username,
    email: response.email,
  };
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SessionStatus>("loading");
  const [token, setToken] = useState<string | null>(null);
  const [player, setPlayer] = useState<PlayerProfile | null>(null);
  const [notice, setNotice] = useState<SessionNotice | null>(null);

  const clearSession = useCallback(() => {
    localStorage.removeItem(PLAYER_TOKEN_KEY);
    setToken(null);
    setPlayer(null);
    setStatus("anonymous");
    setNotice(null);
  }, []);

  const validate = useCallback(
    async (candidate: string) => {
      try {
        const profile = await api.getCurrentPlayer(candidate);
        setToken(candidate);
        setPlayer(profile);
        setStatus("authenticated");
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) clearSession();
        else {
          setToken(candidate);
          setPlayer(null);
          setStatus("anonymous");
          setNotice({
            tone: "warning",
            message: "Your saved session could not be verified. Check the API connection and try again.",
          });
        }
      }
    },
    [clearSession],
  );

  useEffect(() => {
    const storedToken = localStorage.getItem(PLAYER_TOKEN_KEY);
    if (storedToken) void validate(storedToken);
    else setStatus("anonymous");

    const synchronizeTabs = (event: StorageEvent) => {
      if (event.key !== PLAYER_TOKEN_KEY) return;
      if (event.newValue) void validate(event.newValue);
      else clearSession();
    };

    window.addEventListener("storage", synchronizeTabs);
    return () => window.removeEventListener("storage", synchronizeTabs);
  }, [clearSession, validate]);

  const acceptToken = useCallback((response: PlayerTokenResponse) => {
    localStorage.setItem(PLAYER_TOKEN_KEY, response.token);
    setToken(response.token);
    setPlayer(profileFromToken(response));
    setStatus("authenticated");
  }, []);

  const acceptAndMerge = useCallback(async (response: PlayerTokenResponse) => {
    acceptToken(response);
    setNotice(null);
    if (!hasAnonymousProgress()) return;

    try {
      const current = await api.getPlayerProgress(response.token);
      const mergedIds = new Set(current.unlockedVehicleIds.map((id) => id.toLowerCase()));
      loadAnonymousOwnedIds().forEach((id) => mergedIds.add(id));
      const localPreferences = loadAnonymousPreferences();
      await api.updatePlayerProgress(response.token, {
        unlockedVehicleIds: [...mergedIds].sort(),
        preferences: localPreferences ?? current.preferences,
      });
      clearAnonymousProgress();
      recordProgressSync("synced");
      setNotice({
        tone: "success",
        message: "Signed in and merged this browser's saved ownership into your account.",
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : "The progress service could not be reached.";
      recordProgressSync("error", message);
      setNotice({
        tone: "warning",
        message: `You are signed in, but local progress could not be merged (${message}). It remains safely stored in this browser.`,
      });
    }
  }, [acceptToken]);

  const login = useCallback(
    async (request: PlayerLoginRequest) => acceptAndMerge(await api.login(request)),
    [acceptAndMerge],
  );

  const register = useCallback(
    async (request: PlayerRegisterRequest) => acceptAndMerge(await api.register(request)),
    [acceptAndMerge],
  );

  const refresh = useCallback(async () => {
    if (!token) return;
    await validate(token);
  }, [token, validate]);

  const value = useMemo(
    () => ({
      status,
      token,
      player,
      notice,
      login,
      register,
      logout: clearSession,
      refresh,
      dismissNotice: () => setNotice(null),
    }),
    [status, token, player, notice, login, register, clearSession, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext);
  if (!context) throw new Error("useSession must be used within SessionProvider.");
  return context;
}
