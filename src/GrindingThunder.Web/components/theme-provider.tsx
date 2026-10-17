"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import {
  THEME_STORAGE_KEY,
  parseThemePreference,
  resolveTheme,
  type ResolvedTheme,
  type ThemePreference,
} from "@/lib/theme";

export {
  THEME_STORAGE_KEY,
  parseThemePreference,
  resolveTheme,
  type ResolvedTheme,
  type ThemePreference,
};

export interface ThemeContextValue {
  preference: ThemePreference;
  resolvedTheme: ResolvedTheme;
  setPreference: (preference: ThemePreference) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function applyThemeToDocument(preference: ThemePreference, resolved: ResolvedTheme) {
  if (typeof document === "undefined") return;
  const root = document.documentElement;
  root.setAttribute("data-theme", resolved);
  root.setAttribute("data-theme-preference", preference);
  if (resolved === "light") {
    root.classList.add("light");
    root.classList.remove("dark");
  } else {
    root.classList.add("dark");
    root.classList.remove("light");
  }
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>("system");
  const [resolvedTheme, setResolvedTheme] = useState<ResolvedTheme>("dark");

  useEffect(() => {
    const stored = parseThemePreference(localStorage.getItem(THEME_STORAGE_KEY));
    const mediaQuery = window.matchMedia("(prefers-color-scheme: light)");
    const resolved = resolveTheme(stored, mediaQuery.matches);

    setPreferenceState(stored);
    setResolvedTheme(resolved);
    applyThemeToDocument(stored, resolved);

    const handleSystemChange = (e: MediaQueryListEvent) => {
      const currentPref = parseThemePreference(localStorage.getItem(THEME_STORAGE_KEY));
      if (currentPref === "system") {
        const nextResolved = e.matches ? "light" : "dark";
        setResolvedTheme(nextResolved);
        applyThemeToDocument("system", nextResolved);
      }
    };

    mediaQuery.addEventListener("change", handleSystemChange);
    return () => mediaQuery.removeEventListener("change", handleSystemChange);
  }, []);

  const setPreference = useCallback((nextPref: ThemePreference) => {
    const mediaQuery = typeof window !== "undefined"
      ? window.matchMedia("(prefers-color-scheme: light)")
      : { matches: false };
    const nextResolved = resolveTheme(nextPref, mediaQuery.matches);

    setPreferenceState(nextPref);
    setResolvedTheme(nextResolved);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, nextPref);
    } catch {
      // Ignore localStorage write failures in private/restricted modes
    }
    applyThemeToDocument(nextPref, nextResolved);
  }, []);

  return (
    <ThemeContext.Provider value={{ preference, resolvedTheme, setPreference }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within a ThemeProvider");
  }
  return context;
}

