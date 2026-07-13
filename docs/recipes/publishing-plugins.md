# Recipe: authoring & publishing a plugin

How to write an AutomateX plugin, publish it in a signed catalog, and point an instance at it. First-party
plugins live in [AutomateX-Plugins](https://github.com/Eiromplays/AutomateX-Plugins); the same steps let
anyone host their own.

## 1. Write the plugin

Reference the SDK from NuGet — the host provides it at runtime, so exclude the runtime asset:

```xml
<PackageReference Include="AutomateX.Plugin.Sdk">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

Implement actions/triggers/connection types with the SDK attributes:

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

## 2. Add a `plugin.json`

At the project root, next to the source:

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

- `id` is a stable reverse-DNS identity (not the package name); it's how the catalog tracks the plugin.
- `version` is the plugin's **own** semver — bump it in the same change that alters the plugin.
- `permissions.egress` lists the hosts the plugin needs. `[]` = none, `["api.example.com"]` = only that host
  (enforced at runtime), `["*"]` = unrestricted (flagged to the operator at install). List specific hosts
  when you can.

## 3. Sign & publish a catalog

Generate an **unencrypted** minisign key (so CI needs no passphrase) and keep the secret safe:

```bash
minisign -G -W -p minisign.pub -s minisign.key
```

Package every plugin, build the `catalog.json`, and sign it (see the `scripts/` in AutomateX-Plugins):

```bash
MINISIGN_SECRET_KEY="$(cat minisign.key)" scripts/package-plugins.sh v1
minisign -S -s minisign.key -m out/catalog.json -x out/catalog.json.minisig
```

Publish `catalog.json`, `catalog.json.minisig`, and the plugin zips as release assets. Your catalog URL is
then e.g. `https://github.com/you/your-plugins/releases/latest/download/catalog.json`.

## 4. Point AutomateX at it

Set `Engine__AllowPluginUpload=true` to allow installs, then in **Plugins → Catalog sources → Add source**
(instance admin) enter the catalog URL and paste `minisign.pub`'s value as the trusted key. The source now
shows **Verified**; its plugins appear in the catalog with a version picker and (if declared) a permission
consent step on install.

Without a pinned key a source is **Untrusted** — its plugins still list, but installing one needs an instance
admin and an explicit acknowledgement.

## 5. Keep it current (optional)

Set `Engine__PluginAutoUpdateInterval` (e.g. `01:00:00`) with `AllowPluginUpload` on, then toggle
**Auto-update** on a plugin's detail card. The updater moves it to the latest compatible, **same-major**
version from a **verified** source — never a major bump, never an untrusted source.
