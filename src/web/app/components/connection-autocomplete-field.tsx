import { useLayoutEffect, useRef, useState, type ChangeEvent, type KeyboardEvent } from "react";
import { createPortal } from "react-dom";
import {
  applyConnectionCompletion,
  connectionAutocompleteQuery,
  connectionCompletions,
  type Completion,
} from "./connection-autocomplete";
import type { ConnectionLite } from "./connection-refs";

type Props = {
  value: string;
  onChange: (value: string) => void;
  connections: ConnectionLite[];
  multiline?: boolean;
  className?: string;
  placeholder?: string;
};

// A text input or textarea with inline {{connections.…}} autocomplete: typing the prefix pops a
// caret-anchored suggestion list filtered as you go; picking one inserts the full token. Owns the
// element ref so it also handles textarea auto-grow without a second component.
export function ConnectionAutocompleteField({
  value,
  onChange,
  connections,
  multiline,
  className,
  placeholder,
}: Props) {
  const elRef = useRef<HTMLInputElement | HTMLTextAreaElement | null>(null);
  const [completions, setCompletions] = useState<Completion[]>([]);
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null);
  const tokenStart = useRef(0);
  const caret = useRef(0);

  // Auto-grow the textarea to fit content (min height comes from the className floor).
  useLayoutEffect(() => {
    const el = elRef.current;
    if (multiline && el) {
      el.style.height = "auto";
      el.style.height = `${el.scrollHeight}px`;
    }
  }, [value, multiline]);

  const close = () => setCompletions([]);

  const refresh = (text: string, at: number) => {
    const active = connectionAutocompleteQuery(text, at);
    const matches = active ? connectionCompletions(connections, active.query).slice(0, 8) : [];
    if (!active || matches.length === 0) {
      close();
      return;
    }
    tokenStart.current = active.start;
    caret.current = at;
    const rect = elRef.current?.getBoundingClientRect();
    if (rect) {
      setPos({ top: rect.bottom + 4, left: rect.left, width: Math.max(rect.width, 220) });
    }
    setCompletions(matches);
  };

  const handleChange = (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    onChange(e.target.value);
    refresh(e.target.value, e.target.selectionStart ?? e.target.value.length);
  };

  const pick = (token: string) => {
    const next = applyConnectionCompletion(value, caret.current, tokenStart.current, token);
    onChange(next.value);
    close();
    requestAnimationFrame(() => {
      const el = elRef.current;
      if (el) {
        el.focus();
        el.setSelectionRange(next.caret, next.caret);
      }
    });
  };

  const shared = {
    ref: (el: HTMLInputElement | HTMLTextAreaElement | null) => {
      elRef.current = el;
    },
    value,
    placeholder,
    onChange: handleChange,
    onKeyDown: (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        close();
      }
    },
    onBlur: () => setTimeout(close, 120),
  };

  return (
    <>
      {multiline ? (
        <textarea {...shared} className={`resize-none overflow-hidden ${className ?? ""}`} />
      ) : (
        <input type="text" {...shared} className={className} />
      )}
      {completions.length > 0 &&
        pos &&
        createPortal(
          <div
            style={{ position: "fixed", top: pos.top, left: pos.left, width: pos.width }}
            className="z-50 rounded-md border border-zinc-700 bg-zinc-900 p-1 shadow-xl"
          >
            {completions.map((c) => (
              <button
                type="button"
                key={c.token}
                // mouseDown (not click) + preventDefault so the field doesn't blur-close first.
                onMouseDown={(e) => {
                  e.preventDefault();
                  pick(c.token);
                }}
                className="block w-full rounded px-2 py-1 text-left text-xs text-zinc-300 hover:bg-zinc-800"
              >
                <span className="text-zinc-500">{c.name}.</span>
                {c.key}
              </button>
            ))}
          </div>,
          document.body,
        )}
    </>
  );
}
