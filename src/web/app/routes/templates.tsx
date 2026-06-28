import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { type ReactNode, useState } from "react";
import { useNavigate } from "react-router";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { api } from "../lib/api";
import { templates } from "../lib/templates";

type Filterable = { name: string; description: string | null; category: string | null };

export default function Templates() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const confirm = useConfirm();

  const { data: workspaceTemplates } = useQuery({
    queryKey: ["workflow-templates"],
    queryFn: api.workflowTemplates.list,
  });

  const { data: community } = useQuery({
    queryKey: ["template-catalog"],
    queryFn: api.workflowTemplates.catalog,
    staleTime: 300_000,
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.workflowTemplates.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["workflow-templates"] }),
    onError: (e) => toast.error(String(e)),
  });

  const saveFromCommunity = useMutation({
    mutationFn: (template: Filterable & { doc: unknown }) =>
      api.workflowTemplates.save({
        name: template.name,
        description: template.description,
        category: template.category,
        doc: template.doc,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-templates"] });
      toast.success("Saved to your templates.");
    },
    onError: (e) => toast.error(String(e)),
  });

  const use = (doc: unknown) => navigate("/workflows/new", { state: { importDoc: doc } });

  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("all");

  const categories = [
    ...new Set(
      [...(workspaceTemplates ?? []), ...(community ?? []), ...templates]
        .map((t) => t.category)
        .filter((c): c is string => !!c),
    ),
  ].sort();

  const match = (t: Filterable) => {
    const q = search.trim().toLowerCase();
    const okText = !q || t.name.toLowerCase().includes(q) || (t.description ?? "").toLowerCase().includes(q);
    return okText && (category === "all" || t.category === category);
  };

  const yours = (workspaceTemplates ?? []).filter(match);
  const fromCommunity = (community ?? []).filter(match);
  const builtIn = templates.filter(match);

  const inputClass =
    "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

  return (
    <div className="max-w-4xl space-y-8">
      <div>
        <h1 className="text-lg font-semibold">Templates</h1>
        <p className="text-sm text-zinc-500">
          Start from a ready-made workflow — you&apos;ll review and tweak it (URLs, connections, variables)
          before creating.
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <input
          className={`${inputClass} max-w-xs flex-1`}
          placeholder="Search templates…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <select className={inputClass} value={category} onChange={(e) => setCategory(e.target.value)}>
          <option value="all">All categories</option>
          {categories.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
      </div>

      {yours.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-medium text-zinc-300">Your templates</h2>
          <div className="grid gap-3 md:grid-cols-2">
            {yours.map((template) => (
              <Card key={template.id} template={template} onUse={() => use(template.doc)}>
                <button
                  type="button"
                  onClick={() =>
                    confirm({
                      title: `Delete template “${template.name}”?`,
                      confirmLabel: "Delete",
                      destructive: true,
                    }).then((ok) => ok && remove.mutate(template.id))
                  }
                  className="text-xs text-zinc-500 hover:text-red-400"
                >
                  Delete
                </button>
              </Card>
            ))}
          </div>
        </section>
      )}

      {fromCommunity.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-medium text-zinc-300">Community</h2>
          <div className="grid gap-3 md:grid-cols-2">
            {fromCommunity.map((template) => (
              <Card key={template.name} template={template} onUse={() => use(template.doc)}>
                <button
                  type="button"
                  onClick={() => saveFromCommunity.mutate(template)}
                  className="text-xs text-zinc-500 hover:text-emerald-400"
                >
                  Save to my templates
                </button>
              </Card>
            ))}
          </div>
        </section>
      )}

      {builtIn.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-medium text-zinc-300">Built-in</h2>
          <div className="grid gap-3 md:grid-cols-2">
            {builtIn.map((template) => (
              <Card key={template.id} template={template} onUse={() => use(template.doc)} />
            ))}
          </div>
        </section>
      )}
    </div>
  );
}

function Card({
  template,
  onUse,
  children,
}: {
  template: Filterable;
  onUse: () => void;
  children?: ReactNode;
}) {
  return (
    <div className="flex flex-col rounded-lg border border-zinc-800 bg-zinc-900/40 p-4">
      {template.category && (
        <div className="text-xs uppercase tracking-wide text-zinc-600">{template.category}</div>
      )}
      <div className="mt-0.5 text-sm font-medium text-zinc-100">{template.name}</div>
      <p className="mt-1 flex-1 text-sm text-zinc-400">{template.description}</p>
      <div className="mt-3 flex items-center gap-3">
        <button
          type="button"
          onClick={onUse}
          className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
        >
          Use template →
        </button>
        {children}
      </div>
    </div>
  );
}
