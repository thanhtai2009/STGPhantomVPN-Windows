using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// App side of the WFP IPv6 leak block (<see cref="Ipv6Firewall"/>). Adding WFP filters needs administrator
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

        readonly object _gate = new object();
        string _token;
        EventWaitHandle _on, _onLan, _off, _quit, _ok, _fail;
        Process _helper;
        ManualResetEvent _helperExit;

        /// <summary>True while the helper confirmed that the filters are in place.</summary>
        public bool IsActive { get; private set; }

        Ipv6Guard() { }

        /// <summary>Turns the block on (starting the helper with a UAC prompt if needed). False if declined or failed.</summary>
        public bool Enable(bool allowLocalLan)
        {
            lock (_gate)
            {
                if (!EnsureHelper()) return false;
                _ok.Reset();
                _fail.Reset();
                (allowLocalLan ? _onLan : _on).Set();
                var r = WaitHandle.WaitAny(new WaitHandle[] { _ok, _fail, _helperExit }, UacWait);
                IsActive = r == 0;
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
                if (_helper == null || !IsActive) return;
                _ok.Reset();
                _fail.Reset();
                _off.Set();
                var r = WaitHandle.WaitAny(new WaitHandle[] { _ok, _fail, _helperExit }, AckWait);
                IsActive = false;
                Log.Info(r == 0 || r == 2 ? "IPv6 leak block (WFP) removed" : "IPv6 leak block: no answer to switch off; the filters go away when the helper exits");
                if (r == 2) ResetHelper();
            }
        }

        bool EnsureHelper()
        {
            if (_helper != null && !_helperExit.WaitOne(0)) return true;
            ResetHelper();
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            _token = string.Concat(bytes.Select(b => b.ToString("x2")));
            _on = Create(Ipv6Firewall.Signal.On);
            _onLan = Create(Ipv6Firewall.Signal.OnLan);
            _off = Create(Ipv6Firewall.Signal.Off);
            _quit = Create(Ipv6Firewall.Signal.Quit);
            _ok = Create(Ipv6Firewall.Signal.Ok);
            _fail = Create(Ipv6Firewall.Signal.Fail);
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
            Log.Info("IPv6 leak block helper started (pid " + _helper.Id + ")");
            return true;
        }

        EventWaitHandle Create(Ipv6Firewall.Signal s) =>
            new EventWaitHandle(false, EventResetMode.AutoReset, Ipv6Firewall.EventName(_token, s));

        void ResetHelper()
        {
            IsActive = false;
            foreach (var h in new WaitHandle[] { _on, _onLan, _off, _quit, _ok, _fail }) h?.Dispose();
            _on = _onLan = _off = _quit = _ok = _fail = null;
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
    }

    /// <summary>
    /// The elevated side: a dynamic WFP session with the app's sublayer and the filters of <see cref="Ipv6Firewall.Plan"/>.
    /// Structures are written into unmanaged memory at the offsets of <see cref="WfpLayout"/>.
    /// </summary>
    sealed class WfpEngine : IDisposable
    {
        IntPtr _engine;

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

        /// <summary>Adds the sublayer and every filter in one transaction; all or nothing.</summary>
        public int AddIpv6Block(bool allowLocalLan)
        {
            var l = new WfpLayout(IntPtr.Size);
            var plan = Ipv6Firewall.Plan(allowLocalLan);
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

                    foreach (var spec in plan)
                    {
                        var f = mem.Alloc(l.FilterSize);
                        Marshal.WriteIntPtr(f, l.FilterDisplay, name);
                        mem.Guid(f, l.FilterLayerKey, spec.Layer);
                        mem.Guid(f, l.FilterSubLayerKey, Ipv6Firewall.SubLayerKey);
                        Marshal.WriteInt32(f, l.FilterWeight, (int)Ipv6Firewall.DataUint8);
                        Marshal.WriteByte(f, l.FilterWeight + WfpLayout.ValueUnion, spec.Weight);
                        Marshal.WriteInt32(f, l.FilterAction, (int)(spec.Permit ? Ipv6Firewall.ActionPermit : Ipv6Firewall.ActionBlock));
                        if (spec.Prefix != null)
                        {
                            var (addr, len) = Ipv6Firewall.ParsePrefix(spec.Prefix);
                            var mask = mem.Alloc(WfpLayout.V6AddrMaskSize);
                            Marshal.Copy(addr, 0, mask, 16);
                            Marshal.WriteByte(mask, 16, len);
                            var cond = mem.Alloc(WfpLayout.ConditionSize);
                            mem.Guid(cond, 0, Ipv6Firewall.ConditionIpRemoteAddress);
                            Marshal.WriteInt32(cond, WfpLayout.ConditionMatchType, (int)Ipv6Firewall.MatchEqual);
                            Marshal.WriteInt32(cond, WfpLayout.ConditionValue, (int)Ipv6Firewall.DataV6AddrMask);
                            Marshal.WriteIntPtr(cond, WfpLayout.ConditionValue + WfpLayout.ValueUnion, mask);
                            Marshal.WriteInt32(f, l.FilterNumConditions, 1);
                            Marshal.WriteIntPtr(f, l.FilterConditions, cond);
                        }
                        Check(FwpmFilterAdd0(_engine, f, IntPtr.Zero, out _), "FwpmFilterAdd0 " + spec);
                    }
                }
                Check(FwpmTransactionCommit0(_engine), "FwpmTransactionCommit0");
                committed = true;
                return plan.Count;
            }
            finally
            {
                if (!committed) FwpmTransactionAbort0(_engine);
            }
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
