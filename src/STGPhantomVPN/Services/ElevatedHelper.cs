using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Entry point for "STGPhantomVPN.exe --elevated &lt;verb&gt; ...", started through UAC by
    /// <see cref="Elevation.RunHelperAsync"/>. Every input is re-validated here because the caller
    /// runs unelevated. No UI is shown.
    /// </summary>
    public static class ElevatedHelper
    {
        public const int ExitOk = 0;
        public const int ExitBadArgs = 2;
        public const int ExitHashMismatch = 3;
        public const int ExitFailed = 4;
        public const int ExitNewerInstalled = 5;

        /// <summary>Account SID of the unelevated user (S-1-5-21-... for local and domain accounts).</summary>
        static readonly Regex UserSid = new Regex(@"^S-1-5-21(-[0-9]{1,10}){4}$", RegexOptions.Compiled);

        public static int Run(string[] args)
        {
            try
            {
                if (args.Length < 2) return ExitBadArgs;
                if (!HelperLocator.IsAdminOnly(AppPaths.AppDir))
                    Log.Warn("Elevated helper started from a user-writable folder: " + AppPaths.AppDir);
                switch (args[1])
                {
                    case "install-msi": return args.Length == 4 ? InstallMsi(args[2], args[3]) : ExitBadArgs;
                    case "tap-create": return args.Length == 2 ? CreateTapAdapter() : ExitBadArgs;
                    case "remove-data": return args.Length == 2 ? RemoveSecureData() : ExitBadArgs;
                    case Ipv6Firewall.HelperVerb: return RunIpv6Guard(args);
                    default: return ExitBadArgs;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Elevated helper failed", ex);
                return ExitFailed;
            }
        }

        static string AdminDir(string name) => SecureDir.Sub(name);

        /// <summary>
        /// Keeps the WFP IPv6 leak block for the app process given by pid (see <see cref="Ipv6Guard"/>): waits for
        /// the app's on / off / quit signals and exits when the app process ends. The filters live in a dynamic WFP
        /// session owned by this process, so they are removed on "off", on exit and if this process dies.
        /// </summary>
        static int RunIpv6Guard(string[] args)
        {
            if (!Ipv6Firewall.TryParseHelperArgs(args, out var pid, out var token)) return ExitBadArgs;
            Process app;
            try { app = Process.GetProcessById(pid); }
            catch (ArgumentException) { return ExitBadArgs; }
            // Only serves the app itself; anything else asking for a firewall block is refused.
            if (!string.Equals(app.ProcessName, "STGPhantomVPN", StringComparison.OrdinalIgnoreCase)) return ExitBadArgs;

            System.Threading.EventWaitHandle Open(Ipv6Firewall.Signal s) =>
                System.Threading.EventWaitHandle.OpenExisting(Ipv6Firewall.EventName(token, s));
            using (var appExit = new System.Threading.ManualResetEvent(false) { SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(app.Handle, false) })
            using (var quit = Open(Ipv6Firewall.Signal.Quit))
            using (var on = Open(Ipv6Firewall.Signal.On))
            using (var onLan = Open(Ipv6Firewall.Signal.OnLan))
            using (var off = Open(Ipv6Firewall.Signal.Off))
            using (var ok = Open(Ipv6Firewall.Signal.Ok))
            using (var fail = Open(Ipv6Firewall.Signal.Fail))
            {
                Log.Info("IPv6 leak block helper serving pid " + pid);
                WfpEngine engine = null;
                try
                {
                    var handles = new System.Threading.WaitHandle[] { appExit, quit, on, onLan, off };
                    while (true)
                    {
                        var i = System.Threading.WaitHandle.WaitAny(handles);
                        if (i <= 1) break;
                        engine?.Dispose();
                        engine = null;
                        if (i == 4)
                        {
                            Log.Info("IPv6 leak block removed");
                            ok.Set();
                            continue;
                        }
                        try
                        {
                            engine = WfpEngine.OpenDynamic();
                            var n = engine.AddIpv6Block(i == 3);
                            Log.Info("IPv6 leak block: " + n + " WFP filters added (dynamic session)");
                            ok.Set();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("IPv6 leak block failed", ex);
                            engine?.Dispose();
                            engine = null;
                            fail.Set();
                        }
                    }
                }
                finally
                {
                    engine?.Dispose();
                    Log.Info("IPv6 leak block helper exiting; filters removed");
                }
            }
            return ExitOk;
        }

        static int InstallMsi(string sourcePath, string userSid)
        {
            if (!UserSid.IsMatch(userSid)) return ExitBadArgs;
            var pin = InstallerService.Pin();
            var msi = Path.Combine(AdminDir("downloads"), pin.FileName);
            // Unlink anything already there (possibly a planted hard link) instead of copying through it.
            if (File.Exists(msi)) File.Delete(msi);
            File.Copy(sourcePath, msi, false);
            if (!PinnedDownloads.Verify(msi, pin))
            {
                SecureDir.DeleteQuietly(msi);
                return ExitHashMismatch;
            }

            var properties = OpenVpnProperties(out var newer);
            if (newer)
            {
                SecureDir.DeleteQuietly(msi);
                return ExitNewerInstalled;
            }
            var msiLog = Path.Combine(SecureDir.LogDir, "msiexec-openvpn.log");
            SecureDir.DeleteQuietly(msiLog);
            Log.Info("msiexec " + pin.FileName + " " + properties);
            var code = RunWait(Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                "/i " + CommandLine.Quote(msi) + " " + properties + " /passive /norestart /l*v " + CommandLine.Quote(msiLog));
            Log.Info("msiexec " + pin.FileName + " exit code " + code);
            if (code != 0 && code != 3010) return ExitFailed;

            // Lets this user hand dynamically generated profiles to the Interactive Service. Added by SID
            // through the API: no command line to inject into and no dependency on localized account names.
            var rc = LocalGroups.AddMember("OpenVPN Administrators", new SecurityIdentifier(userSid));
            Log.Info("Add " + userSid + " to OpenVPN Administrators: result " + rc);
            return ExitOk;
        }

        /// <summary>
        /// ADDLOCAL for the pinned OpenVPN MSI, keeping features of any installed OpenVPN 2.6 MSI (same
        /// UpgradeCode). <paramref name="newerInstalled"/> is set when the installed version is newer than
        /// the pinned one, which the MSI's launch condition would reject.
        /// </summary>
        static string OpenVpnProperties(out bool newerInstalled)
        {
            var related = MsiProducts.Related(PinnedDownloads.OpenVpnUpgradeCode(ToolLocator.OsArch()));
            newerInstalled = related.Any(p => OpenVpnMsiPlan.IsNewerThanPinned(MsiProducts.Version(p), PinnedDownloads.OpenVpnProductVersion));
            var features = related
                .SelectMany(p => OpenVpnMsiPlan.KnownFeatures.Where(f => MsiProducts.IsFeatureLocal(p, f)))
                .Distinct()
                .ToList();
            var pinnedInstalled = related.Contains(PinnedDownloads.OpenVpnProductCode, StringComparer.OrdinalIgnoreCase);
            Log.Info("Installed OpenVPN MSI products: " + string.Join(", ", related.Select(p => p + " " + MsiProducts.Version(p))) +
                     "; local features: " + string.Join(",", features));
            return OpenVpnMsiPlan.BuildProperties(features, pinnedInstalled, OsSupport.SupportsOvpnDco(OsInfo.Current));
        }

        /// <summary>Adds a TAP-Windows6 adapter with OpenVPN's own tapctl.exe when the driver is staged but no adapter exists.</summary>
        static int CreateTapAdapter()
        {
            var path = ToolLocator.FindTapctl();
            if (path == null) return ExitFailed;
            using (var fs = ToolLocator.OpenVerifiedTapctl(path))
            {
                if (fs == null) return ExitFailed;
                var code = RunWait(path, "create --hwid " + CommandLine.Quote(TapDetector.HardwareId));
                Log.Info("tapctl create exit code " + code);
                return code == 0 ? ExitOk : ExitFailed;
            }
        }

        /// <summary>Tunnel configs written by app versions before 1.0.6; WireGuard's own services may still read them.</summary>
        const string LegacyWireGuardFolder = "wireguard";

        /// <summary>
        /// Used by the uninstaller when the user asks to delete app data: removes %ProgramData%\STGPhantomVPN
        /// except <see cref="LegacyWireGuardFolder"/>, so tunnels a user set up with an older version keep working.
        /// Each folder is re-secured before it is listed, and links are removed without following them, so a
        /// folder planted before the app secured it cannot redirect the deletion. Nothing is logged here
        /// because the log folder is being deleted.
        /// </summary>
        static int RemoveSecureData()
        {
            var root = SecureDir.Root;
            if (!Directory.Exists(root)) return ExitOk;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(root, false);
                return ExitOk;
            }
            return DeleteTree(root, true) ? ExitOk : ExitFailed;
        }

        static bool DeleteTree(string dir, bool isRoot = false)
        {
            SecureDir.Ensure(dir);
            var ok = true;
            var kept = false;
            foreach (var sub in Directory.GetDirectories(dir))
            {
                if (isRoot && string.Equals(Path.GetFileName(sub), LegacyWireGuardFolder, StringComparison.OrdinalIgnoreCase)
                    && (File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0)
                {
                    kept = true;
                    continue;
                }
                try
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) Directory.Delete(sub, false);
                    else ok &= DeleteTree(sub);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { ok = false; }
            }
            foreach (var file in Directory.GetFiles(dir))
            {
                try
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { ok = false; }
            }
            if (!ok) return false;
            if (kept) return true;
            try { Directory.Delete(dir, false); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
            return true;
        }

        static int RunWait(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }

    /// <summary>Read-only Windows Installer queries (msi.dll) about installed products.</summary>
    static class MsiProducts
    {
        const int ERROR_SUCCESS = 0;
        const int ERROR_MORE_DATA = 234;
        const int INSTALLSTATE_LOCAL = 3;

        /// <summary>ProductCodes of installed products with the given UpgradeCode.</summary>
        public static List<string> Related(string upgradeCode)
        {
            var list = new List<string>();
            for (uint i = 0; i < 64; i++)
            {
                var buf = new StringBuilder(39);
                if (MsiEnumRelatedProducts(upgradeCode, 0, i, buf) != ERROR_SUCCESS) break;
                list.Add(buf.ToString());
            }
            return list;
        }

        public static bool IsFeatureLocal(string productCode, string feature) =>
            MsiQueryFeatureState(productCode, feature) == INSTALLSTATE_LOCAL;

        public static string Version(string productCode)
        {
            var len = 64;
            var buf = new StringBuilder(len);
            var rc = MsiGetProductInfo(productCode, "VersionString", buf, ref len);
            if (rc == ERROR_MORE_DATA)
            {
                buf = new StringBuilder(++len);
                rc = MsiGetProductInfo(productCode, "VersionString", buf, ref len);
            }
            return rc == ERROR_SUCCESS ? buf.ToString() : null;
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern int MsiEnumRelatedProducts(string lpUpgradeCode, uint dwReserved, uint iProductIndex, StringBuilder lpProductBuf);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern int MsiQueryFeatureState(string szProduct, string szFeature);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern int MsiGetProductInfo(string szProduct, string szProperty, StringBuilder lpValueBuf, ref int pcchValueBuf);
    }

    /// <summary>Adds an account to a local group by SID (NetLocalGroupAddMembers, level 0).</summary>
    static class LocalGroups
    {
        const int ERROR_MEMBER_IN_ALIAS = 1378;

        /// <summary>0 on success or when already a member, otherwise the Win32/NERR error code.</summary>
        public static int AddMember(string group, SecurityIdentifier sid)
        {
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            var pSid = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pSid, bytes.Length);
                var info = new LOCALGROUP_MEMBERS_INFO_0 { lgrmi0_sid = pSid };
                var rc = NetLocalGroupAddMembers(null, group, 0, ref info, 1);
                return rc == ERROR_MEMBER_IN_ALIAS ? 0 : rc;
            }
            finally { Marshal.FreeHGlobal(pSid); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LOCALGROUP_MEMBERS_INFO_0 { public IntPtr lgrmi0_sid; }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern int NetLocalGroupAddMembers(string servername, string groupname, int level, ref LOCALGROUP_MEMBERS_INFO_0 buf, int totalentries);
    }
}
