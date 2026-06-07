using AutomateX.Engine.Actions;

namespace AutomateX.Engine;

// M0 only: the single hardcoded workflow proving the engine loop end-to-end.
// Replaced by persisted workflow definitions in M1.
public static class HardcodedWorkflow
{
    public static readonly Guid Id = new("0196b6d4-0000-7000-8000-000000000001");

    public const string CronExpression = "* * * * *";

    public static readonly HttpRequestConfig Step = new("GET", "https://api.github.com/zen");
}
