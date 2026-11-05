using System;

namespace STGPhantomVPN.Core
{
    /// <summary>Window bounds in device-independent pixels.</summary>
    public struct WindowBounds
    {
        public double Left, Top, Width, Height, MinWidth, MinHeight;
    }

    /// <summary>
    /// Initial main window size: the compact preferred size when the work area has room, otherwise the work
    /// area minus a small gap, centred in the work area so the window never sits under the taskbar.
    /// </summary>
    public static class WindowFit
    {
        /// <summary>Outer size in DIP, invisible resize borders included: about 706 x 585 visible on Windows 10/11.</summary>
        public const double PreferredWidth = 720, PreferredHeight = 592;
        public const double MinimumWidth = 720, MinimumHeight = 540;
        public const double Gap = 8;

        public static WindowBounds Compute(double workLeft, double workTop, double workWidth, double workHeight)
        {
            if (!(workWidth > 0) || !(workHeight > 0) || double.IsInfinity(workWidth) || double.IsInfinity(workHeight))
                return new WindowBounds { Left = double.NaN, Top = double.NaN, Width = PreferredWidth, Height = PreferredHeight, MinWidth = MinimumWidth, MinHeight = MinimumHeight };

            var width = Math.Max(1, Math.Min(PreferredWidth, workWidth - 2 * Gap));
            var height = Math.Max(1, Math.Min(PreferredHeight, workHeight - 2 * Gap));
            return new WindowBounds
            {
                Width = Math.Floor(width),
                Height = Math.Floor(height),
                MinWidth = Math.Floor(Math.Min(MinimumWidth, width)),
                MinHeight = Math.Floor(Math.Min(MinimumHeight, height)),
                Left = Math.Floor(workLeft + (workWidth - width) / 2),
                Top = Math.Floor(workTop + (workHeight - height) / 2),
            };
        }

        /// <summary>Largest dialog content height that still fits the window client area.</summary>
        public static double DialogMaxHeight(double clientHeight) =>
            double.IsNaN(clientHeight) || clientHeight <= 0 ? 560 : Math.Max(200, Math.Floor(clientHeight - 64));
    }
}
