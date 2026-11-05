using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Creates folders under %ProgramData%\STGPhantomVPN that only Administrators and SYSTEM can read or
    /// write. Files that an elevated openvpn.exe, or the elevated helper consume must
    /// not be swappable by unelevated code.
    /// </summary>
    public static class SecureDir
    {
        /// <summary>%ProgramData%\STGPhantomVPN. Users may create folders in ProgramData, so this one may pre-exist with a user as owner.</summary>
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "STGPhantomVPN");

        public static string RunDir => Sub("run");
        public static string LogDir => Sub("logs");
        public static string StateDir => Sub("state");

        /// <summary>Secures the root first, then the child, so a user-owned root cannot be used to rename or replace the child afterwards.</summary>
        public static string Sub(string name)
        {
            Ensure(Root);
            return Ensure(Path.Combine(Root, name));
        }

        public static string Ensure(string path)
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            var rights = FileSystemRights.FullControl;
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), rights, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), rights, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path, sec);
            }
            else
            {
                // A pre-planted junction/symlink would make SetAccessControl and later writes act on its target.
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Refusing to use a reparse point as a secure folder: " + path);
                Directory.SetAccessControl(path, sec);
            }
            return path;
        }

        /// <summary>
        /// Writes a new file. Any existing entry is deleted first so a pre-planted hard link is unlinked
        /// rather than written through (which would overwrite the link target with admin rights).
        /// </summary>
        public static void WriteFile(string path, string content)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
                w.Write(content);
        }

        public static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
