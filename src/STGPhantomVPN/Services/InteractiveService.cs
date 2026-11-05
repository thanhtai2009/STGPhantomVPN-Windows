using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Client for the OpenVPN Interactive Service (OpenVPNServiceInteractive). The service starts
    /// openvpn.exe as the current user and performs route/DNS/adapter changes on its behalf, so this
    /// app never needs administrator rights to connect. Protocol: doc/interactive-service-notes.rst.
    /// </summary>
    public sealed class InteractiveService : IDisposable
    {
        const string PipeName = @"openvpn\service";
        NamedPipeClientStream _pipe;

        /// <summary>Starts openvpn.exe; returns its PID or throws with the service's error text.</summary>
        public int Start(string workDir, string options, string stdin)
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            _pipe.Connect(5000);
            _pipe.ReadMode = PipeTransmissionMode.Message;
            VerifyServer(_pipe);

            var msg = workDir + "\0" + options + "\0" + stdin + "\0";
            var bytes = Encoding.Unicode.GetBytes(msg);
            _pipe.Write(bytes, 0, bytes.Length);
            _pipe.Flush();
            Array.Clear(bytes, 0, bytes.Length);

            var reply = ReadMessage(_pipe);
            var lines = reply.Split('\n');
            if (lines.Length >= 2 && lines[0].Trim() == "0x00000000")
                return Convert.ToInt32(lines[1].Trim().Substring(2), 16);
            throw new InvalidOperationException("Interactive Service: " + reply.Replace('\n', ' ').Trim());
        }

        static string ReadMessage(NamedPipeClientStream pipe)
        {
            var ms = new MemoryStream();
            var buf = new byte[1024];
            do
            {
                var n = pipe.Read(buf, 0, buf.Length);
                if (n == 0) break;
                ms.Write(buf, 0, n);
            } while (!pipe.IsMessageComplete);
            return Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0');
        }

        /// <summary>
        /// Guards against pipe squatting before the management password is sent. A process name alone can
        /// be faked by any user, so the server must also run in session 0 (services) and the pipe object
        /// must be owned by SYSTEM or Administrators, which an unelevated process cannot arrange.
        /// </summary>
        static void VerifyServer(NamedPipeClientStream pipe)
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid))
                throw new InvalidOperationException("Cannot identify the OpenVPN service pipe owner");
            using (var p = Process.GetProcessById((int)pid))
            {
                if (!string.Equals(p.ProcessName, "openvpnserv", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected OpenVPN service pipe owner: " + p.ProcessName);
                if (p.SessionId != 0)
                    throw new InvalidOperationException("OpenVPN service pipe is not served from session 0");
            }

            var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !(owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
                throw new InvalidOperationException("OpenVPN service pipe has an unexpected owner: " + owner);
        }

        public void Dispose()
        {
            try { _pipe?.Dispose(); } catch (IOException) { }
            _pipe = null;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);
    }
}
