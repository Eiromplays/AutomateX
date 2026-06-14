using System.Reflection;
using FastEndpoints;

namespace AutomateX.Web;

// Reports the running build's version (baked at publish via -p:Version). Anonymous + exempt from
// the auth gate so the UI can show it even on the login screen. Build metadata (+gitsha) is dropped.
public static class GetVersion
{
    private static readonly string Version =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0")
        .Split('+')[0];

    public sealed class Endpoint : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("version");
            AllowAnonymous();
        }

        public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(new Response(GetVersion.Version), ct);
    }

    public sealed record Response(string Version);
}
