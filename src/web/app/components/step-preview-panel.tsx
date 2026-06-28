import { useState } from "react";
import { api, type StepPreviewResult, type StepTestResult } from "../lib/api";
import { AutoTextarea } from "./auto-textarea";

const SAMPLE_PLACEHOLDER = `{
  "triggerPayload": { "email": "a@b.com" },
  "stepOutputs": { "fetch": { "id": 42 } }
}`;

// Control-flow nodes only mean something inside a running workflow — no standalone "run for real".
const CONTROL_FLOW = new Set(["switch", "forEach", "wait", "workflow.call"]);

type SampleContext = { triggerPayload?: unknown; stepOutputs?: Record<string, unknown> };

// Per-step dry-run: previews the step's current (possibly unsaved) config — resolves templates against
// an optional sample context, lists unresolved refs, masks connection values — and (opt-in) runs the
// step for real once. No execution on preview; the real run has real side effects.
export function StepPreviewPanel({
  workflowId,
  actionType,
  configJson,
  stepKeys,
}: {
  workflowId: string;
  actionType: string;
  configJson: string;
  stepKeys: Record<string, number>;
}) {
  const [open, setOpen] = useState(false);
  const [sample, setSample] = useState("");
  const [preview, setPreview] = useState<StepPreviewResult | null>(null);
  const [test, setTest] = useState<StepTestResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<"preview" | "test" | null>(null);

  // Parse the sample-context textarea; empty is valid (undefined context).
  const parseSample = (): { ok: true; value?: SampleContext } | { ok: false } => {
    if (!sample.trim()) return { ok: true };
    try {
      return { ok: true, value: JSON.parse(sample) as SampleContext };
    } catch {
      setError("Sample context is not valid JSON.");
      return { ok: false };
    }
  };

  const runPreview = async () => {
    setError(null);
    const parsed = parseSample();
    if (!parsed.ok) return;
    setBusy("preview");
    try {
      setPreview(await api.workflows.previewStep(workflowId, { configJson, stepKeys, sampleContext: parsed.value }));
    } catch (e) {
      setError(String(e));
      setPreview(null);
    } finally {
      setBusy(null);
    }
  };

  const runForReal = async () => {
    setError(null);
    const parsed = parseSample();
    if (!parsed.ok) return;
    if (!window.confirm("Run this step for real? It executes the action with real side effects (sends, HTTP calls).")) {
      return;
    }
    setBusy("test");
    try {
      setTest(
        await api.workflows.testStep(workflowId, {
          configJson,
          actionType,
          stepKeys,
          sampleContext: parsed.value,
          confirm: true,
        }),
      );
    } catch (e) {
      setError(String(e));
      setTest(null);
    } finally {
      setBusy(null);
    }
  };

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="mt-3 rounded-md border border-zinc-700 px-2.5 py-1 text-xs text-zinc-300 hover:bg-zinc-900"
      >
        Preview / test
      </button>
    );
  }

  const controlFlow = CONTROL_FLOW.has(actionType);

  return (
    <div className="mt-3 space-y-2 rounded-md border border-zinc-800 bg-zinc-950/40 p-3">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-zinc-400">Preview / test</span>
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

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          disabled={busy !== null}
          onClick={runPreview}
          className="rounded-md bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        >
          {busy === "preview" ? "Resolving…" : "Preview config"}
        </button>
        <button
          type="button"
          disabled={busy !== null || controlFlow}
          onClick={runForReal}
          title={controlFlow ? "Control-flow steps can't be run on their own." : "Executes the action for real."}
          className="rounded-md border border-amber-700/60 px-3 py-1 text-xs text-amber-300 hover:bg-amber-950/40 disabled:opacity-40"
        >
          {busy === "test" ? "Running…" : "Run for real"}
        </button>
        <span className="text-[11px] text-zinc-600">Real run has real side effects.</span>
      </div>

      {error && <p className="text-xs text-red-400">{error}</p>}

      {preview && (
        <div className="space-y-2">
          {preview.unresolved.length > 0 && (
            <div className="flex flex-wrap items-center gap-1">
              <span className="text-xs text-zinc-500">Unresolved:</span>
              {preview.unresolved.map((ref) => (
                <span
                  key={ref}
                  className="rounded border border-red-800/60 bg-red-950/40 px-1.5 py-0.5 font-mono text-[11px] text-red-300"
                >
                  {ref}
                </span>
              ))}
            </div>
          )}

          {preview.connectionsUsed.length > 0 && (
            <div className="text-xs text-zinc-500">
              Reads: {preview.connectionsUsed.map((c) => `${c.name} (${c.fields.join(", ")})`).join("; ")}
            </div>
          )}

          <pre className="overflow-x-auto rounded-md border border-zinc-800 bg-zinc-900 p-2 font-mono text-[11px] text-zinc-300">
            {JSON.stringify(preview.resolvedConfig, null, 2)}
          </pre>
        </div>
      )}

      {test && (
        <div className="space-y-1">
          <span
            className={`text-xs font-medium ${test.ok ? "text-emerald-400" : "text-red-400"}`}
          >
            {test.ok ? "Ran successfully" : "Failed"}
          </span>
          <pre className="overflow-x-auto rounded-md border border-zinc-800 bg-zinc-900 p-2 font-mono text-[11px] text-zinc-300">
            {test.ok ? JSON.stringify(test.output, null, 2) : test.error}
          </pre>
        </div>
      )}
    </div>
  );
}
