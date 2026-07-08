using AutomateX.Modules.Workspaces;

namespace AutomateX.Modules.ApiKeys;

public enum ApiKeyScope
{
    Instance = 0,
    Workspace = 1,
    Workflow = 2,
}

// A dynamic API credential. The token itself is never stored: Prefix is a non-secret lookup id and
// SecretHash is SHA-256 of the secret half (see ApiKeyToken). Scope + Role bound what the key can do;
// ExpiresAt/RevokedAt bound how long. Revocation is soft so audit trails survive.
public sealed class ApiKey
{
    private ApiKey()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Prefix { get; private set; } = null!;

    public byte[] SecretHash { get; private set; } = null!;

    public ApiKeyScope Scope { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    public Guid? WorkflowId { get; private set; }

    public WorkspaceRole Role { get; private set; }

    public string CreatedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    // Optional per-key guards (v4.11). Null limit = unlimited; empty allowlist = any client IP.
    public int? RateLimitPerMinute { get; private set; }

    public string[] IpAllowlist { get; private set; } = [];

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public static ApiKey Create(
        string name,
        string prefix,
        byte[] secretHash,
        ApiKeyScope scope,
        Guid? workspaceId,
        Guid? workflowId,
        WorkspaceRole role,
        string createdBy,
        DateTimeOffset? expiresAt,
        int? rateLimitPerMinute = null,
        string[]? ipAllowlist = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Prefix = prefix,
        SecretHash = secretHash,
        Scope = scope,
        WorkspaceId = workspaceId,
        WorkflowId = workflowId,
        // Instance keys are the admin tier; role is meaningless there, so pin it to Owner.
        Role = scope == ApiKeyScope.Instance ? WorkspaceRole.Owner : role,
        CreatedBy = createdBy,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt,
        RateLimitPerMinute = rateLimitPerMinute,
        IpAllowlist = ipAllowlist ?? [],
    };

    public void Revoke() => RevokedAt = DateTimeOffset.UtcNow;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}
