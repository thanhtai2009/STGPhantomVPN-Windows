using System;
using System.ComponentModel;
using System.Windows;
using STGPhantomVPN.Core;
using STGPhantomVPN.Services;
using STGPhantomVPN.ViewModels;

namespace STGPhantomVPN
{
    public partial class MainWindow : Window
    {
        readonly MainViewModel _vm;
        readonly TrayIcon _tray;
        bool _exitRequested, _sessionEnding, _exiting;

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            WindowBackdrop.Attach(this);
            StateChanged += (s, e) => UpdateCaption();
            UpdateCaption();
            FitToWorkArea();
            SizeChanged += (s, e) => Resources["DialogMaxHeight"] = WindowFit.DialogMaxHeight(((FrameworkElement)Content).ActualHeight);
            _vm = new MainViewModel(settings);
            DataContext = _vm;
            _vm.PropertyChanged += OnViewModelChanged;
            _tray = new TrayIcon(_vm);
            _tray.OpenRequested += ShowFromTray;
            _tray.ExitRequested += () => _ = ExitFromTrayAsync();
            Loaded += (s, e) => _vm.Start();
        }

        /// <summary>Opens at the compact size, shrunk and centred to the primary work area when that is smaller.</summary>
        void FitToWorkArea()
        {
            var wa = SystemParameters.WorkArea;
            var b = WindowFit.Compute(wa.Left, wa.Top, wa.Width, wa.Height);
            MinWidth = b.MinWidth;
            MinHeight = b.MinHeight;
            Width = b.Width;
            Height = b.Height;
            if (double.IsNaN(b.Left)) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = b.Left;
            Top = b.Top;
        }

        void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void OnMaximize(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        void OnCloseButton(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Restore glyph and tooltip while maximized. A maximized WindowChrome window reaches past the screen by the
        /// resize frame, so the content is inset by that frame to stay fully visible.
        /// </summary>
        void UpdateCaption()
        {
            var max = WindowState == WindowState.Maximized;
            MaximizeGlyph.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            RestoreGlyph.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
            MaximizeButton.ToolTip = Loc.T(max ? "caption_restore" : "caption_maximize");
            Shell.Margin = max ? MaximizedInset() : new Thickness(0);
            Frame.BorderThickness = new Thickness(max || WindowLook.RoundedCorners(Environment.OSVersion.Version.Build) ? 0 : 1);
        }

        Thickness MaximizedInset()
        {
            const int SmCxFrame = 32, SmCyFrame = 33, SmCxPaddedBorder = 92;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var pad = GetSystemMetrics(SmCxPaddedBorder);
            var x = (GetSystemMetrics(SmCxFrame) + pad) / dpi.DpiScaleX;
            var y = (GetSystemMetrics(SmCyFrame) + pad) / dpi.DpiScaleY;
            return new Thickness(x, y, x, y);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        /// <summary>Restores the window from the tray (or a second launch) and brings it to the front.</summary>
        public void ShowFromTray()
        {
            if (_exiting) return;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            // Topmost toggling lifts the window above others when Windows refuses a plain Activate.
            Topmost = true;
            Topmost = false;
            Focus();
        }

        /// <summary>A dialog that needs the user (TAP driver, Windows updates, timeout) brings a hidden window back.</summary>
        void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (IsVisible) return;
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.TapDialogOpen):
                case nameof(MainViewModel.PrereqDialogOpen):
                case nameof(MainViewModel.TimeoutDialogOpen):
                case nameof(MainViewModel.HasHomeError):
                    if (_vm.TapDialogOpen || _vm.PrereqDialogOpen || _vm.TimeoutDialogOpen || _vm.HasHomeError) ShowFromTray();
                    break;
            }
        }

        /// <summary>Tray "Exit": asks while a tunnel is up, disconnects cleanly, then closes for real.</summary>
        async System.Threading.Tasks.Task ExitFromTrayAsync()
        {
            if (_exiting) return;
            if (TrayText.ExitNeedsConfirmation(_vm.IsConnected, _vm.IsBusy))
            {
                ShowFromTray();
                var answer = MessageBox.Show(this, Loc.T("exit_confirm_body"), AppVersion.AppName,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
                await _vm.StopOpenVpnIfActiveAsync();
            }
            _exitRequested = true;
            Close();
        }

        /// <summary>Windows is signing out or shutting down: exit without hiding or asking.</summary>
        public void OnSessionEnding()
        {
            _sessionEnding = true;
            ExitNow();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_exiting && TrayText.CloseHidesToTray(_vm.MinimizeToTray, _exitRequested, _sessionEnding))
            {
                e.Cancel = true;
                Hide();
                if (_vm.TakeTrayHint()) _tray.ShowHint();
                return;
            }
            ExitNow();
            base.OnClosing(e);
        }

        /// <summary>Stops openvpn.exe gracefully so routes and DNS are restored, and removes the tray icon.</summary>
        void ExitNow()
        {
            if (_exiting) return;
            _exiting = true;
            _vm.PropertyChanged -= OnViewModelChanged;
            _vm.Shutdown();
            _tray.Dispose();
        }
    }
}
