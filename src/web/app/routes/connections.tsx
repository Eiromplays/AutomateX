import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router";
import { api, type ConnectionSummary, type ConnectionTypeInfo } from "../lib/api";
import { toast } from "../components/toast";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none disabled:opacity-50";

type SecretRow = {
  key: number;
  name: string;
  value: string;
  existing: boolean;
  removed: boolean;
  // Type-driven metadata (a "fixed" row's key comes from a connection type).
  fixed: boolean;
  secret: boolean;
  required: boolean;
  label?: string;
  helpText?: string | null;
  docsUrl?: string | null;
};

let nextRow = 1;

const emptyRow = (): SecretRow => ({
  key: nextRow++,
  name: "",
  value: "",
  existing: false,
  removed: false,
  fixed: false,
  secret: true,
  required: false,
});

// Build rows from a connection type's fields, merging any already-stored keys.
function rowsFromType(type: ConnectionTypeInfo, existingKeys: string[]): SecretRow[] {
  const typed = type.fields.map((f) => ({
    key: nextRow++,
    name: f.key,
    value: "",
    existing: existingKeys.includes(f.key),
    removed: false,
    fixed: true,
    secret: f.secret,
    required: f.required,
    label: f.label,
    helpText: f.helpText,
    docsUrl: f.docsUrl,
  }));
  const extra = existingKeys
    .filter((k) => !type.fields.some((f) => f.key === k))
    .map((k) => ({ ...emptyRow(), name: k, existing: true }));
  return [...typed, ...extra];
}

function freeRows(existingKeys: string[]): SecretRow[] {
  return existingKeys.length > 0
    ? [...existingKeys.map((k) => ({ ...emptyRow(), name: k, existing: true })), emptyRow()]
    : [emptyRow()];
}

export default function Connections() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<ConnectionSummary | null>(null);
  const [name, setName] = useState("");
  const [provider, setProvider] = useState("");
  const [rows, setRows] = useState<SecretRow[]>([emptyRow()]);

  const { data: connectionTypes } = useQuery({
    queryKey: ["connection-types"],
    queryFn: api.connections.types,
    staleTime: 60_000,
  });

  const typeFor = (key: string) => connectionTypes?.find((t) => t.type === key) ?? null;
  const selectedType = typeFor(provider);

  const applyProvider = (key: string, connection: ConnectionSummary | null) => {
    setProvider(key);
    const existingKeys = connection?.secretKeys ?? [];
    const type = typeFor(key);
    setRows(type ? rowsFromType(type, existingKeys) : freeRows(existingKeys));
  };

  const resetForm = (connection: ConnectionSummary | null) => {
    setEditing(connection);
    setName(connection?.name ?? "");
    applyProvider(connection?.provider ?? "", connection);
  };

  // Deep-link from the plugins page ("+ Add" on a connection type) preselects the type.
  const [searchParams, setSearchParams] = useSearchParams();
  useEffect(() => {
    const wanted = searchParams.get("type");
    const type = wanted ? (connectionTypes?.find((t) => t.type === wanted) ?? null) : null;
    if (type) {
      setProvider(type.type);
      setRows(rowsFromType(type, []));
      setSearchParams({}, { replace: true });
    }
  }, [connectionTypes, searchParams, setSearchParams]);

  const { data: connections, isLoading } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
  });

  const save = useMutation({
    mutationFn: () => {
      // Merge semantics: filled value = overwrite, removed = delete, untouched = keep.
      const secrets: Record<string, string | null> = {};
      for (const row of rows) {
        if (row.existing && row.removed) {
          secrets[row.name] = null;
        } else if (row.name && row.value) {
          secrets[row.name] = row.value;
        }
      }
      if (editing) {
        return api.connections.update(editing.id, { provider: provider || null, secrets });
      }
      // Create has no merge/delete: nulls can't occur here, so narrow to plain values.
      const created = Object.fromEntries(
        Object.entries(secrets).filter(([, value]) => value !== null),
      ) as Record<string, string>;
      return api.connections.create({ name, provider: provider || null, secrets: created });
    },
    onSuccess: (saved) => {
      resetForm(null);
      queryClient.invalidateQueries({ queryKey: ["connections"] });
      toast.success(`Connection "${saved.name}" saved.`);
    },
    onError: (error) => toast.error(`Save failed — ${String(error)}`),
  });

  const remove = useMutation({
    mutationFn: ({ id, force }: { id: string; force?: boolean }) => api.connections.remove(id, force),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["connections"] });
      toast.success("Connection deleted.");
    },
    onError: (error, variables) => {
      const message = String(error);
      if (message.includes("force=true")) {
        if (window.confirm(`${message}\n\nDelete anyway?`)) {
          remove.mutate({ ...variables, force: true });
        } else {
          remove.reset();
        }
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

  const updateRow = (key: number, patch: Partial<SecretRow>) =>
    setRows((current) => current.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  // Required typed fields must have a value (create) or already exist (edit).
  const missingRequired = rows.some(
    (r) => r.fixed && r.required && !r.value && !(editing && r.existing && !r.removed),
  );
  const createInvalid = !editing && (!name || rows.every((r) => !r.value));

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

      <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
        {connections?.map((connection) => (
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
              <button type="button" onClick={() => resetForm(connection)} className="text-xs text-zinc-500 hover:text-zinc-100">
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
      </ul>

      <div className="max-w-xl space-y-3 rounded-lg border border-zinc-800 p-4">
        <h2 className="text-sm font-medium text-zinc-300">
          {editing ? `Edit ${editing.name}` : "New connection"}
        </h2>

        <div className="flex gap-2">
          <label className="flex-1">
            <span className="mb-1 block text-xs font-medium text-zinc-400">Name</span>
            <input
              className={`${inputClass} w-full`}
              placeholder="my-matrix-bot"
              value={name}
              disabled={editing !== null}
              title={editing ? "Names are immutable — templates reference them" : undefined}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          <label className="flex-1">
            <span className="mb-1 block text-xs font-medium text-zinc-400">Type</span>
            <select
              className={`${inputClass} w-full`}
              value={selectedType ? provider : "__custom__"}
              onChange={(e) => applyProvider(e.target.value === "__custom__" ? "" : e.target.value, editing)}
            >
              <option value="__custom__">Custom (free-form fields)</option>
              {connectionTypes?.map((t) => (
                <option key={t.type} value={t.type}>
                  {t.displayName}
                </option>
              ))}
            </select>
          </label>
        </div>

        {selectedType?.description && (
          <p className="text-xs text-zinc-500">{selectedType.description}</p>
        )}

        {selectedType?.isOAuth && (
          <p className="text-xs text-sky-400">
            After saving, click <strong>Connect</strong> on the connection above to authorize with the provider.
          </p>
        )}

        {!selectedType && (
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-zinc-400">
              Provider <span className="font-normal text-zinc-600">(optional label)</span>
            </span>
            <input
              className={`${inputClass} w-full`}
              placeholder="e.g. github.com"
              value={provider}
              onChange={(e) => setProvider(e.target.value)}
            />
          </label>
        )}

        <div className="space-y-3 rounded-md border border-zinc-800 bg-zinc-900/40 p-3">
          {!selectedType && (
            <div className="text-xs font-medium text-zinc-400">
              Secret fields{" "}
              <span className="font-normal text-zinc-600">
                — each becomes {"{{connections." + (name || "<name>") + ".<field>}}"}
              </span>
            </div>
          )}

          {rows.map((row) =>
            row.fixed ? (
              <label key={row.key} className="block">
                <span className="mb-1 flex items-center gap-2 text-xs font-medium text-zinc-400">
                  {row.label ?? row.name}
                  {row.required && <span className="text-emerald-400">*</span>}
                  <code className="text-[10px] text-zinc-600">{row.name}</code>
                  {row.docsUrl && (
                    <a href={row.docsUrl} target="_blank" rel="noreferrer" className="text-[10px] text-emerald-400 hover:underline">
                      where to get it ↗
                    </a>
                  )}
                </span>
                <input
                  className={`${inputClass} w-full`}
                  type={row.secret ? "password" : "text"}
                  placeholder={editing && row.existing ? "unchanged" : row.helpText ?? ""}
                  value={row.value}
                  onChange={(e) => updateRow(row.key, { value: e.target.value })}
                />
                {row.helpText && !(editing && row.existing) && (
                  <span className="mt-1 block text-[11px] text-zinc-600">{row.helpText}</span>
                )}
              </label>
            ) : (
              <div key={row.key} className="flex items-center gap-2">
                <input
                  className={`${inputClass} flex-1`}
                  placeholder="field (e.g. token)"
                  value={row.name}
                  disabled={row.existing}
                  onChange={(e) => updateRow(row.key, { name: e.target.value })}
                />
                <input
                  className={`${inputClass} flex-1 ${row.removed ? "line-through opacity-40" : ""}`}
                  type="password"
                  placeholder={row.existing ? "unchanged" : "secret value"}
                  value={row.value}
                  disabled={row.removed}
                  onChange={(e) => updateRow(row.key, { value: e.target.value })}
                />
                {row.existing ? (
                  <button
                    type="button"
                    onClick={() => updateRow(row.key, { removed: !row.removed, value: "" })}
                    className={`text-xs ${row.removed ? "text-amber-400" : "text-zinc-500 hover:text-red-400"}`}
                  >
                    {row.removed ? "restore" : "remove"}
                  </button>
                ) : (
                  rows.filter((r) => !r.fixed).length > 1 && (
                    <button
                      type="button"
                      onClick={() => setRows((current) => current.filter((r) => r.key !== row.key))}
                      className="text-xs text-zinc-500 hover:text-red-400"
                    >
                      ✕
                    </button>
                  )
                )}
              </div>
            ),
          )}

          {!selectedType && (
            <button
              type="button"
              disabled={rows.some((r) => !r.existing && !r.fixed && !r.name)}
              onClick={() => setRows((current) => [...current, emptyRow()])}
              className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900 disabled:opacity-50"
            >
              Add field
            </button>
          )}
        </div>

        <div className="flex gap-2">
          <button
            type="button"
            disabled={createInvalid || missingRequired || save.isPending}
            onClick={() => save.mutate()}
            className="rounded-md bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          >
            {save.isPending ? "Saving…" : editing ? "Save changes" : "Create connection"}
          </button>
          {editing && (
            <button type="button" onClick={() => resetForm(null)} className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900">
              Cancel
            </button>
          )}
        </div>
        {save.error && <p className="text-sm text-red-400">{String(save.error)}</p>}
      </div>
    </div>
  );
}
