using System;
using System.Globalization;

namespace STGPhantomVPN.Core
{
    public static class Formatters
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FormatDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return string.Format(Inv, "{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", Inv) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0", Inv) + " MB";
            return (mb / 1024.0).ToString("0.00", Inv) + " GB";
        }

        public static string FormatByteRate(long bytesPerSec)
        {
            if (bytesPerSec <= 0) return "0 B/s";
            if (bytesPerSec < 1024) return bytesPerSec + " B/s";
            double kb = bytesPerSec / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", Inv) + " KB/s";
            return (kb / 1024.0).ToString("0.00", Inv) + " MB/s";
        }

        /// <summary>VPNGate reports link speed in bits per second.</summary>
        public static string FormatLinkSpeed(long bps)
        {
            double mbps = bps / 1000000.0;
            return mbps >= 1 ? mbps.ToString("0.0", Inv) + " Mbps" : (bps / 1000.0).ToString("0", Inv) + " Kbps";
        }
    }
}
