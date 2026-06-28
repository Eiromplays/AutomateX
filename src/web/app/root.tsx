import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { Link, Links, Meta, NavLink, Outlet, Scripts, ScrollRestoration } from "react-router";
import { Toasts, toast } from "./components/toast";
import { ConfirmProvider, usePrompt } from "./components/ui/confirm";
import { api, getWorkspaceId, setWorkspaceId } from "./lib/api";
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

function ApiKeyButton() {
  const prompt = usePrompt();
  return (
    <button
      type="button"
      title="Sign in with API key"
      className="text-zinc-600 hover:text-zinc-100"
      onClick={async () => {
        const key = await prompt({
          title: "Sign in with API key",
          label: "Leave blank to sign out.",
          placeholder: "API key",
          password: true,
          confirmLabel: "Sign in",
        });
        if (key === null) return;
        try {
          if (key) {
            await api.auth.login(key);
          } else {
            await api.auth.logout();
          }
          window.location.reload();
        } catch {
          toast.error("Invalid API key.");
        }
      }}
    >
      ⚿
    </button>
  );
}

const SEEN_STORAGE = "automatex.seenWorkspaces";

function getSeen(): string[] | null {
  const raw = localStorage.getItem(SEEN_STORAGE);
  return raw ? (JSON.parse(raw) as string[]) : null;
}

function markSeen(ids: string[]) {
  const seen = new Set(getSeen() ?? []);
  for (const id of ids) seen.add(id);
  localStorage.setItem(SEEN_STORAGE, JSON.stringify([...seen]));
}

function unmarkSeen(ids: string[]) {
  const seen = new Set(getSeen() ?? []);
  for (const id of ids) seen.delete(id);
  localStorage.setItem(SEEN_STORAGE, JSON.stringify([...seen]));
}

// Surfaces memberships that appeared since the last visit — auto-membership
// with visibility instead of silent dropdown archaeology.
function NewWorkspaceBanner() {
  const [, setTick] = useState(0);
  const { data: workspaces } = useQuery({
    queryKey: ["workspaces"],
    queryFn: api.workspaces.list,
    staleTime: 60_000,
  });

  if (!workspaces) return null;

  if (getSeen() === null) {
    // First visit: everything counts as known EXCEPT fresh invites (server-flagged:
    // memberships our account had never touched before this session).
    markSeen(workspaces.filter((w) => !w.isNew).map((w) => w.id));
  } else {
    // Fresh invites override stale seen-state (e.g. a membership re-created for an
    // already-known workspace). Un-seeing keeps the banner up across refreshes
    // until explicitly switched-to or dismissed.
    const fresh = workspaces.filter((w) => w.isNew).map((w) => w.id);
    if (fresh.length > 0) {
      unmarkSeen(fresh);
    }
  }

  const known = getSeen() ?? [];
  const unseen = workspaces.filter((w) => !known.includes(w.id));
  if (unseen.length === 0) return null;

  return (
    <div className="mb-6 space-y-2">
      {unseen.map((workspace) => (
        <div
          key={workspace.id}
          className="flex items-center justify-between rounded-md border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-sm"
        >
          <span>
            You've been added to <span className="font-medium">{workspace.name}</span> as {workspace.role}.
          </span>
          <span className="flex gap-3">
            <button
              type="button"
              className="text-emerald-400 hover:underline"
              onClick={() => {
                markSeen([workspace.id]);
                setWorkspaceId(workspace.id);
                window.location.href = "/";
              }}
            >
              Switch
            </button>
            <button
              type="button"
              className="text-zinc-500 hover:text-zinc-200"
              onClick={() => {
                markSeen([workspace.id]);
                setTick((t) => t + 1);
              }}
            >
              Dismiss
            </button>
          </span>
        </div>
      ))}
    </div>
  );
}

function WorkspaceSwitcher() {
  const prompt = usePrompt();
  const { data: workspaces } = useQuery({
    queryKey: ["workspaces"],
    queryFn: api.workspaces.list,
    staleTime: 60_000,
  });

  // The stored id is a UI-preference cache, not a source of truth. If it points at a
  // workspace that no longer exists (deleted, or a wiped dev DB), drop it so requests
  // fall back to Default instead of silently scoping every query to a dead id.
  useEffect(() => {
    if (!workspaces) return;
    const stored = getWorkspaceId();
    if (stored && !workspaces.some((w) => w.id === stored)) {
      setWorkspaceId(null);
      window.location.reload();
    }
  }, [workspaces]);

  if (!workspaces || workspaces.length === 0) return null;

  const current = getWorkspaceId() ?? workspaces[0]?.id;

  return (
    <select
      className="rounded-md border border-zinc-800 bg-zinc-900 px-2 py-1 text-xs text-zinc-300 focus:border-emerald-500 focus:outline-none"
      value={current}
      onChange={async (e) => {
        if (e.target.value === "__new__") {
          const name = await prompt({
            title: "New workspace",
            placeholder: "Workspace name",
            confirmLabel: "Create",
          });
          if (!name) return;
          const created = await api.workspaces.create(name);
          setWorkspaceId(created.id);
        } else {
          setWorkspaceId(e.target.value);
        }
        // Land on the workflow list — the current detail route may not exist
        // in the workspace we just switched into.
        window.location.href = "/";
      }}
    >
      {workspaces.map((workspace) => (
        <option key={workspace.id} value={workspace.id}>
          {workspace.name}
        </option>
      ))}
      <option value="__new__">+ New workspace…</option>
    </select>
  );
}

function SignInGate() {
  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <div className="rounded-lg border border-zinc-800 p-8 text-center">
        <div className="mb-2 text-xl font-semibold tracking-tight">
          Automate<span className="text-emerald-400">X</span>
        </div>
        <p className="mb-6 text-sm text-zinc-500">Sign in with your organization account to continue.</p>
        <a
          href={`/auth/login?returnUrl=${encodeURIComponent(window.location.pathname)}`}
          className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500"
        >
          Sign in
        </a>
      </div>
    </div>
  );
}

function Shell() {
  const { data: me, isLoading } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: api.auth.me,
    staleTime: Infinity,
    retry: false,
  });

  if (isLoading) {
    return null;
  }

  const gated = me?.mode === "oidc" && !me.authenticated;

  return (
    <div className="mx-auto max-w-5xl px-6 py-8">
      <header className="mb-8 flex items-center justify-between border-b border-zinc-800 pb-4">
        <Link to="/" className="text-xl font-semibold tracking-tight">
          Automate<span className="text-emerald-400">X</span>
        </Link>
        <nav className="flex items-center gap-5 text-sm">
          {!gated && (
            <>
              <WorkspaceSwitcher />
              <NavLink to="/" end className={navLinkClass}>
                Dashboard
              </NavLink>
              <NavLink to="/workflows" className={navLinkClass}>
                Workflows
              </NavLink>
              <NavLink to="/executions" className={navLinkClass}>
                Executions
              </NavLink>
              <NavLink to="/connections" className={navLinkClass}>
                Connections
              </NavLink>
              <NavLink to="/plugins" className={navLinkClass}>
                Plugins
              </NavLink>
              <NavLink to="/audit" className={navLinkClass}>
                Audit
              </NavLink>
              <NavLink to="/workspace" className={navLinkClass}>
                Workspace
              </NavLink>
            </>
          )}
          {me?.mode === "apikey" && <ApiKeyButton />}
          {me?.mode === "oidc" && me.authenticated && (
            <span className="flex items-center gap-3 text-xs text-zinc-500">
              {me.name ?? me.email}
              <a href="/auth/logout" className="hover:text-zinc-100">
                Sign out
              </a>
            </span>
          )}
        </nav>
      </header>
      {!gated && <NewWorkspaceBanner />}
      {gated ? <SignInGate /> : <Outlet />}
      <footer className="mt-10 border-t border-zinc-800 pt-3 text-center text-xs text-zinc-600">
        <VersionBadge />
      </footer>
      <Toasts />
    </div>
  );
}

// The running build, served by the API (baked at publish). Anonymous + gate-exempt, so it shows
// on the sign-in screen too — a quick visual "what's deployed", handy after a self-update.
function VersionBadge() {
  const { data } = useQuery({
    queryKey: ["version"],
    queryFn: api.meta.version,
    staleTime: 5 * 60_000,
  });
  return <span>AutomateX{data?.version ? ` v${data.version}` : ""}</span>;
}

export default function Root() {
  const [queryClient] = useState(() => new QueryClient());

  return (
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <Shell />
      </ConfirmProvider>
    </QueryClientProvider>
  );
}
