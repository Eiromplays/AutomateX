# AutomateX Plugin SDK

The author-facing SDK for building [AutomateX](https://github.com/Eiromplays/AutomateX) plugins — actions,
triggers, and connection types that run out-of-process under the engine's plugin host.

## Install

```xml
<PackageReference Include="AutomateX.Plugin.Sdk" Version="1.0.0">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

`ExcludeAssets=runtime` is important: the **host provides the SDK assembly at runtime**, so a plugin must not
ship its own copy (the same reason first-party plugins reference it with `<Private>false</Private>`). Package
your plugin as the flat `dotnet publish` output zipped as `<PluginName>.zip` with `<PluginName>.dll` at the
root, plus a `plugin.json` manifest.

## Minimal action

```csharp
using AutomateX.Plugin.Sdk;

public sealed record GreetConfig(string Name);
public sealed record GreetResult(string Message);

[Action("example.greet", "Greet", Description = "Returns a greeting.")]
public sealed class GreetAction : IAction<GreetConfig, GreetResult>
{
    public Task<GreetResult> ExecuteAsync(GreetConfig config, ActionContext context, CancellationToken ct = default) =>
        Task.FromResult(new GreetResult($"Hello, {config.Name}!"));
}
```

## Manifest (`plugin.json`)

Ship a `plugin.json` at the package root so the plugin is browsable in the catalog before install:

```json
{
  "schemaVersion": 1,
  "id": "example.greeter",
  "name": "Example.Greeter",
  "displayName": "Greeter",
  "version": "1.0.0",
  "description": "A tiny example plugin.",
  "author": "you",
  "license": "MIT",
  "minAutomateXVersion": "5.0.0",
  "capabilities": { "actions": ["example.greet"], "triggers": [], "connectionTypes": [] },
  "permissions": { "egress": [] }
}
```

`version` is the plugin's own semver — bump it only when the plugin changes. See the AutomateX repo for
publishing a catalog and hosting your own plugin source.
