// Pure field-mapping for SchemaForm, split out so it can be unit-tested without pulling the
// component's React/Radix/query dependencies into the (node-environment) test.

// Subset of JSON Schema the engine exports per action/trigger config (GET /api/actions).
export type JsonSchema = {
  type?: string | string[];
  format?: string;
  properties?: Record<string, JsonSchema>;
  required?: string[];
  default?: unknown;
};

export type FieldKind = "number" | "boolean" | "text" | "multiline" | "json";

// Maps a property's schema to the control we render. A string carrying format:"multiline"
// (from a [Multiline]-annotated config property) gets a textarea; everything else follows
// its JSON type. Strings/objects/arrays deeper than this fall back to the raw JSON editor.
export function fieldKind(schema: JsonSchema): FieldKind {
  const types = [schema.type ?? []].flat();
  if (types.includes("integer") || types.includes("number")) return "number";
  if (types.includes("boolean")) return "boolean";
  if (types.includes("object") || types.includes("array")) return "json";
  if (types.includes("string") && schema.format === "multiline") return "multiline";
  return "text";
}
