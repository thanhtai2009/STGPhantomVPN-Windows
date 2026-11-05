using System;
using System.Collections.Generic;
using System.Linq;

namespace STGPhantomVPN.Core
{
    /// <summary>Identity shared by the app and installer/STGPhantomVPN.iss.</summary>
    public static class InstallInfo
    {
        /// <summary>Inno Setup AppId; the uninstall key is HKLM\...\Uninstall\{AppId}_is1.</summary>
        public const string AppId = "{8B1BA3B9-35C6-497C-9711-427B7DE8A5CB}";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId + "_is1";
        public const string DefaultFolderName = "STG Phantom VPN";
        public const string SingleInstanceMutex = @"Global\STGPhantomVPN.SingleInstance";
        /// <summary>Set by a second launch in the same session to bring the running window to the front.</summary>
        public const string ActivateEvent = @"Local\STGPhantomVPN.Activate";
    }

    /// <summary>
    /// Decides whether a file-system ACE lets a non-administrator change code that the elevated helper
    /// loads. Pure logic over SID strings and access masks, so it can be tested off Windows.
    /// </summary>
    public static class AclRules
    {
        public const string LocalSystem = "S-1-5-18";
        public const string Administrators = "S-1-5-32-544";
        public const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

        const int WriteData = 0x2, AppendData = 0x4, DeleteChild = 0x40, Delete = 0x10000;
        const int WriteDac = 0x40000, WriteOwner = 0x80000, GenericAll = 0x10000000, GenericWrite = 0x40000000;

        /// <summary>Rights that allow replacing or adding files in the app folder, its subfolders and files.</summary>
        public const int TargetMask = WriteData | AppendData | DeleteChild | Delete | WriteDac | WriteOwner | GenericAll | GenericWrite;

        /// <summary>Rights on a parent folder that allow renaming the app folder away and planting another one.</summary>
        public const int AncestorMask = DeleteChild | Delete | WriteDac | WriteOwner | GenericAll;

        public static bool IsTrustedOwner(string sid) =>
            sid == LocalSystem || sid == Administrators || sid == TrustedInstaller;

        /// <summary>
        /// True when an allow ACE gives an untrusted principal one of the masked rights on the object itself.
        /// Inherit-only ACEs and the CREATOR OWNER / CREATOR GROUP / OWNER RIGHTS placeholders do not apply
        /// to the object (the owner is checked separately). Deny ACEs are ignored, which errs on the safe side.
        /// </summary>
        public static bool GrantsUnsafeRights(string sid, int accessMask, bool inheritOnly, int mask)
        {
            if (inheritOnly || IsTrustedOwner(sid)) return false;
            if (sid == "S-1-3-0" || sid == "S-1-3-1" || sid == "S-1-3-4") return false;
            return (accessMask & mask) != 0;
        }
    }

    public enum HelperSource
    {
        /// <summary>This exe lives in an admin-only folder (for example the Program Files install).</summary>
        Self,
        /// <summary>This copy is portable; the same version installed under an admin-only folder is used instead.</summary>
        Installed,
        /// <summary>No protected copy exists; this exe is used and a warning is logged.</summary>
        UnprotectedSelf,
    }

    public sealed class HelperCandidate
    {
        public string ExePath { get; set; }
        public string FileVersion { get; set; }
        public bool IsProtected { get; set; }
    }

    public sealed class HelperChoice
    {
        public HelperSource Source { get; set; }
        public string ExePath { get; set; }
    }

    /// <summary>
    /// Chooses which STGPhantomVPN.exe is started through UAC for "--elevated" verbs. Code in a folder the
    /// user can write to could be swapped between the UAC prompt and the elevated start, so an admin-only
    /// copy is preferred. Only an installed copy with exactly the same file version is used, because the
    /// helper verbs and argument formats are versioned with the app.
    /// </summary>
    public static class HelperPlacement
    {
        public static HelperChoice Choose(string selfExe, string selfVersion, bool selfProtected, IEnumerable<HelperCandidate> installed)
        {
            if (selfProtected) return new HelperChoice { Source = HelperSource.Self, ExePath = selfExe };
            var match = (installed ?? Enumerable.Empty<HelperCandidate>()).FirstOrDefault(c =>
                c != null && c.IsProtected && !string.IsNullOrEmpty(c.ExePath) && !string.IsNullOrEmpty(selfVersion) &&
                string.Equals(c.FileVersion, selfVersion, StringComparison.Ordinal) &&
                !string.Equals(c.ExePath, selfExe, StringComparison.OrdinalIgnoreCase));
            return match != null
                ? new HelperChoice { Source = HelperSource.Installed, ExePath = match.ExePath }
                : new HelperChoice { Source = HelperSource.UnprotectedSelf, ExePath = selfExe };
        }
    }
}
