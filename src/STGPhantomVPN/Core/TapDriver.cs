using System;
using System.Collections.Generic;
using System.Linq;

namespace STGPhantomVPN.Core
{
    /// <summary>State of the TAP-Windows6 driver that OpenVPN needs for VPNGate (AES-128-CBC) profiles.</summary>
    public enum TapStatus
    {
        /// <summary>At least one TAP-Windows6 adapter is registered and enumerated by Windows.</summary>
        Ready,
        /// <summary>TAP-Windows6 adapters are registered, but none is enumerated (disabled or not present).</summary>
        AdapterInactive,
        /// <summary>The driver package is in the driver store, but no TAP-Windows6 adapter exists.</summary>
        DriverOnly,
        /// <summary>Neither an adapter nor the driver package was found.</summary>
        Missing,
    }

    /// <summary>One subkey of the network adapter class key (HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-...}).</summary>
    public sealed class NetAdapterEntry
    {
        public string ComponentId { get; set; }
        public string NetCfgInstanceId { get; set; }
    }

    /// <summary>Read-only view of the system state the detector needs; implemented over the registry and NetworkInterface on Windows.</summary>
    public interface ITapSystemSource
    {
        /// <summary>All adapters registered under the network adapter device class.</summary>
        IEnumerable<NetAdapterEntry> RegisteredAdapters();

        /// <summary>Interface GUIDs (NetCfgInstanceId form) of the adapters Windows currently enumerates.</summary>
        IEnumerable<string> EnumeratedInterfaceIds();

        /// <summary>True when a tap0901 driver package is staged in the driver store.</summary>
        bool DriverPackagePresent();
    }

    public sealed class TapCheckResult
    {
        public TapStatus Status { get; set; }
        public int RegisteredAdapters { get; set; }
        public int EnumeratedAdapters { get; set; }
        public bool DriverPackage { get; set; }

        public bool IsReady => Status == TapStatus.Ready;

        /// <summary>True when no TAP-Windows6 adapter is registered at all, so an OpenVPN connect cannot succeed.</summary>
        public bool BlocksConnect => Status == TapStatus.Missing || Status == TapStatus.DriverOnly;

        public override string ToString() =>
            Status + " (registered " + RegisteredAdapters + ", enumerated " + EnumeratedAdapters + ", driver package " + DriverPackage + ")";
    }

    /// <summary>How the app repairs a missing TAP-Windows6 adapter or driver.</summary>
    public enum TapRepair
    {
        None,
        /// <summary>tapctl.exe create --hwid root\tap0901 (driver already staged, only the adapter is missing).</summary>
        CreateAdapter,
        /// <summary>Pinned OpenVPN MSI with the Drivers.TAPWindows6 feature (install, add feature or upgrade).</summary>
        InstallMsi,
    }

    /// <summary>
    /// Decides whether a TAP-Windows6 adapter is usable without administrator rights. The primary source
    /// is the network adapter class key, which standard users can read and which OpenVPN itself scans:
    /// every TAP-Windows6 adapter has ComponentId "tap0901" (or "root\tap0901") there. Its
    /// NetCfgInstanceId is matched against the interfaces Windows enumerates, because disabled adapters
    /// keep their class key. The driver store is consulted only to choose the repair.
    /// </summary>
    public static class TapDetector
    {
        public const string HardwareId = "root\\tap0901";
        public const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        static readonly string[] ComponentIds = { "tap0901", "root\\tap0901" };

        public static bool IsTapComponent(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId)) return false;
            var id = componentId.Trim();
            return ComponentIds.Any(c => string.Equals(c, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Normalizes "{GUID}" / "GUID" in any case to the braced upper-case form, or null when it is not a GUID.</summary>
        public static string NormalizeGuid(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Guid.TryParse(value.Trim(), out var g) ? g.ToString("B").ToUpperInvariant() : null;
        }

        public static TapCheckResult Evaluate(ITapSystemSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var tapIds = new HashSet<string>(StringComparer.Ordinal);
            var registered = 0;
            foreach (var a in source.RegisteredAdapters() ?? Enumerable.Empty<NetAdapterEntry>())
            {
                if (a == null || !IsTapComponent(a.ComponentId)) continue;
                registered++;
                var id = NormalizeGuid(a.NetCfgInstanceId);
                if (id != null) tapIds.Add(id);
            }

            var enumerated = new HashSet<string>((source.EnumeratedInterfaceIds() ?? Enumerable.Empty<string>())
                .Select(NormalizeGuid).Where(g => g != null), StringComparer.Ordinal);
            var active = tapIds.Count(enumerated.Contains);

            var result = new TapCheckResult { RegisteredAdapters = registered, EnumeratedAdapters = active };
            if (active > 0)
            {
                result.Status = TapStatus.Ready;
                result.DriverPackage = true;
                return result;
            }

            result.DriverPackage = source.DriverPackagePresent();
            result.Status = registered > 0 ? TapStatus.AdapterInactive
                          : result.DriverPackage ? TapStatus.DriverOnly
                          : TapStatus.Missing;
            return result;
        }

        /// <summary>
        /// Creating an adapter only helps when the driver is staged and OpenVPN's signed tapctl.exe is
        /// available; every other case goes through the pinned MSI, which stages the driver and creates
        /// an adapter itself.
        /// </summary>
        public static TapRepair ChooseRepair(TapCheckResult check, bool tapctlAvailable)
        {
            if (check == null || check.IsReady) return TapRepair.None;
            if (check.Status != TapStatus.Missing && check.DriverPackage && tapctlAvailable) return TapRepair.CreateAdapter;
            return TapRepair.InstallMsi;
        }

        /// <summary>True for a driver store folder of the TAP-Windows6 package (its INF is OemVista.inf).</summary>
        public static bool IsTapDriverStoreFolder(string folderName) =>
            !string.IsNullOrEmpty(folderName) && folderName.StartsWith("oemvista.inf_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What led to a TAP-Windows6 check.</summary>
    public enum TapPromptTrigger
    {
        /// <summary>App start or a passed prerequisites re-check.</summary>
        Startup,
        /// <summary>The user installed OpenVPN from the app just now.</summary>
        AfterOpenVpnInstall,
        /// <summary>The user pressed Connect for an OpenVPN server.</summary>
        Connect,
        /// <summary>OpenVPN reported that it found no usable TAP adapter.</summary>
        DriverError,
    }

    /// <summary>
    /// When to ask the user for permission to install or create the TAP-Windows6 adapter. Nothing is ever
    /// installed without that confirmation. After "Not now" the app stops asking at startup and asks
    /// again only when the user tries to connect.
    /// </summary>
    public static class TapPromptPolicy
    {
        public static bool ShouldPrompt(TapCheckResult check, TapPromptTrigger trigger, bool declinedBefore)
        {
            if (check == null || check.IsReady) return false;
            switch (trigger)
            {
                case TapPromptTrigger.Startup: return !declinedBefore;
                case TapPromptTrigger.Connect: return check.BlocksConnect;
                default: return true;
            }
        }

        /// <summary>The stored "Not now" choice is cleared once an adapter is ready, so a later loss of the driver is reported again.</summary>
        public static bool DeclinedAfterCheck(TapCheckResult check, bool declinedBefore) =>
            declinedBefore && (check == null || !check.IsReady);
    }

    /// <summary>Builds the msiexec properties for the pinned OpenVPN MSI.</summary>
    public static class OpenVpnMsiPlan
    {
        /// <summary>Features of the pinned 2.6.23-I002 MSI (Feature table, identical for amd64, arm64 and x86).</summary>
        public static readonly string[] KnownFeatures =
        {
            "OpenVPN", "OpenVPN.GUI", "OpenVPN.GUI.OnLogon", "OpenVPN.Service", "OpenVPN.PLAP.Register",
            "OpenVPN.Documentation", "OpenVPN.SampleCfg", "OpenSSL", "EasyRSA",
            "Drivers", "Drivers.TAPWindows6", "Drivers.OvpnDco", "Drivers.Wintun",
        };

        /// <summary>Client, Interactive Service and the TAP-Windows6 / ovpn-dco drivers; the GUI is not needed.</summary>
        public static readonly string[] RequiredFeatures =
        {
            "OpenVPN", "OpenVPN.Service", "Drivers", "Drivers.TAPWindows6", "Drivers.OvpnDco",
        };

        public const string TapFeature = "Drivers.TAPWindows6";
        public const string DcoFeature = "Drivers.OvpnDco";

        /// <summary>
        /// ADDLOCAL is the required set plus every known feature that is already installed locally.
        /// Listing ADDLOCAL suppresses MigrateFeatureStates during a major upgrade, so features the user
        /// already had (for example the GUI) are carried over explicitly instead of being dropped. On the
        /// same product version msiexec runs in maintenance mode and ADDLOCAL only adds features.
        /// When the TAP feature is already installed on the pinned product yet the driver is missing,
        /// REINSTALL re-runs that feature's driver installation.
        /// Without <paramref name="includeOvpnDco"/> (Windows older than 10 2004) the ovpn-dco feature is left
        /// out entirely, since its driver cannot install there and would fail the whole MSI transaction.
        /// </summary>
        public static string BuildProperties(IEnumerable<string> installedFeatures, bool pinnedProductInstalled, bool includeOvpnDco = true)
        {
            var installed = new HashSet<string>(installedFeatures ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var reinstallTap = pinnedProductInstalled && installed.Contains(TapFeature);
            var add = KnownFeatures.Where(f => (RequiredFeatures.Contains(f) || installed.Contains(f))
                                               && !(reinstallTap && f == TapFeature)
                                               && (includeOvpnDco || f != DcoFeature));
            var props = "ADDLOCAL=" + string.Join(",", add);
            if (reinstallTap) props += " REINSTALL=" + TapFeature + " REINSTALLMODE=emus";
            return props;
        }

        /// <summary>
        /// True when an installed OpenVPN MSI is newer than the pinned one. Its launch condition then
        /// refuses to run, so the user has to add TAP-Windows6 to the installed version instead.
        /// </summary>
        public static bool IsNewerThanPinned(string installedVersion, string pinnedVersion)
        {
            if (!Version.TryParse(installedVersion ?? "", out var have) || !Version.TryParse(pinnedVersion ?? "", out var pin)) return false;
            return have > pin;
        }
    }
}
