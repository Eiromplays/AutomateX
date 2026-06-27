namespace AutomateX.Modules.Triggers;

public static class TriggerTypes
{
    public const string Cron = "cron";
    public const string Webhook = "webhook";
    public const string Workflow = "workflow";

    // Engine-native: fires its workflow when another execution in the same workspace settles Failed.
    public const string OnFailure = "execution.onFailure";
}

public static class TriggerEditRules
{
    // A webhook's config holds its hashed secret — editing it would clobber the
    // secret, so webhook config is immutable (rotate the secret instead).
    public static bool CanEditConfig(string type) => type != TriggerTypes.Webhook;
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

    // The step order this trigger starts the run at; null = the first step by order (default).
    public int? EntryStepOrder { get; private set; }

    public string ConfigJson { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public DateTimeOffset? NextRunAt { get; private set; }

    public DateTimeOffset? LastFiredAt { get; private set; }

    // Set by the trigger host when a listener crashes (e.g. a feed 404s / won't parse);
    // cleared on the next successful fire. Surfaces "this trigger is broken" in the UI.
    public string? LastError { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Trigger Create(
        Guid workflowId, string type, string configJson, DateTimeOffset? nextRunAt, int? entryStepOrder = null) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowId = workflowId,
        Type = type,
        EntryStepOrder = entryStepOrder,
        ConfigJson = configJson,
        Enabled = true,
        NextRunAt = nextRunAt,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void ReplaceConfig(string configJson)
    {
        ConfigJson = configJson;
    }

    public void Reconfigure(string configJson, DateTimeOffset? nextRunAt)
    {
        ConfigJson = configJson;
        NextRunAt = nextRunAt;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }

    public void SetEntryStep(int? entryStepOrder)
    {
        EntryStepOrder = entryStepOrder;
    }

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
