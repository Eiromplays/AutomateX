import { toast } from "./toast";

// Wrapping, height-capped code display with a hover copy button — long error
// bodies and outputs read as paragraphs instead of an endless horizontal scroll.
export function CodeBlock({ text, tone = "default" }: { text: string; tone?: "default" | "error" }) {
  return (
    <div className="group relative mt-3">
      <pre
        className={
          tone === "error"
            ? "max-h-64 overflow-y-auto whitespace-pre-wrap break-words rounded bg-red-950/40 p-2 text-xs text-danger"
            : "max-h-64 overflow-y-auto whitespace-pre-wrap break-words rounded bg-card p-2 text-xs text-muted-foreground"
        }
      >
        {text}
      </pre>
      <button
        type="button"
        onClick={() =>
          navigator.clipboard
            .writeText(text)
            .then(() => toast.success("Copied to clipboard."))
            .catch(() => toast.error("Copy failed."))
        }
        className="absolute right-2 top-2 hidden rounded border border-line-strong bg-card/90 px-1.5 py-0.5 text-[11px] text-muted-foreground hover:text-fg-strong group-hover:block"
      >
        Copy
      </button>
    </div>
  );
}
