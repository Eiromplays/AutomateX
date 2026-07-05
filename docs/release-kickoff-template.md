# Release kickoff template

One design doc + one chat per release. Write the design doc first, then paste the kickoff below into a
fresh chat and work the slices. Keep the **Done / Decisions locked / Next** lines current so a new chat
can resume mid-feature without re-reading everything.

## Kickoff (paste into a new chat, fill the «placeholders»)

```
Continuing AutomateX «v4.7» — «feature name». Read `docs/«feature»-design.md` first;
it's the full design + the «N»-slice plan. Repo at the usual mount; solution `AutomateX.slnx`.

Workflow: you design + generate code (tests-first for engine slices), I build/run/test/
migrate/commit locally and report back. One commit per slice; you give me the commit
message (format `feat(«scope»): … (v4.7 mN)`). Concise .NET tone, vertical slices, no clean
architecture, minimal comments; web-search to verify a modern .NET best practice if unsure.
Migrations are mine to run (`dotnet ef migrations add <Name> --project src/AutomateX`);
frontend gate is `pnpm test/typecheck/lint/build`.

Done: nothing yet — start with Slice 1.
Decisions locked: «any up-front calls, or "none — follow the design doc"».
Next: Start with Slice 1.
```

When resuming mid-feature, replace the last three lines, e.g.:

```
Done: Slices 1–3 committed, all green.
Decisions locked: «short bullet list of anything decided since the design doc».
Next: Start with Slice 4.
```

## Design doc skeleton (`docs/<feature>-design.md`)

Write this before the kickoff. It's what keeps decisions consistent across slices.

- **What & why** — the problem, and what changes for the user.
- **Shape** — the entity/schema, token/ID formats, the API surface, request resolution.
- **Calls made** — decisions taken up front (and what's deferred), so they aren't re-litigated per slice.
- **Slice plan** — a numbered list of buildable commits, each staying green
  (`dotnet test`; web `pnpm test/typecheck/lint/build`). Note which slice adds a migration.

## Conventions (stable across releases)

- **Roles:** I design + generate code; you build/run/test, add migrations, and commit locally.
- **Tests-first** for engine slices; each slice ends green before moving on.
- **One commit per slice**, message `feat(<scope>): … (v<x.y> m<n>)`; I supply the message.
- **Style:** vertical-slice architecture, no clean architecture, clean self-documenting code, minimal
  comments (only where complex logic genuinely needs it).
- **Migrations are yours:** `dotnet ef migrations add <Name> --project src/AutomateX`.
- **Verify step** in each slice (tests, diff review, or a subagent for high-stakes changes).
