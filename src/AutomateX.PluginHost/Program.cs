using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Protocol;
using AutomateX.Plugin.Sdk;
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

            case "connection.buildOAuthConfig":
                var oauthParams = request["params"]!;
                Send(new JsonObject
                {
                    ["id"] = id,
                    ["result"] = BuildOAuthConfig(assembly, (string)oauthParams["type"]!, (JsonObject)oauthParams["values"]!),
                });
                break;

            case "connection.test":
                var testParams = request["params"]!;
                Send(new JsonObject
                {
                    ["id"] = id,
                    ["result"] = await TestConnectionAsync(assembly, (string)testParams["type"]!, (JsonObject)testParams["values"]!),
                });
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
    var triggers = new JsonArray();
    var connectionTypes = new JsonArray();

    foreach (var type in assembly.GetTypes())
    {
        if (type.IsAbstract)
        {
            continue;
        }

        if (type.GetCustomAttribute<ActionAttribute>() is { } action)
        {
            var contract = ContractOf(type, typeof(IAction<,>));
            actions.Add(new JsonObject
            {
                ["type"] = action.Type,
                ["displayName"] = action.DisplayName,
                ["description"] = action.Description,
                ["configSchema"] = SchemaExport.ForType(contract.GenericTypeArguments[0]),
                ["resultSchema"] = SchemaExport.ForType(contract.GenericTypeArguments[1]),
            });
        }
        else if (type.GetCustomAttribute<TriggerAttribute>() is { } trigger)
        {
            var contract = ContractOf(type, typeof(ITriggerListener<>));
            triggers.Add(new JsonObject
            {
                ["type"] = trigger.Type,
                ["displayName"] = trigger.DisplayName,
                ["description"] = trigger.Description,
                ["configSchema"] = SchemaExport.ForType(contract.GenericTypeArguments[0]),
            });
        }
        else if (type.GetCustomAttribute<ConnectionTypeAttribute>() is { } connection)
        {
            var instance = (IConnectionType)Activator.CreateInstance(type)!;
            var fields = new JsonArray();
            foreach (var field in instance.Fields)
            {
                fields.Add(new JsonObject
                {
                    ["key"] = field.Key,
                    ["label"] = field.Label,
                    ["secret"] = field.Secret,
                    ["required"] = field.Required,
                    ["helpText"] = field.HelpText,
                    ["docsUrl"] = field.DocsUrl,
                });
            }

            connectionTypes.Add(new JsonObject
            {
                ["type"] = connection.Type,
                ["displayName"] = connection.DisplayName,
                ["description"] = connection.Description,
                ["isOAuth"] = instance is IOAuthConnectionType,
                ["isTester"] = instance is IConnectionTester,
                ["fields"] = fields,
            });
        }
    }

    return new JsonObject
    {
        ["actions"] = actions,
        ["triggers"] = triggers,
        ["connectionTypes"] = connectionTypes,
    };
}

static Type ContractOf(Type type, Type openInterface) =>
    Array.Find(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface)
        ?? throw new InvalidOperationException($"{type.FullName} does not implement {openInterface.Name}.");

static JsonObject BuildOAuthConfig(Assembly assembly, string connectionType, JsonObject values)
{
    var instance = FindConnectionType(assembly, connectionType);
    if (instance is not IOAuthConnectionType oauth)
    {
        throw new InvalidOperationException($"Connection type '{connectionType}' is not OAuth.");
    }

    var config = oauth.BuildOAuthConfig(ToDictionary(values));
    return JsonSerializer.SerializeToNode(config, JsonSerializerOptions.Web)!.AsObject();
}

static async Task<JsonObject> TestConnectionAsync(Assembly assembly, string connectionType, JsonObject values)
{
    var instance = FindConnectionType(assembly, connectionType);
    if (instance is not IConnectionTester tester)
    {
        throw new InvalidOperationException($"Connection type '{connectionType}' has no tester.");
    }

    using var http = new HttpClient();
    var result = await tester.TestAsync(ToDictionary(values), http, CancellationToken.None);
    return new JsonObject { ["ok"] = result.Ok, ["message"] = result.Message };
}

static IConnectionType FindConnectionType(Assembly assembly, string connectionType)
{
    var type = Array.Find(
        assembly.GetTypes(), t => t.GetCustomAttribute<ConnectionTypeAttribute>()?.Type == connectionType)
        ?? throw new InvalidOperationException($"No connection type '{connectionType}' in this plugin.");
    return (IConnectionType)Activator.CreateInstance(type)!;
}

static Dictionary<string, string> ToDictionary(JsonObject values) =>
    values.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => (string)p.Value!);

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
