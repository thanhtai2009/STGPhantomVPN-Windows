using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using STGPhantomVPN.Core;
using STGPhantomVPN.Services;

namespace STGPhantomVPN.ViewModels
{
    /// <summary>
    /// Public IP on Home: the real address (measured only while no tunnel runs, at startup and before a connect)
    /// and the address seen through the VPN (measured a few seconds after the tunnel comes up), both for IPv4
    /// and IPv6, with a verdict on whether traffic really leaves through the VPN.
    /// </summary>
    public sealed partial class MainViewModel
    {
        /// <summary>Wait after CONNECTED before measuring, so routes and DNS have settled.</summary>
        static readonly TimeSpan VpnIpDelay = TimeSpan.FromSeconds(4);
        /// <summary>A real address younger than this is reused before a connect.</summary>
        static readonly TimeSpan RealIpMaxAge = TimeSpan.FromMinutes(2);
        /// <summary>Longest wait for the real address before a connect starts anyway.</summary>
        static readonly TimeSpan RealIpWaitBeforeConnect = TimeSpan.FromSeconds(3);

        IpInfo _realV4, _realV6, _vpnV4, _vpnV6;
        bool _realFailed, _vpnFailed, _checkingReal, _checkingVpn;
        int _realRun, _vpnRun;
        VpnState _ipState = VpnState.Disconnected;

        public ICommand CheckIpCommand { get; private set; }
        public ICommand CopyRealIpCommand { get; private set; }
        public ICommand CopyVpnIpCommand { get; private set; }

        void InitIp()
        {
            CheckIpCommand = new AsyncCommand(CheckIpAsync, () => !IsCheckingIp);
            CopyRealIpCommand = new RelayCommand(() => CopyIp(_realV4, _realV6), () => _realV4 != null || _realV6 != null);
            CopyVpnIpCommand = new RelayCommand(() => CopyIp(_vpnV4, _vpnV6), () => _vpnV4 != null || _vpnV6 != null);
        }

        public bool IsCheckingIp => _checkingReal || _checkingVpn;

        public string RealIpText => _realV4?.Ip ?? (_checkingReal ? Loc.T("ip_checking_short") : _realV6?.Ip ?? "—");
        public string RealIpDetails => (_realV4 ?? _realV6)?.Details ?? "";
        public string RealIpCode => (_realV4 ?? _realV6)?.CountryCode ?? "";
        public string RealIpv6Text => _realV4 != null && _realV6 != null ? "IPv6 " + _realV6.Ip : "";

        public string VpnIpText => _vpn.State != VpnState.Connected ? "—"
                                 : _vpnV4?.Ip ?? (_checkingVpn || _vpnV4 == null && !_vpnFailed ? Loc.T("ip_checking_short") : "—");
        public string VpnIpDetails => _vpn.State == VpnState.Connected ? _vpnV4?.Details ?? "" : "";
        public string VpnIpCode => _vpn.State == VpnState.Connected ? _vpnV4?.CountryCode ?? "" : "";
        public string VpnIpv6Text => _vpn.State == VpnState.Connected && _vpnV6 != null ? "IPv6 " + _vpnV6.Ip : "";

        /// <summary>"ok", "warn" or "" (neutral), for the verdict colour.</summary>
        public string IpStatusKind { get; private set; } = "";
        public string IpStatusText { get; private set; } = "";

        bool CanMeasureReal => _vpn.State == VpnState.Disconnected || _vpn.State == VpnState.Error || _vpn.State == VpnState.Refused;

        async Task CheckIpAsync()
        {
            if (_vpn.State == VpnState.Connected) await RefreshVpnIpAsync(TimeSpan.Zero);
            else await RefreshRealIpAsync();
        }

        /// <summary>Measures the real address; the answer is dropped if a tunnel started meanwhile.</summary>
        async Task RefreshRealIpAsync()
        {
            if (!CanMeasureReal) return;
            var run = ++_realRun;
            _checkingReal = true;
            RaiseIp();
            try
            {
                var (v4, v6) = await FetchBothAsync();
                if (run != _realRun || !CanMeasureReal) return;
                _realFailed = v4 == null && v6 == null;
                if (!_realFailed)
                {
                    _realV4 = v4;
                    _realV6 = v6;
                }
                Log.Info("Real IP: " + Describe(v4) + " / " + Describe(v6));
            }
            finally
            {
                if (run == _realRun) _checkingReal = false;
                RaiseIp();
            }
        }

        /// <summary>Measures the address seen through the tunnel of the current connection.</summary>
        async Task RefreshVpnIpAsync(TimeSpan delay)
        {
            var since = _vpn.ConnectedSinceUtc;
            var run = ++_vpnRun;
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            if (run != _vpnRun || _vpn.State != VpnState.Connected || _vpn.ConnectedSinceUtc != since) return;
            _checkingVpn = true;
            RaiseIp();
            try
            {
                var (v4, v6) = await FetchBothAsync();
                if (run != _vpnRun || _vpn.State != VpnState.Connected || _vpn.ConnectedSinceUtc != since) return;
                _vpnV4 = v4;
                _vpnV6 = v6;
                _vpnFailed = v4 == null && v6 == null;
                var a = PublicIp.Assess(_realV4, v4, v6, _vpn.CurrentServer?.Ip);
                var line = "VPN IP: " + Describe(v4) + " / " + Describe(v6) + (a.MatchesServer ? " (server address)" : "");
                if (a.HasProblem) Log.Warn(line + (a.V4NotTunneled ? "; IPv4 equals the real address" : "") + (a.V6Leak ? "; IPv6 bypasses the tunnel" : ""));
                else Log.Info(line);
            }
            finally
            {
                if (run == _vpnRun) _checkingVpn = false;
                RaiseIp();
            }
        }

        static async Task<(IpInfo V4, IpInfo V6)> FetchBothAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                var v4 = Task.Run(() => PublicIp.FetchAsync(IpFamily.V4, cts.Token));
                var v6 = Task.Run(() => PublicIp.FetchAsync(IpFamily.V6, cts.Token));
                try { await Task.WhenAll(v4, v6); }
                catch (OperationCanceledException) { }
                return (v4.Status == TaskStatus.RanToCompletion ? v4.Result : null, v6.Status == TaskStatus.RanToCompletion ? v6.Result : null);
            }
        }

        /// <summary>Before a connect: refresh a missing or stale real address, waiting at most a few seconds.</summary>
        async Task EnsureRealIpBeforeConnectAsync()
        {
            if (!CanMeasureReal) return;
            var fresh = _realV4 != null && DateTime.UtcNow - _realV4.FetchedUtc < RealIpMaxAge;
            if (fresh) return;
            await Task.WhenAny(RefreshRealIpAsync(), Task.Delay(RealIpWaitBeforeConnect));
        }

        /// <summary>Follows tunnel transitions: measure through a new tunnel, forget VPN addresses when it ends.</summary>
        void OnIpStateChanged()
        {
            var state = _vpn.State;
            if (state == _ipState) return;
            var was = _ipState;
            _ipState = state;
            if (state == VpnState.Connected)
            {
                _vpnV4 = _vpnV6 = null;
                _vpnFailed = false;
                _ = RefreshVpnIpAsync(VpnIpDelay);
            }
            else if (was == VpnState.Connected)
            {
                _vpnRun++;
                _checkingVpn = false;
                _vpnV4 = _vpnV6 = null;
            }
            RaiseIp();
        }

        void UpdateIpStatus()
        {
            string kind = "", text = "";
            if (_vpn.State == VpnState.Connected)
            {
                if (_checkingVpn || _vpnV4 == null && _vpnV6 == null && !_vpnFailed) text = Loc.T("ip_status_checking");
                else if (_vpnFailed) { kind = "warn"; text = Loc.T("ip_check_failed"); }
                else
                {
                    var a = PublicIp.Assess(_realV4, _vpnV4, _vpnV6, _vpn.CurrentServer?.Ip);
                    if (a.HasProblem)
                    {
                        kind = "warn";
                        var parts = new System.Collections.Generic.List<string>();
                        if (a.V4NotTunneled) parts.Add(Loc.T("ip_status_not_tunneled"));
                        if (a.V6Leak) parts.Add(Loc.F(BlockIpv6 ? "ip_status_v6_leak" : "ip_status_v6_leak_off", _vpnV6.Ip));
                        text = string.Join(" ", parts);
                    }
                    else
                    {
                        kind = "ok";
                        text = Loc.T(a.MatchesServer ? "ip_status_ok_server" : "ip_status_ok");
                    }
                }
            }
            else if (_checkingReal) text = Loc.T("ip_status_checking");
            else if (_realFailed && _realV4 == null && _realV6 == null) { kind = "warn"; text = Loc.T("ip_check_failed"); }
            IpStatusKind = kind;
            IpStatusText = text;
        }

        void RaiseIp()
        {
            UpdateIpStatus();
            foreach (var p in new[] { nameof(IsCheckingIp), nameof(RealIpText), nameof(RealIpDetails), nameof(RealIpCode), nameof(RealIpv6Text),
                                      nameof(VpnIpText), nameof(VpnIpDetails), nameof(VpnIpCode), nameof(VpnIpv6Text), nameof(IpStatusKind), nameof(IpStatusText) })
                Raise(p);
            CommandManager.InvalidateRequerySuggested();
        }

        void CopyIp(IpInfo v4, IpInfo v6)
        {
            var text = v4 != null && v6 != null ? v4.Ip + Environment.NewLine + v6.Ip : (v4 ?? v6)?.Ip;
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException ex) { Log.Warn("Copying the IP failed: " + ex.Message); }
        }

        static string Describe(IpInfo i) => i == null ? "none" : i.Ip + (i.CountryCode != null ? " " + i.CountryCode : "");
    }
}
