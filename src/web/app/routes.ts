import { index, layout, type RouteConfig, route } from "@react-router/dev/routes";

export default [
  index("routes/dashboard.tsx"),
  route("workflows", "routes/workflows.tsx"),
  route("templates", "routes/templates.tsx"),
  route("workflows/new", "routes/workflow-new.tsx"),
  route("workflows/:id", "routes/workflow-detail.tsx"),
  route("workflows/:id/edit", "routes/workflow-edit.tsx"),
  route("executions", "routes/executions.tsx"),
  route("executions/:id", "routes/execution-detail.tsx"),
  // Pathless layouts group related surfaces under a tab strip without changing URLs.
  layout("routes/resources-layout.tsx", [
    route("connections", "routes/connections.tsx"),
    route("variables", "routes/variables.tsx"),
  ]),
  route("plugins", "routes/plugins.tsx"),
  layout("routes/settings-layout.tsx", [
    route("workspace", "routes/workspace.tsx"),
    route("api-keys", "routes/api-keys.tsx"),
    route("audit", "routes/audit.tsx"),
  ]),
] satisfies RouteConfig;
