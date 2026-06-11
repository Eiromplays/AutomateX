using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using AutomateX.Plugin.Sdk;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

// Verifies a connection's credentials actually work, if its type supports it. The test
// runs server-side with the decrypted secrets; the result (ok/message) is the payload —
// a failed test is still a 200 (it's a diagnostic, not a request error).
public static class TestConnection
{
    public sealed record Response(bool Ok, string Message);

    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        SecretCipher cipher,
        WorkspaceAccess access,
        ConnectionTypeRegistry registry,
        IHttpClientFactory httpClientFactory) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("connections/{id}/test");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var connection = await dbContext.Connections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (connection is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (connection.Provider is not { } provider || registry.GetInstance(provider) is not IConnectionTester tester)
            {
                await Send.OkAsync(new Response(false, "This connection type can't be tested."), ct);
                return;
            }

            Dictionary<string, string> values;
            try
            {
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.Decrypt(connection.EncryptedSecrets)) ?? [];
            }
            catch (SecretCipherException)
            {
                await Send.OkAsync(new Response(false, "Could not decrypt the connection (wrong encryption key?)."), ct);
                return;
            }

            try
            {
                var result = await tester.TestAsync(values, httpClientFactory.CreateClient(), ct);
                await Send.OkAsync(new Response(result.Ok, result.Message), ct);
            }
            catch (Exception ex)
            {
                await Send.OkAsync(new Response(false, ex.Message), ct);
            }
        }
    }
}
