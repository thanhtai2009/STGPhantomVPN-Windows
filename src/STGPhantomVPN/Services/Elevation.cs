using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// The app runs as the normal user. The few steps that need administrator rights (installing the OpenVPN MSI,
    /// creating a TAP adapter) launch the app exe with "--elevated ..." through UAC.
    /// </summary>
    public static class Elevation
    {
        static readonly Lazy<bool> Elevated = new Lazy<bool>(() =>
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        });

        /// <summary>Token elevation cannot change during the life of a process, so it is computed once.</summary>
        public static bool IsElevated => Elevated.Value;

        /// <summary>
        /// Runs the elevated helper with the given arguments; returns its exit code, or -1 if UAC was declined.
        /// The helper is this exe, or the installed Program Files copy when this one is portable (see <see cref="HelperLocator"/>).
        /// </summary>
        public static Task<int> RunHelperAsync(params string[] args)
        {
            return Task.Run(() =>
            {
                var exe = HelperLocator.ExePath;
                var psi = new ProcessStartInfo(exe, "--elevated " + CommandLine.Join(args))
                {
                    UseShellExecute = true,
                    Verb = IsElevated ? null : "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        if (p == null) return -1;
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    Log.Warn("UAC prompt was declined");
                    return -1;
                }
            });
        }
    }

    /// <summary>Windows command-line quoting compatible with CommandLineToArgvW.</summary>
    public static class CommandLine
    {
        public static string Join(params string[] args)
        {
            var parts = new string[args.Length];
            for (var i = 0; i < args.Length; i++) parts[i] = Quote(args[i]);
            return string.Join(" ", parts);
        }

        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0) return arg;
            var sb = new System.Text.StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1);
                else sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
