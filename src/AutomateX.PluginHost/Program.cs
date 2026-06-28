using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Sdk;
using AutomateX.PluginHost;
using Microsoft.Extensions.Logging;

// SPIKE (v4.0): host one plugin out-of-process and serve it over stdio. Handles the action half of the
// protocol — describe + action.execute, with a log callback — enough to validate the boundary before
// the full build (triggers, connections, supervisor) lands. args[0] = path to the plugin dll.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: AutomateX.PluginHost <plugin.dll>");
    return 1;
}

var pluginPath = Path.GetFullPath(args[0]);
var resolver = new AssemblyDependencyResolver(pluginPath);
var loadContext = new AssemblyLoadContext("plugin", isCollectible: false);
loadContext.Resolving += (context, name) =>
{
    // The SDK and Microsoft.Extensions.* must unify with this host's copies (same rule as the in-proc
    // loader); plugin-private deps resolve from the plugin folder; framework assemblies fall through.
    if (name.Name is "AutomateX.Plugin.Sdk" || name.Name!.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
    {
        return null;
    }

    var path = resolver.ResolveAssemblyToPath(name);
    return path is null ? null : context.LoadFromAssemblyPath(path);
};

var assembly = loadContext.LoadFromAssemblyPath(pluginPath);

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var writeLock = new object();

void Send(JsonObject message)
{
    lock (writeLock)
    {
        PluginFrames.Write(stdout, message);
    }
}

while (PluginFrames.TryRead(stdin, out var request))
{
    var id = (string?)request["id"];
    var method = (string?)request["method"];
    try
    {
        switch (method)
        {
            case "describe":
                Send(new JsonObject { ["id"] = id, ["result"] = Describe(assembly) });
                break;

            case "action.execute":
                var parameters = request["params"]!;
                var resultJson = await ExecuteActionAsync(
                    assembly, (string)parameters["type"]!, (string)parameters["configJson"]!, id, Send);
                Send(new JsonObject { ["id"] = id, ["result"] = new JsonObject { ["resultJson"] = resultJson } });
                break;

            default:
                Send(new JsonObject { ["id"] = id, ["error"] = $"Unknown method '{method}'." });
                break;
        }
    }
    catch (Exception ex)
    {
        Send(new JsonObject { ["id"] = id, ["error"] = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message });
    }
}

return 0;

static JsonObject Describe(Assembly assembly)
{
    var actions = new JsonArray();
    foreach (var type in assembly.GetTypes())
    {
        var attribute = type.GetCustomAttribute<ActionAttribute>();
        if (attribute is null || type.IsAbstract)
        {
            continue;
        }

        actions.Add(new JsonObject
        {
            ["type"] = attribute.Type,
            ["displayName"] = attribute.DisplayName,
            ["description"] = attribute.Description,
        });
    }

    return new JsonObject { ["actions"] = actions };
}

static async Task<string?> ExecuteActionAsync(
    Assembly assembly, string actionType, string configJson, string? id, Action<JsonObject> send)
{
    var type = Array.Find(
        assembly.GetTypes(), t => t.GetCustomAttribute<ActionAttribute>()?.Type == actionType)
        ?? throw new InvalidOperationException($"No action '{actionType}' in this plugin.");

    var contract = Array.Find(
        type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAction<,>))
        ?? throw new InvalidOperationException($"{type.FullName} does not implement IAction<,>.");

    var configType = contract.GenericTypeArguments[0];
    var config = JsonSerializer.Deserialize(configJson, configType, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException($"Invalid config for '{actionType}'.");

    var instance = Activator.CreateInstance(type)!; // spike: parameterless ctor
    var context = new ActionContext
    {
        Logger = new FrameLogger(send, id),
        Http = new HttpClient(),
    };

    var task = (Task)contract.GetMethod(nameof(IAction<object, object>.ExecuteAsync))!
        .Invoke(instance, [config, context, CancellationToken.None])!;
    await task.ConfigureAwait(false);

    var result = task.GetType().GetProperty("Result")!.GetValue(task);
    return result is null ? null : JsonSerializer.Serialize(result, result.GetType(), JsonSerializerOptions.Web);
}

// Forwards plugin logs to the host as `log` frames — validates the callback channel.
internal sealed class FrameLogger(Action<JsonObject> send, string? callId) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        send(new JsonObject
        {
            ["method"] = "log",
            ["params"] = new JsonObject
            {
                ["callId"] = callId,
                ["level"] = logLevel.ToString(),
                ["message"] = formatter(state, exception),
            },
        });
}
