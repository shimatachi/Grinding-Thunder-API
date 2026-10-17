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

const PLAYER_TOKEN_KEY = "grinding-thunder.player-token";

type SessionStatus = "loading" | "authenticated" | "anonymous";

interface SessionContextValue {
  status: SessionStatus;
  token: string | null;
  player: PlayerProfile | null;
  login: (request: PlayerLoginRequest) => Promise<void>;
  register: (request: PlayerRegisterRequest) => Promise<void>;
  logout: () => void;
  refresh: () => Promise<void>;
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

  const clearSession = useCallback(() => {
    localStorage.removeItem(PLAYER_TOKEN_KEY);
    setToken(null);
    setPlayer(null);
    setStatus("anonymous");
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
          setStatus("anonymous");
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

  const login = useCallback(
    async (request: PlayerLoginRequest) => acceptToken(await api.login(request)),
    [acceptToken],
  );

  const register = useCallback(
    async (request: PlayerRegisterRequest) => acceptToken(await api.register(request)),
    [acceptToken],
  );

  const refresh = useCallback(async () => {
    if (!token) return;
    await validate(token);
  }, [token, validate]);

  const value = useMemo(
    () => ({ status, token, player, login, register, logout: clearSession, refresh }),
    [status, token, player, login, register, clearSession, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext);
  if (!context) throw new Error("useSession must be used within SessionProvider.");
  return context;
}
