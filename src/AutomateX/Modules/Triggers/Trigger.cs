namespace AutomateX.Modules.Triggers;

public static class TriggerTypes
{
    public const string Cron = "cron";
    public const string Webhook = "webhook";
}

public sealed record CronTriggerConfig(string Cron);

public sealed class Trigger
{
    private Trigger()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkflowId { get; private set; }

    public string Type { get; private set; } = null!;

    public string ConfigJson { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public DateTimeOffset? NextRunAt { get; private set; }

    public DateTimeOffset? LastFiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Trigger Create(Guid workflowId, string type, string configJson, DateTimeOffset? nextRunAt) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowId = workflowId,
        Type = type,
        ConfigJson = configJson,
        Enabled = true,
        NextRunAt = nextRunAt,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void MarkFired(DateTimeOffset? nextRunAt)
    {
        LastFiredAt = DateTimeOffset.UtcNow;
        NextRunAt = nextRunAt;

        // A cron trigger with no future occurrence (or broken config) disables itself.
        if (Type == TriggerTypes.Cron && nextRunAt is null)
        {
            Enabled = false;
        }
    }
}
