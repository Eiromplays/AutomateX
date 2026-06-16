import { describe, expect, it } from "vitest";
import { checkConnectionRefs, hasConnectionRef, type ConnectionLite } from "./connection-refs";

const conns: ConnectionLite[] = [
  { name: "github", secretKeys: ["token"] },
  { name: "ssh", secretKeys: ["host", "privateKey"] },
];

describe("hasConnectionRef", () => {
  it("detects a ref only in strings", () => {
    expect(hasConnectionRef("{{connections.github.token}}")).toBe(true);
    expect(hasConnectionRef("plain")).toBe(false);
    expect(hasConnectionRef(42)).toBe(false);
  });
});

describe("checkConnectionRefs", () => {
  it("returns none for non-strings and strings without refs", () => {
    expect(checkConnectionRefs(undefined, conns).status).toBe("none");
    expect(checkConnectionRefs("just text", conns).status).toBe("none");
  });

  it("resolves a valid ref to ok", () => {
    expect(checkConnectionRefs("Bearer {{connections.github.token}}", conns)).toEqual({ status: "ok", unknown: [] });
  });

  it("flags an unknown connection", () => {
    expect(checkConnectionRefs("{{connections.gitlab.token}}", conns)).toEqual({
      status: "unknown",
      unknown: ["gitlab.token"],
    });
  });

  it("flags an unknown key on a known connection", () => {
    expect(checkConnectionRefs("{{connections.github.secret}}", conns)).toEqual({
      status: "unknown",
      unknown: ["github.secret"],
    });
  });

  it("reports only the unresolved refs when mixed", () => {
    const result = checkConnectionRefs(
      "{{connections.github.token}} {{connections.ssh.host}} {{connections.ssh.nope}}",
      conns,
    );
    expect(result.status).toBe("unknown");
    expect(result.unknown).toEqual(["ssh.nope"]);
  });
});
