using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

// Write-only update: existing values are never returned; the patch merges into the
// decrypted bundle server-side. Name is immutable (templates reference it) — delete
// and recreate to rename.
public static class UpdateConnection
{
    public sealed class Endpoint(AutomateXDbContext dbContext, SecretCipher cipher, WorkspaceAccess access) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Put("connections/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var connection = await dbContext.Connections.FirstOrDefaultAsync(x => x.Id == req.Id && x.WorkspaceId == ws, ct);
            if (connection is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            Dictionary<string, string> merged;
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    cipher.Decrypt(connection.EncryptedSecrets)) ?? [];
                merged = req.Secrets is null ? existing : ConnectionSecretsMerger.Merge(existing, req.Secrets);

                if (merged.Count == 0)
                {
                    ThrowError("A connection must keep at least one secret — delete the connection instead.");
                }

                connection.Update(req.Provider, cipher.Encrypt(JsonSerializer.Serialize(merged)));
            }
            catch (SecretCipherException ex)
            {
                ThrowError(ex.Message);
                return;
            }

            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(connection.Id, connection.Name, connection.Provider, [.. merged.Keys]), ct);
        }
    }

    // Secrets patch: value overwrites, null deletes, absent keys stay untouched.
    public sealed record Request(Guid Id, string? Provider, Dictionary<string, string?>? Secrets);

    public sealed record Response(Guid Id, string Name, string? Provider, List<string> SecretKeys);
}
