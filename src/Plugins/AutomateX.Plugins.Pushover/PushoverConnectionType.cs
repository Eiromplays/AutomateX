using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Pushover;

[ConnectionType("pushover", "Pushover", Description = "Pushover application + user key for mobile push notifications.")]
public sealed class PushoverConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("appToken", "Application token",
            HelpText: "Register an application at pushover.net/apps/build to get its API token.",
            DocsUrl: "https://pushover.net/api"),
        new("userKey", "User or group key",
            HelpText: "Your user key from the Pushover dashboard (or a delivery group key)."),
    ];
}
