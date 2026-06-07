import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type PluginInfo } from "../lib/api";
import { groupBySource, SourceChip, sourceLabel } from "../components/action-source";
import { toast } from "../components/toast";

function configFields(raw: string | null): { name: string; required: boolean }[] {
  if (!raw) return [];
  try {
    const schema = JSON.parse(raw) as { properties?: Record<string, unknown>; required?: string[] };
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

function PluginManager() {
  const queryClient = useQueryClient();
  const { data: plugins } = useQuery({ queryKey: ["plugins"], queryFn: api.plugins.list });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["plugins"] });
    queryClient.invalidateQueries({ queryKey: ["actions"] });
  };

  const upload = useMutation({
    mutationFn: ({ scope, file }: { scope: "global" | "workspace"; file: File }) =>
      api.plugins.upload(scope, file),
    onSuccess: (result) => {
      invalidate();
      if (result.previousFingerprint && result.previousFingerprint === result.fingerprint) {
        toast.success(
          `${result.name}: unchanged — the zip contains the same build (${result.fingerprint}).`,
        );
      } else if (result.previousFingerprint) {
        toast.success(
          `${result.name} updated: ${result.previousFingerprint} → ${result.fingerprint}`,
        );
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
      // The in-use guard: surface the blocking workflows and offer the override.
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
      <ul className="space-y-1">
        {items.map((plugin) => (
          <li key={plugin.name} className="flex items-center justify-between text-sm">
            <span className="text-zinc-300">
              {plugin.name}{" "}
              <span
                className="text-[10px] text-zinc-600"
                title="Build fingerprint (changes every compilation) · DLL write time"
              >
                {plugin.fingerprint}
                {plugin.modifiedAt && ` · ${formatTimestamp(plugin.modifiedAt)}`}
              </span>
            </span>
            {plugins.uploadEnabled && (
              <button
                type="button"
                onClick={() => {
                  if (
                    window.confirm(
                      `Delete plugin "${plugin.name}"? Workflows using its actions will fail.`,
                    )
                  ) {
                    remove.mutate({ scope, name: plugin.name });
                  }
                }}
                className="text-xs text-zinc-600 hover:text-red-400"
              >
                Delete
              </button>
            )}
          </li>
        ))}
        {items.length === 0 && <li className="text-xs text-zinc-600">None installed.</li>}
      </ul>
    </div>
  );

  return (
    <section className="mb-8">
      <div className="flex flex-col gap-3 sm:flex-row">
        {section("global", plugins.global, "Available in every workspace.")}
        {section(
          "workspace",
          plugins.workspace,
          "Only this workspace — shadows global actions on name collisions.",
        )}
      </div>
      {!plugins.uploadEnabled && (
        <p className="mt-2 text-xs text-zinc-600">
          Uploads disabled — set <code>Engine__AllowPluginUpload=true</code> to manage plugins from
          here, or drop folders into <code>plugins/</code> and reload.
        </p>
      )}
      <CatalogPanel />
    </section>
  );
}

// First-party plugins, one click away: entries come from the release-asset
// catalog and are sha256-verified server-side before they touch disk.
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
      toast.success(
        result.previousFingerprint && result.previousFingerprint !== result.fingerprint
          ? `${result.name} ${result.version} installed: ${result.previousFingerprint} → ${result.fingerprint}`
          : `${result.name} ${result.version} installed (${result.fingerprint}).`,
      );
    },
    onError: (installError) => toast.error(`Install failed — ${String(installError)}`),
  });

  if (error) {
    return (
      <p className="mt-3 text-xs text-zinc-600">Plugin catalog unreachable — {String(error)}</p>
    );
  }

  if (!catalog || catalog.entries.length === 0) return null;

  return (
    <div className="mt-3 rounded-lg border border-zinc-800 p-4">
      <h3 className="mb-2 text-sm font-medium text-zinc-300">Catalog</h3>
      <ul className="space-y-2">
        {catalog.entries.map((entry) => (
          <li key={entry.name} className="flex items-center justify-between gap-3 text-sm">
            <span>
              <span className="text-zinc-200">{entry.name}</span>{" "}
              <span className="text-[10px] text-zinc-600">v{entry.version}</span>
              {entry.description && (
                <span className="block text-xs text-zinc-500">{entry.description}</span>
              )}
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
          </li>
        ))}
      </ul>
    </div>
  );
}

export default function Actions() {
  const queryClient = useQueryClient();
  const { data: actions } = useQuery({ queryKey: ["actions"], queryFn: api.actions.list });

  const reload = useMutation({
    mutationFn: api.plugins.reload,
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["actions"] });
      queryClient.invalidateQueries({ queryKey: ["plugins"] });
      toast.success(
        `Plugins reloaded: ${result.globalPlugins} global, ${result.workspacePlugins} workspace-scoped.`,
      );
    },
    onError: (error) => toast.error(`Reload failed — ${String(error)}`),
  });

  if (!actions) return <p className="text-sm text-zinc-500">Loading…</p>;

  return (
    <div className="max-w-3xl">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-lg font-semibold">Plugins</h1>
        <div className="flex items-center gap-3">
          <span className="text-xs text-zinc-500">{actions.length} actions</span>
          <button
            type="button"
            onClick={() => reload.mutate()}
            disabled={reload.isPending}
            className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900 disabled:opacity-50"
          >
            {reload.isPending ? "Reloading…" : "Reload plugins"}
          </button>
        </div>
      </div>
      {reload.error && <p className="mb-4 text-sm text-red-400">{String(reload.error)}</p>}

      <PluginManager />

      <div className="space-y-8">
        {groupBySource(actions).map(([source, items]) => (
          <section key={source}>
            <h2 className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-300">
              {sourceLabel(source)}
              <SourceChip source={source} />
            </h2>

            <div className="space-y-2">
              {items.map((action) => (
                <div key={action.type} className="rounded-lg border border-zinc-800 p-4">
                  <div className="mb-1 flex items-center gap-2">
                    <span className="text-sm font-medium">{action.displayName}</span>
                    <code className="rounded bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-400">
                      {action.type}
                    </code>
                  </div>
                  {action.description && (
                    <p className="mb-2 text-xs text-zinc-500">{action.description}</p>
                  )}
                  {configFields(action.configSchema).length > 0 && (
                    <div className="flex flex-wrap gap-1.5">
                      {configFields(action.configSchema).map((field) => (
                        <span
                          key={field.name}
                          className="rounded border border-zinc-800 bg-zinc-900 px-1.5 py-0.5 text-[11px] text-zinc-400"
                        >
                          {field.name}
                          {field.required && <span className="text-emerald-400"> *</span>}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>

      <TriggerTypesPanel />
    </div>
  );
}

function TriggerTypesPanel() {
  const { data: types } = useQuery({
    queryKey: ["trigger-types"],
    queryFn: api.triggers.types,
    staleTime: 60_000,
  });

  if (!types) return null;

  return (
    <section className="mt-8">
      <h2 className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-300">
        Trigger types
        <span className="text-xs font-normal text-zinc-500">— how workflows start</span>
      </h2>
      <div className="space-y-2">
        {types.map((trigger) => (
          <div key={trigger.type} className="rounded-lg border border-zinc-800 p-4">
            <div className="mb-1 flex items-center gap-2">
              <span className="text-sm font-medium">{trigger.displayName}</span>
              <code className="rounded bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-400">
                {trigger.type}
              </code>
              <SourceChip source={trigger.source} />
            </div>
            {trigger.description && (
              <p className="mb-2 text-xs text-zinc-500">{trigger.description}</p>
            )}
            {configFields(trigger.configSchema).length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {configFields(trigger.configSchema).map((field) => (
                  <span
                    key={field.name}
                    className="rounded border border-zinc-800 bg-zinc-900 px-1.5 py-0.5 text-[11px] text-zinc-400"
                  >
                    {field.name}
                    {field.required && <span className="text-emerald-400"> *</span>}
                  </span>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
