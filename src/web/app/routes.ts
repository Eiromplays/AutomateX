import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/workflows.tsx"),
  route("workflows/new", "routes/workflow-new.tsx"),
  route("workflows/:id", "routes/workflow-detail.tsx"),
  route("workflows/:id/edit", "routes/workflow-edit.tsx"),
  route("executions", "routes/executions.tsx"),
  route("executions/:id", "routes/execution-detail.tsx"),
  route("connections", "routes/connections.tsx"),
  route("plugins", "routes/plugins.tsx"),
  route("workspace", "routes/workspace.tsx"),
] satisfies RouteConfig;
