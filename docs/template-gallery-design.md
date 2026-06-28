# Template gallery (v4.3)

Today's Templates page is a small static list baked into the frontend (`lib/templates.ts`): eight
curated starter workflows that flow through `/workflows/new` via the import-review screen. This grows it
into a real gallery from three sources:

1. **Built-in** — the curated starters that ship with the app (offline, always available).
2. **Workspace** — templates a user saves from their own workflows, stored per workspace.
3. **Community** — a remote, hash-verified catalog of contributed templates, fetched on demand.

A "template" is just a portable workflow document — exactly what `WorkflowTransfer.Export` already
produces (secrets excluded; connections + variables as `{{…}}` name references). So every source yields
the same shape and every "Use" flows through the existing import-review screen. Nothing about a
template is secret or instance-specific, which keeps all three sources simple.

## Built-in (unchanged in spirit)

The static set stays as the offline baseline. It moves from a hand-maintained `lib/templates.ts` to the
repo's `templates/` folder (one JSON doc + metadata per template), so the same files seed the community
catalog at release time — one source of truth instead of two.

## Workspace templates

```
Template (WorkspaceId, Name, Description, Category, Doc, CreatedAt)
```

- **Save as template** — a button on a workflow's detail page exports its latest version
  (`WorkflowTransfer.Export`) and stores it with a name/description/category. Editor role; audited.
- **CRUD** — `GET /templates` (workspace), `POST /templates` (save), `DELETE /templates/{id}`.
- The `Doc` column is the export JSON verbatim; "Use" hands it to the import-review screen like a file
  import. No secrets travel (export already strips them), so workspace templates are safe to keep.

## Community catalog

Mirrors the plugin catalog exactly — the machinery already exists (`PluginCatalog.Parse/Verify`, a
configurable URL, the release packaging script):

- A `catalog.json` lists entries `{ name, description, category, url, sha256 }`, hosted on the repo's
  GitHub release (built from the `templates/` folder by a packaging script, same as plugins).
- `EngineOptions.TemplateCatalogUrl` (defaults to the repo's latest release) points the app at it.
- `GET /templates/catalog` fetches + parses the list. **Use** fetches the entry's doc, verifies its
  sha256, and hands it to import-review. Unlike plugins, there's **no install step** — a template is
  inert JSON, not code, so it never lands on disk or in the DB unless the user then "Save to my
  templates."
- **Curation/contribution:** community templates live as files under `templates/` in the repo;
  contributions are PRs; a release packages them into `catalog.json`. No server-side submission flow,
  no arbitrary remote code — the hash gate guarantees what the app fetches matches the catalog.

## Gallery UX

One Templates page, three labelled sections (or a source filter): **Built-in**, **Your templates**,
**Community**. A search box + category filter across all of them (now that there are more than eight).
Every card has **Use →** (import-review); workspace cards add **Delete**; community cards add **Save to
my templates** (store the verified doc as a workspace template). Community is lazy-loaded and degrades
gracefully when the catalog is unreachable (the other two sources still render).

## API

```
GET    /templates                 workspace templates
POST   /templates                 save { name, description, category, fromWorkflowId }  (exports it)
DELETE /templates/{id}
GET    /templates/catalog         fetch + parse the remote catalog (name/description/category/url/sha256)
```

"Use" needs no endpoint — the client already has the doc (built-in: bundled; workspace: from `GET`;
community: fetched + verified) and routes to `/workflows/new` with it.

## Testing

- Pure: catalog parse/verify (reuse the plugin-catalog tests' shape); "save as template" captures the
  export doc for the latest version; a saved template round-trips back through import.
- API: save (editor), list (workspace-scoped), delete; catalog fetch tolerates an unreachable URL.
- FE: gallery merges the three sources; search/category filter; community failure doesn't break the page.

## Risks

- **Catalog integrity.** A template is JSON, not code, so the blast radius is small, but a malicious doc
  could reference surprising actions/URLs. Mitigations: sha256 verification, the import-review screen
  (the user sees every step before creating), and no auto-install.
- **Two homes for built-ins.** Moving the static set into `templates/` must keep the offline baseline —
  bundle them so the page works with no network.

## Slicing

- **v4.3a** — this design note.
- **v4.3b** — workspace templates: `Template` entity + migration + CRUD API + "Save as template" +
  gallery "Your templates" section. Tests-first on the pure/save bits.
- **v4.3c** — community catalog: move built-ins to `templates/`, packaging script + release step,
  `TemplateCatalogUrl` + `GET /templates/catalog`, gallery "Community" section + "Save to my templates".
- **v4.3d** — gallery UX (sections/search/category) polish + wrap-up docs (CHANGELOG, recipe,
  RELEASE-v4.3.0).
