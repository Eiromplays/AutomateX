import { index, type RouteConfig, route } from "@react-router/dev/routes";

export default [
  index("routes/dashboard.tsx"),
  route("workflows", "routes/workflows.tsx"),
  route("templates", "routes/templates.tsx"),
  route("workflows/new", "routes/workflow-new.tsx"),
  route("workflows/:id", "routes/workflow-detail.tsx"),
  route("workflows/:id/edit", "routes/workflow-edit.tsx"),
  route("executions", "routes/executions.tsx"),
  route("executions/:id", "routes/execution-detail.tsx"),
  route("connections", "routes/connections.tsx"),
  route("variables", "routes/variables.tsx"),
  route("plugins", "routes/plugins.tsx"),
  route("audit", "routes/audit.tsx"),
  route("workspace", "routes/workspace.tsx"),
] satisfies RouteConfig;
