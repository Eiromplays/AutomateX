import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { api, DEFAULT_WORKSPACE_ID, getWorkspaceId, setWorkspaceId, type WorkspaceSummary } from "../lib/api";

const inputClass =
  "rounded-md border border-line-strong bg-card px-3 py-1.5 text-sm " +
  "placeholder:text-fg-placeholder focus:border-brand-bright focus:outline-none";
export default function WorkspaceSettings() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("Editor");

  const { data: workspaces } = useQuery({
    queryKey: ["workspaces"],
    queryFn: api.workspaces.list,
  });
  const { data: me } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: api.auth.me,
    staleTime: Infinity,
  });
  const currentId = getWorkspaceId() ?? DEFAULT_WORKSPACE_ID;
  const current: WorkspaceSummary | undefined =
    workspaces?.find((w) => w.id === currentId) ?? workspaces?.[0];

  const { data: members } = useQuery({
    queryKey: ["members", current?.id],
    queryFn: () => api.workspaces.members.list(current!.id),
    enabled: !!current,
  });

  const upsert = useMutation({
    mutationFn: () => api.workspaces.members.upsert(current!.id, email, role),
    onSuccess: (member) => {
      setEmail("");
      queryClient.invalidateQueries({ queryKey: ["members", current?.id] });
      toast.success(`${member.email} is now ${member.role}.`);
    },
    onError: (error) => toast.error(`Member update failed — ${String(error)}`),
  });

  const remove = useMutation({
    mutationFn: ({ memberId }: { memberId: string; self: boolean }) =>
      api.workspaces.members.remove(current!.id, memberId),
    onSuccess: (_, { self }) => {
      if (self) {
        // Left the current workspace — fall back to Default.
        setWorkspaceId(null);
        window.location.href = "/";
        return;
      }
      queryClient.invalidateQueries({ queryKey: ["members", current?.id] });
      toast.success("Member removed.");
    },
    onError: (error) => toast.error(`Remove failed — ${String(error)}`),
  });

  const removeWorkspace = useMutation({
    mutationFn: () => api.workspaces.remove(current!.id),
    onSuccess: () => {
      setWorkspaceId(null);
      window.location.href = "/";
    },
  });

  const rotateKey = useMutation({
    mutationFn: () => api.workspaces.rotateKey(current!.id),
    onSuccess: (r) =>
      toast.success(`Key rotated to v${r.version} — ${r.reEncrypted} connection(s) re-encrypted.`),
    onError: (error) => toast.error(`Rotation failed — ${String(error)}`),
  });

  const rewrap = useMutation({
    mutationFn: () => api.keys.rewrap(),
    onSuccess: (r) => toast.success(`Re-wrapped ${r.rewrapped} key(s) under the current encryption key.`),
    onError: (error) => toast.error(`Re-wrap failed — ${String(error)}`),
  });

  if (!current) return <p className="text-sm text-muted-foreground">Loading…</p>;

  return (
    <div className="max-w-2xl space-y-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">{current.name}</h1>
          <p className="text-sm text-muted-foreground">
            Your role: <span className="text-fg-body">{current.role}</span>
          </p>
        </div>
        {current.id !== DEFAULT_WORKSPACE_ID && (
          <button
            type="button"
            onClick={async () => {
              if (
                await confirm({
                  title: `Delete workspace "${current.name}"?`,
                  body: "This deletes the workspace and everything in it.",
                  confirmLabel: "Delete",
                  destructive: true,
                })
              ) {
                removeWorkspace.mutate();
              }
            }}
            className="rounded-md border border-red-900 px-3 py-1.5 text-sm text-danger hover:bg-red-950/40"
          >
            Delete workspace
          </button>
        )}
      </div>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-fg-body">Members</h2>
        <p className="text-xs text-muted-foreground">
          Invite by email — access starts the first time they sign in. A workspace with no members is open to
          every signed-in user; adding the first member claims it.
        </p>
        <ul className="divide-y divide-zinc-800 rounded-lg border border-line">
          {members?.map((member) => {
            const isSelf = !!me?.email && member.email === me.email.toLowerCase();
            return (
              <li key={member.id} className="flex items-center justify-between px-4 py-2.5 text-sm">
                <div className="flex items-center gap-3">
                  <span>{member.email}</span>
                  <span className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">
                    {member.role}
                  </span>
                  {!member.signedInBefore && <span className="text-xs text-warning">invited</span>}
                  {isSelf && <span className="text-xs text-muted-foreground">you</span>}
                </div>
                <button
                  type="button"
                  onClick={() => remove.mutate({ memberId: member.id, self: isSelf })}
                  className="text-xs text-muted-foreground hover:text-danger"
                >
                  {isSelf ? "Leave" : "Remove"}
                </button>
              </li>
            );
          })}
          {members?.length === 0 && (
            <li className="px-4 py-4 text-center text-sm text-muted-foreground">
              No members — this workspace is unclaimed.
            </li>
          )}
        </ul>

        <div className="flex gap-2">
          <input
            className={`${inputClass} flex-1`}
            placeholder="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <select className={inputClass} value={role} onChange={(e) => setRole(e.target.value)}>
            <option>Viewer</option>
            <option>Editor</option>
            <option>Owner</option>
          </select>
          <button
            type="button"
            disabled={!email || upsert.isPending}
            onClick={() => upsert.mutate()}
            className="rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-brand-bright disabled:opacity-50"
          >
            Add / update
          </button>
        </div>
        {(upsert.error ?? remove.error ?? removeWorkspace.error) && (
          <p className="text-sm text-danger">
            {String(upsert.error ?? remove.error ?? removeWorkspace.error)}
          </p>
        )}
      </section>

      {me?.isInstanceAdmin && (
        <section className="space-y-3">
          <h2 className="text-sm font-medium text-fg-body">Encryption</h2>
          <p className="text-xs text-muted-foreground">
            Rotate this workspace's data-encryption key (re-encrypts its connection secrets), or re-wrap
            every key under the current instance key after changing <code>Encryption__Key</code>.
          </p>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              disabled={rotateKey.isPending}
              onClick={async () => {
                if (
                  await confirm({
                    title: "Rotate workspace key?",
                    body: "Mints a new data-encryption key and re-encrypts this workspace's connection secrets.",
                    confirmLabel: "Rotate",
                  })
                ) {
                  rotateKey.mutate();
                }
              }}
              className="rounded-md border border-line-strong px-3 py-1.5 text-sm hover:bg-card"
            >
              {rotateKey.isPending ? "Rotating…" : "Rotate workspace key"}
            </button>
            <button
              type="button"
              disabled={rewrap.isPending}
              onClick={async () => {
                if (
                  await confirm({
                    title: "Re-wrap all keys?",
                    body: "Re-wraps every workspace key under the current instance encryption key. Run after changing Encryption__Key (with the old key set as Encryption__PreviousKey).",
                    confirmLabel: "Re-wrap",
                  })
                ) {
                  rewrap.mutate();
                }
              }}
              className="rounded-md border border-line-strong px-3 py-1.5 text-sm hover:bg-card"
            >
              {rewrap.isPending ? "Re-wrapping…" : "Re-wrap all keys"}
            </button>
          </div>
        </section>
      )}
    </div>
  );
}
