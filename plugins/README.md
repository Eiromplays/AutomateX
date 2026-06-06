# Plugins

Drop plugins here as `<PluginName>/<PluginName>.dll` (published with `EnableDynamicLoading`).
`docker-compose.yml` mounts this folder into the api container at `/app/plugins`.

Publish the sample here:

```bash
dotnet publish samples/AutomateX.SamplePlugin -o plugins/AutomateX.SamplePlugin
```
