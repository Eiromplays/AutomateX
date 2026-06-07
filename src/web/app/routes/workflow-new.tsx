import { useMutation } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router";
import { api } from "../lib/api";
import { toast } from "../components/toast";
import { WorkflowForm, type WorkflowFormValue } from "../components/workflow-form";

type ImportDoc = {
  automatex?: number;
  name?: string;
  description?: string | null;
  steps?: { actionType?: string; name?: string | null; config?: Record<string, unknown> }[];
  triggers?: unknown[];
};

export default function WorkflowNew() {
  const navigate = useNavigate();
  const location = useLocation();
  const importDoc = (location.state as { importDoc?: ImportDoc } | null)?.importDoc ?? null;

  const create = useMutation({
    mutationFn: (value: WorkflowFormValue) =>
      importDoc
        ? // Submit the reviewed values through the import endpoint so its
          // validation and cron-trigger creation run in one transaction.
          api.workflows.import({
            ...importDoc,
            name: value.name,
            description: value.description,
            steps: value.steps.map((step) => ({
              actionType: step.actionType,
              name: step.name,
              config: step.config,
            })),
          })
        : api.workflows.create(value),
    onSuccess: (created) => {
      toast.success(importDoc ? "Workflow imported." : "Workflow created.");
      navigate(`/workflows/${created.id}`);
    },
  });

  const initial: WorkflowFormValue | undefined = importDoc
    ? {
        name: importDoc.name ?? "",
        description: importDoc.description ?? null,
        steps: (importDoc.steps ?? []).map((step) => ({
          actionType: step.actionType ?? "",
          name: step.name ?? null,
          config: step.config ?? {},
        })),
      }
    : undefined;

  const triggerCount = importDoc?.triggers?.length ?? 0;

  return (
    <div className="max-w-2xl">
      <h1 className={importDoc ? "mb-1 text-lg font-semibold" : "mb-6 text-lg font-semibold"}>
        {importDoc ? "Import workflow" : "New workflow"}
      </h1>
      {importDoc && (
        <p className="mb-6 text-xs text-zinc-500">
          Review the imported steps and fill in placeholders (room ids, URLs, …) before creating.
          {triggerCount > 0 &&
            ` ${triggerCount} cron trigger${triggerCount === 1 ? "" : "s"} will be created with it.`}
        </p>
      )}
      <WorkflowForm
        initial={initial}
        submitLabel={importDoc ? "Import workflow" : "Create workflow"}
        pendingLabel={importDoc ? "Importing…" : "Creating…"}
        pending={create.isPending}
        error={create.error}
        onSubmit={(value) => create.mutate(value)}
      />
    </div>
  );
}
