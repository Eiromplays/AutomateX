// Inline autocomplete for {{vars.<name>}} while typing in a builder field. Pure detection/completion
// so it can be unit-tested without a DOM — mirrors connection-autocomplete.
import type { Completion } from "./connection-autocomplete";

const OPEN = "{{vars.";

// If the caret sits inside an unclosed {{vars.<partial> token, return where the token starts and the
// partial text after the prefix; else null.
export function varsAutocompleteQuery(text: string, caret: number): { start: number; query: string } | null {
  const before = text.slice(0, caret);
  const start = before.lastIndexOf(OPEN);
  if (start === -1) {
    return null;
  }
  const partial = before.slice(start + OPEN.length);
  if (partial.includes("}}") || /[\s{}.]/.test(partial)) {
    return null; // closed, or a separator means we're past the variable name
  }
  return { start, query: partial };
}

// Variable names starting with the query (case-insensitive), in list order. name is left empty so the
// menu renders just the variable name (no "prefix." like connections/steps).
export function varsCompletions(names: string[], query: string): Completion[] {
  const q = query.toLowerCase();
  return names
    .filter((name) => name.toLowerCase().startsWith(q))
    .map((name) => ({ name: "", key: name, token: `{{vars.${name}}}` }));
}
