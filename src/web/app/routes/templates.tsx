import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { api } from "../lib/api";
import { templates } from "../lib/templates";

export default function Templates() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const confirm = useConfirm();

  const { data: workspaceTemplates } = useQuery({
    queryKey: ["workflow-templates"],
    queryFn: api.workflowTemplates.list,
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.workflowTemplates.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["workflow-templates"] }),
    onError: (e) => toast.error(String(e)),
  });

  const use = (doc: unknown) => navigate("/workflows/new", { state: { importDoc: doc } });

  return (
    <div className="max-w-4xl space-y-8">
      <div>
        <h1 className="text-lg font-semibold">Templates</h1>
        <p className="text-sm text-zinc-500">
          Start from a ready-made workflow — you&apos;ll review and tweak it (URLs, connections, variables)
          before creating.
        </p>
      </div>

      {(workspaceTemplates?.length ?? 0) > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-medium text-zinc-300">Your templates</h2>
          <div className="grid gap-3 md:grid-cols-2">
            {workspaceTemplates?.map((template) => (
              <div key={template.id} className="flex flex-col rounded-lg border border-zinc-800 bg-zinc-900/40 p-4">
                {template.category && (
                  <div className="text-xs uppercase tracking-wide text-zinc-600">{template.category}</div>
                )}
                <div className="mt-0.5 text-sm font-medium text-zinc-100">{template.name}</div>
                <p className="mt-1 flex-1 text-sm text-zinc-400">{template.description}</p>
                <div className="mt-3 flex items-center gap-3">
                  <button
                    type="button"
                    onClick={() => use(template.doc)}
                    className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
                  >
                    Use template →
                  </button>
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
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-zinc-300">Built-in</h2>
        <div className="grid gap-3 md:grid-cols-2">
          {templates.map((template) => (
            <div key={template.id} className="flex flex-col rounded-lg border border-zinc-800 bg-zinc-900/40 p-4">
              <div className="text-xs uppercase tracking-wide text-zinc-600">{template.category}</div>
              <div className="mt-0.5 text-sm font-medium text-zinc-100">{template.name}</div>
              <p className="mt-1 flex-1 text-sm text-zinc-400">{template.description}</p>
              <button
                type="button"
                onClick={() => use(template.doc)}
                className="mt-3 self-start rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
              >
                Use template →
              </button>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
