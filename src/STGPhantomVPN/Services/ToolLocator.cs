using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Finds openvpn.exe and tapctl.exe in fixed, admin-controlled locations only: the app's own
    /// bundled folder and Program Files (also via HKLM\SOFTWARE\OpenVPN\exe_path when it points
    /// there). Parent folders and PATH are never searched (security audit 2026-10-30).
    /// </summary>
    public static class ToolLocator
    {
        internal static List<string> ProgramFilesRoots()
        {
            // Read from HKLM, not %ProgramW6432%: environment variables come from HKCU\Environment, which
            // unelevated code can change, and they are inherited by the elevated helper.
            return new[]
                {
                    NativeProgramFilesDir(),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                }
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => Path.GetFullPath(r).TrimEnd('\\') + "\\")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string NativeProgramFilesDir()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion"))
                {
                    var p = key?.GetValue("ProgramFilesDir", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    return !string.IsNullOrEmpty(p) && p.IndexOf('%') < 0 && Path.IsPathRooted(p) ? p : null;
                }
            }
            catch (Exception) { return null; }
        }

        static bool IsUnderProgramFiles(string path)
        {
            var full = Path.GetFullPath(path);
            return ProgramFilesRoots().Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<string> OpenVpnCandidates()
        {
            // The app's own copy is only trusted when the app itself is installed under Program Files;
            // an unzipped portable copy lives in a user-writable folder where exe and DLLs can be replaced.
            if (IsUnderProgramFiles(AppPaths.AppDir))
                yield return Path.Combine(AppPaths.AppDir, "openvpn", "bin", "openvpn.exe");
            var reg = RegistryExePath();
            if (reg != null) yield return reg;
            foreach (var root in ProgramFilesRoots())
                yield return Path.Combine(root, "OpenVPN", "bin", "openvpn.exe");
        }

        static string RegistryExePath()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\OpenVPN"))
                {
                    var p = key?.GetValue("exe_path") as string;
                    return !string.IsNullOrEmpty(p) && Path.IsPathRooted(p) && IsUnderProgramFiles(p) ? p : null;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// First candidate that exists and is validly signed by OpenVPN Inc. A SHA-256 outside the pinned
        /// list (the user updated OpenVPN) is logged as a warning but accepted; a bad signature is rejected.
        /// </summary>
        public static string FindOpenVpn()
        {
            foreach (var path in OpenVpnCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                try
                {
                    using (OpenVerifiedOpenVpn(path)) return path;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    Log.Warn("Ignoring " + path + ": " + ex.Message);
                }
            }
            return null;
        }

        /// <summary>
        /// Opens openvpn.exe write-locked and checks signature and SHA-256 through that handle. Keep the
        /// returned stream open until the process has been started so the file cannot be swapped.
        /// </summary>
        public static FileStream OpenVerifiedOpenVpn(string path)
        {
            if (!OpenVpnCandidates().Contains(path, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("openvpn.exe is not in an allowed location");
            var fs = Authenticode.OpenLocked(path);
            try
            {
                if (!Authenticode.IsSignedBy(fs, path, "OpenVPN Inc."))
                    throw new InvalidDataException("no valid OpenVPN Inc. signature");
                fs.Position = 0;
                var hash = PinnedDownloads.Sha256OfStream(fs);
                if (!PinnedDownloads.OpenVpnExeHashes.Contains(hash))
                    Log.Warn("openvpn.exe is signed but not a pinned build (" + hash + "): " + path);
                return fs;
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }

        /// <summary>tapctl.exe from the bin folder of a verified openvpn.exe; null when absent or not validly signed by OpenVPN Inc.</summary>
        public static string FindTapctl()
        {
            var exe = FindOpenVpn();
            if (exe == null) return null;
            var path = Path.Combine(Path.GetDirectoryName(exe), "tapctl.exe");
            try
            {
                using (var fs = OpenVerifiedTapctl(path)) return fs != null ? path : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Ignoring " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Write-locked, signature-checked tapctl.exe next to an allowed openvpn.exe location; null when it
        /// does not verify. Keep the stream open until the process has been started.
        /// </summary>
        public static FileStream OpenVerifiedTapctl(string path)
        {
            if (path == null || !File.Exists(path)) return null;
            var allowed = OpenVpnCandidates().Select(c => Path.Combine(Path.GetDirectoryName(c), "tapctl.exe"));
            if (!allowed.Contains(path, StringComparer.OrdinalIgnoreCase)) return null;
            var fs = Authenticode.OpenLocked(path);
            if (Authenticode.IsSignedBy(fs, path, "OpenVPN Inc.")) return fs;
            fs.Dispose();
            Log.Warn("Ignoring tapctl.exe without a valid OpenVPN Inc. signature: " + path);
            return null;
        }

        public static bool IsInteractiveServiceRunning()
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController("OpenVPNServiceInteractive"))
                    return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>
        /// Native OS architecture name used by the OpenVPN MSI file names. On Windows on ARM an
        /// emulated x64 or x86 process is told the OS is x64 or x86, which would pick a driver that cannot load,
        /// so the native machine is read with IsWow64Process2 (Windows 10 1511+) first.
        /// </summary>
        public static string OsArch()
        {
            try
            {
                if (IsWow64Process2(CurrentProcessPseudoHandle, out _, out var native))
                {
                    var arch = MachineArch.FromImageFileMachine(native);
                    if (arch != null) return arch;
                }
            }
            catch (EntryPointNotFoundException) { }
            switch (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
            {
                case System.Runtime.InteropServices.Architecture.Arm64: return "arm64";
                case System.Runtime.InteropServices.Architecture.X86: return "x86";
                default: return "amd64";
            }
        }

        /// <summary>GetCurrentProcess() pseudo-handle; needs no CloseHandle.</summary>
        static readonly IntPtr CurrentProcessPseudoHandle = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    }
}
