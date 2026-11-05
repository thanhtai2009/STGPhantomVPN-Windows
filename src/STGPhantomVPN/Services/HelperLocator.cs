using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Resolves the exe that <see cref="Elevation.RunHelperAsync"/> starts through UAC. An unzipped test
    /// build usually sits in a user-writable folder (Downloads, Desktop), where unelevated code could replace
    /// the exe, its DLLs or its .config before the elevated start; the installed copy under Program Files
    /// is used instead when it has the same version (security review 2026-11, Mây).
    /// </summary>
    public static class HelperLocator
    {
        static readonly Lazy<HelperChoice> Choice = new Lazy<HelperChoice>(Resolve);

        public static string ExePath => Choice.Value.ExePath;

        static HelperChoice Resolve()
        {
            var self = Process.GetCurrentProcess().MainModule.FileName;
            var version = FileVersion(self);
            var choice = HelperPlacement.Choose(self, version, IsAdminOnly(Path.GetDirectoryName(self)), InstalledCandidates());
            switch (choice.Source)
            {
                case HelperSource.Self:
                    Log.Info("Elevated helper: " + choice.ExePath);
                    break;
                case HelperSource.Installed:
                    Log.Warn("This copy runs from a user-writable folder; elevated steps use the installed copy " + choice.ExePath);
                    break;
                default:
                    Log.Warn("Elevated helper runs from a user-writable folder (" + Path.GetDirectoryName(self) +
                             "). Install the app to Program Files to keep elevated code out of reach of unelevated programs.");
                    break;
            }
            return choice;
        }

        static IEnumerable<HelperCandidate> InstalledCandidates()
        {
            var dirs = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hklm.OpenSubKey(InstallInfo.UninstallKey))
                    {
                        var p = key?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(p) && Path.IsPathRooted(p)) dirs.Add(p);
                    }
                }
                catch (Exception ex) { Log.Warn("Reading install location failed: " + ex.Message); }
            }
            dirs.AddRange(ToolLocator.ProgramFilesRoots().Select(r => Path.Combine(r, InstallInfo.DefaultFolderName)));

            foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var exe = Path.Combine(dir, "STGPhantomVPN.exe");
                if (!File.Exists(exe)) continue;
                yield return new HelperCandidate { ExePath = exe, FileVersion = FileVersion(exe), IsProtected = IsAdminOnly(dir) };
            }
        }

        static string FileVersion(string exe)
        {
            try { return FileVersionInfo.GetVersionInfo(exe).FileVersion; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// True when only SYSTEM, Administrators and TrustedInstaller can change the folder, anything inside
        /// it, or rename it through a parent folder. Network paths, reparse points and unreadable ACLs count
        /// as user-writable.
        /// </summary>
        public static bool IsAdminOnly(string dir)
        {
            try
            {
                var full = Path.GetFullPath(dir).TrimEnd('\\');
                if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;

                var tree = new List<string> { full };
                tree.AddRange(Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories));
                foreach (var d in tree)
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) return false;
                    if (!IsSafe(Directory.GetAccessControl(d, AccessControlSections.Access | AccessControlSections.Owner), AclRules.TargetMask)) return false;
                }
                foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0) return false;
                    if (!IsSafe(File.GetAccessControl(f, AccessControlSections.Access | AccessControlSections.Owner), AclRules.TargetMask)) return false;
                }
                for (var parent = Path.GetDirectoryName(full); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                {
                    if (!IsSafe(Directory.GetAccessControl(parent, AccessControlSections.Access | AccessControlSections.Owner), AclRules.AncestorMask)) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read permissions of " + dir + ": " + ex.Message);
                return false;
            }
        }

        static bool IsSafe(FileSystemSecurity sec, int mask)
        {
            var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !AclRules.IsTrustedOwner(owner.Value)) return false;
            foreach (FileSystemAccessRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow) continue;
                var inheritOnly = (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0;
                if (AclRules.GrantsUnsafeRights(rule.IdentityReference.Value, (int)rule.FileSystemRights, inheritOnly, mask)) return false;
            }
            return true;
        }
    }
}
