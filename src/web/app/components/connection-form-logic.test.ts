import { describe, expect, it } from "vitest";
import type { ConnectionSummary, ConnectionTypeInfo } from "../lib/api";
import {
  buildSecretsPayload,
  filterConnections,
  firstReferenceableKey,
  freeRows,
  hasMissingRequired,
  isCreateInvalid,
  rowsFromType,
  type SecretRow,
} from "./connection-form-logic";

const sshType: ConnectionTypeInfo = {
  type: "ssh",
  displayName: "SSH",
  description: null,
  source: "builtin",
  isOAuth: false,
  fields: [
    {
      key: "host",
      label: "Host",
      secret: false,
      required: true,
      helpText: null,
      docsUrl: null,
    },
    {
      key: "privateKey",
      label: "Private key",
      secret: true,
      required: false,
      helpText: null,
      docsUrl: null,
    },
  ],
};

function summary(name: string, provider: string | null, secretKeys: string[]): ConnectionSummary {
  return {
    id: name,
    name,
    provider,
    createdAt: "",
    secretKeys,
    decryptable: true,
    isOAuth: false,
    oauthExpiresAt: null,
  };
}

describe("rowsFromType", () => {
  it("marks type fields existing when already stored and appends unknown stored keys as free-form", () => {
    const rows = rowsFromType(sshType, ["host", "token"]);

    const host = rows.find((r) => r.name === "host")!;
    expect(host.fixed).toBe(true);
    expect(host.required).toBe(true);
    expect(host.existing).toBe(true);

    const privateKey = rows.find((r) => r.name === "privateKey")!;
    expect(privateKey.existing).toBe(false);

    const token = rows.find((r) => r.name === "token")!;
    expect(token.fixed).toBe(false);
    expect(token.existing).toBe(true);
  });
});

describe("freeRows", () => {
  it("seeds one blank row when empty, existing keys plus a blank when not", () => {
    expect(freeRows([])).toHaveLength(1);
    const rows = freeRows(["token"]);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ name: "token", existing: true });
    expect(rows[1].name).toBe("");
  });
});

describe("buildSecretsPayload", () => {
  const row = (patch: Partial<SecretRow>): SecretRow => ({
    key: 0,
    name: "",
    value: "",
    existing: false,
    removed: false,
    fixed: false,
    secret: true,
    required: false,
    ...patch,
  });

  it("overwrites filled, deletes removed-existing (null), and omits untouched", () => {
    const payload = buildSecretsPayload([
      row({ name: "token", value: "new" }),
      row({ name: "old", existing: true, removed: true }),
      row({ name: "kept", existing: true }), // untouched: no value, not removed
    ]);

    expect(payload).toEqual({ token: "new", old: null });
    expect("kept" in payload).toBe(false);
  });
});

describe("validation", () => {
  const filled: SecretRow = {
    key: 1,
    name: "token",
    value: "x",
    existing: false,
    removed: false,
    fixed: false,
    secret: true,
    required: false,
  };

  it("isCreateInvalid requires a name and at least one value", () => {
    expect(isCreateInvalid("", [filled])).toBe(true);
    expect(isCreateInvalid("conn", [{ ...filled, value: "" }])).toBe(true);
    expect(isCreateInvalid("conn", [filled])).toBe(false);
  });

  it("hasMissingRequired flags an empty required typed field on create but not a satisfied existing one on edit", () => {
    const req: SecretRow = {
      ...filled,
      fixed: true,
      required: true,
      value: "",
    };
    expect(hasMissingRequired([req], false)).toBe(true);
    expect(hasMissingRequired([{ ...req, existing: true }], true)).toBe(false);
  });
});

describe("firstReferenceableKey", () => {
  it("returns the first supplied or existing key, else null", () => {
    expect(
      firstReferenceableKey([
        {
          key: 1,
          name: "token",
          value: "v",
          existing: false,
          removed: false,
          fixed: false,
          secret: true,
          required: false,
        },
      ]),
    ).toBe("token");
    expect(
      firstReferenceableKey([
        {
          key: 1,
          name: "",
          value: "",
          existing: false,
          removed: false,
          fixed: false,
          secret: true,
          required: false,
        },
      ]),
    ).toBeNull();
  });
});

describe("filterConnections", () => {
  const conns = [
    summary("github-deploy", "github", ["token"]),
    summary("matrix-bot", "matrix", ["accessToken"]),
    summary("homelab-ssh", null, ["host", "privateKey"]),
  ];

  it("returns all on empty query", () => {
    expect(filterConnections(conns, "  ")).toHaveLength(3);
  });

  it("matches on name, provider, and secret key", () => {
    expect(filterConnections(conns, "git").map((c) => c.name)).toEqual(["github-deploy"]);
    expect(filterConnections(conns, "matrix").map((c) => c.name)).toEqual(["matrix-bot"]);
    expect(filterConnections(conns, "privatekey").map((c) => c.name)).toEqual(["homelab-ssh"]);
  });
});
