using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    public enum VpnState { Disconnected, Connecting, Connected, Disconnecting, Error, Refused }

    /// <summary>Error codes are resource keys; the view model localizes them.</summary>
    public static class VpnErrors
    {
        public const string OpenVpnMissing = "error_openvpn_missing";
        public const string ServiceMissing = "error_service_missing";
        public const string InvalidConfig = "error_invalid_config";
        public const string ConnectFailed = "error_connect_failed";
        public const string AuthFailed = "error_auth_failed";
        /// <summary>The server answered AUTH_FAILED: VPNGate refused the session (not a credentials problem).</summary>
        public const string ServerRefused = "error_server_refused";
        public const string DriverMissing = "error_driver_missing";
        public const string ConnectionLost = "error_connection_lost";
    }

    /// <summary>
    /// Runs OpenVPN for one VPNGate profile. Normally openvpn.exe is started through the OpenVPN
    /// Interactive Service as the current user; if the app itself is elevated it is started directly.
    /// Both paths are driven through the management interface (hold, state, byte counts, SIGTERM),
    /// and the 30-second connect timeout of the Android app is enforced per attempt here.
    /// A routed (tun) session that the server refuses with AUTH_FAILED is retried once in bridged
    /// (tap) mode, see <see cref="TunnelLayer"/>.
    /// </summary>
    public sealed class OpenVpnController : IDisposable
    {
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

        readonly object _gate = new object();
        OpenVpnProcess _proc;
        InteractiveService _service;
        int _session;
        bool _stopRequested;
        string _errorKey;
        string _errorDetail;
        Timer _timeoutTimer;
        TcpClient _mgmt;
        StreamWriter _mgmtWriter;
        ConnectOptions _options;
        TunnelLayer _layer;
        volatile string _lastState;
        /// <summary>Servers that refused a routed session during this run of the app; they start bridged.</summary>
        readonly HashSet<string> _bridgedServers = new HashSet<string>(StringComparer.Ordinal);

        long _lastIn, _lastOut;
        DateTime _lastSample;

        public VpnState State { get; private set; } = VpnState.Disconnected;
        public VpnServer CurrentServer { get; private set; }
        public DateTime? ConnectedSinceUtc { get; private set; }
        public long BytesIn { get; private set; }
        public long BytesOut { get; private set; }
        public long DownBytesPerSec { get; private set; }
        public long UpBytesPerSec { get; private set; }
        public string ErrorKey => _errorKey;
        public string ErrorDetail => _errorDetail;
        /// <summary>Layer of the current or last attempt.</summary>
        public TunnelLayer Layer => _layer;
        /// <summary>OpenVPN log file of the current or last attempt; null before the first one.</summary>
        public string CurrentLogPath { get; private set; }
        /// <summary>True while the current attempt is covered by the WFP IPv6 block (bridged sessions).</summary>
        public bool Ipv6FirewallActive { get; private set; }

        /// <summary>Raised on any state change (background thread).</summary>
        public event Action StateChanged;
        /// <summary>Raised when the 30-second timer expires before the tunnel came up, with the stage reached.</summary>
        public event Action<VpnServer, ConnectStage> ConnectTimedOut;

        /// <summary>When elevated, openvpn.exe runs as administrator and must not write into a user-writable folder (link/junction redirection).</summary>
        public static string LogDirectory => Elevation.IsElevated ? SecureDir.LogDir : AppPaths.LogDir;

        static string PidFile => Path.Combine(Elevation.IsElevated ? SecureDir.StateDir : AppPaths.DataDir, "openvpn.pid");

        /// <summary>
        /// Folder for the sanitized profile. When elevated, openvpn.exe runs as administrator, so the
        /// folder must not be writable by unelevated code; otherwise openvpn.exe runs as this user anyway.
        /// </summary>
        static string RunDir => Elevation.IsElevated ? SecureDir.RunDir : AppPaths.RunDir;

        public Task ConnectAsync(VpnServer server, ConnectOptions options) => Task.Run(() =>
        {
            TunnelLayer layer;
            lock (_gate) layer = server != null && _bridgedServers.Contains(server.Key) ? TunnelLayer.Bridged : TunnelLayer.Routed;
            Connect(server, options, layer, null);
        });

        /// <param name="retryOf">Session whose refused routed attempt this bridged attempt replaces; null for a new connect.</param>
        void Connect(VpnServer server, ConnectOptions options, TunnelLayer layer, int? retryOf)
        {
            if (retryOf == null) StopInternal();

            int session;
            lock (_gate)
            {
                // A disconnect or a new connect between the refusal and this retry cancels the retry.
                if (retryOf != null && (retryOf != _session || _stopRequested)) return;
                session = ++_session;
                _stopRequested = false;
                _errorKey = null;
                _errorDetail = null;
                _options = options;
                _layer = layer;
                _lastState = null;
                CurrentServer = server;
                BytesIn = BytesOut = DownBytesPerSec = UpBytesPerSec = 0;
                ConnectedSinceUtc = null;
            }
            SetState(VpnState.Connecting);

            var elevated = Elevation.IsElevated;
            string exe = null;
            if (elevated)
            {
                exe = ToolLocator.FindOpenVpn();
                if (exe == null) { Fail(session, VpnErrors.OpenVpnMissing, null); return; }
            }
            else if (!ToolLocator.IsInteractiveServiceRunning())
            {
                Fail(session, ToolLocator.FindOpenVpn() == null ? VpnErrors.OpenVpnMissing : VpnErrors.ServiceMissing, null);
                return;
            }

            var sanitized = OvpnSanitizer.Sanitize(server.DecodeOpenVpnConfig());
            if (!sanitized.IsUsable) { Fail(session, VpnErrors.InvalidConfig, null); return; }
            if (sanitized.RemovedDirectives.Count > 0)
                Log.Warn("Removed from " + server.DisplayName + " profile: " + string.Join(", ", sanitized.RemovedDirectives));

            string runDir, profilePath, logPath;
            int port;
            var password = RandomSecret();
            try
            {
                runDir = RunDir;
                foreach (var f in Directory.GetFiles(runDir)) SecureDir.DeleteQuietly(f);
                profilePath = Path.Combine(runDir, "profile.ovpn");
                SecureDir.WriteFile(profilePath, sanitized.Config);
                port = FreeLoopbackPort();
                logPath = NewLogPath();
                CurrentLogPath = logPath;
            }
            catch (Exception ex)
            {
                Log.Error("Preparing the OpenVPN run folder failed", ex);
                Fail(session, VpnErrors.ConnectFailed, ex.Message);
                return;
            }

            // Bridged (TAP) sessions: the IPv6 block routes cannot work there (no Neighbor Discovery answer for
            // their gateway), so IPv6 is blocked with WFP filters instead; see Ipv6Firewall.
            var firewall = false;
            if (Ipv6Firewall.NeededFor(layer, options.BlockIpv6))
            {
                firewall = Ipv6Guard.Instance.Enable(options.AllowLocalLan);
                lock (_gate)
                {
                    if (session != _session || _stopRequested)
                    {
                        Log.Info("Connect cancelled while the IPv6 leak block was being set up");
                        SecureDir.DeleteQuietly(profilePath);
                        if (firewall) Ipv6Guard.Instance.Disable();
                        return;
                    }
                }
                if (!firewall) Log.Warn("Bridged session without the WFP IPv6 block; IPv6 may bypass the tunnel (the IP card shows it)");
            }
            else Ipv6Guard.Instance.Disable();
            Ipv6FirewallActive = firewall;

            var serverIp = TunnelPlan.ServerAddress(sanitized.RemoteHost, server.Ip);
            var args = BuildArguments(profilePath, port, logPath, sanitized, options, layer, serverIp, firewall);
            Log.Info("Starting OpenVPN (" + (elevated ? "direct" : "interactive service") + ", " + LayerName(layer) + ", " +
                     (sanitized.Proto ?? "udp") + ") for " + server.DisplayName + " (" + server.Ip + "); log " + Path.GetFileName(logPath));

            OpenVpnProcess proc;
            InteractiveService svc = null;
            try
            {
                proc = elevated ? StartDirect(exe, runDir, args, password) : StartViaService(runDir, args, password, out svc);
            }
            catch (Exception ex)
            {
                Log.Error("Starting OpenVPN failed", ex);
                SecureDir.DeleteQuietly(profilePath);
                Fail(session, VpnErrors.ConnectFailed, ex.Message);
                return;
            }

            bool owned = false;
            try
            {
                lock (_gate)
                {
                    if (session != _session)
                    {
                        Log.Info("Connect superseded; stopping the openvpn.exe just started");
                    }
                    else
                    {
                        _proc = proc;
                        _service = svc;
                        _timeoutTimer = new Timer(_ => OnConnectTimeout(session), null, ConnectTimeout, Timeout.InfiniteTimeSpan);
                        owned = true;
                    }
                }
                if (!owned)
                {
                    // Nothing has connected to its management interface, so it can only be terminated.
                    proc.TryKill(3000);
                    proc.Dispose();
                    svc?.Dispose();
                    return;
                }

                // The management loop releases the hold; start it before anything else that could fail.
                Task.Run(() => ManagementLoop(session, port, password));

                try { SecureDir.WriteFile(PidFile, proc.Id.ToString()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                proc.Exited += () => OnExited(session, proc);
                proc.StartWatching();
            }
            catch (Exception ex)
            {
                Log.Error("OpenVPN started but monitoring it failed", ex);
                bool current;
                lock (_gate) current = owned && session == _session && _proc == proc;
                if (current)
                {
                    StopInternal();
                    FailAfterStop(VpnErrors.ConnectFailed, ex.Message);
                }
                else if (!owned)
                {
                    proc.TryKill(3000);
                    proc.Dispose();
                    svc?.Dispose();
                }
            }
        }

        /// <summary>
        /// Command-line options come after --config, so they override anything left in the profile.
        /// Only options the Interactive Service allows for members of "OpenVPN Administrators" are used.
        /// </summary>
        static string BuildArguments(string profilePath, int port, string logPath, SanitizedOvpn sanitized, ConnectOptions options, TunnelLayer layer, string serverIp, bool ipv6Firewall)
        {
            var a = new StringBuilder();
            a.Append("--config ").Append(CommandLine.Quote(profilePath));
            a.Append(" --script-security 1");
            a.Append(" --management 127.0.0.1 ").Append(port).Append(" stdin");
            a.Append(" --management-hold --management-query-passwords --auth-retry none");
            a.Append(" --log ").Append(CommandLine.Quote(logPath));
            a.Append(" --verb 3");
            // VPNGate profiles use AES-128-CBC, which ovpn-dco cannot carry; go straight to TAP-Windows6.
            a.Append(" --windows-driver tap-windows6");
            if (sanitized.NeedsAuthUserPass) a.Append(" --auth-user-pass");
            if (!string.IsNullOrEmpty(sanitized.Cipher)) a.Append(" --data-ciphers-fallback ").Append(sanitized.Cipher);
            foreach (var o in TunnelPlan.Options(layer, sanitized.IsUdp, serverIp)) a.Append(' ').Append(o);
            if (options.BlockIpv6)
                foreach (var o in TunnelPlan.Ipv6BlockOptions(ipv6Firewall)) a.Append(' ').Append(o);
            var dns = TunnelPlan.SupportsDnsOverride(layer) ? PublicDns.OpenVpnOptions(options.DnsServers) : Array.Empty<string>();
            if (!TunnelPlan.SupportsDnsOverride(layer) && options.DnsServers != null)
                Log.Info("Bridged session: the chosen DNS cannot be applied; Windows uses the DNS servers of the VPNGate DHCP lease");
            foreach (var o in dns) a.Append(' ').Append(o);
            // Bridged mode always adds --block-outside-dns (TunnelPlan.Options).
            if (options.BlockOutsideDns && dns.Count == 0 && layer == TunnelLayer.Routed) a.Append(" --block-outside-dns");
            if (options.AllowLocalLan)
            {
                a.Append(" --route 10.0.0.0 255.0.0.0 net_gateway");
                a.Append(" --route 172.16.0.0 255.240.0.0 net_gateway");
                a.Append(" --route 192.168.0.0 255.255.0.0 net_gateway");
            }
            return a.ToString();
        }

        static string LayerName(TunnelLayer layer) => layer == TunnelLayer.Bridged ? "bridged/tap" : "routed/tun";

        /// <summary>Path for this attempt's OpenVPN log; older logs beyond <see cref="OpenVpnLogFiles.Keep"/> are deleted first.</summary>
        static string NewLogPath()
        {
            var dir = LogDirectory;
            try
            {
                var names = Directory.GetFiles(dir).Select(Path.GetFileName);
                foreach (var old in OpenVpnLogFiles.ToPruneBeforeNew(names))
                    SecureDir.DeleteQuietly(Path.Combine(dir, old));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var path = Path.Combine(dir, OpenVpnLogFiles.NewName(DateTime.Now));
            for (var i = 1; File.Exists(path) && i < 100; i++)
                path = Path.Combine(dir, Path.GetFileNameWithoutExtension(OpenVpnLogFiles.NewName(DateTime.Now)) + "-" + i + ".log");
            return path;
        }

        static OpenVpnProcess StartViaService(string runDir, string args, string password, out InteractiveService svc)
        {
            svc = new InteractiveService();
            try
            {
                var pid = svc.Start(runDir, args, password + "\n");
                return OpenVpnProcesses.Attach(pid);
            }
            catch
            {
                svc.Dispose();
                svc = null;
                throw;
            }
        }

        static OpenVpnProcess StartDirect(string exe, string runDir, string args, string password)
        {
            // Re-verify through a write-locked handle and keep it open until the process exists,
            // so the file that passed the signature/hash check is the one that is started.
            using (ToolLocator.OpenVerifiedOpenVpn(exe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    // openvpn.exe writes its password prompt to stderr and aborts if that handle is invalid,
                    // which is the case for a child of a GUI process unless both outputs are redirected.
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = runDir,
                };
                var proc = Process.Start(psi) ?? throw new InvalidOperationException("openvpn.exe did not start");
                var wrapped = OpenVpnProcesses.FromOwnChild(proc);
                try
                {
                    proc.OutputDataReceived += (s, e) => { };
                    proc.ErrorDataReceived += (s, e) => { };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.StandardInput.WriteLine(password);
                    proc.StandardInput.Close();
                }
                catch (Exception)
                {
                    wrapped.TryKill(3000);
                    wrapped.Dispose();
                    throw;
                }
                return wrapped;
            }
        }

        public Task DisconnectAsync() => Task.Run(() => StopInternal());

        /// <summary>Asks OpenVPN to exit through the management interface so routes and DNS are restored.</summary>
        void StopInternal()
        {
            OpenVpnProcess proc;
            InteractiveService svc;
            lock (_gate)
            {
                proc = _proc;
                svc = _service;
                _proc = null;
                _service = null;
                _stopRequested = true;
                _session++;
                _timeoutTimer?.Dispose();
                _timeoutTimer = null;
            }
            if (proc != null)
            {
                SetState(VpnState.Disconnecting);
                var signaled = SendManagement("signal SIGTERM");
                if (!signaled) Log.Warn("Management interface not connected; openvpn.exe cannot be asked to exit");
                // Termination is only a last resort; the Interactive Service still undoes its changes then.
                if (!proc.WaitForExit(signaled ? 10000 : 1000))
                {
                    Log.Warn("OpenVPN did not exit" + (signaled ? " after SIGTERM" : "") + "; terminating");
                    if (!proc.TryKill(3000))
                        Log.Error("openvpn.exe pid " + proc.Id + " could not be stopped; it may still hold the TAP adapter");
                }
                proc.Dispose();
            }
            // Closing the service pipe does not stop openvpn.exe; openvpnserv only watches its own pipe to openvpn.exe.
            svc?.Dispose();
            CloseManagement();
            // The tunnel is down, so IPv6 may flow normally again.
            Ipv6Guard.Instance.Disable();
            Ipv6FirewallActive = false;
            try
            {
                foreach (var f in Directory.GetFiles(RunDir)) SecureDir.DeleteQuietly(f);
                SecureDir.DeleteQuietly(PidFile);
            }
            catch (Exception) { }

            lock (_gate)
            {
                ConnectedSinceUtc = null;
                DownBytesPerSec = UpBytesPerSec = 0;
                _errorKey = null;
                _errorDetail = null;
            }
            SetState(VpnState.Disconnected);
        }

        /// <summary>Stops an openvpn.exe left behind by a previous session (crash or forced close). Silent.</summary>
        public static void CleanupOrphan()
        {
            string pidFile = null;
            try
            {
                pidFile = PidFile;
                if (!File.Exists(pidFile)) return;
                if (int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) && pid > 0)
                {
                    using (var p = OpenVpnProcesses.TryAttachOpenVpn(pid))
                    {
                        if (p != null && !p.HasExited)
                        {
                            Log.Info("Stopping orphaned openvpn.exe pid " + pid);
                            if (!p.TryKill(3000)) Log.Warn("Orphaned openvpn.exe pid " + pid + " could not be stopped");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Orphan cleanup failed", ex);
            }
            finally
            {
                try { if (pidFile != null) SecureDir.DeleteQuietly(pidFile); }
                catch (Exception) { }
            }
        }

        void OnConnectTimeout(int session)
        {
            try
            {
                VpnServer server;
                string last;
                lock (_gate)
                {
                    if (session != _session || State == VpnState.Connected) return;
                    server = CurrentServer;
                    last = _lastState;
                }
                var stage = TunnelPlan.StageOf(last);
                Log.Warn("Connect timeout after " + ConnectTimeout.TotalSeconds + "s for " + server?.DisplayName + " (" + LayerName(_layer) +
                         ", last OpenVPN state " + (last ?? "none") + ", stage " + stage + ")");
                StopInternal();
                ConnectTimedOut?.Invoke(server, stage);
            }
            catch (Exception ex)
            {
                Log.Error("Handling the connect timeout failed", ex);
            }
        }

        void OnExited(int session, OpenVpnProcess proc)
        {
            lock (_gate)
            {
                if (session != _session || _stopRequested || _proc != proc) return;
                _proc = null;
                proc.Dispose();
                _timeoutTimer?.Dispose();
                _timeoutTimer = null;
                _service?.Dispose();
                _service = null;
            }
            CloseManagement();
            SecureDir.DeleteQuietly(PidFile);
            Ipv6Guard.Instance.Disable();
            Ipv6FirewallActive = false;

            var wasConnected = State == VpnState.Connected;
            VpnServer server;
            ConnectOptions options;
            bool retryBridged;
            lock (_gate)
            {
                ConnectedSinceUtc = null;
                DownBytesPerSec = UpBytesPerSec = 0;
                if (_errorKey == null) _errorKey = wasConnected ? VpnErrors.ConnectionLost : VpnErrors.ConnectFailed;
                server = CurrentServer;
                options = _options;
                retryBridged = !wasConnected && _errorKey == VpnErrors.ServerRefused && _layer == TunnelLayer.Routed && server != null && options != null;
                if (retryBridged) _bridgedServers.Add(server.Key);
            }
            Log.Warn("openvpn.exe exited (" + LayerName(_layer) + ", last OpenVPN state " + (_lastState ?? "none") + "): " + _errorKey +
                     (string.IsNullOrEmpty(_errorDetail) ? "" : " (" + _errorDetail + ")"));
            if (retryBridged)
            {
                Log.Info("The server refused the routed session; retrying " + server.DisplayName + " in bridged mode");
                Task.Run(() => Connect(server, options, TunnelLayer.Bridged, session));
                return;
            }
            var refused = _errorKey == VpnErrors.ServerRefused || _errorKey == VpnErrors.AuthFailed;
            SetState(refused ? VpnState.Refused : VpnState.Error);
        }

        void Fail(int session, string key, string detail)
        {
            lock (_gate)
            {
                if (session != _session) return;
                _errorKey = key;
                _errorDetail = detail;
            }
            Log.Warn("Connect failed: " + key + (detail == null ? "" : " (" + detail + ")"));
            SetState(VpnState.Error);
        }

        /// <summary>Reports a failure after <see cref="StopInternal"/> has already ended the session.</summary>
        void FailAfterStop(string key, string detail)
        {
            lock (_gate)
            {
                _errorKey = key;
                _errorDetail = detail;
            }
            Log.Warn("Connect failed: " + key + (detail == null ? "" : " (" + detail + ")"));
            SetState(VpnState.Error);
        }

        void Remember(string key, string detail)
        {
            lock (_gate)
            {
                if (key != null && _errorKey == null) _errorKey = key;
                if (detail != null) _errorDetail = detail;
            }
        }

        /// <summary>Status lines that only mean "nothing is running"; never shown as errors.</summary>
        public static bool IsBenignNoise(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            var m = message.Trim().ToLowerInvariant();
            return m.Contains("no process running") || m == "not running." || m == "not running";
        }

        void ManagementLoop(int session, int port, string password)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            TcpClient client = null;
            while (DateTime.UtcNow < deadline && session == _session)
            {
                try
                {
                    client = new TcpClient();
                    client.Connect(IPAddress.Loopback, port);
                    break;
                }
                catch (SocketException)
                {
                    client.Close();
                    client = null;
                    Thread.Sleep(250);
                }
            }
            if (client == null)
            {
                if (session == _session) Remember(null, "management interface unreachable");
                return;
            }

            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                lock (_gate)
                {
                    if (session != _session) { client.Close(); return; }
                    _mgmt = client;
                    _mgmtWriter = writer;
                }
                writer.WriteLine(password);
                writer.WriteLine("state on");
                writer.WriteLine("log on");
                writer.WriteLine("bytecount 1");
                writer.WriteLine("hold release");
                _lastSample = DateTime.UtcNow;
                _lastIn = _lastOut = 0;

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (session != _session) break;
                    HandleManagementLine(session, line);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Error("Management interface loop failed", ex); }
        }

        void HandleManagementLine(int session, string line)
        {
            if (line.StartsWith(">BYTECOUNT:", StringComparison.Ordinal))
            {
                OnByteCount(line.Substring(11));
            }
            else if (line.StartsWith(">STATE:", StringComparison.Ordinal))
            {
                // >STATE:time,STATE,detail,local_ip,remote_ip,...
                var f = line.Substring(7).Split(',');
                if (f.Length > 1 && session == _session) _lastState = f[1];
                if (f.Length > 1 && f[1] == "CONNECTED") OnConnected(session, f.Length > 2 ? f[2] : "");
            }
            else if (line.StartsWith(">LOG:", StringComparison.Ordinal))
            {
                // >LOG:time,flags,message
                var f = line.Substring(5).Split(new[] { ',' }, 3);
                if (f.Length == 3) OnLogLine(f[1], f[2]);
            }
            else if (line.StartsWith(">PASSWORD:Need 'Auth'", StringComparison.Ordinal))
            {
                // VPNGate profiles that ask for credentials use the published vpn / vpn pair.
                SendManagement("username \"Auth\" vpn");
                SendManagement("password \"Auth\" vpn");
            }
            else if (line.StartsWith(">PASSWORD:Verification Failed", StringComparison.Ordinal))
            {
                Remember(VpnErrors.AuthFailed, null);
            }
            else if (line.StartsWith(">FATAL:", StringComparison.Ordinal))
            {
                Remember(null, line.Substring(7).Trim());
            }
            else if (line.StartsWith(">HOLD:", StringComparison.Ordinal))
            {
                SendManagement("hold release");
            }
        }

        void OnConnected(int session, string detail)
        {
            lock (_gate)
            {
                if (session != _session || State == VpnState.Connected) return;
                _timeoutTimer?.Dispose();
                _timeoutTimer = null;
                ConnectedSinceUtc = DateTime.UtcNow;
                _errorKey = null;
                _errorDetail = null;
            }
            if (detail == "ERROR") Log.Warn("OpenVPN connected with errors; see the OpenVPN log");
            SetState(VpnState.Connected);
        }

        void OnLogLine(string flags, string message)
        {
            if (TunnelPlan.IsServerRefusal(message))
                Remember(VpnErrors.ServerRefused, message.Trim());
            else if (message.IndexOf("There are no TAP-Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("currently in use", StringComparison.OrdinalIgnoreCase) >= 0 && message.IndexOf("adapter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("Cannot open TUN/TAP", StringComparison.OrdinalIgnoreCase) >= 0)
                Remember(VpnErrors.DriverMissing, null);
            else if ((flags.Contains("F") || flags.Contains("N")) && !IsBenignNoise(message))
                Remember(null, message.Trim());
        }

        void OnByteCount(string payload)
        {
            var parts = payload.Split(',');
            if (parts.Length < 2 || !long.TryParse(parts[0], out var bin) || !long.TryParse(parts[1], out var bout)) return;
            var now = DateTime.UtcNow;
            var dt = (now - _lastSample).TotalSeconds;
            if (dt > 0.2 && _lastIn > 0)
            {
                DownBytesPerSec = (long)Math.Max(0, (bin - _lastIn) / dt);
                UpBytesPerSec = (long)Math.Max(0, (bout - _lastOut) / dt);
            }
            _lastIn = bin;
            _lastOut = bout;
            _lastSample = now;
            BytesIn = bin;
            BytesOut = bout;
        }

        bool SendManagement(string command)
        {
            lock (_gate)
            {
                if (_mgmtWriter == null) return false;
                try { _mgmtWriter.WriteLine(command); return true; }
                catch (IOException) { return false; }
                catch (ObjectDisposedException) { return false; }
            }
        }

        void CloseManagement()
        {
            lock (_gate)
            {
                try { _mgmt?.Close(); } catch (Exception) { }
                _mgmt = null;
                _mgmtWriter = null;
            }
        }

        void SetState(VpnState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke();
        }

        static int FreeLoopbackPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        static string RandomSecret()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        public void Dispose() => StopInternal();
    }

    public sealed class ConnectOptions
    {
        public bool AllowLocalLan { get; set; }
        public bool BlockOutsideDns { get; set; }
        /// <summary>Keep IPv6 from bypassing the IPv4-only tunnel (<see cref="TunnelPlan.Ipv6BlockOptions"/>).</summary>
        public bool BlockIpv6 { get; set; }
        /// <summary>Resolvers replacing the server's DNS; null keeps what the server pushes.</summary>
        public string[] DnsServers { get; set; }
    }
}
