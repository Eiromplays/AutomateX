using System.Globalization;
using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

public static class GetConnections
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        TenantCipher cipher,
        ConnectionTypeRegistry registry,
        WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("connections");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var connections = await dbContext.Connections
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            List<Response> responses = [];
            foreach (var connection in connections)
            {
                List<string> keys = [];
                var decryptable = true;
                long? oauthExpiresAt = null;
                try
                {
                    var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        await cipher.DecryptAsync(connection.EncryptedSecrets, connection.WorkspaceId, ct)) ?? [];
                    keys = [.. secrets.Keys];
                    if (secrets.TryGetValue("expiresAt", out var raw)
                        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                    {
                        oauthExpiresAt = unix;
                    }
                }
                catch (SecretCipherException)
                {
                    decryptable = false;
                }

                var isOAuth = connection.Provider is not null && registry.IsOAuth(connection.Provider);

                responses.Add(new Response(
                    connection.Id, connection.Name, connection.Provider, connection.CreatedAt, keys, decryptable,
                    isOAuth, oauthExpiresAt));
            }

            await Send.OkAsync(responses, ct);
        }
    }

    public sealed record Response(
        Guid Id,
        string Name,
        string? Provider,
        DateTimeOffset CreatedAt,
        List<string> SecretKeys,
        bool Decryptable,
        bool IsOAuth,
        long? OAuthExpiresAt);
}
