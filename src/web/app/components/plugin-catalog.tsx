import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { api, type CatalogEntry, type InstallRequest } from "../lib/api";
import { toast } from "./toast";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "./ui/dialog";
import { useConfirm } from "./ui/confirm";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

function trustBadge(trust: string) {
  if (trust === "Verified") return <Badge variant="default">Verified</Badge>;
  if (trust === "Blocked") return <Badge variant="destructive">Blocked</Badge>;
  return <Badge variant="outline">Untrusted</Badge>;
}

function capabilitySummary(entry: CatalogEntry): string {
  const parts: string[] = [];
  if (entry.actions) parts.push(`${entry.actions} action${entry.actions === 1 ? "" : "s"}`);
  if (entry.triggers) parts.push(`${entry.triggers} trigger${entry.triggers === 1 ? "" : "s"}`);
  if (entry.connections) parts.push(`${entry.connections} connection${entry.connections === 1 ? "" : "s"}`);
  return parts.join(" · ");
}

function egressSummary(egress: string[]): string {
  if (egress.length === 0) return "no network";
  if (egress.includes("*")) return "network: any";
  return `network: ${egress.join(", ")}`;
}

function matches(query: string, ...fields: (string | null | undefined)[]) {
  const q = query.trim().toLowerCase();
  return q === "" || fields.some((f) => f?.toLowerCase().includes(q));
}

// Install with consent: on a "needs acknowledgement" error (untrusted source and/or declared permissions),
// confirm and retry with acknowledge=true — mirroring the delete-force flow.
function useCatalogInstall() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();

  const install = useMutation({
    mutationFn: (req: InstallRequest) => api.plugins.installFromCatalog(req),
    onSuccess: (result) => {
      for (const key of ["plugins", "actions", "plugin-catalog", "trigger-types", "connection-types"]) {
        queryClient.invalidateQueries({ queryKey: [key] });
      }
      toast.success(`${result.name} ${result.version} installed (${result.trust}).`);
    },
    onError: (error, variables) => {
      const message = String(error);
      if (message.includes("acknowledge=true")) {
        confirm({
          title: "Install anyway?",
          body: message,
          confirmLabel: "Install",
          destructive: true,
        }).then((ok) => {
          if (ok) install.mutate({ ...variables, acknowledge: true });
        });
        return;
      }
      toast.error(`Install failed — ${message}`);
    },
  });

  return install;
}

function installLabel(entry: CatalogEntry): string {
  if (entry.updateAvailable) return "Update";
  if (entry.installed) return "Reinstall";
  return "Install";
}

function CatalogCard({
  entry,
  installEnabled,
  onInstall,
  onOpen,
  busy,
}: {
  entry: CatalogEntry;
  installEnabled: boolean;
  onInstall: () => void;
  onOpen: () => void;
  busy: boolean;
}) {
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-zinc-800 p-4">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="truncate text-sm font-medium text-zinc-200">{entry.displayName}</span>
            <span className="text-[10px] text-zinc-500">v{entry.version}</span>
          </div>
          <div className="text-[11px] text-zinc-600">
            {entry.source}
            {entry.author && ` · ${entry.author}`}
          </div>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1">
          {trustBadge(entry.trust)}
          {entry.updateAvailable && (
            <span className="text-[10px] text-emerald-400">update available</span>
          )}
        </div>
      </div>

      {entry.description && <p className="line-clamp-2 text-xs text-zinc-500">{entry.description}</p>}

      {entry.categories.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {entry.categories.map((c) => (
            <Badge key={c} variant="secondary" className="text-[10px]">
              {c}
            </Badge>
          ))}
        </div>
      )}

      <div className="text-[11px] text-zinc-500">
        {[capabilitySummary(entry), egressSummary(entry.egress)].filter(Boolean).join(" · ")}
      </div>

      <div className="mt-1 flex items-center gap-2">
        {installEnabled ? (
          <Button
            size="sm"
            variant={entry.updateAvailable || !entry.installed ? "default" : "outline"}
            disabled={busy}
            onClick={onInstall}
          >
            {busy ? "Installing…" : installLabel(entry)}
          </Button>
        ) : (
          entry.installed && <span className="text-xs text-emerald-400">installed</span>
        )}
        <Button size="sm" variant="ghost" onClick={onOpen}>
          Details
        </Button>
      </div>
    </div>
  );
}

function CatalogDetail({
  entry,
  installEnabled,
  onClose,
  onInstall,
  busy,
}: {
  entry: CatalogEntry;
  installEnabled: boolean;
  onClose: () => void;
  onInstall: (version: string | undefined) => void;
  busy: boolean;
}) {
  const [version, setVersion] = useState(entry.version);
  const pinned = version === entry.version ? undefined : version;
  const queryClient = useQueryClient();
  const [autoUpdateOn, setAutoUpdateOn] = useState(entry.autoUpdate);

  const autoUpdate = useMutation({
    mutationFn: (enabled: boolean) => api.plugins.setAutoUpdate(entry.name, enabled),
    onSuccess: (_, enabled) => {
      setAutoUpdateOn(enabled);
      queryClient.invalidateQueries({ queryKey: ["plugin-catalog"] });
      toast.success(`Auto-update ${enabled ? "enabled" : "disabled"} for ${entry.displayName}.`);
    },
    onError: (e) => toast.error(`Failed — ${String(e)}`),
  });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {entry.displayName}
            {trustBadge(entry.trust)}
          </DialogTitle>
          <DialogDescription>
            {entry.source}
            {entry.author && ` · ${entry.author}`} · {entry.id}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 text-sm">
          {entry.description && <p className="text-zinc-400">{entry.description}</p>}

          {entry.categories.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {entry.categories.map((c) => (
                <Badge key={c} variant="secondary" className="text-[10px]">
                  {c}
                </Badge>
              ))}
            </div>
          )}

          <dl className="grid grid-cols-[7rem_1fr] gap-x-3 gap-y-1 text-xs">
            <dt className="text-zinc-600">Capabilities</dt>
            <dd className="text-zinc-400">{capabilitySummary(entry) || "—"}</dd>
            <dt className="text-zinc-600">Network</dt>
            <dd className="text-zinc-400">{egressSummary(entry.egress)}</dd>
            <dt className="text-zinc-600">Installed</dt>
            <dd className="text-zinc-400">{entry.installedVersion ? `v${entry.installedVersion}` : "no"}</dd>
          </dl>

          {installEnabled && entry.installed && (
            <div className="flex items-center justify-between rounded-md border border-zinc-800 px-3 py-2 text-xs">
              <span className="text-zinc-400">
                Auto-update
                <span className="block text-[10px] text-zinc-600">
                  Keep on the latest compatible, same-major version from a verified source.
                </span>
              </span>
              <Button
                size="sm"
                variant={autoUpdateOn ? "default" : "outline"}
                disabled={autoUpdate.isPending}
                onClick={() => autoUpdate.mutate(!autoUpdateOn)}
              >
                {autoUpdateOn ? "On" : "Off"}
              </Button>
            </div>
          )}

          {installEnabled && entry.versions.length > 1 && (
            <label className="flex items-center gap-2 text-xs text-zinc-500">
              Version
              <select
                className={inputClass}
                value={version}
                onChange={(e) => setVersion(e.target.value)}
              >
                {entry.versions.map((v) => (
                  <option key={v} value={v}>
                    v{v}
                    {v === entry.version ? " (latest)" : ""}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>

        <DialogFooter>
          {installEnabled && (
            <Button disabled={busy} onClick={() => onInstall(pinned)}>
              {busy ? "Installing…" : pinned ? `Install v${pinned}` : installLabel(entry)}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function CatalogBrowser() {
  const { data: catalog, error } = useQuery({
    queryKey: ["plugin-catalog"],
    queryFn: api.plugins.catalog,
    retry: false,
    staleTime: 300_000,
  });

  const install = useCatalogInstall();
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("");
  const [trust, setTrust] = useState("");
  const [selected, setSelected] = useState<CatalogEntry | null>(null);

  const entries = catalog?.entries ?? [];
  const categories = useMemo(
    () => [...new Set(entries.flatMap((e) => e.categories))].sort(),
    [entries],
  );

  const filtered = entries.filter(
    (e) =>
      matches(search, e.displayName, e.name, e.description, e.author) &&
      (category === "" || e.categories.includes(category)) &&
      (trust === "" || e.trust === trust),
  );

  const installing = (name: string) => install.isPending && install.variables?.name === name;
  const installingId = (id: string) => install.isPending && install.variables?.id === id;

  return (
    <div className="mt-3 rounded-lg border border-zinc-800 p-4">
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <h3 className="mr-auto text-sm font-medium text-zinc-300">Catalog</h3>
        <input
          className={inputClass}
          placeholder="Search plugins…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        {categories.length > 0 && (
          <select className={inputClass} value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="">All categories</option>
            {categories.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        )}
        <select className={inputClass} value={trust} onChange={(e) => setTrust(e.target.value)}>
          <option value="">Any trust</option>
          <option value="Verified">Verified</option>
          <option value="Untrusted">Untrusted</option>
        </select>
      </div>

      {catalog && catalog.sources.some((s) => !s.ok) && (
        <div className="mb-3 space-y-1">
          {catalog.sources
            .filter((s) => !s.ok)
            .map((s) => (
              <p key={s.url} className="text-xs text-amber-500/80">
                Source “{s.name}” unreachable — {s.error ?? "unknown error"}
              </p>
            ))}
        </div>
      )}

      {error ? (
        <p className="text-xs text-zinc-600">Plugin catalog unreachable — {String(error)}</p>
      ) : filtered.length === 0 ? (
        <p className="text-xs text-zinc-600">No matching plugins.</p>
      ) : (
        <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
          {filtered.map((entry) => (
            <CatalogCard
              key={`${entry.source}:${entry.id}`}
              entry={entry}
              installEnabled={catalog?.installEnabled ?? false}
              busy={installing(entry.name) || installingId(entry.id)}
              onInstall={() => install.mutate({ id: entry.id })}
              onOpen={() => setSelected(entry)}
            />
          ))}
        </div>
      )}

      <SourcesPanel />

      {selected && (
        <CatalogDetail
          key={selected.id}
          entry={selected}
          installEnabled={catalog?.installEnabled ?? false}
          busy={installingId(selected.id)}
          onClose={() => setSelected(null)}
          onInstall={(version) => install.mutate({ id: selected.id, version })}
        />
      )}
    </div>
  );
}

function SourcesPanel() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [key, setKey] = useState("");

  // Instance-admin only — a 403 just hides the panel.
  const { data: sources } = useQuery({
    queryKey: ["plugin-catalog-sources"],
    queryFn: api.plugins.sources.list,
    retry: false,
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["plugin-catalog-sources"] });
    queryClient.invalidateQueries({ queryKey: ["plugin-catalog"] });
  };

  const add = useMutation({
    mutationFn: () => api.plugins.sources.add({ name, url, trustedKey: key || undefined }),
    onSuccess: () => {
      invalidate();
      setAdding(false);
      setName("");
      setUrl("");
      setKey("");
      toast.success("Catalog source added.");
    },
    onError: (e) => toast.error(`Add failed — ${String(e)}`),
  });

  const toggle = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
      api.plugins.sources.update(id, { enabled }),
    onSuccess: invalidate,
    onError: (e) => toast.error(`Update failed — ${String(e)}`),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.plugins.sources.remove(id),
    onSuccess: () => {
      invalidate();
      toast.success("Source removed.");
    },
    onError: (e) => toast.error(`Remove failed — ${String(e)}`),
  });

  if (!sources) return null;

  return (
    <div className="mt-4 border-t border-zinc-800 pt-3">
      <div className="mb-2 flex items-center justify-between">
        <h4 className="text-xs font-medium text-zinc-400">Catalog sources</h4>
        <Button size="sm" variant="ghost" onClick={() => setAdding((v) => !v)}>
          {adding ? "Cancel" : "Add source"}
        </Button>
      </div>

      {adding && (
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <input className={inputClass} placeholder="Name" value={name} onChange={(e) => setName(e.target.value)} />
          <input
            className={`${inputClass} flex-1`}
            placeholder="https://…/catalog.json"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
          />
          <input
            className={inputClass}
            placeholder="minisign public key (optional)"
            value={key}
            onChange={(e) => setKey(e.target.value)}
          />
          <Button size="sm" disabled={add.isPending || !name || !url} onClick={() => add.mutate()}>
            {add.isPending ? "Adding…" : "Add"}
          </Button>
        </div>
      )}

      <ul className="space-y-1.5">
        {sources.map((s) => (
          <li key={s.id} className="flex items-center justify-between gap-3 text-xs">
            <span className="min-w-0">
              <span className="text-zinc-300">{s.name}</span>{" "}
              {s.hasKey ? (
                <Badge variant="default" className="text-[10px]">
                  key pinned
                </Badge>
              ) : (
                <Badge variant="outline" className="text-[10px]">
                  no key
                </Badge>
              )}
              <span className="block truncate text-zinc-600">{s.url}</span>
            </span>
            <span className="flex shrink-0 items-center gap-2">
              <Button
                size="sm"
                variant="ghost"
                onClick={() => toggle.mutate({ id: s.id, enabled: !s.enabled })}
              >
                {s.enabled ? "Disable" : "Enable"}
              </Button>
              <button
                type="button"
                className="text-zinc-600 hover:text-red-400"
                onClick={async () => {
                  if (await confirm({ title: `Remove “${s.name}”?`, confirmLabel: "Remove", destructive: true })) {
                    remove.mutate(s.id);
                  }
                }}
              >
                Remove
              </button>
            </span>
          </li>
        ))}
        {sources.length === 0 && (
          <li className="text-xs text-zinc-600">No extra sources — the first-party catalog is built in.</li>
        )}
      </ul>
    </div>
  );
}
