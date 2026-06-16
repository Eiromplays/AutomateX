// Inline autocomplete for {{connections.<name>.<key>}} while typing in a builder field. Pure
// detection/completion/replacement so it can be unit-tested without a DOM.
import type { ConnectionLite } from "./connection-refs";

const OPEN = "{{connections.";

// If the caret sits inside an unclosed {{connections.<partial> token, return where the token
// starts and the partial text after the prefix ("git", "github." or "github.to"); else null.
export function connectionAutocompleteQuery(text: string, caret: number): { start: number; query: string } | null {
  const before = text.slice(0, caret);
  const start = before.lastIndexOf(OPEN);
  if (start === -1) {
    return null;
  }
  const partial = before.slice(start + OPEN.length);
  // A closed token, or whitespace/braces in the partial, means we're not mid-reference.
  if (partial.includes("}}") || /[\s{}]/.test(partial)) {
    return null;
  }
  return { start, query: partial };
}

export type Completion = { name: string; key: string; token: string };

// name.key completions whose "name.key" starts with the query (case-insensitive), in list order.
export function connectionCompletions(connections: ConnectionLite[], query: string): Completion[] {
  const q = query.toLowerCase();
  const out: Completion[] = [];
  for (const connection of connections) {
    for (const key of connection.secretKeys) {
      const candidate = `${connection.name}.${key}`;
      if (candidate.toLowerCase().startsWith(q)) {
        out.push({ name: connection.name, key, token: `{{connections.${connection.name}.${key}}}` });
      }
    }
  }
  return out;
}

// Replace the open {{connections.<partial> (from `start` to `caret`) with the full token, and
// return the caret position just after it.
export function applyConnectionCompletion(
  text: string,
  caret: number,
  start: number,
  token: string,
): { value: string; caret: number } {
  const value = text.slice(0, start) + token + text.slice(caret);
  return { value, caret: start + token.length };
}
