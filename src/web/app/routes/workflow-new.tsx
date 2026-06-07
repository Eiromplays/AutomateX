import { useMutation } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { api } from "../lib/api";
import { toast } from "../components/toast";
import { WorkflowForm, type WorkflowFormValue } from "../components/workflow-form";

export default function WorkflowNew() {
  const navigate = useNavigate();

  const create = useMutation({
    mutationFn: (value: WorkflowFormValue) => api.workflows.create(value),
    onSuccess: (created) => {
      toast.success("Workflow created.");
      navigate(`/workflows/${created.id}`);
    },
  });

  return (
    <div className="max-w-2xl">
      <h1 className="mb-6 text-lg font-semibold">New workflow</h1>
      <WorkflowForm
        submitLabel="Create workflow"
        pendingLabel="Creating…"
        pending={create.isPending}
        error={create.error}
        onSubmit={(value) => create.mutate(value)}
      />
    </div>
  );
}
