using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Per-tenant DEKs: new writes use a per-workspace key (v2), one tenant can't decrypt another's, and
// legacy v1 (single-KEK) ciphertext still decrypts.
public sealed class TenantCipherTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private async Task<Guid> SeedWorkspaceAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workspace = Workspace.Create($"dek-{Guid.CreateVersion7():N}");
        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync();
        return workspace.Id;
    }

    private async Task<T> WithCipherAsync<T>(Func<TenantCipher, Task<T>> body)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        return await body(scope.ServiceProvider.GetRequiredService<TenantCipher>());
    }

    [Fact]
    public async Task Round_trips_under_a_per_workspace_key()
    {
        var ws = await SeedWorkspaceAsync();

        var ciphertext = await WithCipherAsync(c => c.EncryptAsync("hunter2", ws));
        Assert.StartsWith("v2:", ciphertext);

        var plaintext = await WithCipherAsync(c => c.DecryptAsync(ciphertext, ws));
        Assert.Equal("hunter2", plaintext);
    }

    [Fact]
    public async Task One_workspace_cannot_decrypt_anothers_secret()
    {
        var a = await SeedWorkspaceAsync();
        var b = await SeedWorkspaceAsync();

        var ciphertext = await WithCipherAsync(c => c.EncryptAsync("secret-a", a));
        await WithCipherAsync(c => c.EncryptAsync("secret-b", b)); // give b its own DEK too

        await Assert.ThrowsAsync<SecretCipherException>(
            () => WithCipherAsync(c => c.DecryptAsync(ciphertext, b)));
    }

    [Fact]
    public async Task Legacy_v1_ciphertext_still_decrypts()
    {
        var ws = await SeedWorkspaceAsync();

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var cipher = scope.ServiceProvider.GetRequiredService<SecretCipher>();
        var legacy = cipher.Encrypt("from-v1"); // single-KEK format

        var plaintext = await WithCipherAsync(c => c.DecryptAsync(legacy, ws));
        Assert.Equal("from-v1", plaintext);
    }
}
