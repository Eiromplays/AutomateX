// Inline autocomplete for {{steps.<key>.output.<field>}} while typing in a builder field.
// Two phases: pick an upstream step (inserts an open {{steps.<key>.output token to drill into),
// then pick an output field from that step's result schema. Pure so it unit-tests without a DOM.
import type { Completion } from "./connection-autocomplete";
import type { StepOutput } from "./step-refs";

const OPEN = "{{steps.";

// If the caret sits inside an unclosed {{steps.<partial> token, return the token start and the
// partial after the prefix ("ssh", "ssh-deploy.output." or "ssh-deploy.output.std"); else null.
export function stepAutocompleteQuery(text: string, caret: number): { start: number; query: string } | null {
  const before = text.slice(0, caret);
  const start = before.lastIndexOf(OPEN);
  if (start === -1) {
    return null;
  }
  const partial = before.slice(start + OPEN.length);
  if (partial.includes("}}") || /[\s{}]/.test(partial)) {
    return null;
  }
  return { start, query: partial };
}

// Completions for the current partial. Steps are filtered to those upstream of currentOrder
// (can't reference yourself or a later step). When a step's result schema is open/unknown
// (fields empty), only the whole-output token is offered — the graceful downgrade.
export function stepCompletions(steps: StepOutput[], query: string, currentOrder?: number): Completion[] {
  const upstream = currentOrder === undefined ? steps : steps.filter((s) => s.order < currentOrder);
  const drill = query.match(/^(.+?)\.output(?:\.(.*))?$/);

  if (drill) {
    const [, keyPart, fieldPartial = ""] = drill;
    const step = upstream.find((s) => s.key === keyPart);
    if (!step) {
      return [];
    }
    const out: Completion[] = [];
    if (fieldPartial === "") {
      out.push({ name: keyPart, key: "output", token: `{{steps.${keyPart}.output}}` });
    }
    const fp = fieldPartial.toLowerCase();
    for (const field of step.fields) {
      if (field.toLowerCase().startsWith(fp)) {
        out.push({ name: `${keyPart}.output`, key: field, token: `{{steps.${keyPart}.output.${field}}}` });
      }
    }
    return out;
  }

  const q = query.toLowerCase();
  return upstream
    .filter((s) => s.key.toLowerCase().startsWith(q))
    .map((s) => ({ name: "steps", key: s.key, token: `{{steps.${s.key}.output` }));
}
