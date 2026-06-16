using System.Globalization;
using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Connections;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutomateX.Engine.Connections;

// Decrypts a workspace's connections for templating and, for OAuth connections whose access
// token is at/over expiry (minus skew), refreshes it at use-time and persists the new tokens.
// Single-flight per connection id, with a re-check after acquiring the gate, so concurrent
// steps don't double-refresh (which would needlessly rotate the provider's refresh token).
public sealed class ConnectionResolver(
    SecretCipher cipher,
    ConnectionTypeRegistry registry,
    OAuthClient oauthClient,
    IServiceScopeFactory scopeFactory,
    ILogger<ConnectionResolver> logger)
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    // Striped single-flight: a fixed pool of gates keyed by connection id, so the lock set is
    // bounded (no per-id SemaphoreSlim that's never disposed). Two ids sharing a stripe just
    // serialize briefly; the re-check after acquiring the gate keeps refresh correct regardless.
    private const int LockStripes = 32;
    private readonly SemaphoreSlim[] _locks =
        [.. Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1))];

    private SemaphoreSlim LockFor(Guid connectionId) => _locks[(uint)connectionId.GetHashCode() % LockStripes];

    public async Task<Dictionary<string, JsonElement>> ResolveAsync(
        IReadOnlyList<Connection> connections, CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement> result = [];
        foreach (var connection in connections)
        {
            var values = TryDecrypt(connection.EncryptedSecrets);
            if (values is null)
            {
                continue; // undecryptable (key rotated) — the step fails clearly if it needs this one
            }

            if (IsOAuth(connection.Provider) && ShouldRefresh(values))
            {
                values = await RefreshAsync(connection.Id, cancellationToken) ?? values;
            }

            result[connection.Name] = JsonSerializer.SerializeToElement(values);
        }

        return result;
    }

    private bool IsOAuth(string? provider) =>
        provider is not null && registry.GetInstance(provider) is IOAuthConnectionType;

    private static bool ShouldRefresh(Dictionary<string, string> values) =>
        values.ContainsKey("refreshToken")
        && values.TryGetValue("expiresAt", out var raw)
        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
        && OAuthFlow.NeedsRefresh(DateTimeOffset.FromUnixTimeSeconds(unix), DateTimeOffset.UtcNow, RefreshSkew);

    private async Task<Dictionary<string, string>?> RefreshAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var gate = LockFor(connectionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

            var connection = await dbContext.Connections.FirstOrDefaultAsync(x => x.Id == connectionId, cancellationToken);
            var current = connection is null ? null : TryDecrypt(connection.EncryptedSecrets);
            if (connection is null || current is null)
            {
                return null;
            }

            if (!ShouldRefresh(current))
            {
                return current; // another step refreshed it while we waited on the gate
            }

            if (connection.Provider is null || registry.GetInstance(connection.Provider) is not IOAuthConnectionType oauthType)
            {
                return current;
            }

            OAuthTokens tokens;
            try
            {
                tokens = await oauthClient.RefreshAsync(oauthType.BuildOAuthConfig(current), current["refreshToken"], cancellationToken);
            }
            catch (OAuthException ex)
            {
                logger.LogWarning("OAuth refresh failed for connection {Connection}: {Error}", connection.Name, ex.Message);
                return current; // keep the stale token; the step surfaces the auth failure
            }

            current["accessToken"] = tokens.AccessToken;
            if (tokens.RefreshToken is not null)
            {
                current["refreshToken"] = tokens.RefreshToken;
            }

            if (tokens.ExpiresAt is { } expiresAt)
            {
                current["expiresAt"] = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                current.Remove("expiresAt");
            }

            connection.Update(null, cipher.Encrypt(JsonSerializer.Serialize(current)));
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Refreshed OAuth token for connection {Connection}", connection.Name);
            return current;
        }
        finally
        {
            gate.Release();
        }
    }

    private Dictionary<string, string>? TryDecrypt(string encrypted)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.Decrypt(encrypted));
        }
        catch (Exception ex) when (ex is SecretCipherException or JsonException)
        {
            return null;
        }
    }
}
