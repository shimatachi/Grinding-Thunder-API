"use client";

import { useTheme } from "@/components/theme-provider";

export function ThemeToggle({ className = "" }: { className?: string }) {
  const { preference, resolvedTheme, setPreference } = useTheme();

  return (
    <div
      role="radiogroup"
      aria-label="Color theme"
      className={`theme-toggle-group ${className}`}
    >
      <button
        type="button"
        role="radio"
        aria-checked={preference === "light"}
        aria-label="Light theme"
        title="Light theme"
        className="theme-toggle-option"
        onClick={() => setPreference("light")}
      >
        <span aria-hidden="true">☀️</span>
        <span className="theme-toggle-label">Light</span>
      </button>

      <button
        type="button"
        role="radio"
        aria-checked={preference === "dark"}
        aria-label="Dark theme"
        title="Dark theme"
        className="theme-toggle-option"
        onClick={() => setPreference("dark")}
      >
        <span aria-hidden="true">🌙</span>
        <span className="theme-toggle-label">Dark</span>
      </button>

      <button
        type="button"
        role="radio"
        aria-checked={preference === "system"}
        aria-label={`System theme (currently ${resolvedTheme})`}
        title={`System theme (currently ${resolvedTheme})`}
        className="theme-toggle-option"
        onClick={() => setPreference("system")}
      >
        <span aria-hidden="true">💻</span>
        <span className="theme-toggle-label">Auto</span>
      </button>
    </div>
  );
}

