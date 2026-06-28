import { useState } from "react";
import { api, type StepPreviewResult } from "../lib/api";
import { AutoTextarea } from "./auto-textarea";

const SAMPLE_PLACEHOLDER = `{
  "triggerPayload": { "email": "a@b.com" },
  "stepOutputs": { "fetch": { "id": 42 } }
}`;

// Per-step dry-run: previews the step's current (possibly unsaved) config — resolves templates against
// an optional sample context, lists unresolved refs, and masks connection values. No execution.
export function StepPreviewPanel({
  workflowId,
  configJson,
  stepKeys,
}: {
  workflowId: string;
  configJson: string;
  stepKeys: Record<string, number>;
}) {
  const [open, setOpen] = useState(false);
  const [sample, setSample] = useState("");
  const [result, setResult] = useState<StepPreviewResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async () => {
    setBusy(true);
    setError(null);
    let sampleContext: { triggerPayload?: unknown; stepOutputs?: Record<string, unknown> } | undefined;
    if (sample.trim()) {
      try {
        sampleContext = JSON.parse(sample);
      } catch {
        setError("Sample context is not valid JSON.");
        setBusy(false);
        return;
      }
    }

    try {
      setResult(await api.workflows.previewStep(workflowId, { configJson, stepKeys, sampleContext }));
    } catch (e) {
      setError(String(e));
      setResult(null);
    } finally {
      setBusy(false);
    }
  };

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="mt-3 rounded-md border border-zinc-700 px-2.5 py-1 text-xs text-zinc-300 hover:bg-zinc-900"
      >
        Preview
      </button>
    );
  }

  return (
    <div className="mt-3 space-y-2 rounded-md border border-zinc-800 bg-zinc-950/40 p-3">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-zinc-400">Preview (dry-run — no execution)</span>
        <button type="button" onClick={() => setOpen(false)} className="text-xs text-zinc-500 hover:text-zinc-300">
          Close
        </button>
      </div>

      <label className="block text-xs text-zinc-500">
        Sample context (optional)
        <AutoTextarea
          className="mt-1 w-full rounded-md border border-zinc-700 bg-zinc-900 px-2 py-1 font-mono text-xs"
          placeholder={SAMPLE_PLACEHOLDER}
          value={sample}
          onChange={(e) => setSample(e.target.value)}
        />
      </label>

      <button
        type="button"
        disabled={busy}
        onClick={run}
        className="rounded-md bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
      >
        {busy ? "Resolving…" : "Resolve config"}
      </button>

      {error && <p className="text-xs text-red-400">{error}</p>}

      {result && (
        <div className="space-y-2">
          {result.unresolved.length > 0 && (
            <div className="flex flex-wrap items-center gap-1">
              <span className="text-xs text-zinc-500">Unresolved:</span>
              {result.unresolved.map((ref) => (
                <span
                  key={ref}
                  className="rounded border border-red-800/60 bg-red-950/40 px-1.5 py-0.5 font-mono text-[11px] text-red-300"
                >
                  {ref}
                </span>
              ))}
            </div>
          )}

          {result.connectionsUsed.length > 0 && (
            <div className="text-xs text-zinc-500">
              Reads:{" "}
              {result.connectionsUsed
                .map((c) => `${c.name} (${c.fields.join(", ")})`)
                .join("; ")}
            </div>
          )}

          <pre className="overflow-x-auto rounded-md border border-zinc-800 bg-zinc-900 p-2 font-mono text-[11px] text-zinc-300">
            {JSON.stringify(result.resolvedConfig, null, 2)}
          </pre>
        </div>
      )}
    </div>
  );
}
