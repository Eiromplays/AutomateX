using AutomateX.Modules.Workspaces;

namespace AutomateX.Modules.Connections;

// Named secret bundles referenced from step configs via {{connections.<name>.<field>}}.
// Secrets are stored as one AES-GCM-encrypted JSON object; plaintext exists only in
// memory during template resolution and is never persisted or returned by the API.
public sealed class Connection
{
    private Connection()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Provider { get; private set; }

    public string EncryptedSecrets { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Connection Create(string name, string? provider, string encryptedSecrets, Guid? workspaceId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId ?? Workspace.DefaultId,
        Name = name,
        Provider = provider,
        EncryptedSecrets = encryptedSecrets,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Update(string? provider, string encryptedSecrets)
    {
        if (provider is not null)
        {
            Provider = provider;
        }

        EncryptedSecrets = encryptedSecrets;
    }
}
