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
        className="mt-3 rounded-[2px] border border-line-strong px-2.5 py-1 text-xs text-fg-body hover:bg-card"
      >
        Preview / test
      </button>
    );
  }

  const controlFlow = CONTROL_FLOW.has(actionType);

  return (
    <div className="mt-3 space-y-2 rounded-[2px] border border-line bg-background/40 p-3">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-muted-foreground">Preview / test</span>
        <button type="button" onClick={() => setOpen(false)} className="text-xs text-muted-foreground hover:text-fg-body">
          Close
        </button>
      </div>

      <label className="block text-xs text-muted-foreground">
        Sample context (optional)
        <AutoTextarea
          className="mt-1 w-full rounded-[2px] border border-line-strong bg-card px-2 py-1 font-mono text-xs"
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
          className="rounded-[2px] bg-primary px-3 py-1 text-xs font-medium text-primary-foreground hover:bg-brand-bright disabled:bg-panel-recess disabled:text-muted-foreground disabled:hover:bg-panel-recess"
        >
          {busy === "preview" ? "Resolving…" : "Preview config"}
        </button>
        <button
          type="button"
          disabled={busy !== null || controlFlow}
          onClick={runForReal}
          title={controlFlow ? "Control-flow steps can't be run on their own." : "Executes the action for real."}
          className="rounded-[2px] border border-engrave px-3 py-1 text-xs text-fg-body hover:bg-panel-recess disabled:text-muted-foreground"
        >
          {busy === "test" ? "Running…" : "Run for real"}
        </button>
        <span className="text-[11px] text-muted-foreground">Real run has real side effects.</span>
      </div>

      {error && <p className="text-xs text-danger">{error}</p>}

      {preview && (
        <div className="space-y-2">
          {preview.unresolved.length > 0 && (
            <div className="flex flex-wrap items-center gap-1">
              <span className="text-xs text-muted-foreground">Unresolved:</span>
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
            <div className="text-xs text-muted-foreground">
              Reads: {preview.connectionsUsed.map((c) => `${c.name} (${c.fields.join(", ")})`).join("; ")}
            </div>
          )}

          <pre className="overflow-x-auto rounded-[2px] border border-line bg-card p-2 font-mono text-[11px] text-fg-body">
            {JSON.stringify(preview.resolvedConfig, null, 2)}
          </pre>
        </div>
      )}

      {test && (
        <div className="space-y-1">
          <span
            className={`text-xs font-medium ${test.ok ? "text-brand" : "text-danger"}`}
          >
            {test.ok ? "Ran successfully" : "Failed"}
          </span>
          <pre className="overflow-x-auto rounded-[2px] border border-line bg-card p-2 font-mono text-[11px] text-fg-body">
            {test.ok ? JSON.stringify(test.output, null, 2) : test.error}
          </pre>
        </div>
      )}
    </div>
  );
}
