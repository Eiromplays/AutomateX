# AutomateX v4.0.0

The isolation boundary: plugins now run out-of-process. This is the breaking release the v3.x arc was
building toward.

## Highlights

- **Out-of-process plugins.** Every plugin runs in its own `AutomateX.PluginHost` child process with
  its own `AssemblyLoadContext` and dependency closure, addressed over a stdio JSON protocol. A
  misbehaving plugin can't read engine memory, can't crash the host, and can't collide with another
  plugin's (or the engine's) dependency versions. The engine discovers a plugin's actions, triggers,
  and connection types by describing its host, and marshals execution, OAuth config, credential tests,
  and trigger listening across the boundary.
- **Bundled, zero-config host.** The API image carries `AutomateX.PluginHost` under `pluginhost/`;
  nothing to install. `Engine__PluginHostPath` overrides the location if you need it.
- **Hardened reload.** Installing or updating a plugin recycles its host process — new code always
  wins, old code stops running.

## Upgrade notes

- **No database migration.** Configuration and deployment *shape* only.
- **Out-of-proc is the only mode.** `Engine__OutOfProcPlugins` defaults on; the in-proc loader and its
  shadow-copy machinery are gone. No config change for existing deployments — your `./plugins` volume
  works as-is.
- **Deploy plugins with their dependencies.** Drop `plugins/<Name>/<Name>.dll` *and the dlls it
  depends on* in the same folder (a normal `dotnet publish`/`build` output, or the in-app catalog zip
  — both already include them). The host loads each plugin from its own folder.

## Migrating a plugin

Most plugins need **no changes** — actions, triggers, and connection types work as before. Two things
to check:

1. **Constructor.** The host instantiates your type with its longest constructor, filling optional
   parameters with their defaults. A parameterless or optional-only constructor is fine (e.g.
   `MyAction(IThing? thing = null)` as a test seam). A *required* constructor dependency is now
   rejected with a clear error — take services from `ActionContext` (`context.Logger`, `context.Http`)
   instead of constructor injection.
2. **Event listeners.** A plugin that implemented `IEngineEventListener` is no longer discovered — the
   out-of-proc protocol has no listener channel. Use a trigger instead (`execution.onFailure` covers
   the common "react to failures" case), or move the logic into the engine.

Everything else is identical: `IAction<,>`, `ITriggerListener<>`, `IConnectionType` (with
`IOAuthConnectionType` / `IConnectionTester`), JSON-Schema config/result types, and
`{{connections.<name>.<field>}}` templating.

See [docs/plugin-sandboxing-design.md](docs/plugin-sandboxing-design.md) for the architecture. Full
history: [CHANGELOG.md](CHANGELOG.md).

---

*That closes the v3.6 roadmap arc (audit → per-tenant DEKs → retention → sandboxing).*
