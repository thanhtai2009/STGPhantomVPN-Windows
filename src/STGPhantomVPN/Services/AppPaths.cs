using System;
using System.IO;

namespace STGPhantomVPN.Services
{
    public static class AppPaths
    {
        public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Per-user data: settings, server cache, logs, downloaded installers.</summary>
        public static string DataDir => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STGPhantomVPN"));

        /// <summary>Sanitized OpenVPN profile for the current session; wiped on every connect and disconnect.</summary>
        public static string RunDir => Ensure(Path.Combine(DataDir, "run"));
        public static string DownloadDir => Ensure(Path.Combine(DataDir, "downloads"));
        public static string LogDir => Ensure(Path.Combine(DataDir, "logs"));

        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string ServerCacheFile => Path.Combine(DataDir, "vpngate_servers_cache.csv");
        public static string BundledServerList => Path.Combine(AppDir, "Assets", "vpngate_fallback.csv");

        static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
