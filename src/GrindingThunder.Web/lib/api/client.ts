import type {
  ApiProblemDetails,
  NationSummary,
  PlayerLoginRequest,
  PlayerProfile,
  PlayerProgress,
  PlayerRegisterRequest,
  PlayerTokenResponse,
  ResearchCalculationRequest,
  ResearchCalculationResult,
  UpdatePlayerProgressRequest,
  VehicleTreeEntry,
} from "@/lib/api/types";

export const API_BASE_URL = (
  process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5034"
).replace(/\/$/, "");

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly details?: ApiProblemDetails,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

type RequestOptions = Omit<RequestInit, "body"> & {
  token?: string | null;
  body?: unknown;
};

async function parseError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get("content-type") ?? "";
  let message = `${response.status} ${response.statusText}`.trim();
  let details: ApiProblemDetails | undefined;

  if (contentType.includes("application/json")) {
    try {
      details = (await response.json()) as ApiProblemDetails;
      const validationMessage = details.errors
        ? Object.values(details.errors).flat().join(" ")
        : undefined;
      message =
        validationMessage || details.message || details.detail || details.title || message;
    } catch {
      message = `The server returned an unreadable error response (${response.status}).`;
    }
  } else {
    const text = await response.text();
    if (text.trim()) message = text;
  }

  return new ApiError(response.status, message, details);
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set("Accept", "application/json");

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (options.token) {
    headers.set("Authorization", `Bearer ${options.token}`);
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...options,
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError(0, "The Grinding Thunder API could not be reached. Check your connection and try again.");
  }

  if (!response.ok) throw await parseError(response);
  if (response.status === 204) return undefined as T;
  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError(response.status, "The server returned an unreadable response. Please try again.");
  }
}

export const api = {
  getNations(signal?: AbortSignal) {
    return request<NationSummary[]>("/api/nations", { signal });
  },

  getVehicleTree(
    params: {
      nationId: string;
      category: string;
      researchTreeVersionId: string;
    },
    signal?: AbortSignal,
  ) {
    const query = new URLSearchParams({
      nationId: params.nationId,
      type: params.category,
      researchTreeVersionId: params.researchTreeVersionId,
    });
    return request<VehicleTreeEntry[]>(`/api/vehicles/tree?${query}`, { signal });
  },

  register(payload: PlayerRegisterRequest) {
    return request<PlayerTokenResponse>("/api/player/auth/register", {
      method: "POST",
      body: payload,
    });
  },

  login(payload: PlayerLoginRequest) {
    return request<PlayerTokenResponse>("/api/player/auth/login", {
      method: "POST",
      body: payload,
    });
  },

  getCurrentPlayer(token: string, signal?: AbortSignal) {
    return request<PlayerProfile>("/api/player/auth/me", { token, signal });
  },

  getPlayerProgress(token: string, signal?: AbortSignal) {
    return request<PlayerProgress>("/api/player/progress", { token, signal });
  },

  updatePlayerProgress(token: string, payload: UpdatePlayerProgressRequest) {
    return request<PlayerProgress>("/api/player/progress", {
      method: "PUT",
      token,
      body: payload,
    });
  },

  calculateResearch(payload: ResearchCalculationRequest, signal?: AbortSignal) {
    return request<ResearchCalculationResult>("/api/research/calculate", {
      method: "POST",
      body: payload,
      signal,
    });
  },
};
