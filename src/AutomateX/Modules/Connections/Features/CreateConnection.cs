using System.Text.Json;
using System.Text.RegularExpressions;
using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

public static partial class CreateConnection
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex NamePattern();

    public sealed class Endpoint(AutomateXDbContext dbContext, TenantCipher cipher, WorkspaceAccess access, Audit.IAuditSink audit) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("connections");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!NamePattern().IsMatch(req.Name ?? ""))
            {
                ThrowError("Connection name may contain letters, digits, '-' and '_' (max 64) — it is used in {{connections.<name>.<field>}} templates.");
            }

            if (req.Secrets is null || req.Secrets.Count == 0)
            {
                ThrowError("At least one secret key/value is required.");
            }

            if (await dbContext.Connections.AnyAsync(x => x.Name == req.Name && x.WorkspaceId == ws, ct))
            {
                ThrowError($"A connection named '{req.Name}' already exists in this workspace.");
            }

            string encrypted;
            try
            {
                encrypted = await cipher.EncryptAsync(JsonSerializer.Serialize(req.Secrets), ws, ct);
            }
            catch (SecretCipherException ex)
            {
                ThrowError(ex.Message);
                return;
            }

            var connection = Connection.Create(req.Name!, req.Provider, encrypted, ws);
            dbContext.Connections.Add(connection);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "connection.create", ws, WorkspaceAccess.GetActor(User),
                "connection", connection.Id.ToString(), connection.Name, ct);

            // Key names only — secret values never leave the server.
            await Send.OkAsync(new Response(connection.Id, connection.Name, connection.Provider, [.. req.Secrets!.Keys]), ct);
        }
    }

    public sealed record Request(string? Name, string? Provider, Dictionary<string, string>? Secrets);

    public sealed record Response(Guid Id, string Name, string? Provider, List<string> SecretKeys);
}
