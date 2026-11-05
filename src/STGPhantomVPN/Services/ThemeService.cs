using System;
using System.Windows;
using Microsoft.Win32;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Light / Dark / Follow-system for the Fluent theme: swaps Themes/Colors.Light.xaml and Colors.Dark.xaml
    /// in the application resources (every brush is a DynamicResource) and follows the Windows app theme
    /// (AppsUseLightTheme) live while set to System.
    /// </summary>
    public static class ThemeService
    {
        static ThemeMode _mode = ThemeMode.System;
        static bool _hooked;
        static ResourceDictionary _colors;

        public static bool IsDark { get; private set; }
        public static event Action Changed;

        static readonly Uri LightUri = new Uri("pack://application:,,,/STGPhantomVPN;component/Themes/Colors.Light.xaml", UriKind.Absolute);
        static readonly Uri DarkUri = new Uri("pack://application:,,,/STGPhantomVPN;component/Themes/Colors.Dark.xaml", UriKind.Absolute);

        public static void Apply(ThemeMode mode)
        {
            _mode = mode;
            if (!_hooked)
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (_mode == ThemeMode.System && e.Category == UserPreferenceCategory.General)
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(ThemeMode.System)));
                };
                _hooked = true;
            }

            var dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemAppsDark());
            var app = Application.Current;
            if (app == null) return;
            var merged = app.Resources.MergedDictionaries;
            if (_colors == null)
                foreach (var d in merged)
                    if (d.Source != null && d.Source.OriginalString.Contains("Colors.")) { _colors = d; break; }
            if (_colors != null && IsDark == dark && merged.Contains(_colors)) { Changed?.Invoke(); return; }
            var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
            var index = _colors != null ? merged.IndexOf(_colors) : -1;
            if (index >= 0) merged[index] = next; else merged.Insert(0, next);
            _colors = next;
            IsDark = dark;
            Changed?.Invoke();
        }

        /// <summary>Windows 10/11 app theme; Windows 7/8 have none and stay light.</summary>
        static bool SystemAppsDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return false;
            }
        }
    }
}
