using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Connections;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Key rotation: a workspace's DEK can be rotated (new active version, connections re-encrypted), and a
// KEK change is bridged by Encryption__PreviousKey so old-wrapped data still decrypts.
public sealed class KeyRotationTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Rotating_a_workspace_re_encrypts_its_connections_under_a_new_version()
    {
        Guid ws;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<TenantCipher>();

            var workspace = Workspace.Create($"rot-{Guid.CreateVersion7():N}");
            dbContext.Workspaces.Add(workspace);
            await dbContext.SaveChangesAsync();
            ws = workspace.Id;

            var encrypted = await cipher.EncryptAsync("""{"token":"keep-me"}""", ws); // v2, version 1
            dbContext.Connections.Add(Connection.Create("api", null, encrypted, ws));
            await dbContext.SaveChangesAsync();
        }

        var rotation = fixture.Host.Services.GetRequiredService<KeyRotationService>();
        var (version, reEncrypted) = await rotation.RotateWorkspaceAsync(ws, CancellationToken.None);

        Assert.Equal(2, version);
        Assert.Equal(1, reEncrypted);

        await using var read = fixture.Host.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var cipher2 = read.ServiceProvider.GetRequiredService<TenantCipher>();

        var connection = await db.Connections.AsNoTracking().FirstAsync(x => x.WorkspaceId == ws);
        Assert.Equal("""{"token":"keep-me"}""", await cipher2.DecryptAsync(connection.EncryptedSecrets, ws));

        var keys = await db.WorkspaceKeys.AsNoTracking().Where(x => x.WorkspaceId == ws).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.True(keys.Single(k => k.Version == 2).Active);
        Assert.False(keys.Single(k => k.Version == 1).Active);
    }

    [Fact]
    public void Previous_key_decrypts_data_wrapped_under_the_old_kek()
    {
        var oldKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var underOld = new SecretCipher(Options.Create(new EncryptionOptions { Key = oldKey }));
        var ciphertext = underOld.Encrypt("master-secret");

        var rotated = new SecretCipher(Options.Create(new EncryptionOptions { Key = newKey, PreviousKey = oldKey }));
        Assert.Equal("master-secret", rotated.Decrypt(ciphertext)); // falls back to the previous key

        var newOnly = new SecretCipher(Options.Create(new EncryptionOptions { Key = newKey }));
        Assert.Throws<SecretCipherException>(() => newOnly.Decrypt(ciphertext));
    }
}
