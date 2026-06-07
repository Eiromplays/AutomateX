import { useEffect, useState } from "react";

// Minimal app-wide notifications: fire-and-forget via toast.success/error from
// anywhere; <Toasts/> (mounted once in the shell) renders and auto-dismisses.
type ToastItem = { id: number; kind: "success" | "error"; text: string };

const EVENT = "automatex:toast";

function emit(kind: ToastItem["kind"], text: string) {
  window.dispatchEvent(new CustomEvent(EVENT, { detail: { kind, text } }));
}

export const toast = {
  success: (text: string) => emit("success", text),
  error: (text: string) => emit("error", text),
};

let nextId = 1;

export function Toasts() {
  const [items, setItems] = useState<ToastItem[]>([]);

  useEffect(() => {
    const onToast = (event: Event) => {
      const { kind, text } = (event as CustomEvent<{ kind: ToastItem["kind"]; text: string }>).detail;
      const id = nextId++;
      setItems((current) => [...current, { id, kind, text }]);
      window.setTimeout(
        () => setItems((current) => current.filter((item) => item.id !== id)),
        kind === "error" ? 8000 : 4500,
      );
    };

    window.addEventListener(EVENT, onToast);
    return () => window.removeEventListener(EVENT, onToast);
  }, []);

  if (items.length === 0) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 flex w-80 flex-col gap-2">
      {items.map((item) => (
        <button
          key={item.id}
          type="button"
          onClick={() => setItems((current) => current.filter((x) => x.id !== item.id))}
          className={
            item.kind === "success"
              ? "rounded-md border border-emerald-500/40 bg-zinc-900 px-3 py-2 text-left text-sm text-emerald-300 shadow-lg"
              : "rounded-md border border-red-500/40 bg-zinc-900 px-3 py-2 text-left text-sm text-red-300 shadow-lg"
          }
        >
          {item.text}
        </button>
      ))}
    </div>
  );
}
