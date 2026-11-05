using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>Creates <see cref="OpenVpnProcess"/> instances for openvpn.exe started directly or by the Interactive Service.</summary>
    public static class OpenVpnProcesses
    {
        const uint PROCESS_TERMINATE = 0x0001;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint SYNCHRONIZE = 0x00100000;

        /// <summary>Wraps a child process this app started itself (elevated path); the Process object has full access.</summary>
        public static OpenVpnProcess FromOwnChild(Process process) => new ChildOpenVpnProcess(process);

        /// <summary>
        /// Tracks openvpn.exe by PID with only SYNCHRONIZE and limited query rights, which the Interactive
        /// Service's process DACL grants to the user. Falls back to polling the process list if even that fails.
        /// </summary>
        public static OpenVpnProcess Attach(int pid)
        {
            var handle = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (!handle.IsInvalid)
                return new WaitHandleOpenVpnProcess(pid, new NativeWaitHandle(handle), () => TerminateByPid(pid)) { Error = LogError };

            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            Log.Info("openvpn.exe pid " + pid + " cannot be opened for waiting (" + new Win32Exception(error).Message + "); watching it by polling");
            return new PollingOpenVpnProcess(pid, () => IsOpenVpnRunning(pid), () => TerminateByPid(pid)) { Error = LogError };
        }

        /// <summary>Opens a PID only if it is openvpn.exe; null otherwise. Used for orphan cleanup.</summary>
        public static OpenVpnProcess TryAttachOpenVpn(int pid)
        {
            var handle = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return IsOpenVpnRunning(pid) ? new PollingOpenVpnProcess(pid, () => IsOpenVpnRunning(pid), () => TerminateByPid(pid)) { Error = LogError } : null;
            }
            if (!string.Equals(ImageFileName(handle), "openvpn.exe", StringComparison.OrdinalIgnoreCase))
            {
                handle.Dispose();
                return null;
            }
            return new WaitHandleOpenVpnProcess(pid, new NativeWaitHandle(handle), () => TerminateByPid(pid)) { Error = LogError };
        }

        static void LogError(string message, Exception ex) => Log.Error(message, ex);

        /// <summary>
        /// The Interactive Service grants the user PROCESS_TERMINATE on the openvpn.exe it starts; it then
        /// sees its pipe to openvpn.exe close and undoes the routes, DNS and adapter changes it made.
        /// </summary>
        static bool TerminateByPid(int pid)
        {
            using (var h = OpenProcess(PROCESS_TERMINATE, false, pid))
            {
                if (h.IsInvalid)
                {
                    Log.Warn("Cannot open openvpn.exe pid " + pid + " for termination (" + new Win32Exception(Marshal.GetLastWin32Error()).Message + ")");
                    return false;
                }
                if (TerminateProcess(h, 1)) return true;
                Log.Warn("Terminating openvpn.exe pid " + pid + " failed (" + new Win32Exception(Marshal.GetLastWin32Error()).Message + ")");
                return false;
            }
        }

        /// <summary>Uses the process list only (no handle), so it works whatever the process DACL allows.</summary>
        static bool IsOpenVpnRunning(int pid)
        {
            var found = false;
            foreach (var p in Process.GetProcessesByName("openvpn"))
            {
                if (p.Id == pid) found = true;
                p.Dispose();
            }
            return found;
        }

        static string ImageFileName(SafeWaitHandle handle)
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            if (!QueryFullProcessImageName(handle, 0, sb, ref size)) return null;
            try { return Path.GetFileName(sb.ToString(0, size)); }
            catch (ArgumentException) { return null; }
        }

        sealed class NativeWaitHandle : WaitHandle
        {
            public NativeWaitHandle(SafeWaitHandle handle) { SafeWaitHandle = handle; }
        }

        /// <summary>openvpn.exe started by this process (elevated); System.Diagnostics.Process has full access to it.</summary>
        sealed class ChildOpenVpnProcess : OpenVpnProcess
        {
            readonly Process _p;
            readonly int _id;

            public ChildOpenVpnProcess(Process p)
            {
                _p = p ?? throw new ArgumentNullException(nameof(p));
                _id = p.Id;
                Error = LogError;
            }

            public override int Id => _id;

            public override bool HasExited
            {
                get
                {
                    try { return _p.HasExited; }
                    catch (InvalidOperationException) { return true; }
                    catch (Win32Exception ex) { Report("Checking openvpn.exe failed", ex); return false; }
                }
            }

            public override bool WaitForExit(int milliseconds)
            {
                try { return _p.WaitForExit(milliseconds); }
                catch (InvalidOperationException) { return true; }
                catch (Win32Exception ex) { Report("Waiting for openvpn.exe failed", ex); return false; }
            }

            protected override void BeginWatch()
            {
                _p.Exited += (s, e) => RaiseExited();
                _p.EnableRaisingEvents = true;
            }

            protected override bool Terminate()
            {
                try { _p.Kill(); return true; }
                catch (InvalidOperationException) { return true; }
                catch (Win32Exception ex) { Report("Terminating openvpn.exe failed", ex); return false; }
            }

            protected override void DisposeCore() => _p.Dispose();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafeWaitHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool TerminateProcess(SafeWaitHandle process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool QueryFullProcessImageName(SafeWaitHandle process, int flags, StringBuilder exeName, ref int size);
    }
}
