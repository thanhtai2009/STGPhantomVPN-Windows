using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// App side of the WFP IPv6 and system-wide kill-switch filters. Adding WFP filters needs administrator
    /// rights, so the first bridged connect of an app run starts "STGPhantomVPN.exe --elevated ipv6-guard &lt;pid&gt;
    /// &lt;token&gt;" through UAC. That helper stays running without a window and turns the block on and off when the
    /// app signals it, so later connects in the same run need no prompt. The filters live in the helper's dynamic
    /// WFP session: they disappear when it switches the block off, when the app exits or crashes (the helper
    /// watches the app process) and when the helper itself ends for any reason.
    /// </summary>
    public sealed class Ipv6Guard : IDisposable
    {
        public static readonly Ipv6Guard Instance = new Ipv6Guard();

        static readonly TimeSpan UacWait = TimeSpan.FromMinutes(2);
        static readonly TimeSpan AckWait = TimeSpan.FromSeconds(10);
        const int StateMapSize = 1024;
        const int ErrorOffset = 64;
        const int ErrorCapacity = StateMapSize - ErrorOffset - 4;

        readonly object _gate = new object();
        string _token;
        EventWaitHandle _update, _quit, _ok, _fail;
        Process _helper;
        ManualResetEvent _helperExit;
        MemoryMappedFile _stateMap;
        MemoryMappedViewAccessor _stateView;
        bool _ipv6Enabled, _ipv6AllowLan, _killEnabled, _killAllowLan;
        volatile bool _killSwitchRequested;
        IPAddress _killEndpoint;
        ushort _killPort;
        byte _killProtocol;
        ulong _tunnelLuid;
        bool _recoveryAttempted;

        /// <summary>True while the helper confirmed that the filters are in place.</summary>
        public bool IsActive { get; private set; }
        public bool KillSwitchRequested => _killSwitchRequested;

        Ipv6Guard() { }

        public void SetKillSwitchRequested(bool enabled)
        {
            lock (_gate)
            {
                _killSwitchRequested = enabled;
                if (!enabled) DisableKillSwitch();
            }
        }

        /// <summary>Turns the block on (starting the helper with a UAC prompt if needed). False if declined or failed.</summary>
        public bool Enable(bool allowLocalLan)
        {
            lock (_gate)
            {
                _ipv6Enabled = true;
                _ipv6AllowLan = allowLocalLan;
                var r = ApplyState(UacWait);
                IsActive = r == 0;
                if (r == 0) _recoveryAttempted = false;
                if (r == 0) Log.Info("IPv6 leak block (WFP) active" + (allowLocalLan ? ", unique local LAN addresses allowed" : ""));
                else Log.Warn("IPv6 leak block (WFP) could not be enabled: " + (r == 1 ? "helper reported a WFP error (see the admin log)" : r == 2 ? "helper exited" : "no answer"));
                if (r == 2) ResetHelper();
                return IsActive;
            }
        }

        /// <summary>Removes the filters; the helper keeps running for the next connect. Quiet when nothing is active.</summary>
        public void Disable()
        {
            lock (_gate)
            {
                if (_helper == null || !_ipv6Enabled) return;
                _ipv6Enabled = false;
                var r = ApplyState(AckWait);
                IsActive = false;
                Log.Info(r == 0 || r == 2 ? "IPv6 leak block (WFP) removed" : "IPv6 leak block: no answer to switch off; the filters go away when the helper exits");
                if (r == 2) ResetHelper();
            }
        }

        /// <summary>Arms the system-wide kill switch before OpenVPN starts. While disconnected only this VPN endpoint is allowed.</summary>
        public bool EnableKillSwitch(IPAddress endpoint, ushort port, byte protocol, bool allowLocalLan)
        {
            if (!_killSwitchRequested || endpoint == null || endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || port == 0 || (protocol != 6 && protocol != 17))
                return false;
            lock (_gate)
            {
                if (!_killSwitchRequested) return false;
                _killEnabled = true;
                _killAllowLan = allowLocalLan;
                _killEndpoint = endpoint;
                _killPort = port;
                _killProtocol = protocol;
                _tunnelLuid = 0;
                var r = ApplyState(UacWait);
                if (r == 0) { _recoveryAttempted = false; Log.Info("System-wide WFP kill switch active; VPN endpoint allowed"); }
                else Log.Warn("System-wide WFP kill switch could not be enabled");
                return r == 0;
            }
        }

        /// <summary>Allows traffic only on the connected tunnel interface while retaining the physical endpoint exception.</summary>
        public bool SetKillSwitchTunnel(ulong interfaceLuid)
        {
            lock (_gate)
            {
                if (!_killEnabled) return false;
                _tunnelLuid = interfaceLuid;
                return ApplyState(AckWait) == 0;
            }
        }

        /// <summary>Removes the system-wide kill switch after OpenVPN has stopped.</summary>
        public void DisableKillSwitch()
        {
            lock (_gate)
            {
                if (!_killEnabled) return;
                _killEnabled = false;
                _tunnelLuid = 0;
                var r = ApplyState(AckWait);
                if (r != 0) Log.Warn("Kill switch removal was not confirmed; the dynamic WFP filters remain until the helper exits");
            }
        }

        int ApplyState(TimeSpan timeout)
        {
            if (!EnsureHelper()) return 1;
            WriteState();
            _ok.Reset();
            _fail.Reset();
            _update.Set();
            var result = WaitHandle.WaitAny(new WaitHandle[] { _ok, _fail, _helperExit }, timeout);
            if (result == 1)
            {
                var detail = ReadError(_token);
                if (!string.IsNullOrWhiteSpace(detail)) Log.Warn("WFP helper detail: " + detail);
            }
            return result;
        }

        bool EnsureHelper()
        {
            if (_helper != null && !_helperExit.WaitOne(0)) return true;
            ResetHelper();
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            _token = string.Concat(bytes.Select(b => b.ToString("x2")));
            _update = Create(Ipv6Firewall.Signal.On);
            _quit = Create(Ipv6Firewall.Signal.Quit);
            _ok = Create(Ipv6Firewall.Signal.Ok);
            _fail = Create(Ipv6Firewall.Signal.Fail);
            try { _stateMap = MemoryMappedFile.CreateOrOpen(StateName(_token), StateMapSize, MemoryMappedFileAccess.ReadWrite); _stateView = _stateMap.CreateViewAccessor(); }
            catch (Exception ex) { Log.Error("Creating WFP helper state failed", ex); ResetHelper(); return false; }
            var psi = new ProcessStartInfo(HelperLocator.ExePath,
                "--elevated " + CommandLine.Join(Ipv6Firewall.HelperVerb, Process.GetCurrentProcess().Id.ToString(), _token))
            {
                UseShellExecute = true,
                Verb = Elevation.IsElevated ? null : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                _helper = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Warn("UAC prompt for the IPv6 leak block was declined");
                ResetHelper();
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Starting the IPv6 leak block helper failed", ex);
                ResetHelper();
                return false;
            }
            if (_helper == null) { ResetHelper(); return false; }
            _helperExit = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(_helper.Handle, false) };
            _helper.EnableRaisingEvents = true;
            _helper.Exited += OnHelperExited;
            Log.Info("WFP network guard helper started (pid " + _helper.Id + ")");
            return true;
        }

        void OnHelperExited(object sender, EventArgs e)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(sender, _helper)) return;
                var recover = _ipv6Enabled || _killEnabled;
                ResetHelper();
                if (!recover) return;
                if (_recoveryAttempted)
                {
                    Log.Error("WFP network guard helper exited again; protection could not be restored");
                    return;
                }
                _recoveryAttempted = true;
                Log.Error("WFP network guard helper exited unexpectedly; attempting to restore protection");
                var r = ApplyState(UacWait);
                if (r == 0)
                {
                    Log.Warn("WFP network guard protection restored after helper restart");
                }
                else Log.Error("WFP network guard protection was not restored");
            }
        }

        void WriteState()
        {
            if (_stateView == null) return;
            _stateView.Write(0, _ipv6Enabled);
            _stateView.Write(1, _ipv6AllowLan);
            _stateView.Write(2, _killEnabled);
            _stateView.Write(3, _killAllowLan);
            _stateView.Write(8, unchecked((long)_tunnelLuid));
            _stateView.Write(16, _killEndpoint == null ? 0 : _killEndpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 4 : 6);
            _stateView.Write(20, _killPort);
            _stateView.Write(22, _killProtocol);
            _stateView.Write(ErrorOffset, 0);
            var addr = _killEndpoint?.GetAddressBytes() ?? new byte[4];
            var padded = new byte[16];
            Buffer.BlockCopy(addr, 0, padded, 0, Math.Min(addr.Length, padded.Length));
            _stateView.WriteArray(24, padded, 0, padded.Length);
            _stateView.Flush();
        }

        static string StateName(string token) => @"Local\STGPhantomVPN-v6-" + token + "-state";

        EventWaitHandle Create(Ipv6Firewall.Signal s) =>
            new EventWaitHandle(false, EventResetMode.AutoReset, Ipv6Firewall.EventName(_token, s));

        void ResetHelper()
        {
            IsActive = false;
            foreach (var h in new WaitHandle[] { _update, _quit, _ok, _fail }) h?.Dispose();
            _update = _quit = _ok = _fail = null;
            _stateView?.Dispose(); _stateView = null;
            _stateMap?.Dispose(); _stateMap = null;
            _helperExit = null; // shares the process handle; disposed with the Process
            _helper?.Dispose();
            _helper = null;
        }

        /// <summary>App exit: asks the helper to end, which closes its WFP session (it would also notice the app exiting).</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_helper == null) return;
                try { _quit.Set(); } catch (ObjectDisposedException) { }
                ResetHelper();
            }
        }

        internal static (bool Ipv6, bool Ipv6Lan, bool Kill, bool KillLan, ulong TunnelLuid, IPAddress Endpoint, ushort Port, byte Protocol) ReadState(string token)
        {
            using (var map = MemoryMappedFile.OpenExisting(StateName(token), MemoryMappedFileRights.Read))
            using (var view = map.CreateViewAccessor(0, 64, MemoryMappedFileAccess.Read))
            {
                var v6 = view.ReadBoolean(0); var v6Lan = view.ReadBoolean(1);
                var kill = view.ReadBoolean(2); var killLan = view.ReadBoolean(3);
                var luid = unchecked((ulong)view.ReadInt64(8));
                var family = view.ReadInt32(16); var port = view.ReadUInt16(20); var proto = view.ReadByte(22);
                var addr = new byte[16]; view.ReadArray(24, addr, 0, addr.Length);
                IPAddress endpoint = family == 4 ? new IPAddress(addr.Take(4).ToArray()) : family == 6 ? new IPAddress(addr) : null;
                return (v6, v6Lan, kill, killLan, luid, endpoint, port, proto);
            }
        }

        internal static void WriteError(string token, string error)
        {
            var bytes = Encoding.UTF8.GetBytes(error ?? "WFP helper failed");
            if (bytes.Length > ErrorCapacity) Array.Resize(ref bytes, ErrorCapacity);
            using (var map = MemoryMappedFile.OpenExisting(StateName(token), MemoryMappedFileRights.ReadWrite))
            using (var view = map.CreateViewAccessor(ErrorOffset, ErrorCapacity + 4, MemoryMappedFileAccess.ReadWrite))
            {
                view.Write(0, bytes.Length);
                view.WriteArray(4, bytes, 0, bytes.Length);
                view.Flush();
            }
        }

        static string ReadError(string token)
        {
            try
            {
                using (var map = MemoryMappedFile.OpenExisting(StateName(token), MemoryMappedFileRights.Read))
                using (var view = map.CreateViewAccessor(ErrorOffset, ErrorCapacity + 4, MemoryMappedFileAccess.Read))
                {
                    var length = view.ReadInt32(0);
                    if (length <= 0 || length > ErrorCapacity) return null;
                    var bytes = new byte[length];
                    view.ReadArray(4, bytes, 0, length);
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// The elevated side: a dynamic WFP session with the app's sublayer and filters for the IPv6 guard and kill switch.
    /// Structures are written into unmanaged memory at the offsets of <see cref="WfpLayout"/>.
    /// </summary>
    sealed class WfpEngine : IDisposable
    {
        IntPtr _engine;
        readonly System.Collections.Generic.List<ulong> _filterIds = new System.Collections.Generic.List<ulong>();
        bool _sublayerAdded;

        WfpEngine(IntPtr engine) { _engine = engine; }

        public static WfpEngine OpenDynamic()
        {
            var l = new WfpLayout(IntPtr.Size);
            using (var mem = new Native())
            {
                var session = mem.Alloc(l.SessionSize);
                Marshal.WriteInt32(session, l.SessionFlags, (int)Ipv6Firewall.SessionFlagDynamic);
                Check(FwpmEngineOpen0(null, Ipv6Firewall.RpcCAuthnWinNt, IntPtr.Zero, session, out var engine), "FwpmEngineOpen0");
                return new WfpEngine(engine);
            }
        }

        /// <summary>Atomically replaces this app's filters. A failed update leaves the old block in place.</summary>
        public int ReplaceFilters(bool blockIpv6, bool ipv6AllowLan, bool killSwitch, bool killAllowLan,
            IPAddress endpoint, ushort port, byte protocol, ulong tunnelLuid)
        {
            var l = new WfpLayout(IntPtr.Size);
            var plan = new System.Collections.Generic.List<KillSwitchFilterSpec>();
            if (blockIpv6)
                foreach (var f in Ipv6Firewall.Plan(ipv6AllowLan))
                {
                    var spec = new KillSwitchFilterSpec { Layer = f.Layer, LayerName = f.LayerName, Permit = f.Permit, Weight = f.Weight };
                    if (f.Prefix != null) spec.Conditions.Add(new WfpConditionSpec { Field = Ipv6Firewall.ConditionIpRemoteAddress,
                        ValueType = Ipv6Firewall.DataV6AddrMask, Bytes = PrefixBytes(f.Prefix) });
                    plan.Add(spec);
                }
            if (killSwitch)
                plan.AddRange(Ipv6Firewall.KillSwitchPlan(killAllowLan, endpoint, port, protocol, tunnelLuid));

            if (!_sublayerAdded) AddSublayer(l);
            Check(FwpmTransactionBegin0(_engine, 0), "FwpmTransactionBegin0");
            var committed = false;
            try
            {
                using (var mem = new Native())
                {
                    foreach (var id in _filterIds) Check(WfpFilterApi.DeleteById(_engine, id), "WFP filter cleanup");
                    var name = mem.String(Ipv6Firewall.DisplayName);
                    var newIds = new System.Collections.Generic.List<ulong>();
                    foreach (var spec in plan)
                    {
                        var f = mem.Alloc(l.FilterSize);
                        Marshal.WriteIntPtr(f, l.FilterDisplay, name);
                        mem.Guid(f, l.FilterLayerKey, spec.Layer);
                        mem.Guid(f, l.FilterSubLayerKey, Ipv6Firewall.SubLayerKey);
                        Marshal.WriteInt32(f, l.FilterWeight, (int)Ipv6Firewall.DataUint8);
                        Marshal.WriteByte(f, l.FilterWeight + WfpLayout.ValueUnion, spec.Weight);
                        Marshal.WriteInt32(f, l.FilterAction, (int)(spec.Permit ? Ipv6Firewall.ActionPermit : Ipv6Firewall.ActionBlock));
                        var conditions = spec.Conditions.Where(c => c != null).ToList();
                        if (conditions.Count > 0)
                        {
                            var arr = mem.Alloc(WfpLayout.ConditionSize * conditions.Count);
                            for (var i = 0; i < conditions.Count; i++) WriteCondition(mem, arr + i * WfpLayout.ConditionSize, conditions[i]);
                            Marshal.WriteInt32(f, l.FilterNumConditions, conditions.Count);
                            Marshal.WriteIntPtr(f, l.FilterConditions, arr);
                        }
                        Check(FwpmFilterAdd0(_engine, f, IntPtr.Zero, out var id), "FwpmFilterAdd0 " + spec);
                        newIds.Add(id);
                    }
                    Check(FwpmTransactionCommit0(_engine), "FwpmTransactionCommit0");
                    committed = true;
                    _filterIds.Clear();
                    _filterIds.AddRange(newIds);
                    return plan.Count;
                }
            }
            finally
            {
                if (!committed) FwpmTransactionAbort0(_engine);
            }
        }

        static byte[] PrefixBytes(string prefix)
        {
            var (address, length) = Ipv6Firewall.ParsePrefix(prefix);
            var raw = new byte[17];
            Buffer.BlockCopy(address, 0, raw, 0, 16);
            raw[16] = length;
            return raw;
        }

        void AddSublayer(WfpLayout l)
        {
            Check(FwpmTransactionBegin0(_engine, 0), "FwpmTransactionBegin0");
            var committed = false;
            try
            {
                using (var mem = new Native())
                {
                    var name = mem.String(Ipv6Firewall.DisplayName);
                    var sub = mem.Alloc(l.SubLayerSize);
                    mem.Guid(sub, 0, Ipv6Firewall.SubLayerKey);
                    Marshal.WriteIntPtr(sub, l.SubLayerDisplay, name);
                    Marshal.WriteInt16(sub, l.SubLayerWeight, unchecked((short)Ipv6Firewall.SubLayerWeight));
                    Check(FwpmSubLayerAdd0(_engine, sub, IntPtr.Zero), "FwpmSubLayerAdd0");
                }
                Check(FwpmTransactionCommit0(_engine), "FwpmTransactionCommit0");
                committed = true;
                _sublayerAdded = true;
            }
            finally { if (!committed) FwpmTransactionAbort0(_engine); }
        }

        static void WriteCondition(Native mem, IntPtr cond, WfpConditionSpec c)
        {
            mem.Guid(cond, 0, c.Field);
            Marshal.WriteInt32(cond, WfpLayout.ConditionMatchType, (int)Ipv6Firewall.MatchEqual);
            Marshal.WriteInt32(cond, WfpLayout.ConditionValue, unchecked((int)c.ValueType));
            if (c.ValueType == Ipv6Firewall.DataV6AddrMask || c.ValueType == 0x100)
            {
                var p = mem.Alloc(c.Bytes.Length);
                Marshal.Copy(c.Bytes, 0, p, c.Bytes.Length);
                Marshal.WriteIntPtr(cond, WfpLayout.ConditionValue + WfpLayout.ValueUnion, p);
            }
            else if (c.ValueType == 1) Marshal.WriteByte(cond, WfpLayout.ConditionValue + WfpLayout.ValueUnion, c.UInt8);
            else if (c.ValueType == 2) Marshal.WriteInt16(cond, WfpLayout.ConditionValue + WfpLayout.ValueUnion, unchecked((short)c.UInt16));
            else if (c.ValueType == 4)
            {
                var p = mem.Alloc(8);
                Marshal.WriteInt64(p, unchecked((long)c.UInt64));
                Marshal.WriteIntPtr(cond, WfpLayout.ConditionValue + WfpLayout.ValueUnion, p);
            }
            else throw new InvalidOperationException("Unsupported WFP condition value type " + c.ValueType);
        }

        static void Check(uint rc, string what)
        {
            if (rc != 0) throw new Win32Exception(unchecked((int)rc), what + " failed: 0x" + rc.ToString("X8"));
        }

        /// <summary>Closing a dynamic session deletes its sublayer and filters.</summary>
        public void Dispose()
        {
            if (_engine == IntPtr.Zero) return;
            FwpmEngineClose0(_engine);
            _engine = IntPtr.Zero;
        }

        /// <summary>Zeroed unmanaged allocations freed together.</summary>
        sealed class Native : IDisposable
        {
            readonly System.Collections.Generic.List<IntPtr> _blocks = new System.Collections.Generic.List<IntPtr>();

            public IntPtr Alloc(int size)
            {
                var p = Marshal.AllocHGlobal(size);
                _blocks.Add(p);
                Marshal.Copy(new byte[size], 0, p, size);
                return p;
            }

            public IntPtr String(string s)
            {
                var p = Marshal.StringToHGlobalUni(s);
                _blocks.Add(p);
                return p;
            }

            public void Guid(IntPtr p, int offset, Guid g) => Marshal.Copy(g.ToByteArray(), 0, p + offset, 16);

            public void Dispose()
            {
                foreach (var p in _blocks) Marshal.FreeHGlobal(p);
                _blocks.Clear();
            }
        }

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        static extern uint FwpmEngineOpen0(string serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmEngineClose0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, IntPtr subLayer, IntPtr sd);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmFilterAdd0(IntPtr engineHandle, IntPtr filter, IntPtr sd, out ulong id);

    }
}
