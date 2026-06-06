import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, type ConnectionSummary } from "../lib/api";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none disabled:opacity-50";

type SecretRow = { key: number; name: string; value: string; existing: boolean; removed: boolean };

let nextRow = 1;

const emptyRow = (): SecretRow => ({ key: nextRow++, name: "", value: "", existing: false, removed: false });

export default function Connections() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<ConnectionSummary | null>(null);
  const [name, setName] = useState("");
  const [provider, setProvider] = useState("");
  const [rows, setRows] = useState<SecretRow[]>([emptyRow()]);

  const resetForm = (connection: ConnectionSummary | null) => {
    setEditing(connection);
    setName(connection?.name ?? "");
    setProvider(connection?.provider ?? "");
    setRows(
      connection
        ? [
            ...connection.secretKeys.map((k) => ({
              key: nextRow++,
              name: k,
              value: "",
              existing: true,
              removed: false,
            })),
            emptyRow(),
          ]
        : [emptyRow()],
    );
  };

  const { data: connections, isLoading } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
  });

  const save = useMutation({
    mutationFn: () => {
      if (editing) {
        // Merge semantics: filled value = overwrite, removed = delete, untouched = keep.
        const secrets: Record<string, string | null> = {};
        for (const row of rows) {
          if (row.existing && row.removed) {
            secrets[row.name] = null;
          } else if (row.name && row.value) {
            secrets[row.name] = row.value;
          }
        }
        return api.connections.update(editing.id, { provider: provider || null, secrets });
      }
      return api.connections.create({
        name,
        provider: provider || null,
        secrets: Object.fromEntries(rows.filter((r) => r.name && r.value).map((r) => [r.name, r.value])),
      });
    },
    onSuccess: () => {
      resetForm(null);
      queryClient.invalidateQueries({ queryKey: ["connections"] });
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.connections.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["connections"] }),
  });

  const updateRow = (key: number, patch: Partial<SecretRow>) =>
    setRows((current) => current.map((r) => (r.key === key ? { ...r, ...patch } : r)));

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
              {connection.provider && <span className="text-xs text-zinc-500">{connection.provider}</span>}
              <span className="text-xs text-zinc-500">
                {connection.secretKeys.map((k) => `🔒 ${k}`).join("  ")}
              </span>
              {!connection.decryptable && (
                <span className="text-xs text-red-400">undecryptable — wrong encryption key?</span>
              )}
            </div>
            <div className="flex gap-3">
              <button
                type="button"
                onClick={() => resetForm(connection)}
                className="text-xs text-zinc-500 hover:text-zinc-100"
              >
                Edit
              </button>
              <button
                type="button"
                onClick={() => remove.mutate(connection.id)}
                className="text-xs text-zinc-500 hover:text-red-400"
              >
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
              placeholder="github"
              value={name}
              disabled={editing !== null}
              title={editing ? "Names are immutable — templates reference them" : undefined}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          <label className="flex-1">
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
        </div>
        <div className="space-y-2 rounded-md border border-zinc-800 bg-zinc-900/40 p-3">
          <div className="text-xs font-medium text-zinc-400">
            Secret fields{" "}
            <span className="font-normal text-zinc-600">
              — each becomes {"{{connections." + (name || "<name>") + ".<field>}}"}
            </span>
          </div>
        {rows.map((row) => (
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
              rows.length > 1 && (
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
        ))}
          <button
            type="button"
            disabled={rows.some((r) => !r.existing && !r.name)}
            onClick={() => setRows((current) => [...current, emptyRow()])}
            className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900 disabled:opacity-50"
          >
            Add field
          </button>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            disabled={(!editing && (!name || rows.every((r) => !r.name || !r.value))) || save.isPending}
            onClick={() => save.mutate()}
            className="rounded-md bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          >
            {save.isPending ? "Saving…" : editing ? "Save changes" : "Create connection"}
          </button>
          {editing && (
            <button
              type="button"
              onClick={() => resetForm(null)}
              className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
            >
              Cancel
            </button>
          )}
        </div>
        {save.error && <p className="text-sm text-red-400">{String(save.error)}</p>}
      </div>
    </div>
  );
}
