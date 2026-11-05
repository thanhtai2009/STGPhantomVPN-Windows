using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using STGPhantomVPN.Core;
using STGPhantomVPN.ViewModels;
using STGPhantomVPN.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Notification-area icon: shows the connection state in its icon and tooltip, restores the window on
    /// click, and offers a status header, Open, Connect or Disconnect, View logs and Exit. Uses the WinForms
    /// NotifyIcon, which works the same from Windows 7 to 11, with a Windows 11 style renderer: rounded
    /// highlight, theme-aware colours and brand glyph icons rendered per DPI so they stay pixel-sharp.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly MainViewModel _vm;
        readonly Forms.NotifyIcon _icon;
        readonly Forms.ContextMenuStrip _menu;
        readonly Forms.ToolStripMenuItem _header, _open, _connect, _logs, _exit;
        readonly Drawing.Icon _idleIcon, _connectedIcon;
        readonly TrayRenderer _renderer = new TrayRenderer();
        Drawing.Font _itemFont, _boldFont;
        Drawing.Bitmap _openImage, _connectImage, _disconnectImage, _logsImage, _exitImage, _dotImage;
        int _dpi = -1;
        bool _dark, _lookValid;
        TrayDot _dot = (TrayDot)(-1);
        bool _disposed;

        public event Action OpenRequested;
        public event Action ExitRequested;

        public TrayIcon(MainViewModel vm)
        {
            _vm = vm;
            var size = Forms.SystemInformation.SmallIconSize;
            _idleIcon = LoadIcon("Assets/tray-idle.ico", size);
            _connectedIcon = LoadIcon("Assets/tray-connected.ico", size);

            // The status header is information only; the bold item is the default (click) action, as in other tray menus.
            _header = new Forms.ToolStripMenuItem { Enabled = false };
            _open = new Forms.ToolStripMenuItem();
            _open.Click += (s, e) => OpenRequested?.Invoke();
            _connect = new Forms.ToolStripMenuItem();
            _connect.Click += (s, e) => OnConnectClicked();
            _logs = new Forms.ToolStripMenuItem();
            _logs.Click += (s, e) => { if (_vm.ShowLogsCommand.CanExecute(null)) _vm.ShowLogsCommand.Execute(null); };
            _exit = new Forms.ToolStripMenuItem();
            _exit.Click += (s, e) => ExitRequested?.Invoke();
            _menu = new Forms.ContextMenuStrip { Renderer = _renderer, ShowImageMargin = true, ShowCheckMargin = false, DropShadowEnabled = true };
            _menu.Items.AddRange(new Forms.ToolStripItem[]
            {
                _header, new Forms.ToolStripSeparator(), _open, _connect, _logs, new Forms.ToolStripSeparator(), _exit,
            });
            _menu.Opening += (s, e) => PrepareLook(CurrentDpi(), SystemUsesDarkMenus());
            _menu.HandleCreated += (s, e) => RoundCorners(_menu.Handle);

            _icon = new Forms.NotifyIcon { Icon = _idleIcon, ContextMenuStrip = _menu };
            _icon.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
            _icon.BalloonTipClicked += (s, e) => OpenRequested?.Invoke();

            _vm.PropertyChanged += OnViewModelChanged;
            Loc.Instance.PropertyChanged += OnLanguageChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            PrepareLook(CurrentDpi(), SystemUsesDarkMenus());
            Refresh();
            _icon.Visible = true;
        }

        /// <summary>One-time hint after the first hide to the tray.</summary>
        public void ShowHint()
        {
            if (_disposed) return;
            _icon.ShowBalloonTip(5000, Loc.T("tray_hint_title"), Loc.T("tray_hint_body"), Forms.ToolTipIcon.Info);
        }

        void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.StatusTitle):
                case nameof(MainViewModel.SelectedTitle):
                case nameof(MainViewModel.IsConnected):
                case nameof(MainViewModel.IsBusy):
                case nameof(MainViewModel.HasHomeError):
                case null:
                    Refresh();
                    break;
            }
        }

        void OnLanguageChanged(object sender, PropertyChangedEventArgs e) => Refresh();

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Light/dark taskbar or DPI changes: rebuild the palette and bitmaps the next time the menu opens.
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Desktop) _lookValid = false;
        }

        void Refresh()
        {
            if (_disposed) return;
            var action = TrayText.ConnectAction(_vm.IsConnected, _vm.IsBusy);
            _icon.Icon = _vm.IsConnected ? _connectedIcon : _idleIcon;
            _icon.Text = TrayText.Tooltip(AppVersion.AppName, _vm.StatusTitle, _vm.SelectedServer?.CountryDisplay);
            _header.Text = TrayText.Header(_vm.StatusTitle, _vm.SelectedServer?.CountryDisplay, _vm.IsConnected || _vm.IsBusy);
            _open.Text = Loc.T("tray_open");
            _logs.Text = Loc.T("show_logs");
            _exit.Text = Loc.T("tray_exit");
            _connect.Text = action == TrayConnectAction.Disconnect ? Loc.T("tray_disconnect")
                          : action == TrayConnectAction.Busy ? _vm.StatusTitle
                          : Loc.T("tray_connect");
            _connect.Enabled = action != TrayConnectAction.Busy;
            _connect.Image = action == TrayConnectAction.Disconnect ? _disconnectImage : _connectImage;
            var dot = TrayText.Dot(_vm.IsConnected, _vm.IsBusy, _vm.HasHomeError);
            if (dot != _dot || _dotImage == null)
            {
                _dot = dot;
                _dotImage?.Dispose();
                _dotImage = DotBitmap(TrayPalette.DotColor(dot), TrayText.IconPixels(_dpi));
            }
            _header.Image = _dotImage;
        }

        /// <summary>Colours, fonts and icon bitmaps for one DPI and menu theme. Internal so previews can render both themes.</summary>
        internal void PrepareLook(int dpi, bool dark)
        {
            if (_disposed || (_lookValid && dpi == _dpi && dark == _dark)) return;
            _dpi = dpi;
            _dark = dark;
            _lookValid = true;
            var palette = TrayPalette.For(dark);
            _renderer.Palette = palette;
            _renderer.Dpi = dpi;
            _renderer.Rounded = TrayText.RoundedCorners(Environment.OSVersion.Version.Build);

            var px = TrayText.IconPixels(dpi);
            _menu.ImageScalingSize = new Drawing.Size(px, px);
            var pad = TrayText.Scale(dpi, 4);
            _menu.Padding = new Forms.Padding(0, pad, 0, pad);

            _itemFont?.Dispose();
            _boldFont?.Dispose();
            // Points scale with the system DPI already; the menu uses Segoe UI 9 pt like the Windows 11 shell menus.
            _itemFont = new Drawing.Font("Segoe UI", 9f * dpi / SystemDpi(), Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
            _boldFont = new Drawing.Font(_itemFont, Drawing.FontStyle.Bold);
            _menu.Font = _itemFont;
            _open.Font = _boldFont;

            var itemPadding = new Forms.Padding(TrayText.Scale(dpi, 4), TrayText.Scale(dpi, 5), TrayText.Scale(dpi, 12), TrayText.Scale(dpi, 5));
            foreach (Forms.ToolStripItem item in _menu.Items)
                if (item is Forms.ToolStripMenuItem m) m.Padding = itemPadding;

            Replace(ref _openImage, AppIconBitmap(px));
            Replace(ref _connectImage, GlyphBitmap(GlyphKind.Power, px, palette.Accent, palette.Background));
            Replace(ref _disconnectImage, GlyphBitmap(GlyphKind.ShieldX, px, palette.Danger, palette.Background));
            Replace(ref _logsImage, GlyphBitmap(GlyphKind.Log, px, palette.Icon, palette.Background));
            Replace(ref _exitImage, GlyphBitmap(GlyphKind.Exit, px, palette.Icon, palette.Background));
            _open.Image = _openImage;
            _logs.Image = _logsImage;
            _exit.Image = _exitImage;
            _dotImage?.Dispose();
            _dotImage = null;
            Refresh();
        }

        /// <summary>The menu as shown, for preview rendering.</summary>
        internal Forms.ContextMenuStrip Menu => _menu;

        static void Replace(ref Drawing.Bitmap field, Drawing.Bitmap value)
        {
            field?.Dispose();
            field = value;
        }

        void OnConnectClicked()
        {
            // Without a remembered server there is nothing to reconnect to; open the server list instead.
            if (_vm.SelectedServer == null)
            {
                _vm.GoToServersCommand.Execute(null);
                OpenRequested?.Invoke();
                return;
            }
            if (_vm.ConnectToggleCommand.CanExecute(null)) _vm.ConnectToggleCommand.Execute(null);
        }

        static Drawing.Icon LoadIcon(string resource, Drawing.Size size)
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/STGPhantomVPN;component/" + resource, UriKind.Absolute));
            using (var stream = info.Stream) return new Drawing.Icon(stream, size);
        }

        /// <summary>The app logo from the multi-size app.ico (16/20/24/32 px frames), so each DPI gets a hand-sized frame.</summary>
        static Drawing.Bitmap AppIconBitmap(int px)
        {
            using (var icon = LoadIcon("Assets/app.ico", new Drawing.Size(px, px)))
            {
                if (icon.Width == px) return icon.ToBitmap();
                var bmp = new Drawing.Bitmap(px, px);
                using (var g = Drawing.Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawIcon(icon, new Drawing.Rectangle(0, 0, px, px));
                }
                return bmp;
            }
        }

        static Drawing.Bitmap GlyphBitmap(GlyphKind kind, int px, uint color, uint inner) =>
            ToBitmap(Glyph.Render(kind, px, ToMedia(color), ToMedia(inner)));

        static Drawing.Bitmap DotBitmap(uint color, int px)
        {
            var bmp = new Drawing.Bitmap(px, px);
            using (var g = Drawing.Graphics.FromImage(bmp))
            using (var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(unchecked((int)color))))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var d = Math.Max(6f, px * 0.5f);
                g.FillEllipse(brush, (px - d) / 2f, (px - d) / 2f, d, d);
            }
            return bmp;
        }

        static Media.Color ToMedia(uint argb) =>
            Media.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        static Drawing.Bitmap ToBitmap(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                using (var decoded = new Drawing.Bitmap(ms)) return new Drawing.Bitmap(decoded);
            }
        }

        static int SystemDpi()
        {
            using (var g = Drawing.Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(96, (int)Math.Round(g.DpiX));
        }

        /// <summary>DPI of the monitor under the cursor (where the menu opens), else the system DPI.</summary>
        static int CurrentDpi()
        {
            try
            {
                var pos = Forms.Cursor.Position;
                var monitor = MonitorFromPoint(new POINT { X = pos.X, Y = pos.Y }, 2);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x >= 96) return (int)x;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
            return SystemDpi();
        }

        /// <summary>Notification-area menus follow the taskbar theme (SystemUsesLightTheme), not the app theme.</summary>
        static bool SystemUsesDarkMenus()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("SystemUsesLightTheme") is int v && v == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException) { return false; }
        }

        /// <summary>Windows 11 rounds the popup through DWM; older Windows ignores the attribute.</summary>
        static void RoundCorners(IntPtr hwnd)
        {
            if (!TrayText.RoundedCorners(Environment.OSVersion.Version.Build)) return;
            try
            {
                int pref = 3; // DWMWCP_ROUNDSMALL, as the shell uses for menus
                DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Removes the icon from the notification area at once, so no ghost icon stays behind.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _vm.PropertyChanged -= OnViewModelChanged;
            Loc.Instance.PropertyChanged -= OnLanguageChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _icon.Visible = false;
            _menu.Dispose();
            _icon.Dispose();
            _idleIcon.Dispose();
            _connectedIcon.Dispose();
            foreach (var b in new[] { _openImage, _connectImage, _disconnectImage, _logsImage, _exitImage, _dotImage }) b?.Dispose();
            _itemFont?.Dispose();
            _boldFont?.Dispose();
        }
    }

    /// <summary>Flat Windows 11 menu look: plain background, inset rounded hover, muted disabled text, thin separators.</summary>
    sealed class TrayRenderer : Forms.ToolStripRenderer
    {
        public TrayPalette Palette = TrayPalette.Light;
        public int Dpi = 96;
        public bool Rounded;

        static Drawing.Color C(uint argb) => Drawing.Color.FromArgb(unchecked((int)argb));

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using (var b = new Drawing.SolidBrush(C(Palette.Background))) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            // Windows 11 draws its own border around the rounded popup.
            if (Rounded) return;
            var r = new Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var p = new Drawing.Pen(C(Palette.Border))) e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var inset = TrayText.Scale(Dpi, 4);
            var r = new Drawing.Rectangle(inset, 1, e.Item.Width - 2 * inset, e.Item.Height - 2);
            var g = e.Graphics;
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = RoundRect(r, TrayText.Scale(Dpi, 4)))
            using (var b = new Drawing.SolidBrush(C(Palette.Hover)))
                g.FillPath(b, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = C(e.Item.Enabled ? Palette.Text : Palette.MutedText);
            // Centre the text on the whole item height so it lines up with the centred icon at every DPI.
            var r = e.TextRectangle;
            e.TextRectangle = new Drawing.Rectangle(r.X, 0, r.Width, e.Item.Height);
            e.TextFormat = (e.TextFormat & ~(Forms.TextFormatFlags.Top | Forms.TextFormatFlags.Bottom)) | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(Forms.ToolStripItemImageRenderEventArgs e)
        {
            // Draw the bitmap as is (the status dot keeps its colour on the disabled header) and centred in the margin.
            if (e.Image == null) return;
            var r = e.ImageRectangle;
            var x = r.X + (r.Width - e.Image.Width) / 2 + TrayText.Scale(Dpi, 6);
            var y = r.Y + (r.Height - e.Image.Height) / 2;
            e.Graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(e.Image, new Drawing.Rectangle(x, y, e.Image.Width, e.Image.Height));
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            var inset = TrayText.Scale(Dpi, 4);
            using (var p = new Drawing.Pen(C(Palette.Separator))) e.Graphics.DrawLine(p, inset, y, e.Item.Width - inset, y);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = C(Palette.Text);
            base.OnRenderArrow(e);
        }

        static Drawing.Drawing2D.GraphicsPath RoundRect(Drawing.Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new Drawing.Drawing2D.GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
