# Recipe: templates

Don't build common workflows from scratch — start from a template, tweak, and create. The **Templates**
page pulls from three sources, with a search box and category filter across all of them.

## Use a template

Pick any card and **Use template →**. It opens the same review screen as a file import, so you adjust
URLs, connection references, and variables *before* the workflow is created. Nothing runs until you
save.

Templates carry no secrets: connections and variables ride as `{{connections.x}}` / `{{vars.x}}` name
references, so the import screen flags anything you need to wire up in this workspace.

## Save your own

On a workflow's detail page, **⋯ → Save as template** stores its latest version as a reusable template
(workspace-shared — everyone in the workspace sees it). It appears under **Your templates**; delete it
there when it's no longer useful.

## Community catalog

The **Community** section lists templates from a release-published catalog
(`Engine__TemplateCatalogUrl`, default the project's latest release). **Use** one directly, or **Save to
my templates** to keep a copy in your workspace. The section is hidden if the catalog is unreachable.

Contributing a community template: add a `templates/<name>.json` file (one `{ name, description,
category, doc }` per file, where `doc` is a workflow export) and open a PR — releases package
`templates/` into the catalog.

## Tips

- A saved template is a snapshot — editing the source workflow later doesn't change the template.
- Categorise templates (Alerts, Monitoring, AI, …) so the category filter stays useful as the gallery
  grows.
