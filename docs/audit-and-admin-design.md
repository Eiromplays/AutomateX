# Audit log + instance-admin role (v3.6a)

Two linked pieces for the exposed/multi-tenant tier: a durable **who-did-what** trail, and an
**instance-admin** role above workspace-owner that can see across workspaces.

## Today

- Mutations (workflows, connections, triggers, plugins, members) go through FastEndpoints handlers,
  each gated by `WorkspaceAccess.AuthorizeAsync(context, role, ct)`, which already resolves the
  **workspace** and the caller's **role** from the OIDC subject/email (or `Owner` in open/api-key
  mode). Nothing about the mutation is recorded.
- The top role is `WorkspaceRole.Owner` (Viewer < Editor < Owner). There's no cross-workspace operator.

## Part A — Audit log

An append-only `AuditEntry`: `Id`, `At`, `Actor`, `WorkspaceId?` (null = instance-level), `Action`
(e.g. `workflow.create`, `connection.delete`, `trigger.rotate-secret`, `plugin.install`,
`execution.failed`), `TargetType`, `TargetId`, and an optional short `Summary`. Never updated/deleted
by the app.

**Actor resolution.** Extend `WorkspaceAccess` with `GetActor(ClaimsPrincipal)` → subject ?? email ??
`"api-key"` (machine client through the gate) / `"anonymous"` (open mode). One place, reused everywhere.

**Two capture seams, by origin:**

1. **API mutations → explicit `IAuditSink.Record(...)` from the handler.** Precise action names + target
   ids + a meaningful summary (e.g. renamed, version bumped). Audited operations: workflow
   create/update/delete/enable-disable/clone, connection create/update/delete, trigger
   create/update/delete/rotate-secret, plugin install/remove, workspace member add/remove/role-change.
   *(Rejected: a blanket FastEndpoints post-processor — it'd log every mutating route generically, but
   without domain context the rows are low-value and noisy. Explicit calls cost a line per handler and
   read far better.)*
2. **Engine runs → an `IListenFor<>` listener** (like the metrics listener) on
   `ExecutionStarted`/`Completed`/`Failed` → `execution.ran` / `execution.failed` entries. Automatic,
   no per-site code, crash-isolated.

**Read API.** `GET /api/audit` — workspace-scoped for members (their workspace only), global for an
instance-admin (filterable by workspace). Filters: actor, action, targetType, time range; paginated
(reuse the executions list pattern).

**Retention.** Append-only grows; `At` supports a periodic prune / max-age sweep (follow-up, not
load-bearing for correctness).

**Tests-first:** a create/update/delete writes exactly one entry with the right actor + action +
target; a run writes an `execution.*` entry; reads are workspace-scoped (a member can't see another
workspace's trail; an admin can).

## Part B — Instance-admin role

A role above workspace-owner for operators: read the **global** audit log, list/manage **all**
workspaces, manage instance settings.

**Grant via config** `Auth__InstanceAdmins` — a set of OIDC subjects and/or emails. In **api-key**
mode the key holder is the operator (single-tenant) → admin. In **open** mode everyone is already
`Owner`; admin endpoints stay open too (no IdP to distinguish). Config-only, never inferred from data —
an admin grant must be explicit.

- **Seam:** `WorkspaceAccess.IsInstanceAdmin(ClaimsPrincipal)` checks the principal against config (or
  true for api-key/open). Instance-admin endpoints (global audit, all-workspaces, instance settings)
  sit **outside** the per-workspace gate and require `IsInstanceAdmin`.
- **Tests-first:** a configured subject/email is admin and sees the global audit; a normal owner is
  not admin and is workspace-scoped; api-key mode → admin; open mode → admin (documented).
- **Risk:** privilege escalation — over-test the boundary; the admin set comes only from config.

## Scope / sequencing (each its own commit under a v3.6 sub-tag)

1. **This design note.**
2. **Audit core** — `AuditEntry` + `IAuditSink` + the engine-event listener + actor resolution +
   migration; capture on engine runs and a first mutation (tests-first).
3. **Instance-admin** — `Auth__InstanceAdmins` + `IsInstanceAdmin` + `WorkspaceAccess` wiring (tests).
4. **Audit read API** — `GET /api/audit` (workspace-scoped + admin-global) + filters/pagination.
5. **Capture coverage** — wire `IAuditSink` into the remaining mutating endpoints.
6. **UI** — an Audit view (admin: global; owner: workspace), filterable.
7. **Wrap-up** — CHANGELOG + recipe + release notes.

## Risks

- **Coverage gaps** — explicit capture can miss a handler; a checklist in the PR + a test per audited
  operation. (A post-processor backstop can be added later if gaps recur.)
- **PII in the trail** — actor is a subject/email; the audit log inherits the instance's data-handling
  posture. Keep summaries free of secrets (reuse the existing masking).
- **Authorization boundary** — the instance-admin gate is the new privilege edge; over-test it.

---

**Decisions to confirm:** (1) explicit `IAuditSink` capture over a generic post-processor; (2)
instance-admin via `Auth__InstanceAdmins` config; (3) engine-run audit via the existing event bus.
