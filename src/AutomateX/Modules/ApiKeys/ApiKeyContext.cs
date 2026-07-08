using AutomateX.Modules.Workspaces;

namespace AutomateX.Modules.ApiKeys;

// Set on HttpContext.Items by the gate when a request authenticates via an atx_ key. WorkspaceAccess
// consults it before any OIDC/open logic, so a scoped key can never widen past its Scope/Workspace.
// Actor is the audit identity ("key:<name>"), giving per-key attribution instead of a shared "api-key".
public sealed record ApiKeyContext(
    ApiKeyScope Scope,
    Guid? WorkspaceId,
    Guid? WorkflowId,
    WorkspaceRole Role,
    string Actor,
    int? RateLimitPerMinute = null,
    ClientIpAllowlist IpAllowlist = default)
{
    private const string ItemKey = "atx.apikey-context";

    public static ApiKeyContext? From(HttpContext context) => context.Items[ItemKey] as ApiKeyContext;

    public static void Set(HttpContext context, ApiKeyContext value) => context.Items[ItemKey] = value;
}
