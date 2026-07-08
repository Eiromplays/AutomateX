using System.Net;
using System.Net.Sockets;

namespace AutomateX.Modules.ApiKeys;

// A per-key client-IP allowlist: bare IPs and/or CIDRs. Empty (the default) means unrestricted. Parse
// builds the matcher from already-validated stored entries (defensively skipping any bad ones); FindInvalid
// surfaces malformed entries so mint can reject them — for a restricting control, silently dropping a
// typo'd CIDR could leave the list empty and the key open.
public readonly struct ClientIpAllowlist
{
    private readonly IPNetwork[]? _networks;

    private ClientIpAllowlist(IPNetwork[] networks) => _networks = networks;

    public bool IsEmpty => _networks is null || _networks.Length == 0;

    public static ClientIpAllowlist Parse(IReadOnlyList<string> entries)
    {
        List<IPNetwork> networks = [];
        foreach (var entry in entries)
        {
            if (TryParseEntry(entry, out var network))
            {
                networks.Add(network);
            }
        }

        return new ClientIpAllowlist([.. networks]);
    }

    public static IReadOnlyList<string> FindInvalid(IReadOnlyList<string> entries) =>
        [.. entries.Where(entry => !TryParseEntry(entry, out _))];

    public bool Allows(IPAddress address)
    {
        if (_networks is null || _networks.Length == 0)
        {
            return true;
        }

        // A dual-stack Kestrel sees an IPv4 client as ::ffff:a.b.c.d; match it against IPv4 rules.
        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return Array.Exists(_networks, network => network.Contains(candidate));
    }

    private static bool TryParseEntry(string entry, out IPNetwork network)
    {
        entry = entry.Trim();
        if (entry.Contains('/'))
        {
            return IPNetwork.TryParse(entry, out network);
        }

        if (IPAddress.TryParse(entry, out var address))
        {
            network = new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);
            return true;
        }

        network = default;
        return false;
    }
}
