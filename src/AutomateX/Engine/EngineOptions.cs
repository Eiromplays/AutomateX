namespace AutomateX.Engine;

public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    public int MaxStepAttempts { get; set; } = 4;

    public TimeSpan[] StepRetryDelays { get; set; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    ];

    public TimeSpan CronPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan StuckExecutionThreshold { get; set; } = TimeSpan.FromMinutes(30);

    // Relative paths resolve against AppContext.BaseDirectory.
    public string PluginsPath { get; set; } = "plugins";

    // When set, terminal executions older than this are deleted by the sweeper.
    // Null (default) keeps history forever.
    public TimeSpan? ExecutionRetention { get; set; }
}
