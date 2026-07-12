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
    private readonly HashSet<string> _allowOnlyHostnames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IPNetwork> _allowOnlyNetworks = [];

    // allowlist widens the SSRF block (permit otherwise-private targets); allowOnly is the opposite — a
    // restrictive allowlist (a per-plugin declared egress) that permits ONLY its entries and denies
    // everything else, public and private, regardless of BlockPrivate. Null/empty allowOnly = no
    // restriction, i.e. exactly the prior behaviour.
    public EgressPolicy(bool blockPrivate, IEnumerable<string>? allowlist, IEnumerable<string>? allowOnly = null)
    {
        BlockPrivate = blockPrivate;
        List<string> invalid = [];

        Parse(allowlist, _hostnames, _networks, invalid);
        Parse(allowOnly, _allowOnlyHostnames, _allowOnlyNetworks, invalid);

        InvalidEntries = invalid;
    }

    private static void Parse(
        IEnumerable<string>? entries, HashSet<string> hostnames, List<IPNetwork> networks, List<string> invalid)
    {
        foreach (var raw in entries ?? [])
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
                    networks.Add(network);
                }
                else
                {
                    invalid.Add(entry);
                }
            }
            else if (Uri.CheckHostName(entry) != UriHostNameType.Unknown)
            {
                hostnames.Add(entry);
            }
            else
            {
                invalid.Add(entry);
            }
        }
    }

    public bool BlockPrivate { get; }

    // True when a restrictive allowOnly list is in force — the host wires the guard whenever this holds,
    // even if BlockPrivate is off.
    public bool HasRestriction => _allowOnlyHostnames.Count > 0 || _allowOnlyNetworks.Count > 0;

    // Entries that couldn't be parsed as a hostname or CIDR — dropped, not fatal. The engine/host logs
    // these once at startup so a typo is visible without taking the instance down.
    public IReadOnlyList<string> InvalidEntries { get; }

    public bool IsConnectionAllowed(string host, IPAddress address)
    {
        // Restrictive mode: only the declared hosts/CIDRs are reachable — deny everything else outright.
        if (HasRestriction)
        {
            return _allowOnlyHostnames.Contains(host) || _allowOnlyNetworks.Exists(network => network.Contains(address));
        }

        // Widen mode: permitted if the guard is off, the address isn't private, the host is an allowlisted
        // name (pre-resolution), or the dialed IP falls in an allowlisted CIDR. Empty allowlist is exactly
        // !EgressGuard.IsBlockedAddress.
        if (!BlockPrivate || !EgressGuard.IsBlockedAddress(address))
        {
            return true;
        }

        return _hostnames.Contains(host) || _networks.Exists(network => network.Contains(address));
    }
}
