# AutomateX v4.3.0

A real template gallery: start from ready-made workflows, save your own, and pull from a community
catalog.

## Highlights

- **Three sources, one page.** The Templates page now shows **built-in** starters, **your templates**
  (saved from your own workflows, workspace-shared), and a **community** catalog — with search and a
  category filter across all of them.
- **Save as template.** From a workflow's menu, save its latest version as a reusable template. It's
  stored as a portable export doc (secrets excluded; connections/variables as name references), so it's
  safe to keep and share within the workspace.
- **Community catalog.** A release-published `templates-catalog.json` of contributed workflow docs,
  fetched on demand. Use one directly or save it to your workspace. Contributions are PRs that add a
  file under `templates/`.
- Every "Use" flows through the existing import-review screen — you adjust URLs, connections, and
  variables before anything is created, and nothing runs until you save.

## Upgrade notes

- **Migration:** adds the `WorkflowTemplates` table. Additive.
- **Config:** `Engine__TemplateCatalogUrl` points at the community catalog (defaults to the project's
  latest release); the Community section simply hides itself if it's unset or unreachable.
- Built-in templates remain bundled with the app (offline); the community catalog is the network
  source.

See the [templates recipe](docs/recipes/templates.md) and
[design note](docs/template-gallery-design.md). Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Next on the roadmap: plugin operations (logs/console, status, restart, resource limits) — the big
one — then AutomateX-as-MCP-server.*
