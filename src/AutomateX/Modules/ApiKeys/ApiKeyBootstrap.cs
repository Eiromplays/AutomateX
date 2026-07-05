using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutomateX.Modules.ApiKeys;

// The no-IdP first-run path (Vault/Grafana pattern). When Auth__Bootstrap is set, OIDC isn't configured,
// and the instance has no usable key yet, mint one Instance admin key and print it to the log exactly
// once. The operator copies it, signs in, mints scoped keys, then revokes this one. Without the flag,
// open dev and OIDC-only instances are untouched — no static secret ever lives in config.
public static class ApiKeyBootstrap
{
    // Returns the one-time token if a key was minted, else null (already keyed, OIDC, or flag unset).
    public static async Task<string?> EnsureAsync(
        AutomateXDbContext dbContext, AuthOptions auth, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!auth.Bootstrap || auth.OidcConfigured)
        {
            return null;
        }

        if (await dbContext.ApiKeys.AnyAsync(x => x.RevokedAt == null, cancellationToken))
        {
            return null;
        }

        var (token, prefix, hash) = ApiKeyToken.Mint();
        dbContext.ApiKeys.Add(ApiKey.Create(
            "bootstrap", prefix, hash, ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "bootstrap", null));
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("AUTOMATEX BOOTSTRAP KEY: {Token}", token);
        logger.LogWarning(
            "Shown once — save it now, sign in, mint scoped keys, then revoke this bootstrap key.");
        return token;
    }
}
