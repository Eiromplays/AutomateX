using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine.Security;

// Resolves a workspace's data-encryption key (DEK): the active one for new writes (lazily creating the
// first), or a specific version for decrypt. DEKs are stored wrapped (KEK-encrypted) and cached
// unwrapped via DataKeyCache.
public sealed class DataKeyService(AutomateXDbContext dbContext, SecretCipher cipher, DataKeyCache cache)
{
    public async Task<(int Version, byte[] Dek)> GetActiveAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var active = await dbContext.WorkspaceKeys
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Active)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return active is not null
            ? (active.Version, Unwrap(workspaceId, active.Version, active.WrappedDek))
            : await CreateAsync(workspaceId, version: 1, cancellationToken);
    }

    public async Task<byte[]> GetAsync(Guid workspaceId, int version, CancellationToken cancellationToken)
    {
        if (cache.TryGet(workspaceId, version, out var cached))
        {
            return cached;
        }

        var row = await dbContext.WorkspaceKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Version == version, cancellationToken)
            ?? throw new SecretCipherException($"No data key v{version} for workspace {workspaceId}.");

        return Unwrap(workspaceId, version, row.WrappedDek);
    }

    private async Task<(int, byte[])> CreateAsync(Guid workspaceId, int version, CancellationToken cancellationToken)
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var wrapped = cipher.Encrypt(Convert.ToBase64String(dek));
        dbContext.WorkspaceKeys.Add(WorkspaceKey.Create(workspaceId, version, wrapped));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race to create the first key — adopt the winner's instead.
            dbContext.ChangeTracker.Clear();
            var active = await dbContext.WorkspaceKeys
                .AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.Active)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken);

            if (active is null)
            {
                throw;
            }

            return (active.Version, Unwrap(workspaceId, active.Version, active.WrappedDek));
        }

        cache.Set(workspaceId, version, dek);
        return (version, dek);
    }

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
