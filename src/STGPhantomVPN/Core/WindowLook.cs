namespace STGPhantomVPN.Core
{
    /// <summary>Which system backdrop the main window can use (DWMWA_SYSTEMBACKDROP_TYPE and its predecessor).</summary>
    public enum Backdrop { Solid, MicaLegacy, Mica }

    /// <summary>
    /// Windows 11 look decisions by Windows build, kept pure for tests: Mica backdrop, dark title bar attribute,
    /// rounded corners. Windows 7, 8.x and 10 get the solid Fluent background with square corners.
    /// </summary>
    public static class WindowLook
    {
        public const int Windows11 = 22000;
        /// <summary>Windows 11 22H2: DWMWA_SYSTEMBACKDROP_TYPE (38) is documented from this build.</summary>
        public const int Windows11_22H2 = 22621;

        public const int DwmwaUseImmersiveDarkModeOld = 19, DwmwaUseImmersiveDarkMode = 20;
        public const int DwmwaWindowCornerPreference = 33, DwmwaSystemBackdropType = 38, DwmwaMicaEffect = 1029;
        public const int DwmwcpRound = 2, DwmsbtMainWindow = 2;

        public static Backdrop BackdropFor(int build) =>
            build >= Windows11_22H2 ? Backdrop.Mica : build >= Windows11 ? Backdrop.MicaLegacy : Backdrop.Solid;

        /// <summary>The dark title-bar attribute was 19 before Windows 10 20H1 (build 18985) and 20 after; none before 1809.</summary>
        public static int? DarkModeAttribute(int build) =>
            build >= 18985 ? DwmwaUseImmersiveDarkMode : build >= 17763 ? DwmwaUseImmersiveDarkModeOld : (int?)null;

        public static bool RoundedCorners(int build) => build >= Windows11;

        /// <summary>Caption bar height in DIP; the pane and content start below it.</summary>
        public const double TitleBarHeight = 40;
    }
}
