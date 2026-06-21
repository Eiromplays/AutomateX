import { type ChangeEvent, type KeyboardEvent, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  applyConnectionCompletion,
  type Completion,
  connectionAutocompleteQuery,
  connectionCompletions,
} from "./connection-autocomplete";
import type { ConnectionLite } from "./connection-refs";
import { stepAutocompleteQuery, stepCompletions } from "./step-autocomplete";
import type { StepOutput } from "./step-refs";

type Props = {
  value: string;
  onChange: (value: string) => void;
  connections: ConnectionLite[];
  steps?: StepOutput[];
  stepOrder?: number;
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
  steps,
  stepOrder,
  multiline,
  className,
  placeholder,
}: Props) {
  const elRef = useRef<HTMLInputElement | HTMLTextAreaElement | null>(null);
  const [completions, setCompletions] = useState<Completion[]>([]);
  const [pos, setPos] = useState<{
    top: number;
    left: number;
    width: number;
  } | null>(null);
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
    // {{connections.…}} and {{steps.…}} are distinct prefixes, so at most one is active.
    const conn = connectionAutocompleteQuery(text, at);
    let step: { start: number; query: string } | null = null;
    if (!conn && steps) {
      step = stepAutocompleteQuery(text, at);
    }
    const active = conn ?? step;
    let matches: Completion[] = [];
    if (conn) {
      matches = connectionCompletions(connections, conn.query).slice(0, 8);
    } else if (step && steps) {
      matches = stepCompletions(steps, step.query, stepOrder).slice(0, 8);
    }
    if (!active || matches.length === 0) {
      close();
      return;
    }
    tokenStart.current = active.start;
    caret.current = at;
    const rect = elRef.current?.getBoundingClientRect();
    if (rect) {
      setPos({
        top: rect.bottom + 4,
        left: rect.left,
        width: Math.max(rect.width, 220),
      });
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
    // An open token (e.g. picking a step → {{steps.<key>.output) keeps the menu going so the
    // next level — whole-output + fields — appears immediately instead of a dangling token.
    const stillOpen = !token.endsWith("}}");
    if (stillOpen) {
      refresh(next.value, next.caret);
    } else {
      close();
    }
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
    // Re-open when the caret lands inside an existing token via focus or click (not just typing).
    onFocus: (e: { currentTarget: HTMLInputElement | HTMLTextAreaElement }) =>
      refresh(e.currentTarget.value, e.currentTarget.selectionStart ?? e.currentTarget.value.length),
    onClick: (e: { currentTarget: HTMLInputElement | HTMLTextAreaElement }) =>
      refresh(e.currentTarget.value, e.currentTarget.selectionStart ?? e.currentTarget.value.length),
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
            style={{
              position: "fixed",
              top: pos.top,
              left: pos.left,
              width: pos.width,
            }}
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
