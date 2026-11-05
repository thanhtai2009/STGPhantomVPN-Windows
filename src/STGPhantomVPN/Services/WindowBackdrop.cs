using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Integrated title bar and Windows 11 Mica for the main window. Windows 11: the DWM frame is extended over
    /// the whole window, WPF renders on a transparent surface and DWM draws Mica behind it, with rounded corners
    /// and a dark or light frame. Windows 7/8/10: same layout on the solid Fluent background. The caption
    /// buttons are the app's own on every version (the system ones are removed with WS_SYSMENU).
    /// </summary>
    public static class WindowBackdrop
    {
        /// <summary>True while DWM draws Mica behind the window; the window background is then transparent.</summary>
        public static bool IsMicaActive { get; private set; }

        public static void Attach(Window window)
        {
            var chrome = new WindowChrome
            {
                CaptionHeight = WindowLook.TitleBarHeight,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
                GlassFrameThickness = WindowLook.BackdropFor(Build) == Backdrop.Solid ? new Thickness(0) : new Thickness(-1),
            };
            WindowChrome.SetWindowChrome(window, chrome);
            window.SourceInitialized += (s, e) => Apply(window, ThemeService.IsDark);
            ThemeService.Changed += () => Apply(window, ThemeService.IsDark);
        }

        static int Build => Environment.OSVersion.Version.Build;

        public static void Apply(Window window, bool dark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var build = Build;
            var mica = false;
            try
            {
                HideSystemCaptionButtons(hwnd);
                var darkAttr = WindowLook.DarkModeAttribute(build);
                if (darkAttr.HasValue) SetInt(hwnd, darkAttr.Value, dark ? 1 : 0);
                if (WindowLook.RoundedCorners(build)) SetInt(hwnd, WindowLook.DwmwaWindowCornerPreference, WindowLook.DwmwcpRound);
                switch (WindowLook.BackdropFor(build))
                {
                    case Backdrop.Mica:
                        mica = ExtendFrame(hwnd) && SetInt(hwnd, WindowLook.DwmwaSystemBackdropType, WindowLook.DwmsbtMainWindow);
                        break;
                    case Backdrop.MicaLegacy:
                        mica = ExtendFrame(hwnd) && SetInt(hwnd, WindowLook.DwmwaMicaEffect, 1);
                        break;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                mica = false;
            }

            IsMicaActive = mica;
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = mica ? Colors.Transparent : (dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            if (mica) window.Background = Brushes.Transparent;
            else window.SetResourceReference(Window.BackgroundProperty, "Fluent.WindowBg");
        }

        /// <summary>Standard-frame windows (log viewer): dark or light title bar to match the app theme.</summary>
        public static void ApplyFrameTheme(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var attr = WindowLook.DarkModeAttribute(Build);
            if (hwnd == IntPtr.Zero || !attr.HasValue) return;
            try { SetInt(hwnd, attr.Value, ThemeService.IsDark ? 1 : 0); }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
        }

        static bool ExtendFrame(IntPtr hwnd)
        {
            var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            return DwmExtendFrameIntoClientArea(hwnd, ref m) == 0;
        }

        static bool SetInt(IntPtr hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;

        /// <summary>Without WS_SYSMENU Windows draws no caption buttons of its own over the extended frame.</summary>
        static void HideSystemCaptionButtons(IntPtr hwnd)
        {
            const int GwlStyle = -16, WsSysMenu = 0x00080000;
            var style = GetWindowLong(hwnd, GwlStyle);
            if ((style & WsSysMenu) != 0) SetWindowLong(hwnd, GwlStyle, style & ~WsSysMenu);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    }
}
