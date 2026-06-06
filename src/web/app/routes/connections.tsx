import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "../lib/api";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

type SecretRow = { key: number; name: string; value: string };

let nextRow = 1;

export default function Connections() {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [provider, setProvider] = useState("");
  const [rows, setRows] = useState<SecretRow[]>([{ key: 0, name: "", value: "" }]);

  const { data: connections, isLoading } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
  });

  const create = useMutation({
    mutationFn: () =>
      api.connections.create({
        name,
        provider: provider || null,
        secrets: Object.fromEntries(rows.filter((r) => r.name).map((r) => [r.name, r.value])),
      }),
    onSuccess: () => {
      setName("");
      setProvider("");
      setRows([{ key: nextRow++, name: "", value: "" }]);
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
          never shown again after creation.
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
            <button
              type="button"
              onClick={() => remove.mutate(connection.id)}
              className="text-xs text-zinc-500 hover:text-red-400"
            >
              Delete
            </button>
          </li>
        ))}
        {connections?.length === 0 && (
          <li className="px-4 py-6 text-center text-sm text-zinc-500">No connections yet.</li>
        )}
      </ul>

      <div className="max-w-xl space-y-3 rounded-lg border border-zinc-800 p-4">
        <h2 className="text-sm font-medium text-zinc-300">New connection</h2>
        <div className="flex gap-2">
          <input
            className={`${inputClass} flex-1`}
            placeholder="name (e.g. github)"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <input
            className={`${inputClass} flex-1`}
            placeholder="provider (optional)"
            value={provider}
            onChange={(e) => setProvider(e.target.value)}
          />
        </div>
        {rows.map((row) => (
          <div key={row.key} className="flex gap-2">
            <input
              className={`${inputClass} flex-1`}
              placeholder="field (e.g. token)"
              value={row.name}
              onChange={(e) => updateRow(row.key, { name: e.target.value })}
            />
            <input
              className={`${inputClass} flex-1`}
              type="password"
              placeholder="secret value"
              value={row.value}
              onChange={(e) => updateRow(row.key, { value: e.target.value })}
            />
          </div>
        ))}
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setRows((current) => [...current, { key: nextRow++, name: "", value: "" }])}
            className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
          >
            Add field
          </button>
          <button
            type="button"
            disabled={!name || rows.every((r) => !r.name) || create.isPending}
            onClick={() => create.mutate()}
            className="rounded-md bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          >
            {create.isPending ? "Creating…" : "Create connection"}
          </button>
        </div>
        {create.error && <p className="text-sm text-red-400">{String(create.error)}</p>}
      </div>
    </div>
  );
}
