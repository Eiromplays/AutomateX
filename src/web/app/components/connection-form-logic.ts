// Pure helpers behind ConnectionForm, split out so the row/merge/validation rules can be
// unit-tested without React. Shared by the Connections page and the in-builder create modal.
import type { ConnectionSummary, ConnectionTypeInfo } from "../lib/api";

export type SecretRow = {
  key: number;
  name: string;
  value: string;
  existing: boolean;
  removed: boolean;
  // Type-driven metadata (a "fixed" row's key comes from a connection type).
  fixed: boolean;
  secret: boolean;
  required: boolean;
  label?: string;
  helpText?: string | null;
  docsUrl?: string | null;
};

let nextRow = 1;

export function emptyRow(): SecretRow {
  return {
    key: nextRow++,
    name: "",
    value: "",
    existing: false,
    removed: false,
    fixed: false,
    secret: true,
    required: false,
  };
}

// Build rows from a connection type's fields, merging any already-stored keys; keys the type
// doesn't define but the connection still holds are appended as removable free-form rows.
export function rowsFromType(type: ConnectionTypeInfo, existingKeys: string[]): SecretRow[] {
  const typed: SecretRow[] = type.fields.map((f) => ({
    key: nextRow++,
    name: f.key,
    value: "",
    existing: existingKeys.includes(f.key),
    removed: false,
    fixed: true,
    secret: f.secret,
    required: f.required,
    label: f.label,
    helpText: f.helpText,
    docsUrl: f.docsUrl,
  }));
  const extra = existingKeys
    .filter((k) => !type.fields.some((f) => f.key === k))
    .map((k) => ({ ...emptyRow(), name: k, existing: true }));
  return [...typed, ...extra];
}

export function freeRows(existingKeys: string[]): SecretRow[] {
  return existingKeys.length > 0
    ? [
        ...existingKeys.map((k) => ({
          ...emptyRow(),
          name: k,
          existing: true,
        })),
        emptyRow(),
      ]
    : [emptyRow()];
}

// Merge semantics: filled value = overwrite, existing+removed = delete (null), untouched = omit (keep).
export function buildSecretsPayload(rows: SecretRow[]): Record<string, string | null> {
  const secrets: Record<string, string | null> = {};
  for (const row of rows) {
    if (row.existing && row.removed) {
      secrets[row.name] = null;
    } else if (row.name && row.value) {
      secrets[row.name] = row.value;
    }
  }
  return secrets;
}

// Required typed fields must have a value (create) or already exist and not be removed (edit).
export function hasMissingRequired(rows: SecretRow[], editing: boolean): boolean {
  return rows.some((r) => r.fixed && r.required && !r.value && !(editing && r.existing && !r.removed));
}

export function isCreateInvalid(name: string, rows: SecretRow[]): boolean {
  return !name || rows.every((r) => !r.value);
}

// The connection key to reference after a save (for the builder's token insertion): the first
// row the user supplied or that already exists.
export function firstReferenceableKey(rows: SecretRow[]): string | null {
  return rows.find((r) => r.name && (r.value || r.existing))?.name ?? null;
}

// Substring match across name, provider label, and secret key names — for the list/picker search.
export function filterConnections(connections: ConnectionSummary[], query: string): ConnectionSummary[] {
  const q = query.trim().toLowerCase();
  if (!q) return connections;
  return connections.filter(
    (c) =>
      c.name.toLowerCase().includes(q) ||
      (c.provider?.toLowerCase().includes(q) ?? false) ||
      c.secretKeys.some((k) => k.toLowerCase().includes(q)),
  );
}
