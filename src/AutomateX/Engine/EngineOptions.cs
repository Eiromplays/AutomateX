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

    // How often the plugin-trigger supervisor reconciles listeners with trigger rows.
    public TimeSpan TriggerSyncInterval { get; set; } = TimeSpan.FromSeconds(10);

    // Where the install-from-catalog feature fetches its plugin list.
    public string PluginCatalogUrl { get; set; } =
        "https://github.com/Eiromplays/AutomateX/releases/latest/download/catalog.json";

    // Maximum workflow-chaining depth: a chained execution may itself chain until
    // its depth exceeds this — then the firing is skipped (logged, not failed).
    public int MaxChainDepth { get; set; } = 5;

    // Relative paths resolve against AppContext.BaseDirectory.
    public string PluginsPath { get; set; } = "plugins";

    // Watch the plugins directory and hot-reload on changes (debounced).
    // Off by default; the AppHost enables it for local development.
    public bool WatchPlugins { get; set; }

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
