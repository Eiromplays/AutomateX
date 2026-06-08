using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Matrix;

[ConnectionType("matrix", "Matrix", Description = "A Matrix bot account for sending and receiving messages.")]
public sealed class MatrixConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("homeserverUrl", "Homeserver URL", Secret: false,
            HelpText: "e.g. https://matrix-client.matrix.org"),
        new("accessToken", "Access token",
            HelpText: "A bot account's access token — get one via the login API (see the recipe).",
            DocsUrl: "https://github.com/Eiromplays/AutomateX/blob/main/docs/recipes/jarvis-lite.md"),
    ];
}
