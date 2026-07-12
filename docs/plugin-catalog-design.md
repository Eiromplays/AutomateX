# v5.2 — plugin catalog overhaul (design)

Cuts plugins loose from the API's release train and turns the catalog into a proper **multi-source,
independently-versioned registry** — a real manifest schema, its own open-source repo and release cadence, a
browsable/searchable UI, per-plugin permissions, opt-in auto-update, and the trust machinery third-party code
needs. This is **foundation-first**: v5.2 builds the decoupled, git/Releases-backed registry and the client
that consumes it; the **hosted registry + submission portal (and paid/licensed plugins) is the committed next
arc (v5.3+)**, built on the schema this one lands. Backend-first, **tests-first for the engine slices**, mirroring v5.0/v5.1: I design +
generate code; you build/run/test/migrate/commit locally and report back. One commit per slice; migrations
are yours (`dotnet ef migrations add <Name> --project src/AutomateX`); the frontend slice gates on
`pnpm test/typecheck/lint/build`. This arc spans **two repos** — `AutomateX` (runtime, SDK, client) and a new
`AutomateX-Plugins` (the first-party plugins + their catalog).

Two hard guardrails frame every slice: the **plugin runtime** (`PluginProcessSupervisor`, `PluginTriggerHost`,
`PluginArchive`, hot-reload, fingerprints) and the **v5.0 cross-node reload propagation**
(`PluginReloadCoordinator` → `pg_notify`) are correct and stay untouched at the wiring level. A catalog
install still ends exactly where an upload ends today — files on disk, then `ReloadAndNotifyAsync`.

## What & why

The real problem is **versioning coupling**, and it's structural. `release.yml` runs
`scripts/package-plugins.sh` on every `v*` API tag, so every plugin is re-packaged and stamped with the
**API's** version. A plugin whose code hasn't changed since v4-something still publishes as `v5.0.0`, the
catalog shows an "update", the user installs it — and gets byte-identical code. Version means nothing; you
can't tell a real change from a release-train artefact. On top of that the "catalog" is the thinnest possible
thing: a single `catalog.json` (`{ name, version, description, url, sha256 }`) baked into this repo, with the
plugin **descriptions hard-coded in a bash associative array** in the packaging script; the app reads exactly
**one** source (`EngineOptions.PluginCatalogUrl`); and the UI (`plugins.tsx` → `CatalogPanel`) is an
unsearchable flat list of Install/Update buttons. There's no manifest, so a plugin's author, license,
categories, icon, capabilities, and required permissions live nowhere; capabilities are only knowable *after*
install by describing the running host. And trust is first-party-only integrity: sha256 proves a download
matches the index, but nothing proves *who* published it, and the install endpoint gates on **Viewer**
(`InstallCatalogPlugin` authorises `WorkspaceRole.Viewer`) — a viewer can install host-trusted native code.

v5.2 fixes the root cause and builds out from it:

- **Decouple plugins from the API release.** First-party plugins move to a **separate open-source repo**
  (`AutomateX-Plugins`) with their **own per-plugin semver** and **own release/catalog cadence**. A plugin's
  version lives in its manifest and bumps **only when the plugin changes**, so an "update" in the catalog is
  always a real change. The main repo stops packaging plugins.
- **A real schema (`plugin.json`).** Each plugin ships a manifest — the single source of truth that replaces
  the bash `DESCRIPTIONS` map — carrying identity, independent version, metadata, declared capabilities,
  **required permissions**, compatibility range, and licensing fields.
- **Multiple runtime-configurable catalog sources**, npm/nuget-style. Operators add/trust/remove catalogs at
  runtime (public, first-party, or a **private** one on a LAN host), each with its own trust settings —
  instead of the single hard-coded URL.
- **Trust for code, not just integrity.** sha256 (integrity) + Ed25519/minisign **index signatures**
  (authenticity) + per-source **pinned keys**, and a real install gate.
- **The overhaul extras you called for:** per-plugin **permissions** (declared + consented + enforced) and
  opt-in **auto-update** (meaningful now that versions are real). Paid/licensed plugins are held for the
  registry arc, but the manifest schema is kept forward-compatible for them (below).

The whole thing **degrades to today**: a legacy `catalog.json` still parses, the first-party source is seeded
by default, and with no extra sources/signatures/permissions configured the behaviour is the current one. The
template gallery (v4.3) proved the "static, hash-verified, PR-contributed catalog" pattern; a plugin is
**code**, not inert JSON, so v5.2 adds the manifest, signatures, per-source trust, permissions, and a raised
gate that templates never needed.

## Shape

### The separate plugins repo (`AutomateX-Plugins`) + SDK as a package

- **All first-party plugins move to `AutomateX-Plugins`** (the nine `src/Plugins/*`). It's open source and
  PR-friendly: a contributor adds a plugin folder with a `plugin.json` and opens a PR. The `AutomateX` repo
  keeps the **runtime** (`PluginHost`, supervisor, engine), the **SDK/Protocol**, and **one sample**
  (`samples/AutomateX.SamplePlugin`) as the reference plugin + an integration fixture.
- **The SDK becomes a NuGet package.** Today plugins reference `AutomateX.Plugin.Sdk` by `ProjectReference`
  with `<Private>false</Private>` / `ExcludeAssets=runtime` ("provided by the host at runtime — never ship a
  private copy"). Out-of-repo plugins can't project-reference it, so we publish `AutomateX.Plugin.Sdk`
  (already `IsPackable=true`) — and `AutomateX.Plugin.Protocol` if a plugin needs it — to NuGet, versioned
  with the SDK's own contract. External plugins `PackageReference` it with `PrivateAssets=all` /
  `ExcludeAssets=runtime`, preserving the "host provides it at runtime" semantics. This is the one hard
  prerequisite for the repo split.
- **The plugins repo owns its release cadence.** Its workflow builds **only changed plugins** (git-diff since
  last release), reads each `plugin.json` for the version, packages + **signs** each artefact, and
  regenerates the aggregate `catalog.json` — unchanged plugins keep their prior version and url. It publishes
  to GitHub Releases (and/or Pages) as the default first-party catalog. The `AutomateX` `release.yml` drops
  the plugin-packaging + upload steps (templates stay).

### The manifest — `plugin.json`

Ships at the root of every plugin package (next to `<Name>.dll`) and lives beside the source:

```jsonc
{
  "schemaVersion": 1,
  "id": "automatex.slack",              // stable, reverse-DNS; the catalog identity (not the dll name)
  "name": "AutomateX.Plugins.Slack",    // the package/dll name (PluginArchive still expects <name>.dll)
  "displayName": "Slack",
  "version": "1.4.0",                    // the PLUGIN's own semver — bumps only when the plugin changes
  "description": "Post messages to a Slack channel via an incoming webhook.",
  "author": "AutomateX",
  "homepage": "https://github.com/Eiromplays/AutomateX-Plugins/tree/main/Slack",
  "license": "MIT",                      // SPDX id (plain metadata, shown in detail)
  "categories": ["notifications", "chat"],
  "icon": "slack",
  "minAutomateXVersion": "5.0.0",        // compatibility floor; blocks/warns on install
  "capabilities": {                      // declared, so browse shows them BEFORE install
    "actions": ["slack.postMessage"], "triggers": [], "connectionTypes": ["slack"]
  },
  "permissions": {                       // declared required access — consented at install, enforced at runtime
    "egress": ["hooks.slack.com"]        // hostnames/CIDRs the plugin needs; [] = none, "*" = unrestricted (flagged)
  }
  // pricing/entitlement fields are intentionally omitted in v5.2; unknown fields are ignored, so a
  // future "pricing" block drops in without a schema break when the registry arc adds paid plugins.
}
```

- **`id` ≠ `name`.** `id` is the catalog identity (survives a package rename, namespaces third parties);
  `name` stays the dll/package name — `PluginArchive`'s `<name>.dll` rule is unchanged.
- **`version` is the plugin's own semver**, independent of the API. This is the fix: the catalog only shows an
  update when this bumps.
- **`capabilities`** are declared so browse/detail can show what a plugin does pre-install; at package time
  they're **cross-checked against the reflected reality** (the same discovery the running host exposes) — a
  lying manifest fails CI. Post-install, the live registry stays the source of truth.
- **`permissions.egress`** declares the network the plugin needs; consented at install and wired to the
  existing plugin egress guard at runtime (below).
- `PluginManifest` is a `record` with pure `Parse`/`Validate` (required fields, `id`/`name` well-formed,
  `version`/`minAutomateXVersion` valid semver). No runtime dependency — used by packaging, the catalog index,
  and the install flow.

### Catalog schema + multi-source config

The catalog stays a **static, signed JSON index** (git/Releases-backed) — no server-side catalog DB in v5.2.
Each entry is the manifest **plus** per-version distribution + trust; the file gains a schema version and a
signing publisher:

```jsonc
{
  "schemaVersion": 2,
  "generated": "2026-07-12T…Z",
  "publisher": { "name": "AutomateX", "keyId": "AX1" },
  "plugins": [
    { /* …manifest fields (id, name, displayName, description, author, license,
         categories, icon, capabilities, permissions)… */
      "versions": [                       // newest-first; unchanged plugins keep their entry across releases
        { "version": "1.4.0", "minAutomateXVersion": "5.0.0",
          "url": "https://github.com/…/releases/download/slack-v1.4.0/AutomateX.Plugins.Slack.zip",
          "sha256": "…" }
      ]
    }
  ]
}
```

- **Additive / degrade-to-today.** `PluginCatalog.Parse` keeps accepting the v1 shape (top-level
  `name/version/description/url/sha256`), folding each legacy entry into a single-version entry with no
  manifest. A missing `schemaVersion` = v1. Any third party still emitting v1 keeps working.
- **`versions[]`** carries the versioning story in the index: each version has its own url + sha256 +
  `minAutomateXVersion`. Install can target latest (default) or a pinned version; detail shows history.
- **Sources are runtime config, not one hard-coded URL.** A `PluginCatalogSource` table (instance-admin CRUD)
  holds `Id, Name, Url, TrustedKey` (pinned publisher key, nullable = untrusted), `Enabled`, timestamps.
  Seeded with the first-party source (`EngineOptions.PluginCatalogUrl` + the built-in AutomateX key), so a
  fresh instance behaves like today with zero config. This is the npm/nuget "add a source" model — including
  a **private** catalog on a LAN host.
- **Installed provenance** — a `InstalledPlugin` table records where an installed plugin came from: `Name`,
  `Scope`, `SourceId`, `PluginId`, `Version`, `Sha256`, `Publisher`, `InstalledAt`, `InstalledBy`,
  `AutoUpdate` (opt-in flag). Nullable/best-effort — an uploaded or disk-dropped plugin has no row and the UI
  falls back to today's name-match. When present it powers real "update available", auto-update, "where from",
  and richer audit. Neither table touches the runtime, which still enumerates plugin **files** on disk.

### Trust — sha256 (integrity) + signatures (authenticity) + per-source tiers

- **Integrity (have it):** `PluginCatalog.Verify` sha256-checks the artefact against the index before extract.
  Unchanged floor.
- **Authenticity (new):** the **index** is signed by its publisher (detached `catalog.json.minisig`); the
  operator pins that key on the source. A valid signature proves the index — and every `sha256` in it — came
  from that key's holder. **Ed25519/minisign**: small, no PKI/CA, fits self-hosting. The first-party key is
  **built-in and pinned by default**, so the first-party catalog is "verified" out of the box.
- **Tiers drive UI + gate:** **Verified** (pinned key, signature checks) installs at the normal bar;
  **Untrusted** (no pinned key, or signature absent) still lists (sha256 holds) but is badged and requires
  **instance-admin + an explicit acknowledgement**; **Blocked** (signature present but fails the pinned key)
  is refused, no install offered.
- **Raised gate.** `AllowPluginUpload` stays the master switch. On top: Verified installs stay low-bar;
  Untrusted installs require `IsInstanceAdmin` + the ack flag — the same `IsInstanceAdmin` gate the plugin
  logs/ops endpoints already use — closing the "Viewer installs native code" hole.

### Per-plugin permissions (declared → consented → enforced)

- The manifest's `permissions.egress` is **surfaced at install** as a consent step ("Slack needs network
  access to: hooks.slack.com") — the detail view shows it before you commit.
- At runtime it **narrows** the plugin's network reach via the existing plugin egress guard. The supervisor
  already takes `blockPrivateEgress` + an `egressAllowlist` per host process (`PluginProcessSupervisor`
  ctor / `PluginClient`); v5.2 feeds the manifest's declared egress in as a **per-plugin allowlist**, so a
  plugin only reaches what it declared. `"*"` is allowed but flagged as broad in the consent screen. This is
  additive — a plugin with no `permissions` block behaves exactly as today.

### Opt-in auto-update

- Per plugin, an operator can enable auto-update (`InstalledPlugin.AutoUpdate`). A background **checker**
  compares provenance `Version` to the catalog's latest **compatible, trusted** version and, when enabled,
  runs the same install/update path (verify → extract → `ReloadAndNotifyAsync`) and refreshes provenance.
- It **never** auto-installs an Untrusted source, never crosses a major version by default, and honours
  `minAutomateXVersion`. It must be **single-owner across nodes** — registered as a Wolverine `SingularAgent`
  exactly like `CronScheduler`/`PluginTriggerHost` (v5.0), so N replicas don't each update. Off by default;
  the manual flow is unchanged.

### What stays untouched (guardrails)

- **`PluginProcessSupervisor`, `PluginTriggerHost`, `PluginArchive.Extract`** (zip-slip + `<name>.dll`),
  **fingerprints**, and **hot-reload** — no changes. Install/update still terminates in extract +
  `ReloadAndNotifyAsync`; the permission wiring reuses the supervisor's existing egress-allowlist ctor arg.
- **v5.0 cross-node propagation.** `ReloadAndNotifyAsync` (`pg_notify` → `ApplyPeerSignal`) stays the
  propagation path for installs, updates, and auto-updates. New state (provenance) is written by the
  originating node before the reload; peers recycle + re-describe as today.
- **`EngineOptions.AllowPluginUpload`** stays the master switch; **`PluginCatalogUrl`** stays valid (it seeds
  the default source). Upload and disk-drop paths keep working with no manifest/provenance.

## Slice plan

Workflow mirrors v5.0/v5.1: I design + generate code (tests-first for the engine slices); you build/run/test,
migrate, commit locally, report back. One commit per slice; the two security-sensitive engine slices (m2
signatures, m4 install gate) get a **subagent review** like v5.0 m2/m3. Larger arc than usual (two repos +
permissions + auto-update) — every slice still degrades to today. Dependency order: the plugins-repo split
(m7) needs the SDK-on-NuGet slice (m6); everything else is largely independent.

**m1 — manifest + versioned catalog schema (engine, tests-first, no migration).**
`PluginManifest` (`Parse`/`Validate`, incl. `permissions`; unknown fields ignored so a later `pricing` block
is non-breaking) and extend `PluginCatalog.Parse` to the v2 index (`schemaVersion`, `publisher`, per-plugin
`versions[]`, manifest fields) while still accepting v1. Tests: parse v2 + v1, semver + id validation,
missing-required rejection, legacy fold-in, latest/pinned version selection. *Verify:* `dotnet test`.
*Commit:* `feat(plugins): plugin manifest + versioned catalog schema (v5.2 m1)`.

**m2 — catalog signature verification (engine, tests-first, security).**
`PluginCatalog.VerifySignature(indexBytes, signature, publicKey)` (Ed25519/minisign) + the built-in
first-party key. Tests: valid passes; tampered fails; wrong key fails; absent signature ⇒ Untrusted, not an
exception. **Subagent security review.** *Verify:* `dotnet test`.
*Commit:* `feat(plugins): catalog signature verification (v5.2 m2)`.

**m3 — catalog sources + provenance + aggregated browse (backend, tests-first, migration = yours).**
`PluginCatalogSource` + `InstalledPlugin` entities + `AutomateXDbContext` + migration; seed the first-party
source. Rework `GET plugins/catalog` to aggregate enabled sources, dedupe by `id` (highest semver), and label
each entry with trust (verify each source's signature), installed state, and update-available (vs provenance).
Instance-admin sources CRUD. Tests: multi-source aggregate + dedupe, trust labelling, update detection,
per-source unreachable degrades. *Verify:* `dotnet test`; you run `dotnet ef migrations add PluginCatalog`.
*Commit:* `feat(plugins): multi-source catalog + install provenance (v5.2 m3)`.

**m4 — trusted install/update + permission consent (backend, tests-first, security).**
Rework `POST plugins/catalog/install`: resolve entry (+ target version) across sources; verify sha256 **and**
signature per tier; gate Untrusted behind `IsInstanceAdmin` + `acknowledge`; check `minAutomateXVersion`;
require declared-permission consent; extract + `ReloadAndNotifyAsync` (unchanged); feed the manifest's egress
into the supervisor's per-plugin allowlist; record/refresh provenance; audit with provenance. Tests: verified
install; untrusted requires ack + admin (Viewer refused); checksum/signature mismatch refused; incompatible
version blocked; permission consent required; provenance recorded; reload path invoked. **Subagent security
review.** *Verify:* `dotnet test`.
*Commit:* `feat(plugins): trusted install/update + permissions (v5.2 m4)`.

**m5 — opt-in auto-update (backend, tests-first).**
An `InstalledPlugin.AutoUpdate` flag + a `SingularAgent` checker that, for enabled plugins, finds the latest
compatible **trusted** version and runs the m4 update path; never untrusted, never a major bump by default,
honours `minAutomateXVersion`; single-owner across nodes. Config opt-in. Tests: picks the right version;
skips untrusted/incompatible/major; single-owner under two hosts; disabled = no-op. *Verify:* `dotnet test`.
*Commit:* `feat(plugins): opt-in auto-update (v5.2 m5)`.

**m6 — publish SDK (+ Protocol) as NuGet packages (build, no runtime change).**
Package `AutomateX.Plugin.Sdk` (already `IsPackable`) and `AutomateX.Plugin.Protocol`; add a NuGet publish
workflow keyed to an SDK version tag; document the `PackageReference` (`PrivateAssets=all`,
`ExcludeAssets=runtime`) so external plugins keep the host-provides-at-runtime semantics. *Verify:* pack
locally; a sample external build resolves against the package. *Commit:* `build(sdk): publish plugin SDK to
NuGet (v5.2 m6)`.

**m7 — split first-party plugins into `AutomateX-Plugins` (new repo + main repo).**
New repo: move the nine plugins, add each `plugin.json`, switch to the SDK NuGet ref, per-plugin independent
semver, a release workflow that builds changed plugins, generates + **signs** `catalog.json`, and publishes
it; add `validate-plugin.sh` + `CONTRIBUTING` for third-party PRs. Main repo: drop the plugin-package/upload
steps from `release.yml`; keep the sample. *Verify:* plugins repo CI green; a tag produces a signed catalog +
artefacts the app installs. *Commit (main repo):* `build(plugins): remove first-party plugin packaging (moved
to AutomateX-Plugins) (v5.2 m7)`.

**m8 — browsable catalog + detail UI (frontend, `pnpm` gate).**
Replace `CatalogPanel` with a card grid (icon, publisher, trust badge, categories, capability counts), search
+ category/trust filters, a detail view (full manifest, versions, permissions consent, provenance,
compatibility, license, install/update with untrusted-ack + version picker + auto-update toggle), and
instance-admin source management. Reuse v5.1 shadcn primitives; keep the existing query-invalidation.
*Verify:* `pnpm test/typecheck/lint/build`; manual: browse/search/detail, untrusted-ack, permission consent,
unreachable-source degrade. *Commit:* `feat(web): browsable plugin catalog + detail view (v5.2 m8)`.

**m9 — docs + release wrap-up (docs).**
`CHANGELOG` v5.2.0, `RELEASE-v5.2.0.md`, recipes (authoring + publishing a plugin, per-plugin versioning,
hosting a private/third-party catalog + adding a source, permissions, auto-update, licensing), extend the
plugin-operations recipe, link the new `AutomateX-Plugins` repo. *Verify:* `dotnet test` + `pnpm` gate green;
diff review. *Commit:* `docs(plugins): v5.2 catalog + registry docs (v5.2 m9)`.

## Calls made

- **Fix the root cause: decouple plugin versioning from the API release.** A plugin's version lives in its
  manifest and bumps only when it changes, so a catalog "update" is always real. This is the whole point —
  every other piece hangs off it.
- **First-party plugins move to their own open-source repo (`AutomateX-Plugins`).** Own release cadence, own
  signed catalog, PR-friendly for contributors; the main repo keeps runtime + SDK + one sample. Cleanest
  decoupling and the natural home for community contributions.
- **SDK ships as a NuGet package.** The hard prerequisite for out-of-repo plugins; the SDK is already
  `IsPackable`, and external plugins keep the "host provides the assembly at runtime" semantics via
  `PrivateAssets`/`ExcludeAssets=runtime`.
- **Foundation now, hosted registry next.** v5.2 builds the decoupled, git/Releases-backed, multi-source
  registry and the client; the **hosted submission portal/registry service is the committed v5.3+ arc**,
  designed against this schema. Ships the versioning fix fast and de-risks the service.
- **Multi-source runtime config (npm/nuget model).** A `PluginCatalogSource` table, instance-admin CRUD,
  per-source trust — public, first-party, or private LAN catalogs — replacing the single hard-coded URL.
- **Trust = sha256 + Ed25519/minisign index signature + per-source pinned keys**, first-party key built-in.
  Signatures add the authorship that sha256 can't; minisign avoids PKI. The Viewer install gate is raised to
  `IsInstanceAdmin` + ack for untrusted code.
- **Permissions declared → consented → enforced**, reusing the supervisor's existing per-process egress
  allowlist — additive, no runtime change for permission-less plugins.
- **Auto-update is opt-in, trusted-only, single-owner** (a `SingularAgent`, per v5.0) — meaningful precisely
  because versions are now real.
- **Paid/licensed is out of v5.2, but designed for.** It needs accounts + payments + the hosted registry, so
  it lands with that arc; v5.2 only keeps the manifest forward-compatible (unknown fields ignored) so pricing
  drops in later without a schema break. (Earlier draft over-reached by including entitlement groundwork.)
- **Runtime + v5.0 propagation are guardrails, not scope** — install still ends in `ReloadAndNotifyAsync`;
  supervisor/triggers/hot-reload/`PluginArchive`/`pg_notify` are verified-unchanged each slice.
- **Everything degrades to today** — v1 `catalog.json` parses, default source seeded, no sig/sources/perms =
  current behaviour, uploads + disk-drops keep working.
- **Commit scopes:** m1–m5 `feat(plugins): …`; m6 `build(sdk): …`; m7 `build(plugins): …` (main repo);
  m8 `feat(web): …`; m9 `docs(plugins): …`. `CHANGELOG` lands with m9.

## Deferred (the v5.3+ registry arc + beyond)

- **Hosted registry + submission portal** — a service (accounts + submission API + server-side
  validation/signing + web portal + search) replacing the git/Releases catalog for those who want it. The
  committed next arc; v5.2's schema + multi-source client are built to consume it unchanged.
- **Paid / licensed plugins (storefront, entitlement, payments)** — deferred whole to the registry arc; v5.2
  keeps the manifest schema forward-compatible so pricing/entitlement fields add without a break.
- **Inter-plugin dependencies / version resolution** — manifests are self-contained for now.
- **Filesystem/other permission classes beyond egress** — v5.2 declares + enforces network egress; broader
  sandboxing capabilities are a follow-up.
- **Cross-host shared plugin storage** — still the v5.0 single-host-replicas constraint; unchanged.
- **Workspace-scoped catalog installs** — catalog install stays global (as today); workspace plugins remain
  upload-only. Revisit on demand.
