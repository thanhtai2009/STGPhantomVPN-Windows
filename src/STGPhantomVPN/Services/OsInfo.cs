using System;
using System.Runtime.InteropServices;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// The real Windows version. Environment.OSVersion and GetVersionEx are shimmed to the newest OS in the
    /// manifest's supportedOS list, so ntdll!RtlGetVersion, which always reports the true version, is used.
    /// </summary>
    public static class OsInfo
    {
        static readonly Lazy<WindowsVersion> _current = new Lazy<WindowsVersion>(Read);

        public static WindowsVersion Current => _current.Value;

        static WindowsVersion Read()
        {
            try
            {
                var info = new RTL_OSVERSIONINFOEXW { dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEXW)) };
                if (RtlGetVersion(ref info) == 0)
                    return new WindowsVersion((int)info.dwMajorVersion, (int)info.dwMinorVersion, (int)info.dwBuildNumber, info.wServicePackMajor);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                Log.Warn("RtlGetVersion unavailable: " + ex.Message);
            }
            var v = Environment.OSVersion.Version;
            return new WindowsVersion(v.Major, v.Minor, v.Build);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RTL_OSVERSIONINFOEXW
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll")]
        static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEXW versionInfo);
    }
}
