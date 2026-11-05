using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using STGPhantomVPN.Core;
using STGPhantomVPN.Services;

namespace STGPhantomVPN
{
    public partial class App : Application
    {
        Mutex _single;
        EventWaitHandle _activate;
        RegisteredWaitHandle _activateWait;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0 && e.Args[0] == "--elevated")
            {
                Shutdown(ElevatedHelper.Run(e.Args));
                return;
            }

            // One instance only: two copies would fight over openvpn.exe and the run folder.
            _single = new Mutex(true, InstallInfo.SingleInstanceMutex, out var first);
            if (!first)
            {
                SignalRunningInstance();
                Shutdown();
                return;
            }

            // Windows 10+: keep SystemDefault so Schannel picks TLS 1.2/1.3. Windows 7/8.x: request TLS 1.2
            // explicitly, since their defaults for .NET 4.8 can stop at TLS 1.0.
            var os = OsInfo.Current;
            Log.Info("Windows " + os + " (" + OsSupport.DisplayName(os) + "), " + (Environment.Is64BitProcess ? "64" : "32") + "-bit process");
            if (OsSupport.NeedsExplicitTls12(os))
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Error("Unhandled", a.ExceptionObject as Exception);

            var settings = SettingsStore.Load();
            Loc.Instance.SetLanguage(settings.Language);
            base.OnStartup(e);
            ThemeService.Apply(settings.Theme);

            var window = new MainWindow(settings);
            MainWindow = window;
            ListenForActivation(window);
            SessionEnding += (s, a) => window.OnSessionEnding();
            window.Show();
        }

        /// <summary>
        /// Asks the running instance (possibly hidden in the tray) to show its window. The running copy may be
        /// elevated or belong to another session; its event then cannot be opened and this launch just exits.
        /// </summary>
        static void SignalRunningInstance()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(InstallInfo.ActivateEvent))
                {
                    // This process was started by the user, so it may hand the foreground to the running one.
                    AllowSetForegroundWindow(AsfwAny);
                    ev.Set();
                }
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException || ex is UnauthorizedAccessException || ex is IOException)
            {
                Log.Info("Running instance could not be activated: " + ex.Message);
            }
        }

        void ListenForActivation(MainWindow window)
        {
            try
            {
                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, InstallInfo.ActivateEvent);
                _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate,
                    (state, timedOut) => Dispatcher.BeginInvoke(new Action(window.ShowFromTray)), null, Timeout.Infinite, false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is WaitHandleCannotBeOpenedException)
            {
                Log.Warn("Activation event unavailable: " + ex.Message);
            }
        }

        const int AsfwAny = -1;

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);

        void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Ends the IPv6 leak block helper, if one was started; its WFP filters go with it.
            Services.Ipv6Guard.Instance.Dispose();
            _activateWait?.Unregister(null);
            _activate?.Dispose();
            try { _single?.ReleaseMutex(); } catch (ApplicationException) { }
            base.OnExit(e);
        }
    }
}
