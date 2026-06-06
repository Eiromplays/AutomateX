using System.Text.Json;
using System.Text.RegularExpressions;
using AutomateX.Database;
using AutomateX.Engine.Security;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

public static partial class CreateConnection
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex NamePattern();

    public sealed class Endpoint(AutomateXDbContext dbContext, SecretCipher cipher) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("connections");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (!NamePattern().IsMatch(req.Name ?? ""))
            {
                ThrowError("Connection name may contain letters, digits, '-' and '_' (max 64) — it is used in {{connections.<name>.<field>}} templates.");
            }

            if (req.Secrets is null || req.Secrets.Count == 0)
            {
                ThrowError("At least one secret key/value is required.");
            }

            if (await dbContext.Connections.AnyAsync(x => x.Name == req.Name, ct))
            {
                ThrowError($"A connection named '{req.Name}' already exists.");
            }

            string encrypted;
            try
            {
                encrypted = cipher.Encrypt(JsonSerializer.Serialize(req.Secrets));
            }
            catch (SecretCipherException ex)
            {
                ThrowError(ex.Message);
                return;
            }

            var connection = Connection.Create(req.Name!, req.Provider, encrypted);
            dbContext.Connections.Add(connection);
            await dbContext.SaveChangesAsync(ct);

            // Key names only — secret values never leave the server.
            await Send.OkAsync(new Response(connection.Id, connection.Name, connection.Provider, [.. req.Secrets!.Keys]), ct);
        }
    }

    public sealed record Request(string? Name, string? Provider, Dictionary<string, string>? Secrets);

    public sealed record Response(Guid Id, string Name, string? Provider, List<string> SecretKeys);
}
