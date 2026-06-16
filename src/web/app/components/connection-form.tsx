import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { api, type ConnectionSummary } from "../lib/api";
import { AutoTextarea } from "./auto-textarea";
import {
  buildSecretsPayload,
  emptyRow,
  firstReferenceableKey,
  freeRows,
  hasMissingRequired,
  isCreateInvalid,
  rowsFromType,
  type SecretRow,
} from "./connection-form-logic";
import { toast } from "./toast";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none disabled:opacity-50";

type ConnectionFormProps = {
  // null = create mode. Set to a connection to edit it (name immutable, secrets merge).
  editing?: ConnectionSummary | null;
  // Preselect a connection type in create mode (deep-link from /plugins, or the builder modal).
  initialType?: string;
  // Called after a successful save. firstKey is the first referenceable secret key (for the
  // builder to drop a {{connections.<name>.<firstKey>}} token); null when none applies.
  onSaved?: (saved: { id: string; name: string }, firstKey: string | null) => void;
  onCancel?: () => void;
};

// The connection create/edit form, shared by the Connections page and the in-builder "New
// connection" modal. Owns persistence (create/update + cache invalidation + toast); callers
// decide what happens next via onSaved.
export function ConnectionForm({ editing = null, initialType, onSaved, onCancel }: ConnectionFormProps) {
  const queryClient = useQueryClient();
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

  // (Re)initialize when the edited connection or preselected type changes, and again once the
  // type catalog finishes loading (so a typed edit doesn't fall back to free-form rows).
  useEffect(() => {
    if (editing) {
      setName(editing.name);
      applyProvider(editing.provider ?? "", editing);
    } else {
      setName("");
      applyProvider(initialType ?? "", null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing, initialType, connectionTypes]);

  const updateRow = (key: number, patch: Partial<SecretRow>) =>
    setRows((current) => current.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  const save = useMutation({
    mutationFn: () => {
      const secrets = buildSecretsPayload(rows);
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
      queryClient.invalidateQueries({ queryKey: ["connections"] });
      toast.success(`Connection "${saved.name}" saved.`);
      onSaved?.(saved, firstReferenceableKey(rows));
    },
    onError: (error) => toast.error(`Save failed — ${String(error)}`),
  });

  const saveDisabled =
    (!editing && isCreateInvalid(name, rows)) || hasMissingRequired(rows, editing !== null) || save.isPending;

  return (
    <div className="space-y-3">
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

      {selectedType?.description && <p className="text-xs text-zinc-500">{selectedType.description}</p>}

      {selectedType?.isOAuth && (
        <p className="text-xs text-sky-400">
          After saving, click <strong>Connect</strong> on the connection in the list to authorize with the provider.
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
              {row.secret ? (
                // Textarea, not a single-line input: a one-line field strips newlines on paste,
                // which corrupts multi-line secrets like SSH private keys (SSH.NET then rejects them).
                <AutoTextarea
                  className={`${inputClass} min-h-[3rem] w-full font-mono`}
                  placeholder={editing && row.existing ? "unchanged" : row.helpText ?? ""}
                  value={row.value}
                  onChange={(e) => updateRow(row.key, { value: e.target.value })}
                />
              ) : (
                <input
                  className={`${inputClass} w-full`}
                  type="text"
                  placeholder={editing && row.existing ? "unchanged" : row.helpText ?? ""}
                  value={row.value}
                  onChange={(e) => updateRow(row.key, { value: e.target.value })}
                />
              )}
              {row.helpText && !(editing && row.existing) && (
                <span className="mt-1 block text-[11px] text-zinc-600">{row.helpText}</span>
              )}
            </label>
          ) : (
            <div key={row.key} className="flex items-start gap-2">
              <input
                className={`${inputClass} flex-1`}
                placeholder="field (e.g. token)"
                value={row.name}
                disabled={row.existing}
                onChange={(e) => updateRow(row.key, { name: e.target.value })}
              />
              <AutoTextarea
                className={`${inputClass} min-h-[2.25rem] flex-1 font-mono ${row.removed ? "line-through opacity-40" : ""}`}
                placeholder={row.existing ? "unchanged" : "secret value (or paste a key)"}
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

        {/* Typed connections can carry extra custom keys too (the store is a free-form KV);
            the type's fields are just suggestions. This makes that doable in the UI, not only the API. */}
        <button
          type="button"
          disabled={rows.some((r) => !r.existing && !r.fixed && !r.name)}
          onClick={() => setRows((current) => [...current, emptyRow()])}
          className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900 disabled:opacity-50"
        >
          {selectedType ? "Add custom field" : "Add field"}
        </button>
      </div>

      <div className="flex gap-2">
        <button
          type="button"
          disabled={saveDisabled}
          onClick={() => save.mutate()}
          className="rounded-md bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        >
          {save.isPending ? "Saving…" : editing ? "Save changes" : "Create connection"}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel} className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900">
            Cancel
          </button>
        )}
      </div>
      {save.error && <p className="text-sm text-red-400">{String(save.error)}</p>}
    </div>
  );
}
