using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace STGPhantomVPN.Core
{
    /// <summary>A DNS choice for the tunnel. <see cref="Logo"/> is a pack resource path, empty for the generic icon.</summary>
    public sealed class DnsProvider
    {
        public DnsProvider(string id, string name, string primary, string secondary, string logo)
        {
            Id = id; Name = name; Primary = primary; Secondary = secondary; Logo = logo;
        }

        public string Id { get; }
        public string Name { get; }
        public string Primary { get; }
        public string Secondary { get; }
        public string Logo { get; }
        public string Addresses => string.IsNullOrEmpty(Secondary) ? Primary ?? "" : Primary + " · " + Secondary;
    }

    /// <summary>
    /// DNS used while connected: the VPN server's (what it pushes, the default), a well-known public resolver, or
    /// custom IPv4 addresses. A chosen resolver replaces the pushed DNS servers and Windows is kept from asking
    /// the DNS servers of other adapters, so lookups cannot leak outside the tunnel.
    /// </summary>
    public static class PublicDns
    {
        public const string ServerId = "", CustomId = "custom";

        public static readonly IReadOnlyList<DnsProvider> Providers = new[]
        {
            new DnsProvider("cloudflare", "Cloudflare", "1.1.1.1", "1.0.0.1", "Assets/Dns/cloudflare.png"),
            new DnsProvider("google", "Google Public DNS", "8.8.8.8", "8.8.4.4", "Assets/Dns/google.png"),
            new DnsProvider("quad9", "Quad9", "9.9.9.9", "149.112.112.112", "Assets/Dns/quad9.png"),
            new DnsProvider("opendns", "OpenDNS (Cisco)", "208.67.222.222", "208.67.220.220", "Assets/Dns/opendns.png"),
            new DnsProvider("adguard", "AdGuard DNS", "94.140.14.14", "94.140.15.15", "Assets/Dns/adguard.png"),
            new DnsProvider("cloudflare-family", "Cloudflare for Families", "1.1.1.3", "1.0.0.3", "Assets/Dns/cloudflare.png"),
        };

        public static DnsProvider Find(string id) =>
            Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Strict dotted-quad IPv4 (no leading zeros, no octal/hex/short forms) of a public resolver. Private,
        /// shared (100.64/10), loopback, link-local, multicast and reserved ranges are refused: the resolver is
        /// reached through the tunnel and DNS on the other adapters is blocked, so a LAN resolver could not answer.
        /// </summary>
        public static bool TryParseResolver(string text, out string normalized)
        {
            normalized = null;
            if (text == null) return false;
            var parts = text.Trim().Split('.');
            if (parts.Length != 4) return false;
            var b = new int[4];
            for (int i = 0; i < 4; i++)
            {
                var p = parts[i];
                if (p.Length == 0 || p.Length > 3 || (p.Length > 1 && p[0] == '0') || !p.All(c => c >= '0' && c <= '9')) return false;
                b[i] = int.Parse(p, CultureInfo.InvariantCulture);
                if (b[i] > 255) return false;
            }
            if (b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224) return false;
            if ((b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)) return false;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
            if ((b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2)) || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) ||
                (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113)) return false;
            normalized = string.Join(".", b);
            return true;
        }

        /// <summary>Resolvers for a choice; null means "use what the server pushes". Invalid custom input gives null too.</summary>
        public static string[] Resolvers(string id, string custom1, string custom2)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseResolver(custom1, out var a)) return null;
                if (string.IsNullOrWhiteSpace(custom2) || !TryParseResolver(custom2, out var b) || b == a) return new[] { a };
                return new[] { a, b };
            }
            var p = Find(id);
            return p == null ? null : new[] { p.Primary, p.Secondary }.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        /// <summary>
        /// OpenVPN command-line options for the resolvers: drop the pushed DNS servers (IPv4, IPv6 and the 2.6
        /// "dns" form), set ours on the TAP adapter, route them through the tunnel even if the server does not
        /// redirect the default gateway, and block DNS on other adapters. Empty for the server's DNS.
        /// </summary>
        public static IReadOnlyList<string> OpenVpnOptions(string[] resolvers)
        {
            if (resolvers == null || resolvers.Length == 0) return Array.Empty<string>();
            var o = new List<string>
            {
                "--pull-filter ignore \"dhcp-option DNS\"",
                "--pull-filter ignore \"dns server\"",
            };
            o.AddRange(resolvers.Select(r => "--dhcp-option DNS " + r));
            o.AddRange(resolvers.Select(r => "--route " + r + " 255.255.255.255 vpn_gateway"));
            o.Add("--block-outside-dns");
            return o;
        }
    }
}
