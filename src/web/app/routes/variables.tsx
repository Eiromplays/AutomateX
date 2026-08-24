import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { api, type EnvironmentSummary, type VariableSummary } from "../lib/api";

const inputClass =
  "rounded-[2px] border border-line-strong bg-card px-2 py-1 text-sm placeholder:text-fg-placeholder focus:border-brand-bright focus:outline-none";

export default function Variables() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();

  const { data: environments } = useQuery({ queryKey: ["environments"], queryFn: api.environments.list });
  const { data: variables } = useQuery({ queryKey: ["variables"], queryFn: () => api.variables.list() });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["environments"] });
    queryClient.invalidateQueries({ queryKey: ["variables"] });
  };

  const [newEnv, setNewEnv] = useState("");
  const [newVar, setNewVar] = useState("");
  const [newSecret, setNewSecret] = useState(false);

  const createEnv = useMutation({
    mutationFn: () => api.environments.create(newEnv.trim()),
    onSuccess: () => {
      setNewEnv("");
      invalidate();
    },
    onError: (e) => toast.error(String(e)),
  });

  const setActive = useMutation({
    mutationFn: (id: string) => api.environments.setActive(id),
    onSuccess: invalidate,
    onError: (e) => toast.error(String(e)),
  });

  const removeEnv = useMutation({
    mutationFn: (id: string) => api.environments.remove(id),
    onSuccess: invalidate,
    onError: (e) => toast.error(String(e)),
  });

  const createVar = useMutation({
    mutationFn: () => api.variables.create({ name: newVar.trim(), secret: newSecret }),
    onSuccess: () => {
      setNewVar("");
      setNewSecret(false);
      invalidate();
    },
    onError: (e) => toast.error(String(e)),
  });

  const removeVar = useMutation({
    mutationFn: (id: string) => api.variables.remove(id),
    onSuccess: invalidate,
    onError: (e) => toast.error(String(e)),
  });

  const envs = environments ?? [];

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-semibold tracking-[-0.01em]">Variables</h1>
        <p className="text-sm text-muted-foreground max-w-[38em]">
          Reusable values referenced in step configs as{" "}
          <code className="text-brand">{"{{vars.<name>}}"}</code>. Each variable holds a value per
          environment; the active environment is used at run time. Secret values are write-only and masked.
        </p>
      </div>

      <section className="space-y-2">
        <h2 className="text-sm font-medium text-fg-body">Environments</h2>
        <div className="flex flex-wrap items-center gap-2">
          {envs.map((env) => (
            <span
              key={env.id}
              className={`flex items-center gap-1.5 rounded-[2px] border px-2.5 py-1 text-xs ${
                env.active ? "border-emerald-600 text-emerald-300" : "border-line-strong text-muted-foreground"
              }`}
            >
              {env.active && <span title="Active">●</span>}
              {env.name}
              {!env.active && (
                <button
                  type="button"
                  onClick={() => setActive.mutate(env.id)}
                  className="text-muted-foreground hover:text-brand"
                  title="Set active"
                >
                  ✓
                </button>
              )}
              {envs.length > 1 && (
                <button
                  type="button"
                  onClick={() =>
                    confirm({
                      title: `Delete environment “${env.name}”?`,
                      body: "Its variable values are removed too.",
                      confirmLabel: "Delete",
                      destructive: true,
                    }).then((ok) => ok && removeEnv.mutate(env.id))
                  }
                  className="text-muted-foreground hover:text-danger"
                  title="Delete"
                >
                  ✕
                </button>
              )}
            </span>
          ))}
          <input
            className={inputClass}
            placeholder="New environment"
            value={newEnv}
            onChange={(e) => setNewEnv(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && newEnv.trim() && createEnv.mutate()}
          />
        </div>
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-fg-body">Workspace variables</h2>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-line text-left text-xs text-muted-foreground">
                <th className="px-3 py-2 font-medium">Name</th>
                {envs.map((env) => (
                  <th key={env.id} className="px-3 py-2 font-medium">
                    {env.name}
                  </th>
                ))}
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {(variables ?? []).map((variable) => (
                <tr key={variable.id} className="border-b border-card">
                  <td className="px-3 py-2">
                    <span className="font-medium">{variable.name}</span>
                    {variable.secret && (
                      <span className="ml-2 rounded border border-line-strong px-1 text-[11px] text-warning">secret</span>
                    )}
                  </td>
                  {envs.map((env) => (
                    <td key={env.id} className="px-3 py-2">
                      <ValueCell variable={variable} environment={env} />
                    </td>
                  ))}
                  <td className="px-3 py-2 text-right">
                    <button
                      type="button"
                      onClick={() =>
                        confirm({
                          title: `Delete variable “${variable.name}”?`,
                          confirmLabel: "Delete",
                          destructive: true,
                        }).then((ok) => ok && removeVar.mutate(variable.id))
                      }
                      className="text-xs text-muted-foreground hover:text-danger"
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
              {(variables?.length ?? 0) === 0 && (
                <tr>
                  <td colSpan={envs.length + 2} className="px-3 py-6 text-center text-muted-foreground">
                    No variables yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <input
            className={inputClass}
            placeholder="New variable name"
            value={newVar}
            onChange={(e) => setNewVar(e.target.value)}
          />
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <input
              type="checkbox"
              className="accent-emerald-500"
              checked={newSecret}
              onChange={(e) => setNewSecret(e.target.checked)}
            />
            Secret
          </label>
          <button
            type="button"
            disabled={!newVar.trim() || createVar.isPending}
            onClick={() => createVar.mutate()}
            className="rounded-[2px] border border-line-strong px-2.5 py-1 text-xs hover:bg-card disabled:text-muted-foreground"
          >
            Add variable
          </button>
        </div>
      </section>
    </div>
  );
}

// One variable's value for one environment. Plain values are editable in place (save on blur); secret
// values are write-only — shown as "set" when present, replaced by typing a new value.
function ValueCell({ variable, environment }: { variable: VariableSummary; environment: EnvironmentSummary }) {
  const queryClient = useQueryClient();
  const isSet = variable.environmentIds.includes(environment.id);
  const [value, setValue] = useState("");

  const save = useMutation({
    mutationFn: (v: string) => api.variables.setValue(variable.id, environment.id, v),
    onSuccess: () => {
      if (variable.secret) setValue("");
      queryClient.invalidateQueries({ queryKey: ["variables"] });
    },
    onError: (e) => toast.error(String(e)),
  });

  return (
    <input
      type={variable.secret ? "password" : "text"}
      className={`${inputClass} w-40`}
      placeholder={
        variable.secret
          ? isSet
            ? "•••••• (set)"
            : "set value"
          : isSet
            ? "(set — type to replace)"
            : "set value"
      }
      value={value}
      onChange={(e) => setValue(e.target.value)}
      onBlur={() => value.trim() && save.mutate(value)}
      onKeyDown={(e) => e.key === "Enter" && value.trim() && save.mutate(value)}
    />
  );
}
