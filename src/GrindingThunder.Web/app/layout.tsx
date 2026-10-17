import type { Metadata } from "next";
import { Suspense, type ReactNode } from "react";
import { Footer } from "@/components/footer";
import { Navbar } from "@/components/navbar";
import { SessionProvider } from "@/components/session-provider";
import { ThemeProvider } from "@/components/theme-provider";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "Grinding Thunder",
    template: "%s | Grinding Thunder",
  },
  description: "Plan War Thunder research paths, costs, and estimated matches.",
};

const antiFlashScript = `(function() {
  try {
    var preference = localStorage.getItem("theme-preference") || "system";
    var resolved = preference;
    if (preference === "system") {
      resolved = window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
    }
    document.documentElement.setAttribute("data-theme", resolved);
    document.documentElement.setAttribute("data-theme-preference", preference);
    if (resolved === "light") {
      document.documentElement.classList.add("light");
      document.documentElement.classList.remove("dark");
    } else {
      document.documentElement.classList.add("dark");
      document.documentElement.classList.remove("light");
    }
  } catch (e) {}
})();`;

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: antiFlashScript }} />
      </head>
      <body>
        <ThemeProvider>
          <SessionProvider>
            <div className="flex min-h-screen flex-col">
              <Suspense fallback={<div className="h-16 border-b border-white/10 bg-canvas" aria-hidden="true" />}>
                <Navbar />
              </Suspense>
              <main className="flex-1">{children}</main>
              <Footer />
            </div>
          </SessionProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
