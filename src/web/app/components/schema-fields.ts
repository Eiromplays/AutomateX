// Pure field-mapping for SchemaForm, split out so it can be unit-tested without pulling the
// component's React/Radix/query dependencies into the (node-environment) test.

// Subset of JSON Schema the engine exports per action/trigger config (GET /api/actions).
export type JsonSchema = {
  type?: string | string[];
  format?: string;
  properties?: Record<string, JsonSchema>;
  required?: string[];
  default?: unknown;
  // Present on Dictionary<string,string> exports as {"type":"string"} — the string-map signal.
  additionalProperties?: JsonSchema | boolean;
};

export type FieldKind = "number" | "boolean" | "text" | "multiline" | "map" | "json";

// Maps a property's schema to the control we render. A string carrying format:"multiline"
// (from a [Multiline]-annotated config property) gets a textarea; a free-form string→string
// object (Dictionary<string,string>, e.g. headers) gets the key/value editor; everything else
// follows its JSON type. Shaped/nested objects and arrays fall back to the raw JSON editor.
export function fieldKind(schema: JsonSchema): FieldKind {
  const types = [schema.type ?? []].flat();
  if (types.includes("integer") || types.includes("number")) return "number";
  if (types.includes("boolean")) return "boolean";
  if (types.includes("object")) {
    const ap = schema.additionalProperties;
    const apTypes = typeof ap === "object" && ap !== null ? [ap.type ?? []].flat() : [];
    // A string-valued additionalProperties with no named properties is a free-form string map.
    if (!schema.properties && apTypes.includes("string")) return "map";
    return "json";
  }
  if (types.includes("array")) return "json";
  if (types.includes("string") && schema.format === "multiline") return "multiline";
  return "text";
}
