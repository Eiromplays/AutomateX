import { describe, expect, it } from "vitest";
import {
  applyConnectionCompletion,
  connectionAutocompleteQuery,
  connectionCompletions,
} from "./connection-autocomplete";
import type { ConnectionLite } from "./connection-refs";

const conns: ConnectionLite[] = [
  { name: "github", secretKeys: ["token"] },
  { name: "ssh", secretKeys: ["host", "privateKey"] },
];

describe("connectionAutocompleteQuery", () => {
  it("detects an open token at the caret", () => {
    const text = "Bearer {{connections.git";
    expect(connectionAutocompleteQuery(text, text.length)).toEqual({
      start: 7,
      query: "git",
    });
  });

  it("returns null when the token is already closed", () => {
    const text = "{{connections.github.token}}";
    expect(connectionAutocompleteQuery(text, text.length)).toBeNull();
  });

  it("returns null when whitespace breaks the token", () => {
    const text = "{{connections.git hub";
    expect(connectionAutocompleteQuery(text, text.length)).toBeNull();
  });

  it("uses the most recent open prefix", () => {
    const text = "{{connections.github.token}} and {{connections.ss";
    expect(connectionAutocompleteQuery(text, text.length)?.query).toBe("ss");
  });
});

describe("connectionCompletions", () => {
  it("matches name.key by prefix", () => {
    expect(connectionCompletions(conns, "git").map((c) => c.token)).toEqual(["{{connections.github.token}}"]);
  });

  it("narrows to a key once the name and dot are typed", () => {
    expect(connectionCompletions(conns, "ssh.p").map((c) => c.key)).toEqual(["privateKey"]);
  });

  it("lists all keys of a connection at 'name.'", () => {
    expect(connectionCompletions(conns, "ssh.").map((c) => c.key)).toEqual(["host", "privateKey"]);
  });
});

describe("applyConnectionCompletion", () => {
  it("replaces the open token and returns the new caret", () => {
    const text = "Bearer {{connections.git";
    const { value, caret } = applyConnectionCompletion(text, text.length, 7, "{{connections.github.token}}");
    expect(value).toBe("Bearer {{connections.github.token}}");
    expect(caret).toBe(value.length);
  });

  it("keeps text after the caret", () => {
    const text = "{{connections.gi rest";
    const { value } = applyConnectionCompletion(text, 16, 0, "{{connections.github.token}}");
    expect(value).toBe("{{connections.github.token}} rest");
  });
});
