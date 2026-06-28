using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine.Security;

// Key rotation. Per-workspace DEK rotation mints a fresh data key and re-encrypts the workspace's
// connections to it (no downtime). KEK re-wrap migrates every wrapped DEK to the current instance key
// after a key change (paired with Encryption__PreviousKey for the transition).
public sealed class KeyRotationService(
    IServiceScopeFactory scopeFactory, SecretCipher cipher, DataKeyCache cache, TenantCipher tenantCipher)
{
    public async Task<(int Version, int ReEncrypted)> RotateWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var keys = await dbContext.WorkspaceKeys.Where(x => x.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
        var nextVersion = keys.Count == 0 ? 1 : keys.Max(k => k.Version) + 1;

        foreach (var key in keys.Where(k => k.Active))
        {
            key.Deactivate();
        }

        var dek = RandomNumberGenerator.GetBytes(32);
        dbContext.WorkspaceKeys.Add(WorkspaceKey.Create(workspaceId, nextVersion, cipher.Encrypt(Convert.ToBase64String(dek))));
        await dbContext.SaveChangesAsync(cancellationToken);

        // Reload so the cipher resolves the new active version (and the now-inactive old one) from DB.
        cache.Invalidate(workspaceId);

        // Re-encrypt the workspace's connections under the new active DEK.
        var connections = await dbContext.Connections.Where(x => x.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
        var reEncrypted = 0;
        foreach (var connection in connections)
        {
            string plaintext;
            try
            {
                plaintext = await tenantCipher.DecryptAsync(connection.EncryptedSecrets, workspaceId, cancellationToken);
            }
            catch (SecretCipherException)
            {
                continue; // undecryptable (e.g. lost key) — leave it rather than destroy it
            }

            connection.Update(null, await tenantCipher.EncryptAsync(plaintext, workspaceId, cancellationToken));
            reEncrypted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (nextVersion, reEncrypted);
    }

    // Re-wrap every DEK under the current KEK. Run after setting Encryption__Key to the new key and
    // Encryption__PreviousKey to the old one; afterwards PreviousKey can be removed. DEK bytes are
    // unchanged, so connection ciphertext and the cache stay valid.
    public async Task<int> RewrapAllAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var keys = await dbContext.WorkspaceKeys.ToListAsync(cancellationToken);
        foreach (var key in keys)
        {
            var dek = cipher.Decrypt(key.WrappedDek); // current key, falling back to PreviousKey
            key.Rewrap(cipher.Encrypt(dek));          // re-wrap under the current key
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return keys.Count;
    }
}
