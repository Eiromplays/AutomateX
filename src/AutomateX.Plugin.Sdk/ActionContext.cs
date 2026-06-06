using Microsoft.Extensions.Logging;

namespace AutomateX.Plugin.Sdk;

// Host-provided services for an action invocation. Execution metadata (ids, payloads)
// arrives here when input/output mapping lands.
public sealed class ActionContext
{
    public required ILogger Logger { get; init; }

    public required HttpClient Http { get; init; }
}
