import { useNavigate } from "react-router";
import { templates } from "../lib/templates";

export default function Templates() {
  const navigate = useNavigate();

  return (
    <div className="max-w-4xl space-y-4">
      <div>
        <h1 className="text-lg font-semibold">Templates</h1>
        <p className="text-sm text-zinc-500">
          Start from a ready-made workflow — you&apos;ll review and tweak it (URLs, connections, rooms) before
          creating.
        </p>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        {templates.map((template) => (
          <div
            key={template.id}
            className="flex flex-col rounded-lg border border-zinc-800 bg-zinc-900/40 p-4"
          >
            <div className="text-xs uppercase tracking-wide text-zinc-600">{template.category}</div>
            <div className="mt-0.5 text-sm font-medium text-zinc-100">{template.name}</div>
            <p className="mt-1 flex-1 text-sm text-zinc-400">{template.description}</p>
            <button
              type="button"
              onClick={() =>
                navigate("/workflows/new", {
                  state: { importDoc: template.doc },
                })
              }
              className="mt-3 self-start rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
            >
              Use template →
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
