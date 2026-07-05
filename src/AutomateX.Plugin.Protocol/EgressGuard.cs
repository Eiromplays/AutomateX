using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AutomateX.Plugin.Protocol;

// The shared SSRF/egress guard, used by both the in-proc engine actions and the out-of-proc plugin
// host so HTTP behaviour is identical on both sides of the boundary. When Engine:BlockPrivateNetworkRequests
// is on it resolves the target host and blocks if any address is loopback / private (RFC1918, ULA) /
// link-local — which covers the cloud metadata endpoint 169.254.169.254. Off by default so internal
// targets (a local LLM, LAN services) keep working. Pre-connect resolution: a determined DNS-rebinding
// attacker could still bind late; the ConnectCallback below closes that by gating the IP actually dialed.
public static class EgressGuard
{
    // Pre-flight check for the in-proc actions. The policy widens the base block rule: a target is
    // rejected only if it's private *and* the policy doesn't allow it. An empty allowlist reproduces
    // v4.7 exactly, and a guard-off policy is a no-op.
    public static async Task GuardAsync(string url, EgressPolicy policy, CancellationToken cancellationToken)
    {
        if (!policy.BlockPrivate)
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"http.request: invalid URL '{url}'.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                throw new InvalidOperationException($"http.request: could not resolve host '{uri.Host}'.");
            }
        }

        if (Array.Exists(addresses, address => !policy.IsConnectionAllowed(uri.Host, address)))
        {
            throw new InvalidOperationException(
                $"http.request: '{uri.Host}' resolves to a private/loopback/link-local address, blocked by "
                + "Engine:BlockPrivateNetworkRequests.");
        }
    }

    // Builds a SocketsHttpHandler.ConnectCallback that closes over the policy and only connects to a
    // permitted address — the rebinding-proof check, since it gates the IP the socket actually dials
    // (not a pre-resolved one a late DNS flip could bypass). Wire it onto the action/trigger HTTP
    // clients when Engine:BlockPrivateNetworkRequests is on. Empty allowlist = the v4.7 filter.
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> MakeConnectCallback(
        EgressPolicy policy) => async (context, cancellationToken) =>
    {
        var host = context.DnsEndPoint.Host;
        var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = Array.FindAll(resolved, address => policy.IsConnectionAllowed(host, address));
        if (allowed.Length == 0)
        {
            throw new IOException(
                $"http: '{host}' resolves only to private/loopback/link-local addresses, "
                + "blocked by Engine:BlockPrivateNetworkRequests.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    };

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedAddress(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        // Note: 100.64.0.0/10 (CGNAT) is deliberately NOT blocked — Tailscale node IPs live there,
        // and reaching Tailscale targets is a normal, intended use of this self-hosted tool.
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 0 // 0.0.0.0/8 "this network"
                || bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254),
            AddressFamily.InterNetworkV6 =>
                address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC,
            _ => false,
        };
    }
}
