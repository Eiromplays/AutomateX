using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine.Security;

// Resolves a workspace's data-encryption key (DEK): the active one for new writes (lazily creating the
// first), or a specific version for decrypt. DEKs are stored wrapped (KEK-encrypted) and cached
// unwrapped via DataKeyCache. Singleton — DB access goes through a scope so it can serve singletons
// (e.g. ConnectionResolver) as well as endpoints.
public sealed class DataKeyService(IServiceScopeFactory scopeFactory, SecretCipher cipher, DataKeyCache cache)
{
    public async Task<(int Version, byte[] Dek)> GetActiveAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var active = await ActiveAsync(dbContext, workspaceId, cancellationToken);
        if (active is not null)
        {
            return (active.Version, Unwrap(workspaceId, active.Version, active.WrappedDek));
        }

        var dek = RandomNumberGenerator.GetBytes(32);
        dbContext.WorkspaceKeys.Add(WorkspaceKey.Create(workspaceId, 1, cipher.Encrypt(Convert.ToBase64String(dek))));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race to create the first key — adopt the winner's instead.
            dbContext.ChangeTracker.Clear();
            var winner = await ActiveAsync(dbContext, workspaceId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return (winner.Version, Unwrap(workspaceId, winner.Version, winner.WrappedDek));
        }

        cache.Set(workspaceId, 1, dek);
        return (1, dek);
    }

    public async Task<byte[]> GetAsync(Guid workspaceId, int version, CancellationToken cancellationToken)
    {
        if (cache.TryGet(workspaceId, version, out var cached))
        {
            return cached;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var row = await dbContext.WorkspaceKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Version == version, cancellationToken)
            ?? throw new SecretCipherException($"No data key v{version} for workspace {workspaceId}.");

        return Unwrap(workspaceId, version, row.WrappedDek);
    }

    private static Task<WorkspaceKey?> ActiveAsync(AutomateXDbContext dbContext, Guid workspaceId, CancellationToken ct) =>
        dbContext.WorkspaceKeys
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Active)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

    private byte[] Unwrap(Guid workspaceId, int version, string wrappedDek)
    {
        if (cache.TryGet(workspaceId, version, out var cached))
        {
            return cached;
        }

        var dek = Convert.FromBase64String(cipher.Decrypt(wrappedDek));
        cache.Set(workspaceId, version, dek);
        return dek;
    }
}
