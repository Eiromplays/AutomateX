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

    // Where the template gallery fetches its community catalog (inline workflow docs).
    public string TemplateCatalogUrl { get; set; } =
        "https://github.com/Eiromplays/AutomateX/releases/latest/download/templates-catalog.json";

    // Maximum workflow-chaining depth: a chained execution may itself chain until
    // its depth exceeds this — then the firing is skipped (logged, not failed).
    public int MaxChainDepth { get; set; } = 5;

    // Relative paths resolve against AppContext.BaseDirectory.
    public string PluginsPath { get; set; } = "plugins";

    // v4.0: run plugins out-of-process (each in its own AutomateX.PluginHost child) for true isolation,
    // instead of loading them into the host. On by default — plugin code never runs in the engine.
    public bool OutOfProcPlugins { get; set; } = true;

    // Path to AutomateX.PluginHost.dll for the out-of-proc runtime; null resolves it next to the app.
    public string? PluginHostPath { get; set; }

    // Watch the plugins directory and hot-reload on changes (debounced).
    // Off by default; the AppHost enables it for local development.
    public bool WatchPlugins { get; set; }

    // Plugin upload over the API is remote code execution by design — disabled
    // unless the operator explicitly opts in.
    public bool AllowPluginUpload { get; set; }

    // When set, terminal executions older than this are deleted by the sweeper.
    // Null (default) keeps history forever.
    public TimeSpan? ExecutionRetention { get; set; }

    // When set, audit entries older than this are pruned by the retention sweeper. Null (default)
    // keeps the audit trail forever — auto-deleting it is opt-in, since it's often compliance data.
    public TimeSpan? AuditRetention { get; set; }

    // When set, idempotency records older than this are pruned. Null (default) keeps them forever;
    // a pruned key simply allows a re-send after its window (the records are dedup cache, not data).
    public TimeSpan? IdempotencyRetention { get; set; }

    // The operator-declared public origin (e.g. https://automatex.example.com) used to
    // build absolute webhook URLs. Unset = relative URLs, UI prefixes its own origin.
    public string? PublicBaseUrl { get; set; }

    // SSRF guard for http.request: when true, requests whose host resolves to a loopback /
    // private / link-local (incl. cloud metadata) address are blocked. Off by default so internal
    // targets (a local LLM, LAN services) keep working — turn it on for internet-exposed instances.
    public bool BlockPrivateNetworkRequests { get; set; }
}
