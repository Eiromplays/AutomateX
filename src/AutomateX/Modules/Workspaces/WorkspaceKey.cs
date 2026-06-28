namespace AutomateX.Modules.Workspaces;

// A workspace's data-encryption key (DEK), stored wrapped (KEK-encrypted) — never unwrapped at rest.
// Versions support rotation: the Active one encrypts new writes; older versions stay to decrypt until
// a re-encrypt pass retires them.
public sealed class WorkspaceKey
{
    private WorkspaceKey()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public int Version { get; private set; }

    // The DEK encrypted with the instance KEK (SecretCipher v1: format).
    public string WrappedDek { get; private set; } = null!;

    public bool Active { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkspaceKey Create(Guid workspaceId, int version, string wrappedDek) => new()
    {
        WorkspaceId = workspaceId,
        Version = version,
        WrappedDek = wrappedDek,
        Active = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Deactivate() => Active = false;
}
