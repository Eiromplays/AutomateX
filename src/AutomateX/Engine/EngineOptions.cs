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

    // Maximum workflow-chaining depth: a chained execution may itself chain until
    // its depth exceeds this — then the firing is skipped (logged, not failed).
    public int MaxChainDepth { get; set; } = 5;

    // Relative paths resolve against AppContext.BaseDirectory.
    public string PluginsPath { get; set; } = "plugins";

    // Plugin upload over the API is remote code execution by design — disabled
    // unless the operator explicitly opts in.
    public bool AllowPluginUpload { get; set; }

    // When set, terminal executions older than this are deleted by the sweeper.
    // Null (default) keeps history forever.
    public TimeSpan? ExecutionRetention { get; set; }

    // The operator-declared public origin (e.g. https://automatex.example.com) used to
    // build absolute webhook URLs. Unset = relative URLs, UI prefixes its own origin.
    public string? PublicBaseUrl { get; set; }
}
