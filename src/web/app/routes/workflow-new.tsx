import { useMutation } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router";
import { toast } from "../components/toast";
import { WorkflowForm, type WorkflowFormValue } from "../components/workflow-form";
import { applyTriggers, importedDraftTriggers } from "../components/workflow-triggers";
import { api } from "../lib/api";

type ImportDoc = {
  automatex?: number;
  name?: string;
  description?: string | null;
  continueOnFailure?: boolean;
  steps?: {
    actionType?: string;
    name?: string | null;
    config?: Record<string, unknown>;
    idempotencyKey?: string | null;
  }[];
  edges?: { from: number; to: number; label: string | null }[];
  triggers?: { type?: string; config?: Record<string, unknown> }[];
};

export default function WorkflowNew() {
  const navigate = useNavigate();
  const location = useLocation();
  const importDoc = (location.state as { importDoc?: ImportDoc } | null)?.importDoc ?? null;

  // Import is just a prefilled builder: the doc seeds steps/edges/triggers, then everything is
  // created through the same path as a fresh workflow — so imported triggers are shown and
  // editable, and webhook secrets surface on save like anywhere else.
  const create = useMutation({
    mutationFn: async (value: WorkflowFormValue) => {
      const created = await api.workflows.create({
        name: value.name,
        description: value.description,
        steps: value.steps,
        edges: value.edges,
        continueOnFailure: value.continueOnFailure,
      });
      const secrets = await applyTriggers(created.id, value.triggers ?? [], []);
      return { created, secrets };
    },
    onSuccess: ({ created, secrets }) => {
      for (const info of secrets) toast.success(`Webhook — copy it now, it's shown only once — ${info}`);
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
          idempotencyKey: step.idempotencyKey ?? null,
        })),
        edges: importDoc.edges ?? [],
        triggers: importedDraftTriggers(importDoc.triggers ?? []),
        continueOnFailure: importDoc.continueOnFailure ?? false,
      }
    : undefined;

  return (
    <div className="max-w-5xl">
      <h1 className={importDoc ? "mb-1 text-lg font-semibold" : "mb-6 text-lg font-semibold"}>
        {importDoc ? "Import workflow" : "New workflow"}
      </h1>
      {importDoc && (
        <p className="mb-6 text-xs text-zinc-500">
          Review the imported steps, triggers, and connection placeholders (room ids, URLs, …) before
          creating.
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
