import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router";
import { ConnectionForm } from "../components/connection-form";
import { filterConnections } from "../components/connection-form-logic";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { api, type ConnectionSummary } from "../lib/api";

export default function Connections() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();
  const [editing, setEditing] = useState<ConnectionSummary | null>(null);
  // Preselected type for create mode (deep-link from the plugins page).
  const [initialType, setInitialType] = useState<string | undefined>(undefined);
  const [search, setSearch] = useState("");

  const { data: connectionTypes } = useQuery({
    queryKey: ["connection-types"],
    queryFn: api.connections.types,
    staleTime: 60_000,
  });

  const typeFor = (key: string) => connectionTypes?.find((t) => t.type === key) ?? null;

  const { data: connections, isLoading } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
  });

  const [searchParams, setSearchParams] = useSearchParams();

  // Deep-link from the plugins page ("+ Add" on a connection type) preselects the type.
  useEffect(() => {
    const wanted = searchParams.get("type");
    if (wanted) {
      setEditing(null);
      setInitialType(wanted);
      setSearchParams({}, { replace: true });
    }
  }, [searchParams, setSearchParams]);

  const remove = useMutation({
    mutationFn: ({ id, force }: { id: string; force?: boolean }) => api.connections.remove(id, force),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["connections"] });
      toast.success("Connection deleted.");
    },
    onError: (error, variables) => {
      const message = String(error);
      if (message.includes("force=true")) {
        confirm({ title: "Delete anyway?", body: message, confirmLabel: "Delete", destructive: true }).then((ok) => {
          if (ok) remove.mutate({ ...variables, force: true });
          else remove.reset();
        });
        return;
      }
      toast.error(`Delete failed — ${message}`);
    },
  });

  const test = useMutation({
    mutationFn: (id: string) => api.connections.test(id),
    onSuccess: (result) =>
      result.ok ? toast.success(`✓ ${result.message}`) : toast.error(`Test failed — ${result.message}`),
    onError: (error) => toast.error(`Test failed — ${String(error)}`),
  });

  // Hand off to the provider's consent screen; the callback returns to /connections?oauth=…
  const connect = useMutation({
    mutationFn: (id: string) => api.connections.oauthStart(id),
    onSuccess: ({ authorizeUrl }) => {
      window.location.href = authorizeUrl;
    },
    onError: (error) => toast.error(`Connect failed — ${String(error)}`),
  });

  // Surface the OAuth callback outcome (?oauth=connected / ?oauth_error=…) once, then clear it.
  useEffect(() => {
    if (searchParams.get("oauth") === "connected") {
      toast.success("Connected.");
      queryClient.invalidateQueries({ queryKey: ["connections"] });
      setSearchParams({}, { replace: true });
    } else if (searchParams.get("oauth_error")) {
      toast.error(`Connect failed — ${searchParams.get("oauth_error")}`);
      setSearchParams({}, { replace: true });
    }
  }, [searchParams, setSearchParams, queryClient]);

  const edit = (connection: ConnectionSummary) => {
    setInitialType(undefined);
    setEditing(connection);
  };

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-lg font-semibold">Connections</h1>
        <p className="text-sm text-zinc-500">
          Encrypted secret bundles — reference them in step configs as{" "}
          <code className="text-emerald-400">{"{{connections.<name>.<field>}}"}</code>. Values are
          write-only: never shown again after saving.
        </p>
      </div>

      {isLoading && <p className="text-sm text-zinc-500">Loading…</p>}

      {(connections?.length ?? 0) > 0 && (
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search connections…"
          className="w-full max-w-xs rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none"
        />
      )}

      <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
        {filterConnections(connections ?? [], search).map((connection) => (
          <li key={connection.id} className="flex items-center justify-between px-4 py-3 text-sm">
            <div className="flex items-center gap-3">
              <span className="font-medium">{connection.name}</span>
              {connection.provider && (
                <span className="rounded-full border border-zinc-700 px-1.5 py-0.5 text-[10px] text-zinc-400">
                  {typeFor(connection.provider)?.displayName ?? connection.provider}
                </span>
              )}
              <span className="text-xs text-zinc-500">
                {connection.secretKeys.map((k) => `🔒 ${k}`).join("  ")}
              </span>
              {!connection.decryptable && (
                <span className="text-xs text-red-400">undecryptable — wrong encryption key?</span>
              )}
            </div>
            <div className="flex items-center gap-3">
              {connection.isOAuth ? (
                <>
                  {(() => {
                    const connected = connection.secretKeys.includes("accessToken");
                    const expired =
                      connection.oauthExpiresAt != null && connection.oauthExpiresAt * 1000 < Date.now();
                    return (
                      <span
                        className={`text-xs ${!connected ? "text-zinc-500" : expired ? "text-amber-400" : "text-emerald-400"}`}
                      >
                        {!connected ? "● not connected" : expired ? "● expired" : "● connected"}
                      </span>
                    );
                  })()}
                  <button
                    type="button"
                    onClick={() => connect.mutate(connection.id)}
                    disabled={connect.isPending}
                    className="text-xs text-zinc-500 hover:text-emerald-400 disabled:opacity-50"
                  >
                    {connection.secretKeys.includes("accessToken") ? "Reconnect" : "Connect"}
                  </button>
                </>
              ) : (
                <button
                  type="button"
                  onClick={() => test.mutate(connection.id)}
                  disabled={test.isPending}
                  className="text-xs text-zinc-500 hover:text-emerald-400 disabled:opacity-50"
                >
                  {test.isPending && test.variables === connection.id ? "Testing…" : "Test"}
                </button>
              )}
              <button type="button" onClick={() => edit(connection)} className="text-xs text-zinc-500 hover:text-zinc-100">
                Edit
              </button>
              <button type="button" onClick={() => remove.mutate({ id: connection.id })} className="text-xs text-zinc-500 hover:text-red-400">
                Delete
              </button>
            </div>
          </li>
        ))}
        {connections?.length === 0 && (
          <li className="px-4 py-6 text-center text-sm text-zinc-500">No connections yet.</li>
        )}
        {(connections?.length ?? 0) > 0 && filterConnections(connections ?? [], search).length === 0 && (
          <li className="px-4 py-6 text-center text-sm text-zinc-500">No connections match “{search}”.</li>
        )}
      </ul>

      <div className="max-w-xl space-y-3 rounded-lg border border-zinc-800 p-4">
        <h2 className="text-sm font-medium text-zinc-300">{editing ? `Edit ${editing.name}` : "New connection"}</h2>
        <ConnectionForm
          editing={editing}
          initialType={initialType}
          onSaved={() => {
            setEditing(null);
            setInitialType(undefined);
          }}
          onCancel={editing ? () => setEditing(null) : undefined}
        />
      </div>
    </div>
  );
}
