using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>Unprivileged, read-only checks of the local machine for <see cref="Prerequisites"/>.</summary>
    public sealed class SystemProbe : ISystemProbe
    {
        const string CbsPackages = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages";

        /// <summary>CBS CurrentState values meaning the package is in effect: superseded, installed, permanent.</summary>
        static readonly int[] InstalledStates = { 0x50, 0x70, 0x80 };

        /// <summary>The native System32 folder, also from a 32-bit process on 64-bit Windows.</summary>
        static string NativeSystemDir()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
            {
                var sysnative = Path.Combine(windows, "Sysnative");
                if (Directory.Exists(sysnative)) return sysnative;
            }
            return Path.Combine(windows, "System32");
        }

        public bool SystemFileExists(string fileName)
        {
            try { return File.Exists(Path.Combine(NativeSystemDir(), fileName)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        public Version SystemFileVersion(string fileName)
        {
            try
            {
                var path = Path.Combine(NativeSystemDir(), fileName);
                if (!File.Exists(path)) return null;
                var fv = FileVersionInfo.GetVersionInfo(path);
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Component Based Servicing package first (fast, readable by standard users), then the
        /// Win32_QuickFixEngineering list that "Installed updates" shows.
        /// </summary>
        public bool IsHotfixInstalled(string kb) => CbsPackageInstalled(kb) || QuickFixListed(kb);

        static bool CbsPackageInstalled(string kb)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var packages = hklm.OpenSubKey(CbsPackages))
                {
                    if (packages == null) return false;
                    var prefix = "Package_for_" + kb + "~";
                    foreach (var name in packages.GetSubKeyNames().Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        using (var pkg = packages.OpenSubKey(name))
                        {
                            if (pkg?.GetValue("CurrentState") is int state && InstalledStates.Contains(state)) return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                Log.Warn("CBS lookup for " + kb + " failed: " + ex.Message);
            }
            return false;
        }

        static bool QuickFixListed(string kb)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(kb, "^KB[0-9]{6,8}$")) return false;
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT HotFixID FROM Win32_QuickFixEngineering WHERE HotFixID = '" + kb + "'"))
                using (var results = searcher.Get())
                    return results.Count > 0;
            }
            catch (Exception ex) when (ex is ManagementException || ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Win32_QuickFixEngineering lookup for " + kb + " failed: " + ex.Message);
                return false;
            }
        }

        public bool IsRootCertificatePresent(string sha1Thumbprint) =>
            InStore(StoreName.Root, sha1Thumbprint) || InStore(StoreName.AuthRoot, sha1Thumbprint);

        static bool InStore(StoreName name, string thumbprint)
        {
            try
            {
                using (var store = new X509Store(name, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    try { return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false).Count > 0; }
                    finally { store.Close(); }
                }
            }
            catch (System.Security.Cryptography.CryptographicException) { return false; }
        }

        public bool RootAutoUpdateDisabled
        {
            get
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\SystemCertificates\AuthRoot"))
                        return k?.GetValue("DisableRootAutoUpdate") is int v && v != 0;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                {
                    return false;
                }
            }
        }
    }
}
