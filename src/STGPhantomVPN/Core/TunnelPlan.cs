using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace STGPhantomVPN.Core
{
    /// <summary>
    /// How the OpenVPN session is carried by the VPNGate (SoftEther) server.
    /// Routed is "dev tun": the server allocates the client address itself through its virtual DHCP and
    /// pushes it. Bridged is "dev tap": the server only bridges Ethernet frames and the TAP adapter gets its
    /// address from the VPNGate virtual DHCP server like the SoftEther VPN Client does. SoftEther reports any
    /// failure of the routed setup (hub refusal, DHCP allocation failure) as AUTH_FAILED, while the bridged
    /// session is accepted by the same servers.
    /// </summary>
    public enum TunnelLayer { Routed, Bridged }

    /// <summary>Where an attempt stopped, from the last OpenVPN management state.</summary>
    public enum ConnectStage { NotStarted, Network, Handshake, Adapter }

    public static class TunnelPlan
    {
        /// <summary>Seconds OpenVPN waits for the bridged TAP adapter to get its DHCP lease before adding routes.</summary>
        public const int BridgedDhcpWindowSeconds = 20;

        /// <summary>
        /// Extra OpenVPN options for the layer. Bridged mode overrides the profile's "dev tun", waits for the
        /// DHCP lease on the adapter, keeps the tunnel's own traffic on the physical gateway (the DHCP lease
        /// installs a default route through the tunnel) and lowers the adapter metric through
        /// --block-outside-dns so that default route wins. UDP sessions announce their exit so the server
        /// frees the session at once instead of holding it until its ping timeout.
        /// </summary>
        public static IReadOnlyList<string> Options(TunnelLayer layer, bool udp, string serverIp)
        {
            var o = new List<string>();
            if (layer == TunnelLayer.Bridged)
            {
                o.Add("--dev tap");
                o.Add("--route-delay 0 " + BridgedDhcpWindowSeconds.ToString(CultureInfo.InvariantCulture));
                if (IsIPv4(serverIp)) o.Add("--route " + serverIp + " 255.255.255.255 net_gateway");
                o.Add("--block-outside-dns");
            }
            if (udp) o.Add("--explicit-exit-notify 1");
            return o;
        }

        /// <summary>Client-side IPv6 subnet for the leak block; a unique local prefix that is never routed anywhere.</summary>
        public const string Ipv6BlockLocal = "fd53:5447:5056::2/64";
        public const string Ipv6BlockGateway = "fd53:5447:5056::1";

        /// <summary>Global unicast, unique local and the low ranges: what --redirect-gateway ipv6 covers, without touching IPv4.</summary>
        public static readonly string[] Ipv6BlockedRoutes = { "::/3", "2000::/4", "3000::/4", "fc00::/7" };

        /// <summary>
        /// VPNGate carries no IPv6, so without this a dual-stack PC keeps using its own IPv6 route next to the tunnel.
        /// The options give the adapter a private IPv6 address, route every public IPv6 range into the tunnel and let
        /// OpenVPN answer those packets with ICMPv6 "no route" (--block-ipv6), so nothing reaches the server or the
        /// physical network. The block lives and dies with the OpenVPN process: the Interactive Service removes the
        /// address and routes when openvpn.exe exits, also after a crash. Link-local and on-link LAN prefixes keep
        /// their more specific routes.
        /// </summary>
        /// <param name="firewallActive">
        /// True when the WFP block of <see cref="Ipv6Firewall"/> is in place (bridged sessions). The routes are then
        /// left out: in TAP mode their next hop fd53:5447:5056::1 never answers Neighbor Discovery, so they cannot
        /// stop anything and only delay the IPv4 fallback of applications. --block-ipv6 stays, so no IPv6 frame the
        /// adapter hands to OpenVPN reaches the VPNGate bridge.
        /// </param>
        public static IReadOnlyList<string> Ipv6BlockOptions(bool firewallActive = false)
        {
            if (firewallActive) return new[] { "--block-ipv6" };
            var o = new List<string> { "--ifconfig-ipv6 " + Ipv6BlockLocal + " " + Ipv6BlockGateway };
            foreach (var r in Ipv6BlockedRoutes) o.Add("--route-ipv6 " + r);
            o.Add("--block-ipv6");
            return o;
        }

        /// <summary>Public DNS overrides need the pushed ifconfig and gateway, which only routed sessions have.</summary>
        public static bool SupportsDnsOverride(TunnelLayer layer) => layer == TunnelLayer.Routed;

        /// <summary>Picks the server address used for the physical-gateway route: the profile's remote if it is an IPv4 literal, else the list IP.</summary>
        public static string ServerAddress(string remoteHost, string listIp) =>
            IsIPv4(remoteHost) ? remoteHost.Trim() : IsIPv4(listIp) ? listIp.Trim() : null;

        public static bool IsIPv4(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            if (t.Count(c => c == '.') != 3 || !t.All(c => c == '.' || c >= '0' && c <= '9')) return false;
            return IPAddress.TryParse(t, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork &&
                   ip.ToString() == t;
        }

        /// <summary>Maps an OpenVPN management state name (>STATE:...,NAME,...) to the connect stage it belongs to.</summary>
        public static ConnectStage StageOf(string state)
        {
            switch ((state ?? "").Trim().ToUpperInvariant())
            {
                case "": return ConnectStage.NotStarted;
                case "AUTH":
                case "GET_CONFIG":
                case "AUTH_PENDING":
                    return ConnectStage.Handshake;
                case "ASSIGN_IP":
                case "ADD_ROUTES":
                    return ConnectStage.Adapter;
                default:
                    return ConnectStage.Network;
            }
        }

        /// <summary>True for the server's refusal of the session ("AUTH: Received control message: AUTH_FAILED", also with a reason).</summary>
        public static bool IsServerRefusal(string logLine) =>
            !string.IsNullOrEmpty(logLine) && logLine.IndexOf("AUTH_FAILED", StringComparison.Ordinal) >= 0;
    }

    /// <summary>Per-attempt OpenVPN log files ("openvpn-yyyyMMdd-HHmmss-fff.log"); only the newest few are kept.</summary>
    public static class OpenVpnLogFiles
    {
        public const int Keep = 5;
        public const string Prefix = "openvpn-";
        /// <summary>Single log file written by 1.0.7 and earlier.</summary>
        public const string LegacyName = "openvpn.log";

        public static string NewName(DateTime localNow) =>
            Prefix + localNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".log";

        public static bool IsOpenVpnLog(string fileName) =>
            fileName != null && (fileName.Equals(LegacyName, StringComparison.OrdinalIgnoreCase) ||
                                 fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase));

        /// <summary>OpenVPN logs, newest first; the legacy file counts as the oldest.</summary>
        public static List<string> NewestFirst(IEnumerable<string> fileNames) =>
            fileNames.Where(IsOpenVpnLog)
                     .OrderBy(n => n.Equals(LegacyName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                     .ThenByDescending(n => n, StringComparer.OrdinalIgnoreCase)
                     .ToList();

        /// <summary>Files to delete before a new attempt so that, with the new log, at most <paramref name="keep"/> remain.</summary>
        public static List<string> ToPruneBeforeNew(IEnumerable<string> fileNames, int keep = Keep) =>
            NewestFirst(fileNames).Skip(Math.Max(0, keep - 1)).ToList();
    }

    /// <summary>Which files "Xóa nhật ký" may remove from the log folder.</summary>
    public static class LogCleanup
    {
        public const string AppLog = "app.log";
        public const string AppLogRotated = "app.log.1";

        /// <summary>Only the app log, its rotation and OpenVPN logs; never the msiexec log or anything else in the folder.</summary>
        public static bool IsClearable(string fileName) =>
            !string.IsNullOrEmpty(fileName) && fileName == System.IO.Path.GetFileName(fileName) &&
            (fileName.Equals(AppLog, StringComparison.OrdinalIgnoreCase) || fileName.Equals(AppLogRotated, StringComparison.OrdinalIgnoreCase) ||
             OpenVpnLogFiles.IsOpenVpnLog(fileName));

        /// <summary>
        /// Files to delete and the one to empty instead: the OpenVPN log of a running connection stays open in
        /// openvpn.exe, so it is truncated rather than deleted.
        /// </summary>
        public static (List<string> Delete, List<string> Truncate) Plan(IEnumerable<string> fileNames, string inUseName)
        {
            var delete = new List<string>();
            var truncate = new List<string>();
            foreach (var n in fileNames.Where(IsClearable).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(inUseName) && n.Equals(inUseName, StringComparison.OrdinalIgnoreCase)) truncate.Add(n);
                else delete.Add(n);
            }
            return (delete, truncate);
        }
    }
}
