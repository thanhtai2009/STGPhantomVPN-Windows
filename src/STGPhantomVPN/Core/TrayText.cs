namespace STGPhantomVPN.Core
{
    /// <summary>What the tray menu's connect item does for the current connection state.</summary>
    public enum TrayConnectAction { Connect, Disconnect, Busy }

    /// <summary>Colour of the status dot in the tray menu header.</summary>
    public enum TrayDot { Idle, Busy, Connected, Problem }

    /// <summary>
    /// Tray menu colours (ARGB) close to the Windows 11 menus, one set per system theme. The menu follows the
    /// taskbar theme (SystemUsesLightTheme), not the app theme, like the other notification-area menus.
    /// </summary>
    public sealed class TrayPalette
    {
        public uint Background, Border, Hover, Text, MutedText, Separator, Icon, Accent, Danger;

        public static readonly TrayPalette Light = new TrayPalette
        {
            Background = 0xFFF9F9F9, Border = 0xFFE0E0E0, Hover = 0xFFEAEAEA, Text = 0xFF1B1F1E, MutedText = 0xFF5F6B69,
            Separator = 0xFFE3E3E3, Icon = 0xFF1B1F1E, Accent = 0xFF0E9688, Danger = 0xFFDC2626,
        };

        public static readonly TrayPalette Dark = new TrayPalette
        {
            Background = 0xFF2B2B2B, Border = 0xFF3F3F3F, Hover = 0xFF3A3A3A, Text = 0xFFF2F2F2, MutedText = 0xFFB4BDBB,
            Separator = 0xFF404040, Icon = 0xFFE3E8E7, Accent = 0xFF2DD4BF, Danger = 0xFFF87171,
        };

        public static TrayPalette For(bool dark) => dark ? Dark : Light;

        /// <summary>Status dot colours: idle grey, connecting amber, connected green, failed red.</summary>
        public static uint DotColor(TrayDot dot)
        {
            switch (dot)
            {
                case TrayDot.Connected: return 0xFF22C55E;
                case TrayDot.Busy: return 0xFFF59E0B;
                case TrayDot.Problem: return 0xFFEF4444;
                default: return 0xFF9CA3AF;
            }
        }
    }

    /// <summary>Texts and menu state for the notification-area icon; pure so it can be tested off Windows.</summary>
    public static class TrayText
    {
        /// <summary>NotifyIcon.Text on .NET Framework rejects more than 63 characters.</summary>
        public const int MaxTooltipLength = 63;

        /// <summary>"App name", "status" and the server country on separate lines, shortened to fit the tooltip limit.</summary>
        public static string Tooltip(string appName, string status, string country)
        {
            var text = appName ?? "";
            if (!string.IsNullOrWhiteSpace(status)) text += "\n" + status.Trim();
            if (!string.IsNullOrWhiteSpace(country)) text += "\n" + country.Trim();
            return text.Length <= MaxTooltipLength ? text : text.Substring(0, MaxTooltipLength - 1).TrimEnd() + "…";
        }

        public static TrayConnectAction ConnectAction(bool connected, bool busy) =>
            busy ? TrayConnectAction.Busy : connected ? TrayConnectAction.Disconnect : TrayConnectAction.Connect;

        /// <summary>Menu icons are 16 DIP; one bitmap per DPI keeps them pixel-exact (16, 20, 24, 32 px at 100-200 %).</summary>
        public static int IconPixels(int dpi) => Scale(dpi, 16);

        public static int Scale(int dpi, int dip) => (int)System.Math.Round(dip * (dpi > 0 ? dpi : 96) / 96.0, System.MidpointRounding.AwayFromZero);

        /// <summary>Header line: "Status · Country" while a server is in use, else the status alone.</summary>
        public static string Header(string status, string country, bool showCountry)
        {
            var text = (status ?? "").Trim();
            if (showCountry && !string.IsNullOrWhiteSpace(country)) text += (text.Length > 0 ? " · " : "") + country.Trim();
            return text;
        }

        public static TrayDot Dot(bool connected, bool busy, bool failed) =>
            busy ? TrayDot.Busy : connected ? TrayDot.Connected : failed ? TrayDot.Problem : TrayDot.Idle;

        /// <summary>Windows 11 (build 22000+) rounds popup menus through DWM; older versions keep square corners.</summary>
        public static bool RoundedCorners(int windowsBuild) => windowsBuild >= 22000;

        /// <summary>Close hides to the tray unless the user turned that off or the app is exiting or Windows is ending the session.</summary>
        public static bool CloseHidesToTray(bool minimizeToTray, bool exitRequested, bool sessionEnding) =>
            minimizeToTray && !exitRequested && !sessionEnding;

        /// <summary>Exiting from the tray asks first only while a tunnel is up or being set up.</summary>
        public static bool ExitNeedsConfirmation(bool connected, bool busy) => connected || busy;
    }
}
