import { describe, expect, it } from "vitest";
import { varsAutocompleteQuery, varsCompletions } from "./vars-autocomplete";

describe("varsAutocompleteQuery", () => {
  it("detects an open {{vars. token and returns the partial", () => {
    const text = "url is {{vars.reg";
    expect(varsAutocompleteQuery(text, text.length)).toEqual({ start: 7, query: "reg" });
  });

  it("returns null for a closed token", () => {
    const text = "{{vars.region}}";
    expect(varsAutocompleteQuery(text, text.length)).toBeNull();
  });

  it("returns null when a separator follows the prefix", () => {
    const text = "{{vars.a b";
    expect(varsAutocompleteQuery(text, text.length)).toBeNull();
  });
});

describe("varsCompletions", () => {
  it("matches names case-insensitively and emits whole tokens with an empty prefix", () => {
    const completions = varsCompletions(["region", "Retries", "baseUrl"], "re");

    expect(completions).toEqual([
      { name: "", key: "region", token: "{{vars.region}}" },
      { name: "", key: "Retries", token: "{{vars.Retries}}" },
    ]);
  });
});
