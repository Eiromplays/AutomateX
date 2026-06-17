import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router";
import { groupBySource, SourceChip, sourceLabel } from "../components/action-source";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { type ActionDescriptor, api, type PluginInfo } from "../lib/api";

function configFields(raw: string | null): { name: string; required: boolean }[] {
  if (!raw) return [];
  try {
    const schema = JSON.parse(raw) as {
      properties?: Record<string, unknown>;
      required?: string[];
    };
    const required = new Set(schema.required ?? []);
    return Object.keys(schema.properties ?? {}).map((name) => ({
      name,
      required: required.has(name),
    }));
  } catch {
    return [];
  }
}

// Same-day builds show just the time; older ones carry their date so the
// timestamp never lies about which day "7:10 PM" was.
function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return date.toDateString() === new Date().toDateString()
    ? date.toLocaleTimeString()
    : date.toLocaleString();
}

const fieldChip = "rounded border border-zinc-800 bg-zinc-900 px-1.5 py-0.5 text-[11px] text-zinc-400";
const cardGrid = "grid grid-cols-1 gap-2 md:grid-cols-2";
const searchClass =
  "mb-4 w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

function matches(query: string, ...fields: (string | null | undefined)[]) {
  const q = query.trim().toLowerCase();
  return q === "" || fields.some((f) => f?.toLowerCase().includes(q));
}

function FieldChips({ fields }: { fields: { name: string; required: boolean }[] }) {
  if (fields.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1.5">
      {fields.map((field) => (
        <span key={field.name} className={fieldChip}>
          {field.name}
          {field.required && <span className="text-emerald-400"> *</span>}
        </span>
      ))}
    </div>
  );
}

function PluginManager() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();
  const { data: plugins } = useQuery({
    queryKey: ["plugins"],
    queryFn: api.plugins.list,
  });
  const { data: actions } = useQuery({
    queryKey: ["actions"],
    queryFn: api.actions.list,
  });
  const { data: triggerTypes } = useQuery({
    queryKey: ["trigger-types"],
    queryFn: api.triggers.types,
    staleTime: 60_000,
  });
  const { data: connectionTypes } = useQuery({
    queryKey: ["connection-types"],
    queryFn: api.connections.types,
    staleTime: 60_000,
  });

  // What a plugin contributes, by matching its source tag (plugin:<name> / workspace:<name>).
  const capabilities = (scope: "global" | "workspace", name: string) => {
    const src = `${scope === "global" ? "plugin" : "workspace"}:${name}`;
    const parts: string[] = [];
    const a = actions?.filter((x) => x.source === src).length ?? 0;
    const t = triggerTypes?.filter((x) => x.source === src).length ?? 0;
    const c = connectionTypes?.filter((x) => x.source === src).length ?? 0;
    if (a) parts.push(`${a} action${a === 1 ? "" : "s"}`);
    if (t) parts.push(`${t} trigger${t === 1 ? "" : "s"}`);
    if (c) parts.push(`${c} connection${c === 1 ? "" : "s"}`);
    return parts.join(" · ");
  };

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["plugins"] });
    queryClient.invalidateQueries({ queryKey: ["actions"] });
    queryClient.invalidateQueries({ queryKey: ["trigger-types"] });
    queryClient.invalidateQueries({ queryKey: ["connection-types"] });
  };

  const upload = useMutation({
    mutationFn: ({ scope, file }: { scope: "global" | "workspace"; file: File }) =>
      api.plugins.upload(scope, file),
    onSuccess: (result) => {
      invalidate();
      if (result.previousFingerprint && result.previousFingerprint === result.fingerprint) {
        toast.success(`${result.name}: unchanged — the zip contains the same build (${result.fingerprint}).`);
      } else if (result.previousFingerprint) {
        toast.success(`${result.name} updated: ${result.previousFingerprint} → ${result.fingerprint}`);
      } else {
        toast.success(`${result.name} installed (${result.fingerprint ?? "no actions found"}).`);
      }
    },
    onError: (error) => toast.error(`Upload failed — ${String(error)}`),
  });

  const remove = useMutation({
    mutationFn: ({ scope, name, force }: { scope: "global" | "workspace"; name: string; force?: boolean }) =>
      api.plugins.remove(scope, name, force),
    onSuccess: (_, { name }) => {
      invalidate();
      toast.success(`${name} deleted.`);
    },
    onError: (error, variables) => {
      const message = String(error);
      if (message.includes("force=true")) {
        confirm({
          title: "Delete anyway?",
          body: message,
          confirmLabel: "Delete",
          destructive: true,
        }).then((ok) => {
          if (ok) remove.mutate({ ...variables, force: true });
          else remove.reset();
        });
        return;
      }
      toast.error(`Delete failed — ${message}`);
    },
  });

  if (!plugins) return null;

  const section = (scope: "global" | "workspace", items: PluginInfo[], hint: string) => (
    <div className="flex-1 rounded-lg border border-zinc-800 p-4">
      <div className="mb-2 flex items-center justify-between">
        <h3 className="text-sm font-medium text-zinc-300">
          {scope === "global" ? "Global plugins" : "Workspace plugins"}
        </h3>
        {plugins.uploadEnabled && (
          <label className="cursor-pointer rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900">
            Upload zip
            <input
              type="file"
              accept=".zip"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0];
                e.target.value = "";
                if (file) upload.mutate({ scope, file });
              }}
            />
          </label>
        )}
      </div>
      <p className="mb-3 text-xs text-zinc-500">{hint}</p>
      <ul className="space-y-2">
        {items.map((plugin) => {
          const summary = capabilities(scope, plugin.name);
          return (
            <li key={plugin.name} className="flex items-start justify-between gap-3 text-sm">
              <div className="min-w-0">
                <div className="text-zinc-300">
                  {plugin.name} <span className="text-[10px] text-zinc-500">v{plugin.version}</span>
                </div>
                <div
                  className="text-[10px] text-zinc-600"
                  title="Build fingerprint (changes every compilation) · DLL write time"
                >
                  {plugin.fingerprint}
                  {plugin.modifiedAt && ` · ${formatTimestamp(plugin.modifiedAt)}`}
                </div>
                {summary && <div className="text-[11px] text-zinc-500">{summary}</div>}
              </div>
              {plugins.uploadEnabled && (
                <button
                  type="button"
                  onClick={async () => {
                    if (
                      await confirm({
                        title: `Delete plugin "${plugin.name}"?`,
                        body: "Workflows using its actions will fail.",
                        confirmLabel: "Delete",
                        destructive: true,
                      })
                    ) {
                      remove.mutate({ scope, name: plugin.name });
                    }
                  }}
                  className="shrink-0 text-xs text-zinc-600 hover:text-red-400"
                >
                  Delete
                </button>
              )}
            </li>
          );
        })}
        {items.length === 0 && <li className="text-xs text-zinc-600">None installed.</li>}
      </ul>
    </div>
  );

  return (
    <section>
      <div className="grid gap-3 lg:grid-cols-2">
        {section("global", plugins.global, "Available in every workspace.")}
        {section("workspace", plugins.workspace, "Only this workspace — shadows global on name collisions.")}
      </div>
      {!plugins.uploadEnabled && (
        <p className="mt-2 text-xs text-zinc-600">
          Uploads disabled — set <code>Engine__AllowPluginUpload=true</code> to manage plugins from here, or
          drop folders into <code>plugins/</code> and reload.
        </p>
      )}
      <CatalogPanel />
    </section>
  );
}

function CatalogPanel() {
  const queryClient = useQueryClient();
  const { data: catalog, error } = useQuery({
    queryKey: ["plugin-catalog"],
    queryFn: api.plugins.catalog,
    retry: false,
    staleTime: 300_000,
  });

  const install = useMutation({
    mutationFn: (name: string) => api.plugins.installFromCatalog(name),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["plugins"] });
      queryClient.invalidateQueries({ queryKey: ["actions"] });
      queryClient.invalidateQueries({ queryKey: ["plugin-catalog"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-types"] });
      queryClient.invalidateQueries({ queryKey: ["connection-types"] });
      toast.success(
        result.previousFingerprint && result.previousFingerprint !== result.fingerprint
          ? `${result.name} ${result.version} installed: ${result.previousFingerprint} → ${result.fingerprint}`
          : `${result.name} ${result.version} installed (${result.fingerprint}).`,
      );
    },
    onError: (installError) => toast.error(`Install failed — ${String(installError)}`),
  });

  if (error) {
    return <p className="mt-3 text-xs text-zinc-600">Plugin catalog unreachable — {String(error)}</p>;
  }

  if (!catalog || catalog.entries.length === 0) return null;

  return (
    <div className="mt-3 rounded-lg border border-zinc-800 p-4">
      <h3 className="mb-2 text-sm font-medium text-zinc-300">Catalog</h3>
      <div className={cardGrid}>
        {catalog.entries.map((entry) => (
          <div
            key={entry.name}
            className="flex items-center justify-between gap-3 rounded-md border border-zinc-800 px-3 py-2 text-sm"
          >
            <span>
              <span className="text-zinc-200">{entry.name}</span>{" "}
              <span className="text-[10px] text-zinc-600">v{entry.version}</span>
              {entry.description && <span className="block text-xs text-zinc-500">{entry.description}</span>}
            </span>
            {catalog.installEnabled ? (
              <button
                type="button"
                onClick={() => install.mutate(entry.name)}
                disabled={install.isPending}
                className={
                  entry.installed
                    ? "rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900 disabled:opacity-50"
                    : "rounded-md bg-emerald-600 px-2.5 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
                }
              >
                {install.isPending ? "Installing…" : entry.installed ? "Update" : "Install"}
              </button>
            ) : (
              entry.installed && <span className="text-xs text-emerald-400">installed</span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function ActionsPanel({ actions }: { actions: ActionDescriptor[] }) {
  const [search, setSearch] = useState("");
  const filtered = actions.filter((a) => matches(search, a.type, a.displayName, a.description));

  return (
    <div>
      <input
        className={searchClass}
        placeholder="Search actions…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <div className="space-y-6">
        {groupBySource(filtered).map(([source, items]) => (
          <section key={source}>
            <h2 className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-300">
              {sourceLabel(source)}
              <SourceChip source={source} />
            </h2>
            <div className={cardGrid}>
              {items.map((action) => (
                <div key={action.type} className="rounded-lg border border-zinc-800 p-4">
                  <div className="mb-1 flex items-center gap-2">
                    <span className="text-sm font-medium">{action.displayName}</span>
                    <code className="rounded bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-400">
                      {action.type}
                    </code>
                  </div>
                  {action.description && <p className="mb-2 text-xs text-zinc-500">{action.description}</p>}
                  <FieldChips fields={configFields(action.configSchema)} />
                </div>
              ))}
            </div>
          </section>
        ))}
        {filtered.length === 0 && <p className="text-sm text-zinc-500">No matching actions.</p>}
      </div>
    </div>
  );
}

function TriggerTypesPanel() {
  const [search, setSearch] = useState("");
  const { data: types } = useQuery({
    queryKey: ["trigger-types"],
    queryFn: api.triggers.types,
    staleTime: 60_000,
  });
  if (!types) return null;

  const filtered = types.filter((t) => matches(search, t.type, t.displayName, t.description));

  return (
    <div>
      <input
        className={searchClass}
        placeholder="Search trigger types…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <div className="space-y-6">
        {groupBySource(filtered).map(([source, items]) => (
          <section key={source}>
            <h2 className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-300">
              {sourceLabel(source)}
              <SourceChip source={source} />
            </h2>
            <div className={cardGrid}>
              {items.map((trigger) => (
                <div key={trigger.type} className="rounded-lg border border-zinc-800 p-4">
                  <div className="mb-1 flex items-center gap-2">
                    <span className="text-sm font-medium">{trigger.displayName}</span>
                    <code className="rounded bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-400">
                      {trigger.type}
                    </code>
                  </div>
                  {trigger.description && <p className="mb-2 text-xs text-zinc-500">{trigger.description}</p>}
                  <FieldChips fields={configFields(trigger.configSchema)} />
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>
      {filtered.length === 0 && <p className="text-sm text-zinc-500">No matching trigger types.</p>}
    </div>
  );
}

function ConnectionTypesPanel() {
  const [search, setSearch] = useState("");
  const { data: types } = useQuery({
    queryKey: ["connection-types"],
    queryFn: api.connections.types,
    staleTime: 60_000,
  });
  if (!types) return null;

  const filtered = types.filter((t) => matches(search, t.type, t.displayName, t.description));

  return (
    <div>
      <input
        className={searchClass}
        placeholder="Search connection types…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      {types.length === 0 ? (
        <p className="text-xs text-zinc-600">
          No connection types — plugins declare these to guide their setup.
        </p>
      ) : (
        <div className="space-y-6">
          {groupBySource(filtered).map(([source, items]) => (
            <section key={source}>
              <h2 className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-300">
                {sourceLabel(source)}
                <SourceChip source={source} />
              </h2>
              <div className={cardGrid}>
                {items.map((type) => (
                  <div key={type.type} className="rounded-lg border border-zinc-800 p-4">
                    <div className="mb-1 flex items-center gap-2">
                      <span className="text-sm font-medium">{type.displayName}</span>
                      <code className="rounded bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-400">
                        {type.type}
                      </code>
                      <Link
                        to={`/connections?type=${type.type}`}
                        className="ml-auto text-xs text-emerald-400 hover:underline"
                      >
                        + Add
                      </Link>
                    </div>
                    {type.description && <p className="mb-2 text-xs text-zinc-500">{type.description}</p>}
                    <div className="flex flex-wrap gap-1.5">
                      {type.fields.map((field) => (
                        <span key={field.key} className={fieldChip} title={field.helpText ?? undefined}>
                          {field.secret ? "🔒 " : ""}
                          {field.label}
                          {field.required && <span className="text-emerald-400"> *</span>}
                        </span>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
      {types.length > 0 && filtered.length === 0 && (
        <p className="text-sm text-zinc-500">No matching connection types.</p>
      )}
    </div>
  );
}

type Tab = "installed" | "actions" | "triggers" | "connections";

export default function Plugins() {
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<Tab>("installed");
  const { data: actions } = useQuery({
    queryKey: ["actions"],
    queryFn: api.actions.list,
  });
  const { data: triggerTypes } = useQuery({
    queryKey: ["trigger-types"],
    queryFn: api.triggers.types,
    staleTime: 60_000,
  });
  const { data: connectionTypes } = useQuery({
    queryKey: ["connection-types"],
    queryFn: api.connections.types,
    staleTime: 60_000,
  });

  const reload = useMutation({
    mutationFn: api.plugins.reload,
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["actions"] });
      queryClient.invalidateQueries({ queryKey: ["plugins"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-types"] });
      queryClient.invalidateQueries({ queryKey: ["connection-types"] });
      toast.success(
        `Plugins reloaded: ${result.globalPlugins} global, ${result.workspacePlugins} workspace-scoped.`,
      );
    },
    onError: (error) => toast.error(`Reload failed — ${String(error)}`),
  });

  const count = (n: number | undefined) => (n === undefined ? "" : ` (${n})`);
  const tabs: { id: Tab; label: string }[] = [
    { id: "installed", label: "Installed" },
    { id: "actions", label: `Actions${count(actions?.length)}` },
    { id: "triggers", label: `Triggers${count(triggerTypes?.length)}` },
    {
      id: "connections",
      label: `Connections${count(connectionTypes?.length)}`,
    },
  ];

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold">Plugins</h1>
        <button
          type="button"
          onClick={() => reload.mutate()}
          disabled={reload.isPending}
          className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900 disabled:opacity-50"
        >
          {reload.isPending ? "Reloading…" : "Reload plugins"}
        </button>
      </div>

      <div className="mb-6 flex gap-1 border-b border-zinc-800 text-sm">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            onClick={() => setTab(t.id)}
            className={
              tab === t.id
                ? "-mb-px border-b-2 border-emerald-500 px-3 py-2 text-zinc-100"
                : "-mb-px border-b-2 border-transparent px-3 py-2 text-zinc-500 hover:text-zinc-200"
            }
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "installed" && <PluginManager />}
      {tab === "actions" &&
        (actions ? <ActionsPanel actions={actions} /> : <p className="text-sm text-zinc-500">Loading…</p>)}
      {tab === "triggers" && <TriggerTypesPanel />}
      {tab === "connections" && <ConnectionTypesPanel />}
    </div>
  );
}
