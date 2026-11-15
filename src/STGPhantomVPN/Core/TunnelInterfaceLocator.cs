using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace STGPhantomVPN.Core
{
    /// <summary>Resolves OpenVPN's assigned tunnel address to its Windows interface LUID for WFP filtering.</summary>
    public static class TunnelInterfaceLocator
    {
        public static ulong FindLuid(string localAddress)
        {
            if (!IPAddress.TryParse(localAddress ?? "", out var target) || target.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (!nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(target))) continue;
                    var index = nic.GetIPProperties().GetIPv4Properties()?.Index ?? 0;
                    if (index > 0 && ConvertInterfaceIndexToLuid((uint)index, out var luid) == 0) return luid;
                }
                catch (NetworkInformationException) { }
                catch (InvalidOperationException) { }
            }
            return 0;
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        static extern uint ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);
    }
}
