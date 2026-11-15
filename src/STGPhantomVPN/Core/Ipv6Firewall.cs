using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace STGPhantomVPN.Core
{
    /// <summary>One WFP filter of the IPv6 leak block: permit a remote prefix, or block everything (Prefix null).</summary>
    public sealed class Ipv6FilterSpec
    {
        public Guid Layer;
        public string LayerName;
        public bool Permit;
        /// <summary>Weight inside the app's sublayer (0-15); the highest matching weight decides.</summary>
        public byte Weight;
        public string Prefix;

        public override string ToString() => LayerName + " " + (Permit ? "PERMIT " + Prefix : "BLOCK all") + " w" + Weight;
    }

    /// <summary>A typed WFP filter condition used by the system-wide kill switch.</summary>
    public sealed class WfpConditionSpec
    {
        public Guid Field;
        public uint ValueType;
        public byte[] Bytes;
        public ushort UInt16;
        public byte UInt8;
        public ulong UInt64;
    }

    /// <summary>A WFP outbound filter. Permit filters have higher weight than the final block-all filter.</summary>
    public sealed class KillSwitchFilterSpec
    {
        public Guid Layer;
        public string LayerName;
        public bool Permit;
        public byte Weight;
        public readonly List<WfpConditionSpec> Conditions = new List<WfpConditionSpec>();
        public override string ToString() => LayerName + " " + (Permit ? "PERMIT" : "BLOCK all");
    }

    /// <summary>
    /// IPv6 leak block through the Windows Filtering Platform, used for bridged (TAP) sessions.
    ///
    /// Why: in TAP mode OpenVPN installs the IPv6 block routes with the literal gateway fd53:5447:5056::1 as next
    /// hop (only TUN mode uses fe80::8, which the TAP-Windows6 driver answers itself). Nobody answers Neighbor
    /// Discovery for that gateway on a bridged VPNGate session, so the next hop stays unreachable, Windows keeps
    /// sending IPv6 through the physical ::/0 route and --block-ipv6 never sees those packets. WFP filters do not
    /// depend on routing or Neighbor Discovery.
    ///
    /// What: in a dynamic WFP session (filters vanish when the session handle closes, also when the elevated helper
    /// that owns it crashes or is killed) the app's own sublayer permits link-local (fe80::/10), multicast
    /// (ff00::/8: Neighbor Discovery, router advertisements, DHCPv6), loopback (::1), IPv4-mapped addresses of
    /// dual-stack sockets (::ffff:0:0/96) and, when "allow local LAN" is on, unique local addresses (fc00::/7);
    /// every other IPv6 flow and packet is blocked, outbound and inbound. IPv4 and the physical adapter's IPv6
    /// configuration are not touched.
    /// </summary>
    public static class Ipv6Firewall
    {
        // Filtering layers and condition (fwpmu.h).
        public static readonly Guid LayerAleAuthConnectV6 = new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
        public static readonly Guid LayerAleAuthRecvAcceptV6 = new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f");
        public static readonly Guid LayerOutboundTransportV6 = new Guid("e1735bde-013f-4655-b351-a49e15762df0");
        public static readonly Guid LayerOutboundTransportV4 = new Guid("09e61aea-d214-46e2-9b21-b26b0b2f28c8");
        public static readonly Guid LayerInboundTransportV6 = new Guid("634a869f-fc23-4b90-b0c1-bf620a36ae6f");
        public static readonly Guid ConditionIpRemoteAddress = new Guid("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
        public static readonly Guid ConditionIpLocalInterface = new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
        public static readonly Guid ConditionIpRemotePort = new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b");
        public static readonly Guid ConditionIpProtocol = new Guid("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");

        /// <summary>The app's own sublayer, created inside the dynamic session (so it disappears with it).</summary>
        public static readonly Guid SubLayerKey = new Guid("6f1b7d43-5c2e-4f0a-9d8e-53544750564e");
        public const ushort SubLayerWeight = 0x0F00;

        public const uint SessionFlagDynamic = 0x1;
        public const uint RpcCAuthnWinNt = 10;
        public const uint ActionBlock = 0x1001, ActionPermit = 0x1002;
        public const uint DataEmpty = 0, DataUint8 = 1, DataV6AddrMask = 0x101;
        public const uint MatchEqual = 0;
        public const string DisplayName = "STG Phantom VPN IPv6 leak block";

        public const string PrefixLinkLocal = "fe80::/10", PrefixMulticast = "ff00::/8", PrefixLoopback = "::1/128",
                            PrefixMapped = "::ffff:0:0/96", PrefixUniqueLocal = "fc00::/7";

        public static IReadOnlyList<string> PermittedPrefixes(bool allowLocalLan)
        {
            var p = new List<string> { PrefixLinkLocal, PrefixMulticast, PrefixLoopback, PrefixMapped };
            if (allowLocalLan) p.Add(PrefixUniqueLocal);
            return p;
        }

        static readonly (Guid Key, string Name)[] Layers =
        {
            (LayerAleAuthConnectV6, "ALE_AUTH_CONNECT_V6"),
            (LayerAleAuthRecvAcceptV6, "ALE_AUTH_RECV_ACCEPT_V6"),
            (LayerOutboundTransportV6, "OUTBOUND_TRANSPORT_V6"),
            (LayerInboundTransportV6, "INBOUND_TRANSPORT_V6"),
        };

        /// <summary>The filters to add: per layer, the permits (weight 15) and one block-all (weight 0).</summary>
        public static List<Ipv6FilterSpec> Plan(bool allowLocalLan)
        {
            var list = new List<Ipv6FilterSpec>();
            foreach (var (key, name) in Layers)
            {
                foreach (var prefix in PermittedPrefixes(allowLocalLan))
                    list.Add(new Ipv6FilterSpec { Layer = key, LayerName = name, Permit = true, Weight = 15, Prefix = prefix });
                list.Add(new Ipv6FilterSpec { Layer = key, LayerName = name, Permit = false, Weight = 0 });
            }
            return list;
        }

        /// <summary>
        /// Outbound system-wide kill switch. Only loopback, optional private LAN ranges, the VPN endpoint,
        /// and traffic leaving through the established VPN interface are permitted. The endpoint exception
        /// is limited to its exact address, port and transport protocol.
        /// </summary>
        public static List<KillSwitchFilterSpec> KillSwitchPlan(bool allowLocalLan, IPAddress endpoint, ushort port, byte protocol, ulong tunnelLuid)
        {
            if (endpoint == null || endpoint.AddressFamily != AddressFamily.InterNetwork || port == 0 || (protocol != 6 && protocol != 17))
                throw new ArgumentException("Kill switch requires a valid IPv4 VPN endpoint, port and TCP/UDP protocol");
            var list = new List<KillSwitchFilterSpec>();
            AddLayer(list, LayerOutboundTransportV4, "OUTBOUND_TRANSPORT_V4", false, allowLocalLan, endpoint, port, protocol, tunnelLuid);
            AddLayer(list, LayerOutboundTransportV6, "OUTBOUND_TRANSPORT_V6", true, allowLocalLan, endpoint, port, protocol, tunnelLuid);
            return list;
        }

        static void AddLayer(List<KillSwitchFilterSpec> list, Guid layer, string name, bool v6,
            bool allowLocalLan, IPAddress endpoint, ushort port, byte protocol, ulong tunnelLuid)
        {
            void PermitPrefix(string prefix)
            {
                var (address, length) = ParsePrefix(prefix);
                var raw = new byte[17];
                Buffer.BlockCopy(address, 0, raw, 0, 16);
                raw[16] = length;
                var f = NewFilter(layer, name, true, 15);
                f.Conditions.Add(new WfpConditionSpec { Field = ConditionIpRemoteAddress, ValueType = 0x101, Bytes = raw });
                list.Add(f);
            }
            void PermitV4(string address, byte length)
            {
                var f = NewFilter(layer, name, true, 15);
                f.Conditions.Add(new WfpConditionSpec { Field = ConditionIpRemoteAddress, ValueType = 0x100, Bytes = V4Mask(IPAddress.Parse(address), length) });
                list.Add(f);
            }

            if (v6)
            {
                foreach (var p in new[] { PrefixLoopback, PrefixLinkLocal, PrefixMulticast, PrefixMapped }) PermitPrefix(p);
                if (allowLocalLan) PermitPrefix(PrefixUniqueLocal);
            }
            else
            {
                PermitV4("127.0.0.0", 8);
                PermitV4("169.254.0.0", 16);
                if (allowLocalLan)
                {
                    PermitV4("10.0.0.0", 8);
                    PermitV4("172.16.0.0", 12);
                    PermitV4("192.168.0.0", 16);
                }
                // TAP bridged sessions need the virtual DHCP lease before OpenVPN reports CONNECTED.
                var dhcp = NewFilter(layer, name, true, 15);
                dhcp.Conditions.Add(new WfpConditionSpec { Field = ConditionIpRemotePort, ValueType = 2, UInt16 = 67 });
                dhcp.Conditions.Add(new WfpConditionSpec { Field = ConditionIpProtocol, ValueType = 1, UInt8 = 17 });
                list.Add(dhcp);
                var endpointFilter = NewFilter(layer, name, true, 15);
                endpointFilter.Conditions.Add(new WfpConditionSpec { Field = ConditionIpRemoteAddress, ValueType = 0x100, Bytes = V4Mask(endpoint, 32) });
                endpointFilter.Conditions.Add(new WfpConditionSpec { Field = ConditionIpRemotePort, ValueType = 2, UInt16 = port });
                endpointFilter.Conditions.Add(new WfpConditionSpec { Field = ConditionIpProtocol, ValueType = 1, UInt8 = protocol });
                list.Add(endpointFilter);
            }

            if (tunnelLuid != 0)
            {
                var tunnel = NewFilter(layer, name, true, 15);
                tunnel.Conditions.Add(new WfpConditionSpec { Field = ConditionIpLocalInterface, ValueType = 4, UInt64 = tunnelLuid });
                list.Add(tunnel);
            }
            list.Add(NewFilter(layer, name, false, 0));
        }

        static byte[] V4Mask(IPAddress ip, int length)
        {
            // FWP_V4_ADDR_AND_MASK fields are host-order UINT32 values. On Windows (little-endian),
            // their in-memory bytes are therefore the reverse of IPAddress.GetAddressBytes().
            var raw = new byte[8];
            var address = ip.GetAddressBytes();
            for (var i = 0; i < 4; i++) raw[i] = address[3 - i];
            for (var bit = 0; bit < length; bit++) raw[4 + (3 - bit / 8)] |= (byte)(0x80 >> (bit % 8));
            return raw;
        }

        static KillSwitchFilterSpec NewFilter(Guid layer, string name, bool permit, byte weight) =>
            new KillSwitchFilterSpec { Layer = layer, LayerName = name, Permit = permit, Weight = weight };

        /// <summary>Address bytes and prefix length of "addr/len" (IPv6 only); throws on anything else.</summary>
        public static (byte[] Address, byte Length) ParsePrefix(string prefix)
        {
            var parts = (prefix ?? "").Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6 ||
                !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var len) || len > 128)
                throw new FormatException("Not an IPv6 prefix: " + prefix);
            return (ip.GetAddressBytes(), len);
        }

        /// <summary>True when an IPv6 <paramref name="address"/> falls inside a permitted prefix (what the filters let through).</summary>
        public static bool IsPermitted(IPAddress address, bool allowLocalLan)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetworkV6) return true;
            var a = address.GetAddressBytes();
            foreach (var p in PermittedPrefixes(allowLocalLan))
            {
                var (net, len) = ParsePrefix(p);
                if (PrefixMatch(a, net, len)) return true;
            }
            return false;
        }

        static bool PrefixMatch(byte[] a, byte[] net, int len)
        {
            for (var i = 0; i < len; i++)
            {
                var mask = 0x80 >> (i % 8);
                if ((a[i / 8] & mask) != (net[i / 8] & mask)) return false;
            }
            return true;
        }

        /// <summary>Only bridged sessions need it; routed (TUN) sessions route via fe80::8, which the driver answers.</summary>
        public static bool NeededFor(TunnelLayer layer, bool blockIpv6) => blockIpv6 && layer == TunnelLayer.Bridged;

        // ---- Elevated helper protocol: "--elevated ipv6-guard <app pid> <32 hex token>" ----

        public const string HelperVerb = "ipv6-guard";
        static readonly Regex TokenPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled);

        public static bool IsValidToken(string token) => token != null && TokenPattern.IsMatch(token);

        public static bool TryParseHelperArgs(string[] args, out int pid, out string token)
        {
            pid = 0;
            token = null;
            if (args == null || args.Length != 4 || args[1] != HelperVerb) return false;
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out pid) || pid <= 0) return false;
            if (!IsValidToken(args[3])) { pid = 0; return false; }
            token = args[3];
            return true;
        }

        /// <summary>Signals between the app and the helper; created by the (unelevated) app in its session namespace.</summary>
        public enum Signal { On, OnLan, Off, Quit, Ok, Fail }

        public static string EventName(string token, Signal signal)
        {
            if (!IsValidToken(token)) throw new ArgumentException("bad token", nameof(token));
            return @"Local\STGPhantomVPN-v6-" + token + "-" + signal.ToString().ToLowerInvariant();
        }
    }

    /// <summary>
    /// C layout of the WFP structures for 32-bit and 64-bit processes (natural alignment, as in fwpmtypes.h), so
    /// they can be written into unmanaged memory field by field without relying on marshalling rules.
    /// </summary>
    public sealed class WfpLayout
    {
        public readonly int Ptr;
        /// <summary>FWP_VALUE0 / FWP_CONDITION_VALUE0: UINT32 type + 8-byte union.</summary>
        public const int ValueSize = 16, ValueUnion = 8;
        /// <summary>FWPM_ACTION0: UINT32 type + GUID.</summary>
        public const int ActionSize = 20;
        /// <summary>FWP_V6_ADDR_AND_MASK: 16 address bytes + prefix length.</summary>
        public const int V6AddrMaskSize = 17;
        /// <summary>FWPM_FILTER_CONDITION0: GUID fieldKey, UINT32 matchType, FWP_CONDITION_VALUE0 (8-aligned).</summary>
        public const int ConditionSize = 40, ConditionMatchType = 16, ConditionValue = 24;

        public readonly int DisplayDataSize, BlobSize;
        public readonly int SessionFlags, SessionSize;
        public readonly int SubLayerDisplay, SubLayerFlags, SubLayerWeight, SubLayerSize;
        public readonly int FilterDisplay, FilterFlags, FilterLayerKey, FilterSubLayerKey, FilterWeight, FilterNumConditions,
                            FilterConditions, FilterAction, FilterSize;

        public WfpLayout(int pointerSize)
        {
            if (pointerSize != 4 && pointerSize != 8) throw new ArgumentOutOfRangeException(nameof(pointerSize));
            Ptr = pointerSize;
            DisplayDataSize = 2 * Ptr;                 // FWPM_DISPLAY_DATA0: wchar_t* name, wchar_t* description
            BlobSize = Align(4, Ptr) + Ptr;            // FWP_BYTE_BLOB: UINT32 size, UINT8* data

            // FWPM_SESSION0
            var o = 16;                                // GUID sessionKey
            o += DisplayDataSize;
            SessionFlags = o; o += 4;                  // flags
            o += 4;                                    // txnWaitTimeoutInMSec
            o += 4;                                    // processId
            o = Align(o, Ptr) + Ptr;                   // SID* sid
            o += Ptr;                                  // wchar_t* username
            o += 4;                                    // BOOL kernelMode
            SessionSize = Align(o, Ptr);

            // FWPM_SUBLAYER0
            o = 16;                                    // subLayerKey
            SubLayerDisplay = o; o += DisplayDataSize;
            SubLayerFlags = o; o += 4;
            o = Align(o, Ptr) + Ptr;                   // GUID* providerKey
            o = Align(o, Ptr) + BlobSize;              // providerData
            SubLayerWeight = o; o += 2;                // UINT16 weight
            SubLayerSize = Align(o, Ptr);

            // FWPM_FILTER0
            o = 16;                                    // filterKey
            FilterDisplay = o; o += DisplayDataSize;
            FilterFlags = o; o += 4;
            o = Align(o, Ptr) + Ptr;                   // GUID* providerKey
            o = Align(o, Ptr) + BlobSize;              // providerData
            FilterLayerKey = o; o += 16;
            FilterSubLayerKey = o; o += 16;
            FilterWeight = o = Align(o, 8); o += ValueSize;
            FilterNumConditions = o; o += 4;
            FilterConditions = o = Align(o, Ptr); o += Ptr;
            FilterAction = o; o += ActionSize;
            o = Align(o, 8) + 16;                      // union { UINT64 rawContext; GUID providerContextKey; }
            o = Align(o, Ptr) + Ptr;                   // GUID* reserved
            o = Align(o, 8) + 8;                       // UINT64 filterId
            o = Align(o, 8) + ValueSize;               // effectiveWeight
            FilterSize = Align(o, 8);
        }

        static int Align(int offset, int alignment) => (offset + alignment - 1) / alignment * alignment;
    }
}
