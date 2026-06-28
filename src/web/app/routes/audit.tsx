import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { ListSkeleton } from "../components/ui/skeleton";
import { api } from "../lib/api";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

export default function Audit() {
  const [actor, setActor] = useState("");
  const [action, setAction] = useState("");
  const [take, setTake] = useState(100);

  const { data: entries, isLoading } = useQuery({
    queryKey: ["audit", actor, action, take],
    queryFn: () =>
      api.audit.list({ take, actor: actor || undefined, action: action || undefined }),
  });

  return (
    <section className="space-y-4">
      <div>
        <h1 className="text-lg font-semibold">Audit log</h1>
        <p className="text-sm text-zinc-500">
          Who changed, ran, or deleted what. Instance admins see every workspace; others see their own.
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <input
          className={inputClass}
          placeholder="Filter by actor"
          value={actor}
          onChange={(e) => setActor(e.target.value)}
        />
        <input
          className={inputClass}
          placeholder="Filter by action (e.g. workflow.delete)"
          value={action}
          onChange={(e) => setAction(e.target.value)}
        />
      </div>

      {isLoading ? (
        <ListSkeleton />
      ) : !entries || entries.length === 0 ? (
        <p className="text-sm text-zinc-600">No audit entries match.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-zinc-800">
          <table className="w-full text-sm">
            <thead className="border-b border-zinc-800 text-left text-xs uppercase text-zinc-500">
              <tr>
                <th className="px-3 py-2 font-medium">When</th>
                <th className="px-3 py-2 font-medium">Actor</th>
                <th className="px-3 py-2 font-medium">Action</th>
                <th className="px-3 py-2 font-medium">Target</th>
                <th className="px-3 py-2 font-medium">Details</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.id} className="border-b border-zinc-900 last:border-0">
                  <td className="whitespace-nowrap px-3 py-2 text-zinc-400">
                    {new Date(entry.at).toLocaleString()}
                  </td>
                  <td className="px-3 py-2 text-zinc-300">{entry.actor}</td>
                  <td className="px-3 py-2">
                    <code className="text-xs text-emerald-400">{entry.action}</code>
                  </td>
                  <td className="px-3 py-2 text-zinc-500">
                    {entry.targetType ? `${entry.targetType} ${entry.targetId ?? ""}`.trim() : "—"}
                  </td>
                  <td className="px-3 py-2 text-zinc-400">{entry.summary ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {entries && entries.length >= take && (
        <button
          type="button"
          onClick={() => setTake((t) => t + 100)}
          className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm hover:bg-zinc-900"
        >
          Load more
        </button>
      )}
    </section>
  );
}
