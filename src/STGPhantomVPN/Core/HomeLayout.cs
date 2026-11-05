using System;

namespace STGPhantomVPN.Core
{
    /// <summary>
    /// Home page layout: one column on the compact window, two columns (status and connect on the left,
    /// server, statistics and IP on the right) when the page is wide and short, e.g. a maximized window on a
    /// laptop. The block is scaled to the page, down without limit and up to <see cref="MaxScale"/>.
    /// </summary>
    public static class HomeLayout
    {
        public const double SingleColumnWidth = 540;
        public const double WideColumnWidth = 440;
        public const double ColumnGap = 24;
        /// <summary>Text and cards stop growing here; the rest of a very large page stays as margin.</summary>
        public const double MaxScale = 1.5;
        public const double MinWideWidth = 860;
        public const double MinWideAspect = 1.35;

        public static bool UseTwoColumns(double width, double height) =>
            width >= MinWideWidth && height > 0 && width / height >= MinWideAspect;

        /// <summary>Uniform scale that fits the natural size into the available size, capped at <see cref="MaxScale"/>.</summary>
        public static double Scale(double availableWidth, double availableHeight, double naturalWidth, double naturalHeight)
        {
            if (!(availableWidth > 0) || !(availableHeight > 0) || !(naturalWidth > 0) || !(naturalHeight > 0)) return 1;
            return Math.Max(0.1, Math.Min(MaxScale, Math.Min(availableWidth / naturalWidth, availableHeight / naturalHeight)));
        }
    }
}
