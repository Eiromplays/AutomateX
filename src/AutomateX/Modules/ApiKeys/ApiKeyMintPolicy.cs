using AutomateX.Modules.Workspaces;

namespace AutomateX.Modules.ApiKeys;

// Who may mint what. Instance admins mint any scope. Everyone else may mint only Workspace/Workflow keys,
// and only in a workspace they Own, at a role no higher than their own. Kept pure (no DB/HttpContext) so
// the matrix is unit-testable; the endpoint feeds it the caller's admin status and role in the target.
public static class ApiKeyMintPolicy
{
    public static bool CanMint(
        ApiKeyScope scope, WorkspaceRole role, bool isInstanceAdmin, WorkspaceRole? callerWorkspaceRole)
    {
        if (isInstanceAdmin)
        {
            return true;
        }

        if (scope == ApiKeyScope.Instance)
        {
            return false;
        }

        return callerWorkspaceRole is WorkspaceRole.Owner && role <= callerWorkspaceRole.Value;
    }
}
