using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Connections;

// Connection types come from host sources + GLOBAL plugins (same rule as triggers/
// event listeners — workspace plugins contribute actions only). Rebuild() swaps the
// snapshot on plugin reload, so installing a plugin teaches the UI its connection shape.
// With OutOfProcPlugins on, plugin connection types are discovered by describing the host
// (no in-host instance) and BuildOAuthConfig/Test route to the supervisor.
public sealed class ConnectionTypeRegistry
{
    private readonly IReadOnlyList<IConnectionTypeSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly PluginProcessSupervisor _supervisor;
    private readonly ILogger<ConnectionTypeRegistry> _logger;
    private volatile FrozenDictionary<string, ConnectionTypeDescriptor> _descriptors;
    private volatile FrozenDictionary<string, IConnectionType> _instances;
    private volatile FrozenDictionary<string, string> _outOfProcDll = FrozenDictionary<string, string>.Empty;

    public ConnectionTypeRegistry(
        IEnumerable<IConnectionTypeSource> sources,
        PluginAssemblies plugins,
        PluginProcessSupervisor supervisor,
        ILogger<ConnectionTypeRegistry> logger)
    {
        _sources = [.. sources];
        _plugins = plugins;
        _supervisor = supervisor;
        _logger = logger;
        (_descriptors, _instances) = Build();
    }

    public void Rebuild() => (_descriptors, _instances) = Build();

    public IReadOnlyCollection<ConnectionTypeDescriptor> Descriptors => _descriptors.Values;

    // The live type instance for a provider key — in-proc only. Null for out-of-proc plugin types;
    // callers route credential work through the methods below instead.
    public IConnectionType? GetInstance(string typeKey) => _instances.GetValueOrDefault(typeKey);

    public bool IsOAuth(string typeKey) => _descriptors.GetValueOrDefault(typeKey)?.IsOAuth ?? false;

    public bool HasTester(string typeKey) => _descriptors.GetValueOrDefault(typeKey)?.IsTester ?? false;

    // The plugin dll backing an out-of-proc connection type, or null for in-proc/host types.
    public string? OutOfProcPluginDll(string typeKey) => _outOfProcDll.GetValueOrDefault(typeKey);

    // Build the OAuth parameters for a connection type, in-proc or via its host process. Null if the
    // type is unknown or not OAuth.
    public async Task<OAuthConfig?> BuildOAuthConfigAsync(
        string typeKey, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        if (_instances.GetValueOrDefault(typeKey) is IOAuthConnectionType oauth)
        {
            return oauth.BuildOAuthConfig(values);
        }

        if (_outOfProcDll.GetValueOrDefault(typeKey) is { } dll)
        {
            var result = await _supervisor.BuildOAuthConfigAsync(dll, typeKey, ToJsonObject(values), cancellationToken);
            return result.Deserialize<OAuthConfig>(JsonSerializerOptions.Web);
        }

        return null;
    }

    // Run a connection type's credential test, in-proc or via its host process. Null if the type is
    // unknown or has no tester.
    public async Task<ConnectionTestResult?> TestAsync(
        string typeKey, IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken = default)
    {
        if (_instances.GetValueOrDefault(typeKey) is IConnectionTester tester)
        {
            return await tester.TestAsync(values, http, cancellationToken);
        }

        if (_outOfProcDll.GetValueOrDefault(typeKey) is { } dll)
        {
            var result = await _supervisor.TestConnectionAsync(dll, typeKey, ToJsonObject(values), cancellationToken);
            return new ConnectionTestResult((bool?)result["ok"] ?? false, (string?)result["message"] ?? "");
        }

        return null;
    }

    private (FrozenDictionary<string, ConnectionTypeDescriptor> Descriptors, FrozenDictionary<string, IConnectionType> Instances) Build()
    {
        Dictionary<string, ConnectionTypeDescriptor> descriptors = [];
        Dictionary<string, IConnectionType> instances = [];

        foreach (var registered in _sources.SelectMany(x => x.GetConnectionTypes()))
        {
            Add(registered);
        }

        // Plugins run out-of-process: discover connection types by describing each host, never loading
        // it in-host. Built-in types (from sources above) keep their in-host instance.
        Dictionary<string, string> outOfProc = [];
        foreach (var path in _plugins.EnumeratePaths().Where(p => p.WorkspaceId is null))
        {
            foreach (var descriptor in DescribeOutOfProc(path))
            {
                descriptors[descriptor.Type] = descriptor;
                outOfProc[descriptor.Type] = path.DllPath;
                _logger.LogInformation("Registered connection type {Type} from plugin {Plugin} (out-of-proc)", descriptor.Type, path.Name);
            }
        }

        _outOfProcDll = outOfProc.ToFrozenDictionary();
        return (descriptors.ToFrozenDictionary(), instances.ToFrozenDictionary());

        void Add(RegisteredConnectionType registered)
        {
            descriptors[registered.Descriptor.Type] = registered.Descriptor;
            instances[registered.Descriptor.Type] = registered.Instance;
        }
    }

    // Describe a plugin host's connection types (blocking at startup/reload). No instance is created —
    // OAuth/test route to the supervisor.
    private IEnumerable<ConnectionTypeDescriptor> DescribeOutOfProc(PluginPath path)
    {
        JsonArray connectionTypes;
        try
        {
            var described = _supervisor.DescribeAsync(path.DllPath).GetAwaiter().GetResult();
            connectionTypes = (JsonArray?)described["result"]?["connectionTypes"] ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to describe out-of-proc plugin {Plugin}", path.Name);
            yield break;
        }

        foreach (var connection in connectionTypes)
        {
            var type = (string)connection!["type"]!;
            List<ConnectionField> fields = [];
            foreach (var field in (JsonArray?)connection["fields"] ?? [])
            {
                fields.Add(new ConnectionField(
                    (string)field!["key"]!,
                    (string?)field["label"] ?? "",
                    (bool?)field["secret"] ?? true,
                    (bool?)field["required"] ?? true,
                    (string?)field["helpText"],
                    (string?)field["docsUrl"]));
            }

            yield return new ConnectionTypeDescriptor(
                type,
                (string?)connection["displayName"] ?? type,
                (string?)connection["description"],
                $"plugin:{path.Name}",
                fields,
                IsOAuth: (bool?)connection["isOAuth"] ?? false,
                IsTester: (bool?)connection["isTester"] ?? false);
        }
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values)
        {
            obj[key] = value;
        }

        return obj;
    }
}

public sealed class BuiltInConnectionTypeSource(IServiceProvider serviceProvider) : IConnectionTypeSource
{
    public IEnumerable<RegisteredConnectionType> GetConnectionTypes() =>
        ConnectionTypeDiscovery.FromAssembly(typeof(BuiltInConnectionTypeSource).Assembly, "builtin", serviceProvider);
}
