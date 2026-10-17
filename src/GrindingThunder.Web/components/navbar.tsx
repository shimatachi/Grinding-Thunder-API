"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { useSession } from "@/components/session-provider";

const navigation = [
  { href: "/", label: "Overview" },
  { href: "/tree", label: "Tech tree" },
];

export function Navbar() {
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const { status, player, logout } = useSession();

  return (
    <header className="sticky top-0 z-50 border-b border-white/10 bg-canvas/85 backdrop-blur-xl">
      <div className="page-shell flex h-16 items-center justify-between gap-5">
        <Link href="/" className="group flex items-center gap-3" onClick={() => setMenuOpen(false)}>
          <span className="grid h-9 w-9 place-items-center border border-signal/60 bg-signal/10 text-sm font-black text-signal transition group-hover:bg-signal/20">
            GT
          </span>
          <span className="font-display text-sm font-bold uppercase tracking-[0.18em] text-white sm:text-base">
            Grinding Thunder
          </span>
        </Link>

        <button
          type="button"
          className="button-ghost px-3 md:hidden"
          aria-expanded={menuOpen}
          aria-label="Toggle navigation"
          onClick={() => setMenuOpen((open) => !open)}
        >
          {menuOpen ? "Close" : "Menu"}
        </button>

        <nav
          className={`${menuOpen ? "flex" : "hidden"} absolute inset-x-0 top-16 flex-col gap-2 border-b border-line bg-panel p-4 md:static md:flex md:flex-row md:items-center md:border-0 md:bg-transparent md:p-0`}
          aria-label="Primary navigation"
        >
          {navigation.map((item) => {
            const active = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);
            return (
              <Link
                key={item.href}
                href={item.href}
                className={`nav-link ${active ? "nav-link-active" : ""}`}
                onClick={() => setMenuOpen(false)}
              >
                {item.label}
              </Link>
            );
          })}

          <span className="mx-2 hidden h-6 w-px bg-line md:block" />

          {status === "loading" ? (
            <span className="px-3 text-sm text-steel">Checking session…</span>
          ) : player ? (
            <div className="flex items-center gap-2">
              <Link href="/profile" className="button-ghost" onClick={() => setMenuOpen(false)}>
                {player.username}
              </Link>
              <button
                type="button"
                className="button-ghost text-steel"
                onClick={() => {
                  logout();
                  setMenuOpen(false);
                }}
              >
                Log out
              </button>
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <Link href="/login" className="button-ghost" onClick={() => setMenuOpen(false)}>
                Log in
              </Link>
              <Link href="/register" className="button-primary" onClick={() => setMenuOpen(false)}>
                Create account
              </Link>
            </div>
          )}
        </nav>
      </div>
    </header>
  );
}
