using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using STGPhantomVPN.Core;
using STGPhantomVPN.Services;
using STGPhantomVPN.Views;

namespace STGPhantomVPN.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        public const int TabHome = 0, TabServers = 1;

        readonly AppSettings _settings;
        readonly VpnGateClient _gate = new VpnGateClient(AppPaths.ServerCacheFile, AppPaths.BundledServerList);
        readonly OpenVpnController _vpn = new OpenVpnController();
        readonly DispatcherTimer _ticker;
        readonly HashSet<string> _timedOut = new HashSet<string>(StringComparer.Ordinal);
        readonly Dispatcher _ui;

        List<VpnServer> _all = new List<VpnServer>();
        DateTime? _lastNetworkListUtc;
        CancellationTokenSource _refreshCts;
        Task _refreshTask = Task.CompletedTask;

        public Loc L => Loc.Instance;

        public MainViewModel(AppSettings settings)
        {
            _settings = settings;
            _ui = Application.Current.Dispatcher;

            _vpn.StateChanged += () => _ui.BeginInvoke(new Action(OnVpnStateChanged));
            _vpn.ConnectTimedOut += (s, stage) => _ui.BeginInvoke(new Action(() => OnConnectTimedOut(s, stage)));
            Loc.Instance.PropertyChanged += (s, e) => { RefreshTexts(); BuildDnsChoices(); RaiseDns(); };

            ConnectToggleCommand = new AsyncCommand(ConnectToggleAsync, () => !IsBusy);
            ConnectServerCommand = new AsyncCommand(p => SelectAndConnectAsync(p as VpnServer), p => p is VpnServer);
            RefreshCommand = new RelayCommand(() => RefreshServers(), () => !IsLoading);
            GoToServersCommand = new RelayCommand(() => SelectedTab = TabServers);
            TryAnotherServerCommand = new AsyncCommand(TryAnotherServerAsync);
            TimeoutServerListCommand = new RelayCommand(() => { CloseTimeoutDialog(); SelectedTab = TabServers; });
            TimeoutCancelCommand = new RelayCommand(() => { CloseTimeoutDialog(); _timedOut.Clear(); });
            InstallOpenVpnCommand = new AsyncCommand(InstallOpenVpnAsync, () => !IsInstallingOpenVpn);
            TapRepairCommand = new AsyncCommand(RepairTapAsync, () => !IsTapBusy);
            TapDismissCommand = new RelayCommand(DismissTapDialog, () => !IsTapBusy);
            OpenLogsCommand = new RelayCommand(() => OpenShell(System.IO.Path.GetDirectoryName(Log.FilePath)));
            ShowLogsCommand = new RelayCommand(ShowLogs);
            InitIp();
            ClearLogsCommand = new RelayCommand(ClearLogs);
            OpenLicensesCommand = new RelayCommand(() => OpenShell(System.IO.Path.Combine(AppPaths.AppDir, "licenses")));
            FeedbackCommand = new RelayCommand(SendFeedback);
            SetSortCommand = new RelayCommand(p => { if (p is ServerSort s) Sort = s; });
            PrereqRecheckCommand = new AsyncCommand(RecheckPrerequisitesAsync, () => !IsPrereqBusy);
            PrereqDismissCommand = new RelayCommand(() => PrereqDialogOpen = false, () => !IsPrereqBusy);
            ShowPrereqCommand = new RelayCommand(OpenPrereqDialog);
            ReconnectCommand = new AsyncCommand(ReconnectAsync, () => !IsBusy);
            OpenUpdateLinkCommand = new RelayCommand(p => { if (p is string url && Prerequisites.IsTrustedUpdateLink(url)) OpenShell(url); });

            _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _ticker.Tick += (s, e) => Tick();
            _ticker.Start();

            BuildDnsChoices();
            RefreshTexts();
        }

        /// <summary>Loads cached servers, starts a network refresh and probes for OpenVPN. Never shows an error on its own.</summary>
        public void Start()
        {
            Task.Run(() => OpenVpnController.CleanupOrphan());
            var initial = _gate.LoadInitial();
            if (initial.Count > 0) ApplyServerList(initial);
            RefreshServers();
            _ = StartupChecksAsync();
            _ = RefreshRealIpAsync();
        }

        bool _shutDown;

        /// <summary>Stops the timers and openvpn.exe (gracefully, see OpenVpnController). Safe to call more than once.</summary>
        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            _ticker.Stop();
            _refreshCts?.Cancel();
            _vpn.Dispose();
        }

        #region Navigation

        int _selectedTab;
        public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }

        #endregion

        #region Server list

        List<VpnServer> _filtered = new List<VpnServer>();
        public List<VpnServer> FilteredServers { get => _filtered; private set => Set(ref _filtered, value); }

        string _query = "";
        public string Query { get => _query; set { if (Set(ref _query, value)) ApplyFilter(); } }

        ServerSort _sort = ServerSort.Score;
        public ServerSort Sort { get => _sort; set { if (Set(ref _sort, value)) ApplyFilter(); } }

        bool _isLoading;
        public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) Raise(nameof(ShowListSpinner)); } }
        public bool ShowListSpinner => IsLoading && _all.Count == 0;

        string _serversError;
        public string ServersError { get => _serversError; private set { if (Set(ref _serversError, value)) Raise(nameof(HasServersError)); } }
        public bool HasServersError => !string.IsNullOrEmpty(ServersError);

        public string ServerCountText => Loc.F("servers_count", _filtered.Count);
        public bool ShowEmptyList => !IsLoading && _filtered.Count == 0;

        VpnServer _selected;
        public VpnServer SelectedServer
        {
            get => _selected;
            set
            {
                if (value == null || !Set(ref _selected, value)) return;
                _settings.SelectedHost = value.HostName;
                _settings.SelectedIp = value.Ip;
                SettingsStore.Save(_settings);
                RefreshTexts();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ConnectServerCommand { get; }
        public ICommand SetSortCommand { get; }
        public ICommand GoToServersCommand { get; }

        public void RefreshServers()
        {
            _refreshCts?.Cancel();
            var cts = new CancellationTokenSource();
            _refreshCts = cts;
            IsLoading = true;
            ServersError = null;
            _refreshTask = RefreshCoreAsync(cts);
        }

        async Task RefreshCoreAsync(CancellationTokenSource cts)
        {
            try
            {
                var list = await _gate.FetchAsync(cts.Token);
                if (cts.IsCancellationRequested) return;
                _lastNetworkListUtc = DateTime.UtcNow;
                ApplyServerList(list);
            }
            catch (Exception ex) when (!cts.IsCancellationRequested)
            {
                Log.Warn("VPNGate refresh failed: " + ex.GetType().Name + ": " + ex.Message);
                // A cached or bundled list stays usable; only report when there is nothing to show.
                if (_all.Count == 0)
                    ServersError = ex is TaskCanceledException ? Loc.T("error_load_servers_timeout") : Loc.T("error_load_servers") + " (" + ex.Message + ")";
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_refreshCts == cts) IsLoading = false;
                Raise(nameof(ShowEmptyList));
            }
        }

        void ApplyServerList(List<VpnServer> list)
        {
            _all = list;
            Raise(nameof(ShowListSpinner));
            RestoreSelection();
            ApplyFilter();
        }

        void RestoreSelection()
        {
            var host = _selected?.HostName ?? _settings.SelectedHost;
            var ip = _selected?.Ip ?? _settings.SelectedIp;
            var found = _all.FirstOrDefault(s => s.HostName == host && s.Ip == ip)
                        ?? _all.FirstOrDefault(s => !string.IsNullOrEmpty(host) && s.HostName == host)
                        ?? _all.FirstOrDefault(s => !string.IsNullOrEmpty(ip) && s.Ip == ip);
            SelectedServer = found ?? ServerPicker.PickDefault(_all);
        }

        void ApplyFilter()
        {
            FilteredServers = ServerPicker.FilterAndSort(_all, _query, _sort);
            Raise(nameof(ServerCountText));
            Raise(nameof(ShowEmptyList));
        }

        #endregion

        #region Connection

        public VpnState State => _vpn.State;
        public bool IsConnected => _vpn.State == VpnState.Connected;
        public bool IsBusy => _vpn.State == VpnState.Connecting || _vpn.State == VpnState.Disconnecting;

        string _statusTitle, _statusSubtitle, _homeError, _connectLabel, _selectedTitle, _selectedSubtitle;
        public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
        public string StatusSubtitle { get => _statusSubtitle; private set => Set(ref _statusSubtitle, value); }
        public string HomeError { get => _homeError; private set { if (Set(ref _homeError, value)) Raise(nameof(HasHomeError)); } }
        public bool HasHomeError => !string.IsNullOrEmpty(HomeError);
        public string ConnectLabel { get => _connectLabel; private set => Set(ref _connectLabel, value); }
        public string SelectedTitle { get => _selectedTitle; private set => Set(ref _selectedTitle, value); }
        public string SelectedSubtitle { get => _selectedSubtitle; private set => Set(ref _selectedSubtitle, value); }
        public string SelectedCode => _selected?.CountryCode ?? "—";

        string _connectionTime = "00:00:00", _dataTransferred = "0 B", _down = "—", _up = "—";
        public string ConnectionTime { get => _connectionTime; private set => Set(ref _connectionTime, value); }
        public string DataTransferred { get => _dataTransferred; private set => Set(ref _dataTransferred, value); }
        public string DownSpeed { get => _down; private set => Set(ref _down, value); }
        public string UpSpeed { get => _up; private set => Set(ref _up, value); }

        /// <summary>Transient notice (e.g. "refreshing before connect"); cleared by every new action.</summary>
        string _notice;

        public ICommand ConnectToggleCommand { get; }

        async Task ConnectToggleAsync()
        {
            if (_vpn.State == VpnState.Connected)
            {
                _timedOut.Clear();
                _notice = null;
                await _vpn.DisconnectAsync();
                RefreshTexts();
                return;
            }
            if (_selected == null)
            {
                SetNotice(Loc.T("error_select_server"));
                return;
            }
            await ConnectAsync(_selected);
        }

        async Task SelectAndConnectAsync(VpnServer server) => await SelectAndConnectAsync(server, false);

        async Task SelectAndConnectAsync(VpnServer server, bool automatic)
        {
            if (server == null) return;
            SelectedServer = server;
            SelectedTab = TabHome;
            await ConnectAsync(server, automatic);
        }

        /// <summary>The running attempt was started by the app after a refusal, not by the user.</summary>
        bool _automaticAttempt;
        /// <summary>Shown under the status while the automatic attempt runs.</summary>
        string _connectNote;

        async Task ConnectAsync(VpnServer server, bool automatic = false)
        {
            _notice = null;
            _automaticAttempt = automatic;
            if (!automatic) _connectNote = null;
            if (!await EnsurePrerequisitesAsync()) return;
            if (_openVpnPath == null) await DetectOpenVpnAsync();
            if (_openVpnPath != null && !await EnsureTapReadyAsync()) return;

            if (_lastNetworkListUtc == null && _gate.IsCacheStale)
            {
                // Bundled or stale list: refresh first so the profile is current (Android ensureFreshServer).
                SetNotice(Loc.T("refreshing_servers_before_connect"));
                if (!IsLoading) RefreshServers();
                await _refreshTask;
                server = _all.FirstOrDefault(s => s.Key == server.Key) ?? _all.FirstOrDefault(s => s.HostName == server.HostName) ?? _selected ?? server;
                SelectedServer = server;
                _notice = null;
            }

            await EnsureRealIpBeforeConnectAsync();
            var options = CurrentConnectOptions();
            if (_settings.DnsProvider == PublicDns.CustomId && options.DnsServers == null)
                Log.Warn("Custom DNS is not a valid public IPv4 address; using the server's DNS");
            _appliedOptions = OptionsKey(options);
            Raise(nameof(ConnectionSettingsPending));
            await _vpn.ConnectAsync(server, options);
            RefreshTexts();
        }

        ConnectOptions CurrentConnectOptions() => new ConnectOptions
        {
            AllowLocalLan = _settings.AllowLocalLan,
            BlockOutsideDns = _settings.BlockOutsideDns,
            BlockIpv6 = !_settings.Ipv6BlockOff,
            DnsServers = PublicDns.Resolvers(_settings.DnsProvider, _settings.CustomDns1, _settings.CustomDns2),
        };

        static string OptionsKey(ConnectOptions o) =>
            o.AllowLocalLan + "|" + o.BlockOutsideDns + "|" + o.BlockIpv6 + "|" + (o.DnsServers == null ? "" : string.Join(",", o.DnsServers));

        string _appliedOptions;

        /// <summary>Connected with connection settings that have changed since; they apply on the next connect.</summary>
        public bool ConnectionSettingsPending =>
            _vpn.State == VpnState.Connected && _appliedOptions != null && _appliedOptions != OptionsKey(CurrentConnectOptions());

        public ICommand ReconnectCommand { get; }

        async Task ReconnectAsync()
        {
            var server = _vpn.CurrentServer ?? _selected;
            if (server == null || IsBusy) return;
            await _vpn.DisconnectAsync();
            await ConnectAsync(server);
        }

        public async Task StopOpenVpnIfActiveAsync()
        {
            if (_vpn.State != VpnState.Disconnected && _vpn.State != VpnState.Error && _vpn.State != VpnState.Refused)
            {
                await _vpn.DisconnectAsync();
                RefreshTexts();
            }
        }

        public bool IsOpenVpnActive => _vpn.State == VpnState.Connected || _vpn.State == VpnState.Connecting;

        void OnVpnStateChanged()
        {
            if (_vpn.State == VpnState.Connected) _timedOut.Clear();
            Raise(nameof(ConnectionSettingsPending));
            if (_vpn.State == VpnState.Error && _vpn.ErrorKey == VpnErrors.DriverMissing) _ = CheckTapAsync(TapPromptTrigger.DriverError);
            if (_vpn.State == VpnState.Refused) OnServerRefused();
            OnIpStateChanged();
            RefreshTexts();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// A refusal is server-side, so the next best server is tried once automatically. The refused
        /// server stays excluded until a connect succeeds or the user disconnects.
        /// </summary>
        void OnServerRefused()
        {
            var refused = _vpn.CurrentServer;
            if (refused != null) _timedOut.Add(refused.Key);
            if (_automaticAttempt || refused == null) return;
            var next = ServerPicker.PickNext(_all, _timedOut);
            if (next == null) return;
            _automaticAttempt = true;
            _connectNote = Loc.F("auto_next_server_note", refused.DisplayName, next.DisplayName);
            Log.Info("Server " + refused.DisplayName + " refused the session; trying " + next.DisplayName + " once automatically");
            _ui.BeginInvoke(new Action(() => _ = AutoConnectAsync(next)));
        }

        async Task AutoConnectAsync(VpnServer server)
        {
            try { await SelectAndConnectAsync(server, true); }
            catch (Exception ex) { Log.Error("Automatic connect to " + server.DisplayName + " failed", ex); }
        }

        void SetNotice(string text)
        {
            _notice = text;
            RefreshTexts();
        }

        void RefreshTexts()
        {
            var st = _vpn.State;
            switch (st)
            {
                case VpnState.Connected:
                    StatusTitle = Loc.T("status_connected");
                    StatusSubtitle = Loc.T("subtitle_connected");
                    break;
                case VpnState.Connecting:
                    StatusTitle = Loc.T("status_connecting");
                    StatusSubtitle = _automaticAttempt && _connectNote != null ? _connectNote : Loc.T("subtitle_connecting");
                    break;
                case VpnState.Disconnecting:
                    StatusTitle = Loc.T("status_disconnecting");
                    StatusSubtitle = Loc.T("subtitle_disconnecting");
                    break;
                case VpnState.Refused:
                    StatusTitle = Loc.T("status_server_refused");
                    StatusSubtitle = Loc.T("subtitle_ready");
                    break;
                case VpnState.Error:
                    StatusTitle = Loc.T("status_error");
                    StatusSubtitle = Loc.T("subtitle_ready");
                    break;
                default:
                    StatusTitle = Loc.T("status_not_connected");
                    StatusSubtitle = Loc.T("subtitle_ready");
                    break;
            }

            // Errors appear only after a real connect attempt failed; idle and connected states stay clean.
            string error = null;
            if (st == VpnState.Error || st == VpnState.Refused)
            {
                var key = _vpn.ErrorKey ?? VpnErrors.ConnectFailed;
                error = key == VpnErrors.ServerRefused ? Loc.F(key, _vpn.CurrentServer?.DisplayName ?? "") : Loc.T(key);
                var detail = _vpn.ErrorDetail;
                if (!string.IsNullOrWhiteSpace(detail) && !OpenVpnController.IsBenignNoise(detail) && key == VpnErrors.ConnectFailed)
                    error += ": " + detail;
            }
            else if (st == VpnState.Disconnected && !string.IsNullOrEmpty(_notice))
            {
                error = _notice;
            }
            HomeError = error;

            ConnectLabel = st == VpnState.Connected ? Loc.T("disconnect")
                         : st == VpnState.Connecting ? Loc.T("connecting_ellipsis")
                         : st == VpnState.Disconnecting ? Loc.T("status_disconnecting")
                         : Loc.T("connect");
            SelectedTitle = _selected?.CountryDisplay ?? Loc.T("no_server");
            SelectedSubtitle = _selected?.DisplayName ?? Loc.T("tap_to_choose");

            Raise(nameof(State));
            Raise(nameof(IsConnected));
            Raise(nameof(IsBusy));
            Raise(nameof(SelectedCode));
            Raise(nameof(ServerCountText));
            Raise(nameof(VersionText));
            Raise(nameof(OpenVpnStatusText));
            Raise(nameof(OpenVpnInstallLabel));
            Raise(nameof(TapStatusText));
            Raise(nameof(OsText));
            if (_tap != null && TapDialogOpen) UpdateTapDialogTexts();
            UpdatePrereqTexts();
            Tick();
        }

        void Tick()
        {
            if (_vpn.State == VpnState.Connected && _vpn.ConnectedSinceUtc.HasValue)
            {
                ConnectionTime = Formatters.FormatDuration(DateTime.UtcNow - _vpn.ConnectedSinceUtc.Value);
                DataTransferred = Formatters.FormatBytes(_vpn.BytesIn + _vpn.BytesOut);
                DownSpeed = Formatters.FormatByteRate(_vpn.DownBytesPerSec);
                UpSpeed = Formatters.FormatByteRate(_vpn.UpBytesPerSec);
            }
            else
            {
                ConnectionTime = "00:00:00";
                DataTransferred = "0 B";
                DownSpeed = "—";
                UpSpeed = "—";
            }
        }

        #endregion

        #region Connect timeout dialog

        bool _timeoutOpen;
        public bool TimeoutDialogOpen { get => _timeoutOpen; set => Set(ref _timeoutOpen, value); }

        string _timeoutMessage;
        public string TimeoutMessage { get => _timeoutMessage; private set => Set(ref _timeoutMessage, value); }

        VpnServer _timedOutServer;

        public ICommand TryAnotherServerCommand { get; }
        public ICommand TimeoutServerListCommand { get; }
        public ICommand TimeoutCancelCommand { get; }

        void OnConnectTimedOut(VpnServer server, ConnectStage stage)
        {
            _timedOutServer = server;
            TimeoutMessage = Loc.F(TimeoutMessageKey(stage), server?.DisplayName ?? "", (int)OpenVpnController.ConnectTimeout.TotalSeconds);
            TimeoutDialogOpen = true;
            RefreshTexts();
        }

        static string TimeoutMessageKey(ConnectStage stage)
        {
            switch (stage)
            {
                case ConnectStage.Network: return "connect_timeout_message_network";
                case ConnectStage.Handshake: return "connect_timeout_message_handshake";
                case ConnectStage.Adapter: return "connect_timeout_message_adapter";
                default: return "connect_timeout_message";
            }
        }

        void CloseTimeoutDialog() => TimeoutDialogOpen = false;

        async Task TryAnotherServerAsync()
        {
            CloseTimeoutDialog();
            if (_timedOutServer != null) _timedOut.Add(_timedOutServer.Key);
            var next = ServerPicker.PickNext(_all, _timedOut);
            if (next == null)
            {
                SetNotice(Loc.T("connect_timeout_no_other"));
                return;
            }
            await SelectAndConnectAsync(next);
        }

        #endregion

        #region OpenVPN install

        string _openVpnPath;
        bool _openVpnMissing;
        public bool OpenVpnMissing { get => _openVpnMissing; private set => Set(ref _openVpnMissing, value); }
        public string OpenVpnStatusText => _openVpnPath != null ? Loc.F("openvpn_found", _openVpnPath) : Loc.T("openvpn_not_found");
        public string OpenVpnInstallLabel => Loc.F("openvpn_install", PinnedDownloads.OpenVpnVersion);

        bool _installingOpenVpn;
        public bool IsInstallingOpenVpn { get => _installingOpenVpn; private set => Set(ref _installingOpenVpn, value); }

        string _openVpnInstallMessage;
        public string OpenVpnInstallMessage { get => _openVpnInstallMessage; private set => Set(ref _openVpnInstallMessage, value); }

        public ICommand InstallOpenVpnCommand { get; }

        async Task DetectOpenVpnAsync()
        {
            _openVpnPath = await Task.Run(() => ToolLocator.FindOpenVpn());
            OpenVpnMissing = _openVpnPath == null;
            Raise(nameof(OpenVpnStatusText));
        }

        async Task InstallOpenVpnAsync()
        {
            if (!await EnsurePrerequisitesAsync()) return;
            IsInstallingOpenVpn = true;
            OpenVpnInstallMessage = Loc.T("installing");
            var progress = new Progress<double>(p => OpenVpnInstallMessage = Loc.F("downloading", (int)(p * 100)));
            var err = await InstallerService.InstallAsync(progress, CancellationToken.None);
            await DetectOpenVpnAsync();
            OpenVpnInstallMessage = err == null ? Loc.T("install_done") : Loc.T(err);
            IsInstallingOpenVpn = false;
            if (err == null && _openVpnPath != null) await CheckTapAsync(TapPromptTrigger.AfterOpenVpnInstall);
        }

        /// <summary>
        /// Windows prerequisites first (Windows 7 / 8.x only), then OpenVPN; TAP-Windows6 only matters once
        /// OpenVPN is installed and usable (otherwise the OpenVPN card or the prerequisites dialog covers it).
        /// </summary>
        async Task StartupChecksAsync()
        {
            _prereqTask = CheckPrerequisitesAsync();
            var report = await _prereqTask;
            if (!report.IsClean) OpenPrereqDialog();
            await DetectOpenVpnAsync();
            if (_openVpnPath != null && !report.BlocksOpenVpn) await CheckTapAsync(TapPromptTrigger.Startup);
        }

        #endregion

        #region Windows version and prerequisites

        public string OsText => Loc.F("about_os", OsSupport.DisplayName(OsInfo.Current));

        PrerequisiteReport _prereq = PrerequisiteReport.None;
        Task<PrerequisiteReport> _prereqTask = Task.FromResult(PrerequisiteReport.None);

        public bool PrerequisitesBlock => _prereq.BlocksOpenVpn;
        public bool HasPrereqIssues => !_prereq.IsClean;

        bool _prereqOpen;
        public bool PrereqDialogOpen { get => _prereqOpen; set => Set(ref _prereqOpen, value); }

        bool _prereqBusy;
        public bool IsPrereqBusy { get => _prereqBusy; private set { if (Set(ref _prereqBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

        List<PrereqUpdateItem> _prereqUpdates = new List<PrereqUpdateItem>();
        public List<PrereqUpdateItem> PrereqUpdates { get => _prereqUpdates; private set => Set(ref _prereqUpdates, value); }

        string _prereqTitle, _prereqBody, _prereqRoots, _prereqMessage;
        public string PrereqDialogTitle { get => _prereqTitle; private set => Set(ref _prereqTitle, value); }
        public string PrereqDialogBody { get => _prereqBody; private set => Set(ref _prereqBody, value); }
        public string PrereqRootWarning { get => _prereqRoots; private set { if (Set(ref _prereqRoots, value)) Raise(nameof(HasPrereqRootWarning)); } }
        public bool HasPrereqRootWarning => !string.IsNullOrEmpty(PrereqRootWarning);
        public string PrereqMessage { get => _prereqMessage; private set { if (Set(ref _prereqMessage, value)) Raise(nameof(HasPrereqMessage)); } }
        public bool HasPrereqMessage => !string.IsNullOrEmpty(PrereqMessage);

        public ICommand PrereqRecheckCommand { get; }
        public ICommand PrereqDismissCommand { get; }
        public ICommand ShowPrereqCommand { get; }
        public ICommand OpenUpdateLinkCommand { get; }

        /// <summary>Runs off the UI thread: the hotfix query goes through WMI and can take a few seconds.</summary>
        async Task<PrerequisiteReport> CheckPrerequisitesAsync()
        {
            var os = OsInfo.Current;
            PrerequisiteReport report;
            try { report = await Task.Run(() => Prerequisites.Evaluate(os, new SystemProbe())); }
            catch (Exception ex)
            {
                Log.Error("Prerequisite check failed", ex);
                report = PrerequisiteReport.None;
            }
            if (!report.IsClean)
                Log.Warn("Prerequisites: missing updates [" + string.Join(", ", report.MissingUpdates.Select(u => u.Kb)) +
                         "], missing roots [" + string.Join(", ", report.MissingRoots.Select(r => r.Name)) +
                         "], root auto-update disabled: " + report.RootAutoUpdateDisabled);
            _prereq = report;
            Raise(nameof(PrerequisitesBlock));
            Raise(nameof(HasPrereqIssues));
            UpdatePrereqTexts();
            return report;
        }

        /// <summary>Called before installing or connecting OpenVPN; opens the dialog and returns false while updates are missing.</summary>
        async Task<bool> EnsurePrerequisitesAsync()
        {
            var report = await _prereqTask;
            if (!report.BlocksOpenVpn) return true;
            OpenPrereqDialog();
            SetNotice(Loc.T("prereq_blocked_notice"));
            return false;
        }

        void OpenPrereqDialog()
        {
            PrereqMessage = null;
            UpdatePrereqTexts();
            PrereqDialogOpen = true;
        }

        async Task RecheckPrerequisitesAsync()
        {
            IsPrereqBusy = true;
            PrereqMessage = Loc.T("prereq_checking");
            _prereqTask = CheckPrerequisitesAsync();
            var report = await _prereqTask;
            IsPrereqBusy = false;
            if (report.BlocksOpenVpn)
            {
                PrereqMessage = Loc.F("prereq_still_missing", string.Join(", ", report.MissingUpdates.Select(u => u.Kb)));
                return;
            }
            PrereqMessage = Loc.T("prereq_all_ok");
            if (report.IsClean) PrereqDialogOpen = false;
            _notice = null;
            RefreshTexts();
            await DetectOpenVpnAsync();
            if (_openVpnPath != null) await CheckTapAsync(PrereqDialogOpen ? (TapPromptTrigger?)null : TapPromptTrigger.Startup);
        }

        void UpdatePrereqTexts()
        {
            if (_prereq == null) return;
            PrereqDialogTitle = Loc.T(_prereq.BlocksOpenVpn ? "prereq_title" : "prereq_warn_title");
            PrereqDialogBody = _prereq.BlocksOpenVpn ? Loc.T("prereq_body") : null;
            PrereqUpdates = _prereq.MissingUpdates.Select(u => new PrereqUpdateItem(u.Kb, Loc.T(u.DescriptionKey), u.Url)).ToList();
            PrereqRootWarning = _prereq.HasRootWarning
                ? Loc.F("prereq_roots_missing", string.Join(", ", _prereq.MissingRoots.Select(r => r.Name))) +
                  (_prereq.RootAutoUpdateDisabled ? "\n" + Loc.T("prereq_roots_policy") : "")
                : null;
        }

        #endregion

        #region TAP-Windows6 driver

        TapCheckResult _tap;
        TapRepair _tapPlan;

        public string TapStatusText
        {
            get
            {
                if (_tap == null || _openVpnPath == null) return "";
                switch (_tap.Status)
                {
                    case TapStatus.Ready: return Loc.T("tap_status_ready");
                    case TapStatus.AdapterInactive: return Loc.T("tap_status_inactive");
                    case TapStatus.DriverOnly: return Loc.T("tap_status_no_adapter");
                    default: return Loc.T("tap_status_missing");
                }
            }
        }

        bool _tapDialogOpen;
        public bool TapDialogOpen { get => _tapDialogOpen; set => Set(ref _tapDialogOpen, value); }

        bool _tapBusy;
        public bool IsTapBusy { get => _tapBusy; private set { if (Set(ref _tapBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

        bool _tapDone;
        /// <summary>True after a successful repair; the dialog then shows the confirmation and a single Close button.</summary>
        public bool TapDone { get => _tapDone; private set => Set(ref _tapDone, value); }

        string _tapTitle, _tapBody, _tapActionLabel, _tapMessage;
        public string TapDialogTitle { get => _tapTitle; private set => Set(ref _tapTitle, value); }
        public string TapDialogBody { get => _tapBody; private set => Set(ref _tapBody, value); }
        public string TapActionLabel { get => _tapActionLabel; private set => Set(ref _tapActionLabel, value); }
        public string TapMessage { get => _tapMessage; private set { if (Set(ref _tapMessage, value)) Raise(nameof(HasTapMessage)); } }
        public bool HasTapMessage => !string.IsNullOrEmpty(TapMessage);

        bool _tapError;
        public bool TapMessageIsError { get => _tapError; private set => Set(ref _tapError, value); }

        public ICommand TapRepairCommand { get; }
        public ICommand TapDismissCommand { get; }

        /// <summary>
        /// Re-reads the TAP-Windows6 state without admin rights. With a trigger, the confirmation dialog opens
        /// when <see cref="TapPromptPolicy"/> says so; without one the state is only refreshed.
        /// </summary>
        async Task<TapCheckResult> CheckTapAsync(TapPromptTrigger? trigger)
        {
            var result = await TapDriverService.CheckAsync();
            _tap = result;
            Raise(nameof(TapStatusText));
            var declined = TapPromptPolicy.DeclinedAfterCheck(result, _settings.TapPromptDeclined);
            if (declined != _settings.TapPromptDeclined)
            {
                _settings.TapPromptDeclined = declined;
                SettingsStore.Save(_settings);
            }
            if (trigger.HasValue && !TapDialogOpen && TapPromptPolicy.ShouldPrompt(result, trigger.Value, declined))
                await OpenTapDialogAsync();
            return result;
        }

        /// <summary>"Not now" is remembered so the next start does not ask again; Connect still does.</summary>
        void DismissTapDialog()
        {
            if (!TapDone && !_settings.TapPromptDeclined)
            {
                _settings.TapPromptDeclined = true;
                SettingsStore.Save(_settings);
            }
            TapDialogOpen = false;
        }

        /// <summary>
        /// Called before every OpenVPN connect. Blocks only when no TAP-Windows6
        /// adapter is registered at all; a registered but inactive adapter is left to OpenVPN, whose own
        /// error reopens the dialog.
        /// </summary>
        async Task<bool> EnsureTapReadyAsync()
        {
            var result = await CheckTapAsync(TapPromptTrigger.Connect);
            return !result.BlocksConnect;
        }

        async Task OpenTapDialogAsync()
        {
            var tapctl = await Task.Run(() => ToolLocator.FindTapctl());
            _tapPlan = TapDetector.ChooseRepair(_tap, tapctl != null);
            TapDone = false;
            TapMessage = null;
            TapMessageIsError = false;
            UpdateTapDialogTexts();
            TapDialogOpen = true;
        }

        void UpdateTapDialogTexts()
        {
            if (TapDone)
            {
                TapDialogTitle = Loc.T("tap_ready_title");
                TapDialogBody = Loc.T("tap_ready_body");
                return;
            }
            TapDialogTitle = Loc.T("tap_dialog_title");
            if (_tapPlan == TapRepair.CreateAdapter)
            {
                TapDialogBody = Loc.T(_tap?.Status == TapStatus.AdapterInactive ? "tap_body_inactive" : "tap_body_create");
                TapActionLabel = Loc.T("tap_create_action");
            }
            else
            {
                TapDialogBody = Loc.F("tap_body_missing", PinnedDownloads.OpenVpnVersion);
                TapActionLabel = Loc.T("tap_install_action");
            }
        }

        async Task RepairTapAsync()
        {
            IsTapBusy = true;
            TapMessageIsError = false;
            TapMessage = Loc.T("tap_repairing");
            var progress = new Progress<double>(p => TapMessage = Loc.F("downloading", (int)(p * 100)));
            string err;
            try { err = await TapDriverService.RepairAsync(progress); }
            catch (Exception ex)
            {
                Log.Error("TAP-Windows6 repair failed", ex);
                err = "tap_still_missing";
            }
            await DetectOpenVpnAsync();
            await CheckTapAsync(null);
            IsTapBusy = false;
            if (err == null)
            {
                TapDone = true;
                TapMessage = null;
                UpdateTapDialogTexts();
            }
            else
            {
                TapMessageIsError = true;
                TapMessage = Loc.T(err);
            }
            RefreshTexts();
        }

        #endregion

        #region Settings

        public ThemeMode Theme
        {
            get => _settings.Theme;
            set
            {
                if (_settings.Theme == value) return;
                _settings.Theme = value;
                ThemeService.Apply(value);
                SettingsStore.Save(_settings);
                Raise();
            }
        }

        public string Language
        {
            get => _settings.Language;
            set
            {
                if (_settings.Language == value || string.IsNullOrEmpty(value)) return;
                _settings.Language = value;
                Loc.Instance.SetLanguage(value);
                SettingsStore.Save(_settings);
                Raise();
            }
        }

        /// <summary>Settings toggle: closing the window hides it to the notification area (default on).</summary>
        public bool MinimizeToTray
        {
            get => !_settings.CloseQuitsApp;
            set
            {
                if (_settings.CloseQuitsApp == !value) return;
                _settings.CloseQuitsApp = !value;
                SettingsStore.Save(_settings);
                Raise();
            }
        }

        /// <summary>True once; the caller shows the "still running in the tray" notification.</summary>
        public bool TakeTrayHint()
        {
            if (_settings.TrayHintShown) return false;
            _settings.TrayHintShown = true;
            SettingsStore.Save(_settings);
            return true;
        }

        public bool AllowLocalLan
        {
            get => _settings.AllowLocalLan;
            set
            {
                if (_settings.AllowLocalLan == value) return;
                _settings.AllowLocalLan = value;
                SettingsStore.Save(_settings);
                Raise();
                Raise(nameof(ConnectionSettingsPending));
            }
        }

        public bool BlockOutsideDns
        {
            get => _settings.BlockOutsideDns;
            set
            {
                if (_settings.BlockOutsideDns == value) return;
                _settings.BlockOutsideDns = value;
                SettingsStore.Save(_settings);
                Raise();
                Raise(nameof(ConnectionSettingsPending));
            }
        }

        public bool BlockIpv6
        {
            get => !_settings.Ipv6BlockOff;
            set
            {
                if (_settings.Ipv6BlockOff == !value) return;
                _settings.Ipv6BlockOff = !value;
                SettingsStore.Save(_settings);
                Raise();
                Raise(nameof(ConnectionSettingsPending));
            }
        }

        /// <summary>One row of the DNS picker.</summary>
        public sealed class DnsChoice
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Detail { get; set; }
            /// <summary>Pack path of the provider's logo; null shows <see cref="Glyph"/> instead.</summary>
            public string Logo { get; set; }
            public GlyphKind Glyph { get; set; } = GlyphKind.Dns;
            public bool HasLogo => !string.IsNullOrEmpty(Logo);
        }

        List<DnsChoice> _dnsChoices;
        public List<DnsChoice> DnsChoices { get => _dnsChoices; private set => Set(ref _dnsChoices, value); }

        void BuildDnsChoices()
        {
            var list = new List<DnsChoice> { new DnsChoice { Id = PublicDns.ServerId, Name = Loc.T("dns_server_default"), Detail = Loc.T("dns_server_default_detail") } };
            list.AddRange(PublicDns.Providers.Select(p => new DnsChoice { Id = p.Id, Name = p.Name, Detail = p.Addresses, Logo = "pack://application:,,,/" + p.Logo }));
            list.Add(new DnsChoice { Id = PublicDns.CustomId, Name = Loc.T("dns_custom"), Detail = Loc.T("dns_custom_detail"), Glyph = GlyphKind.Edit });
            var id = _settings.DnsProvider ?? "";
            DnsChoices = list;
            _selectedDns = list.FirstOrDefault(c => c.Id == id) ?? list[0];
            Raise(nameof(SelectedDns));
        }

        DnsChoice _selectedDns;
        public DnsChoice SelectedDns
        {
            get => _selectedDns;
            set
            {
                if (value == null || value == _selectedDns) return;
                _selectedDns = value;
                _settings.DnsProvider = value.Id;
                SettingsStore.Save(_settings);
                Raise();
                RaiseDns();
            }
        }

        public bool IsCustomDns => _selectedDns?.Id == PublicDns.CustomId;
        public bool UsesOwnDns => !string.IsNullOrEmpty(_selectedDns?.Id);

        public string CustomDns1
        {
            get => _settings.CustomDns1 ?? "";
            set { _settings.CustomDns1 = (value ?? "").Trim(); SettingsStore.Save(_settings); RaiseDns(); }
        }

        public string CustomDns2
        {
            get => _settings.CustomDns2 ?? "";
            set { _settings.CustomDns2 = (value ?? "").Trim(); SettingsStore.Save(_settings); RaiseDns(); }
        }

        /// <summary>Validation message under the custom fields; empty when both are fine.</summary>
        public string CustomDnsError
        {
            get
            {
                if (!IsCustomDns) return "";
                if (!PublicDns.TryParseResolver(CustomDns1, out _)) return Loc.T(string.IsNullOrEmpty(CustomDns1) ? "dns_custom_required" : "dns_invalid");
                if (!string.IsNullOrEmpty(CustomDns2) && !PublicDns.TryParseResolver(CustomDns2, out _)) return Loc.T("dns_invalid");
                return "";
            }
        }

        void RaiseDns()
        {
            Raise(nameof(IsCustomDns));
            Raise(nameof(UsesOwnDns));
            Raise(nameof(CustomDnsError));
            Raise(nameof(ConnectionSettingsPending));
        }

        public string VersionText => Loc.F("about_version", AppVersion.Current);

        public ICommand OpenLogsCommand { get; }
        public ICommand ShowLogsCommand { get; }

        static string[] LogFolders => new[] { System.IO.Path.GetDirectoryName(Log.FilePath), OpenVpnController.LogDirectory };

        /// <summary>OpenVPN log that openvpn.exe still writes, or null when no connection runs.</summary>
        string ActiveOpenVpnLog() =>
            _vpn.State == VpnState.Connected || _vpn.State == VpnState.Connecting || _vpn.State == VpnState.Disconnecting ? _vpn.CurrentLogPath : null;

        void ShowLogs() => LogViewerWindow.ShowSingle(LogFolders, _vpn.CurrentLogPath, ActiveOpenVpnLog);

        public ICommand ClearLogsCommand { get; }

        void ClearLogs()
        {
            var message = LogViewerWindow.ConfirmAndClear(Application.Current?.MainWindow, LogFolders, ActiveOpenVpnLog());
            if (message != null) LogsMessage = message;
        }

        string _logsMessage;
        public string LogsMessage { get => _logsMessage; private set => Set(ref _logsMessage, value); }
        public ICommand OpenLicensesCommand { get; }
        public ICommand FeedbackCommand { get; }

        void SendFeedback()
        {
            var subject = Uri.EscapeDataString(Loc.T("feedback_mail_subject") + " — " + AppVersion.AppName + " " + AppVersion.Current);
            OpenShell("mailto:phuongnam.TaiNT13@fpt.net?subject=" + subject);
        }

        public static void OpenShell(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Opening " + target + " failed", ex); }
        }

        public static void OpenShellArgs(string program, string arguments)
        {
            try { Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Starting " + program + " " + arguments + " failed", ex); }
        }

        #endregion
    }

    /// <summary>One row of the prerequisites dialog.</summary>
    public sealed class PrereqUpdateItem
    {
        public string Kb { get; }
        public string Description { get; }
        public string Url { get; }

        public PrereqUpdateItem(string kb, string description, string url)
        {
            Kb = kb;
            Description = description;
            Url = url;
        }
    }
}
