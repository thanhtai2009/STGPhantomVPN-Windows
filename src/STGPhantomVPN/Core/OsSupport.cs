using System;

namespace STGPhantomVPN.Core
{
    /// <summary>Real Windows version as reported by RtlGetVersion (not affected by compatibility shims).</summary>
    public struct WindowsVersion
    {
        public int Major { get; }
        public int Minor { get; }
        public int Build { get; }
        public int ServicePackMajor { get; }

        public WindowsVersion(int major, int minor, int build, int servicePackMajor = 0)
        {
            Major = major;
            Minor = minor;
            Build = build;
            ServicePackMajor = servicePackMajor;
        }

        public bool IsKnown => Major > 0 && Build > 0;

        public override string ToString() => Major + "." + Minor + "." + Build + (ServicePackMajor > 0 ? " SP" + ServicePackMajor : "");
    }

    public enum WindowsFamily { Unknown, Older, Windows7, Windows8, Windows81, Windows10OrLater }

    /// <summary>Feature gating by Windows build. Builds are used because major/minor are 10.0 for both Windows 10 and 11.</summary>
    public static class OsSupport
    {
        public const int Windows7Sp1Build = 7601;
        public const int Windows8Build = 9200;
        public const int Windows81Build = 9600;
        /// <summary>Windows 10 RTM (1507).</summary>
        public const int Windows10Build = 10240;
        /// <summary>Windows 10 2004 (20H1): first build supported by the ovpn-dco-win driver.</summary>
        public const int Windows10_2004Build = 19041;

        public static WindowsFamily Family(WindowsVersion v)
        {
            if (!v.IsKnown) return WindowsFamily.Unknown;
            if (v.Build >= Windows10Build) return WindowsFamily.Windows10OrLater;
            if (v.Build >= Windows81Build) return WindowsFamily.Windows81;
            if (v.Build >= Windows8Build) return WindowsFamily.Windows8;
            if (v.Build >= 7600) return WindowsFamily.Windows7;
            return WindowsFamily.Older;
        }

        /// <summary>
        /// An unreadable version is treated as Windows 10+: .NET Framework 4.8 only runs on Windows 7 SP1 or
        /// newer, and RtlGetVersion exists on all of them, so this only happens in tests or broken setups.
        /// </summary>
        public static bool IsWindows10OrLater(WindowsVersion v) => !v.IsKnown || v.Build >= Windows10Build;

        /// <summary>ovpn-dco-win is only offered on Windows 10 2004+; on older builds the MSI feature can fail to install.</summary>
        public static bool SupportsOvpnDco(WindowsVersion v) => !v.IsKnown || v.Build >= Windows10_2004Build;

        /// <summary>
        /// Windows 10+ negotiates TLS 1.2/1.3 through SystemDefault. On Windows 7 / 8.x .NET 4.8 may fall
        /// back to SSL3/TLS 1.0 unless TLS 1.2 is requested explicitly.
        /// </summary>
        public static bool NeedsExplicitTls12(WindowsVersion v) => !IsWindows10OrLater(v);

        /// <summary>Short, language-neutral label for logs and the About section.</summary>
        public static string DisplayName(WindowsVersion v)
        {
            switch (Family(v))
            {
                case WindowsFamily.Windows10OrLater: return (v.Build >= 22000 ? "Windows 11" : "Windows 10") + " (build " + v.Build + ")";
                case WindowsFamily.Windows81: return "Windows 8.1 (build " + v.Build + ")";
                case WindowsFamily.Windows8: return "Windows 8 (build " + v.Build + ")";
                case WindowsFamily.Windows7: return "Windows 7" + (v.ServicePackMajor > 0 ? " SP" + v.ServicePackMajor : "") + " (build " + v.Build + ")";
                case WindowsFamily.Older: return "Windows " + v.Major + "." + v.Minor + " (build " + v.Build + ")";
                default: return "Windows";
            }
        }
    }
}
