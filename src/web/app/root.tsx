import type { ReactNode } from "react";
import { useState } from "react";
import { Link, Links, Meta, NavLink, Outlet, Scripts, ScrollRestoration } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { api } from "./lib/api";
import "./app.css";

export function Layout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
        <title>AutomateX</title>
        <Meta />
        <Links />
      </head>
      <body className="min-h-screen bg-zinc-950 text-zinc-100 antialiased">
        {children}
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? "text-zinc-100" : "text-zinc-400 hover:text-zinc-100";

export default function Root() {
  const [queryClient] = useState(() => new QueryClient());

  return (
    <QueryClientProvider client={queryClient}>
      <div className="mx-auto max-w-5xl px-6 py-8">
        <header className="mb-8 flex items-center justify-between border-b border-zinc-800 pb-4">
          <Link to="/" className="text-xl font-semibold tracking-tight">
            Automate<span className="text-emerald-400">X</span>
          </Link>
          <nav className="flex items-center gap-5 text-sm">
            <NavLink to="/" end className={navLinkClass}>
              Workflows
            </NavLink>
            <NavLink to="/executions" className={navLinkClass}>
              Executions
            </NavLink>
            <NavLink to="/connections" className={navLinkClass}>
              Connections
            </NavLink>
            <button
              type="button"
              title="Sign in with API key"
              className="text-zinc-600 hover:text-zinc-100"
              onClick={async () => {
                const key = window.prompt("API key (leave blank to sign out):");
                if (key === null) return;
                try {
                  if (key) {
                    await api.auth.login(key);
                  } else {
                    await api.auth.logout();
                  }
                  window.location.reload();
                } catch {
                  window.alert("Invalid API key.");
                }
              }}
            >
              ⚿
            </button>
          </nav>
        </header>
        <Outlet />
      </div>
    </QueryClientProvider>
  );
}
