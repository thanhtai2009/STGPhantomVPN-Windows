using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Read-only TAP-Windows6 probe for a standard user. Adapters come from the network adapter class key
    /// (readable without elevation, the same key OpenVPN scans), liveness from NetworkInterface (which
    /// omits disabled adapters), and the staged driver from HKLM\SYSTEM\DriverDatabase\DeviceIds or,
    /// failing that, the OemVista.inf folders in the driver store.
    /// </summary>
    public sealed class WindowsTapSource : ITapSystemSource
    {
        static readonly Regex InstanceKey = new Regex(@"^\d{4}$", RegexOptions.Compiled);

        public IEnumerable<NetAdapterEntry> RegisteredAdapters()
        {
            var list = new List<NetAdapterEntry>();
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var cls = hklm.OpenSubKey(TapDetector.NetClassKey))
                {
                    if (cls == null) return list;
                    foreach (var name in cls.GetSubKeyNames().Where(n => InstanceKey.IsMatch(n)))
                    {
                        // Some instance keys (and the "Properties" key) deny read access to standard users.
                        try
                        {
                            using (var k = cls.OpenSubKey(name))
                            {
                                if (k == null) continue;
                                list.Add(new NetAdapterEntry
                                {
                                    ComponentId = k.GetValue("ComponentId") as string,
                                    NetCfgInstanceId = k.GetValue("NetCfgInstanceId") as string,
                                });
                            }
                        }
                        catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException) { }
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                Log.Warn("Reading the network adapter class key failed: " + ex.Message);
            }
            return list;
        }

        public IEnumerable<string> EnumeratedInterfaceIds()
        {
            try { return NetworkInterface.GetAllNetworkInterfaces().Select(n => n.Id).ToList(); }
            catch (NetworkInformationException ex)
            {
                Log.Warn("Enumerating network interfaces failed: " + ex.Message);
                return Enumerable.Empty<string>();
            }
        }

        public bool DriverPackagePresent() => DriverDatabaseHasTap() || DriverStoreHasTap();

        static bool DriverDatabaseHasTap()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (var id in new[] { TapDetector.HardwareId, "tap0901" })
                    {
                        using (var k = hklm.OpenSubKey(@"SYSTEM\DriverDatabase\DeviceIds\" + id))
                        {
                            if (k != null && k.GetValueNames().Any(v => v.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException) { }
            return false;
        }

        static bool DriverStoreHasTap()
        {
            try
            {
                var repo = Path.Combine(NativeSystemDir(), "DriverStore", "FileRepository");
                if (!Directory.Exists(repo)) return false;
                return Directory.EnumerateDirectories(repo)
                    .Where(d => TapDetector.IsTapDriverStoreFolder(Path.GetFileName(d)))
                    .Any(d => File.Exists(Path.Combine(d, "tap0901.sys")));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                return false;
            }
        }

        /// <summary>System32 of the native OS; a 32-bit process on 64-bit Windows reaches it through Sysnative.</summary>
        static string NativeSystemDir()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(windows, "Sysnative")
                : Path.Combine(windows, "System32");
        }
    }

    /// <summary>Checks for TAP-Windows6 and repairs it through the elevated helper (one UAC prompt per attempt).</summary>
    public static class TapDriverService
    {
        public static TapCheckResult Check()
        {
            var result = TapDetector.Evaluate(new WindowsTapSource());
            Log.Info("TAP-Windows6 check: " + result);
            return result;
        }

        public static Task<TapCheckResult> CheckAsync() => Task.Run(() => Check());

        /// <summary>Repairs and re-checks. Returns null when TAP-Windows6 is ready afterwards, otherwise a resource key.</summary>
        public static async Task<string> RepairAsync(IProgress<double> progress)
        {
            var before = await CheckAsync().ConfigureAwait(false);
            if (before.IsReady) return null;

            var tapctl = await Task.Run(() => ToolLocator.FindTapctl()).ConfigureAwait(false);
            var plan = TapDetector.ChooseRepair(before, tapctl != null);
            Log.Info("TAP-Windows6 repair plan: " + plan);

            if (plan == TapRepair.CreateAdapter)
            {
                var code = await Elevation.RunHelperAsync("tap-create").ConfigureAwait(false);
                Log.Info("tap-create helper exit code " + code);
                if (code == -1) return "install_uac_declined";
                var afterCreate = await RecheckAsync().ConfigureAwait(false);
                if (afterCreate.IsReady) return null;
                // The staged driver could not back a new adapter; reinstall it from the pinned MSI instead.
                plan = TapRepair.InstallMsi;
            }

            if (plan == TapRepair.InstallMsi)
            {
                var err = await InstallerService.InstallAsync(progress, System.Threading.CancellationToken.None).ConfigureAwait(false);
                if (err != null) return err;
            }

            var after = await RecheckAsync().ConfigureAwait(false);
            return after.IsReady ? null : "tap_still_missing";
        }

        /// <summary>A new adapter can take a moment to appear in the interface list, so a failed check is retried briefly.</summary>
        static async Task<TapCheckResult> RecheckAsync()
        {
            var result = await CheckAsync().ConfigureAwait(false);
            for (var i = 0; i < 3 && !result.IsReady; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
                result = await CheckAsync().ConfigureAwait(false);
            }
            return result;
        }
    }
}
