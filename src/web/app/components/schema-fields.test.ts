import { describe, expect, it } from "vitest";
import { fieldKind } from "./schema-fields";

describe("fieldKind", () => {
  it("maps integers and numbers to number", () => {
    expect(fieldKind({ type: "integer" })).toBe("number");
    expect(fieldKind({ type: "number" })).toBe("number");
  });

  it("maps booleans, objects and arrays to their controls", () => {
    expect(fieldKind({ type: "boolean" })).toBe("boolean");
    expect(fieldKind({ type: "object" })).toBe("json");
    expect(fieldKind({ type: "array" })).toBe("json");
  });

  it("maps plain strings to a single-line text input", () => {
    expect(fieldKind({ type: "string" })).toBe("text");
  });

  it("maps strings with format multiline to a textarea", () => {
    expect(fieldKind({ type: "string", format: "multiline" })).toBe("multiline");
  });

  it("treats a nullable multiline string (union type) as multiline", () => {
    // The exporter emits ["string","null"] for nullable [Multiline] props.
    expect(fieldKind({ type: ["string", "null"], format: "multiline" })).toBe("multiline");
  });

  it("ignores format on non-string types", () => {
    expect(fieldKind({ type: "integer", format: "multiline" })).toBe("number");
  });

  it("maps a string→string object with no properties to the key/value editor", () => {
    // The exporter emits this shape for a Dictionary<string,string> (e.g. http.poll headers).
    expect(fieldKind({ type: "object", additionalProperties: { type: "string" } })).toBe("map");
  });

  it("treats a nullable string-map (union type) as a map", () => {
    // A nullable Dictionary<string,string> exports as ["object","null"] with additionalProperties.
    expect(fieldKind({ type: ["object", "null"], additionalProperties: { type: "string" } })).toBe(
      "map",
    );
  });

  it("keeps a shaped object (named properties) on the raw JSON editor", () => {
    expect(
      fieldKind({
        type: "object",
        properties: { a: { type: "string" } },
        additionalProperties: { type: "string" },
      }),
    ).toBe("json");
  });

  it("falls back to JSON for non-string maps, boolean and absent additionalProperties", () => {
    expect(fieldKind({ type: "object", additionalProperties: { type: "integer" } })).toBe("json");
    expect(fieldKind({ type: "object", additionalProperties: true })).toBe("json");
    expect(fieldKind({ type: "object" })).toBe("json");
  });
});
