# Contributing to AutomateX

Thanks for your interest. AutomateX is a self-hosted, .NET-native automation engine; this guide
covers local setup, the conventions the codebase follows, and how to get a change merged.

## Local setup

Prerequisites: **.NET 10 SDK**, **Docker** (tests and `aspire run` spin up Postgres via
Testcontainers / Aspire), **Node + pnpm** (the web app), and optionally the
[Aspire CLI](https://aspire.dev).

```bash
dotnet tool restore
aspire run                 # api + web (Vite) + Postgres; open the "web" resource
```

The web app lives in `src/web` (React Router v7, React 19, TanStack Query, Tailwind 4). Aspire
installs its packages on first start; to work on it directly:

```bash
cd src/web
pnpm install
pnpm dev          # or pnpm typecheck / pnpm test
```

## Tests

Backend and frontend both have tests, and changes are expected to keep them green.

```bash
dotnet test                          # engine + module tests (needs Docker for Postgres/SSH containers)
cd src/web && pnpm test && pnpm typecheck
```

We work **tests-first** for engine and pure-logic changes: lock the behavior in a test, then
implement. Pure logic (routing derivation, schema field mapping, connection refs, KV dedup, etc.)
is factored out so it can be unit-tested without a database or the DOM — prefer that shape for new
logic.

## Conventions

- **Commits** follow Conventional Commits, scoped by area: `feat(builder): …`, `fix(engine): …`,
  `refactor(connections): …`, `docs(recipes): …`. One logical change per commit.
- **C#**: nullable enabled, `WarningsAsErrors=Nullable`, `EnforceCodeStyleInBuild`. Vertical-slice
  modules under `src/AutomateX/Modules`, engine internals under `src/AutomateX/Engine`. Keep code
  self-documenting; comment only where the *why* isn't obvious.
- **Packages** are centrally pinned in `Directory.Packages.props` (CPM). `aspire update` keeps the
  Aspire packages aligned.
- **Migrations**: schema changes need an EF Core migration; it applies on startup
  (`Database__MigrateOnStartup`).

## Adding an action or trigger

Built-in actions live in `src/AutomateX/Engine/Actions` (auto-discovered). Plugin actions implement
`IAction<TConfig, TResult>` + `[Action]` against `AutomateX.Plugin.Sdk`; triggers implement
`ITriggerListener<TConfig>` + `[Trigger]`. Config/result types are exported as JSON Schema and drive
the builder forms automatically. Scaffold a plugin:

```bash
dotnet new install ./templates/automatex-plugin
dotnet new automatex-plugin -n MyPlugin
```

See the README's "Writing a plugin" section and the first-party plugins under `src/Plugins` for
reference.

## Pull requests

1. Open an issue first for anything non-trivial, so the approach can be agreed before the work.
2. Keep PRs focused; include tests for new behavior.
3. Make sure `dotnet test` and the web checks pass locally.
4. Note any migration, config, or plugin-rebuild impact in the PR description.

## Security

Please don't open public issues for vulnerabilities — see [SECURITY.md](SECURITY.md) for private
reporting.
