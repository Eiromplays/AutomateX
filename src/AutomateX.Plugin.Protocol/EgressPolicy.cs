using System.Net;

namespace AutomateX.Plugin.Protocol;

// A parsed egress policy: the BlockPrivateNetworkRequests flag plus an allowlist that widens the guard.
// The allowlist can only *permit* otherwise-blocked targets — it never blocks something the base rule
// allows — so an empty allowlist is exactly the v4.7 behaviour. Parsed once from raw strings; a malformed
// entry is dropped (surfaced via InvalidEntries) and never fails construction.
public sealed class EgressPolicy
{
    // Guard-off policy: allows everything. Handy default for callers that never enable the guard.
    public static EgressPolicy Off { get; } = new(blockPrivate: false, allowlist: null);

    private readonly HashSet<string> _hostnames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IPNetwork> _networks = [];

    public EgressPolicy(bool blockPrivate, IEnumerable<string>? allowlist)
    {
        BlockPrivate = blockPrivate;
        List<string> invalid = [];

        foreach (var raw in allowlist ?? [])
        {
            var entry = raw.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (entry.Contains('/', StringComparison.Ordinal))
            {
                if (IPNetwork.TryParse(entry, out var network))
                {
                    _networks.Add(network);
                }
                else
                {
                    invalid.Add(entry);
                }
            }
            else if (Uri.CheckHostName(entry) != UriHostNameType.Unknown)
            {
                _hostnames.Add(entry);
            }
            else
            {
                invalid.Add(entry);
            }
        }

        InvalidEntries = invalid;
    }

    public bool BlockPrivate { get; }

    // Entries that couldn't be parsed as a hostname or CIDR — dropped, not fatal. The engine/host logs
    // these once at startup so a typo is visible without taking the instance down.
    public IReadOnlyList<string> InvalidEntries { get; }

    // Widens-only. Permitted if the guard is off, the address isn't private in the first place, the host
    // is an allowlisted name (pre-resolution), or the dialed IP falls in an allowlisted CIDR. With an
    // empty allowlist this is exactly !EgressGuard.IsBlockedAddress.
    public bool IsConnectionAllowed(string host, IPAddress address)
    {
        if (!BlockPrivate || !EgressGuard.IsBlockedAddress(address))
        {
            return true;
        }

        if (_hostnames.Contains(host))
        {
            return true;
        }

        foreach (var network in _networks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }
}
