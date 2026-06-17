using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AutomateX.Engine.Actions;

// Opt-in SSRF guard for http.request (Engine:BlockPrivateNetworkRequests). Resolves the target
// host and blocks if any address is loopback / private (RFC1918, ULA) / link-local — which covers
// the cloud metadata endpoint 169.254.169.254. Off by default so internal targets (a local LLM,
// LAN services) keep working. Pre-connect resolution: a determined DNS-rebinding attacker could
// still bind late; a SocketsHttpHandler ConnectCallback would close that (a later hardening).
public static class SsrfGuard
{
    public static async Task GuardAsync(string url, CancellationToken cancellationToken)
    {
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

        if (Array.Exists(addresses, IsBlockedAddress))
        {
            throw new InvalidOperationException(
                $"http.request: '{uri.Host}' resolves to a private/loopback/link-local address, blocked by "
                + "Engine:BlockPrivateNetworkRequests.");
        }
    }

    // A SocketsHttpHandler.ConnectCallback that resolves the host and only connects to a non-blocked
    // address — the rebinding-proof check, since it gates the IP the socket actually dials (not a
    // pre-resolved one a late DNS flip could bypass). Wire it onto the action/trigger HTTP clients
    // when Engine:BlockPrivateNetworkRequests is on.
    public static async ValueTask<Stream> FilteringConnectCallback(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var resolved = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var allowed = Array.FindAll(resolved, address => !IsBlockedAddress(address));
        if (allowed.Length == 0)
        {
            throw new IOException(
                $"http: '{context.DnsEndPoint.Host}' resolves only to private/loopback/link-local addresses, "
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
    }

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
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254),
            AddressFamily.InterNetworkV6 =>
                address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC,
            _ => false,
        };
    }
}
