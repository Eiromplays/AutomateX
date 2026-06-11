import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router";
import { api } from "../lib/api";
import { toast } from "../components/toast";
import { WorkflowForm, type WorkflowFormValue } from "../components/workflow-form";

export default function WorkflowEdit() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data: workflow, isLoading, error } = useQuery({
    queryKey: ["workflow", id],
    queryFn: () => api.workflows.get(id),
    retry: false,
  });

  const update = useMutation({
    mutationFn: (value: WorkflowFormValue) => api.workflows.update(id, value),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["workflow", id] });
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      toast.success(`Saved as v${result.version}.`);
      navigate(`/workflows/${id}`);
    },
  });

  if (isLoading) return <p className="text-sm text-zinc-500">Loading…</p>;
  if (error || !workflow) {
    return (
      <p className="text-sm text-zinc-500">
        Workflow not found in this workspace —{" "}
        <a href="/workflows" className="text-emerald-400 hover:underline">
          back to workflows
        </a>
        .
      </p>
    );
  }

  return (
    <div className="max-w-5xl">
      <h1 className="mb-1 text-lg font-semibold">Edit workflow</h1>
      <p className="mb-6 text-xs text-zinc-500">
        Saving appends version v{workflow.latestVersion.version + 1} — past executions keep the
        version they ran.
      </p>
      <WorkflowForm
        initial={{
          name: workflow.name,
          description: workflow.description,
          steps: workflow.latestVersion.steps
            .slice()
            .sort((a, b) => a.order - b.order)
            .map((step) => ({
              actionType: step.actionType,
              name: step.name,
              config: JSON.parse(step.configJson) as Record<string, unknown>,
            })),
          edges: workflow.latestVersion.edges,
        }}
        triggers={workflow.triggers}
        submitLabel={`Save as v${workflow.latestVersion.version + 1}`}
        pendingLabel="Saving…"
        pending={update.isPending}
        error={update.error}
        onSubmit={(value) => update.mutate(value)}
      />
    </div>
  );
}
