import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, getWorkspaceId, setWorkspaceId, type WorkspaceSummary } from "../lib/api";
import { toast } from "../components/toast";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

const DEFAULT_WORKSPACE_ID = "00000000-0000-0000-0000-000000000001";

export default function WorkspaceSettings() {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("Editor");

  const { data: workspaces } = useQuery({ queryKey: ["workspaces"], queryFn: api.workspaces.list });
  const { data: me } = useQuery({ queryKey: ["auth", "me"], queryFn: api.auth.me, staleTime: Infinity });
  const currentId = getWorkspaceId() ?? DEFAULT_WORKSPACE_ID;
  const current: WorkspaceSummary | undefined = workspaces?.find((w) => w.id === currentId) ?? workspaces?.[0];

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

  if (!current) return <p className="text-sm text-zinc-500">Loading…</p>;

  return (
    <div className="max-w-2xl space-y-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">{current.name}</h1>
          <p className="text-sm text-zinc-500">
            Your role: <span className="text-zinc-300">{current.role}</span>
          </p>
        </div>
        {current.id !== DEFAULT_WORKSPACE_ID && (
          <button
            type="button"
            onClick={() => {
              if (window.confirm(`Delete workspace "${current.name}" and everything in it?`)) {
                removeWorkspace.mutate();
              }
            }}
            className="rounded-md border border-red-900 px-3 py-1.5 text-sm text-red-400 hover:bg-red-950/40"
          >
            Delete workspace
          </button>
        )}
      </div>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-zinc-300">Members</h2>
        <p className="text-xs text-zinc-500">
          Invite by email — access starts the first time they sign in. A workspace with no
          members is open to every signed-in user; adding the first member claims it.
        </p>
        <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
          {members?.map((member) => {
            const isSelf = !!me?.email && member.email === me.email.toLowerCase();
            return (
              <li key={member.id} className="flex items-center justify-between px-4 py-2.5 text-sm">
                <div className="flex items-center gap-3">
                  <span>{member.email}</span>
                  <span className="rounded bg-zinc-800 px-1.5 py-0.5 text-xs text-zinc-400">{member.role}</span>
                  {!member.signedInBefore && <span className="text-xs text-amber-400">invited</span>}
                  {isSelf && <span className="text-xs text-zinc-600">you</span>}
                </div>
                <button
                  type="button"
                  onClick={() => remove.mutate({ memberId: member.id, self: isSelf })}
                  className="text-xs text-zinc-500 hover:text-red-400"
                >
                  {isSelf ? "Leave" : "Remove"}
                </button>
              </li>
            );
          })}
          {members?.length === 0 && (
            <li className="px-4 py-4 text-center text-sm text-zinc-500">
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
            className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          >
            Add / update
          </button>
        </div>
        {(upsert.error ?? remove.error ?? removeWorkspace.error) && (
          <p className="text-sm text-red-400">
            {String(upsert.error ?? remove.error ?? removeWorkspace.error)}
          </p>
        )}
      </section>
    </div>
  );
}
