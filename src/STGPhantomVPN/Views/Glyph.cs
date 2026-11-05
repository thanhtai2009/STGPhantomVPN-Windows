using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Views
{
    /// <summary>
    /// One of the app's own vector glyphs (<see cref="GlyphData"/>), drawn in the inherited foreground with a
    /// 2/24 round stroke. Size it with Width/Height like PackIcon; it defaults to 24 x 24.
    /// </summary>
    public sealed class Glyph : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(GlyphKind), typeof(Glyph), new FrameworkPropertyMetadata(GlyphKind.Shield, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
            typeof(Glyph), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Brush of "k" parts, e.g. the check knocked out of the filled shield.</summary>
        public static readonly DependencyProperty InnerBrushProperty = DependencyProperty.Register(
            nameof(InnerBrush), typeof(Brush), typeof(Glyph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Stroke width on the 24 grid: 2 for the brand look, 1.5 for the lighter Fluent weight in the chrome.</summary>
        public static readonly DependencyProperty StrokeWeightProperty = DependencyProperty.Register(
            nameof(StrokeWeight), typeof(double), typeof(Glyph), new FrameworkPropertyMetadata(GlyphData.Stroke, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

        public double StrokeWeight { get => (double)GetValue(StrokeWeightProperty); set => SetValue(StrokeWeightProperty, value); }

        static readonly Dictionary<GlyphKind, (char Mode, Geometry Data)[]> Cache = new Dictionary<GlyphKind, (char, Geometry)[]>();

        public GlyphKind Kind { get => (GlyphKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public Brush InnerBrush { get => (Brush)GetValue(InnerBrushProperty); set => SetValue(InnerBrushProperty, value); }

        public Glyph()
        {
            Width = Height = GlyphData.Grid;
            SnapsToDevicePixels = true;
        }

        static (char, Geometry)[] Parts(GlyphKind kind)
        {
            if (Cache.TryGetValue(kind, out var parts)) return parts;
            var src = GlyphData.Parts.TryGetValue(kind, out var p) ? p : Array.Empty<string>();
            parts = new (char, Geometry)[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var g = Geometry.Parse(src[i].Substring(2));
                g.Freeze();
                parts[i] = (src[i][0], g);
            }
            Cache[kind] = parts;
            return parts;
        }

        static Pen RoundPen(Brush brush, double weight = GlyphData.Stroke)
        {
            var p = new Pen(brush, weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            return p;
        }

        /// <summary>The glyph as a bitmap of exactly <paramref name="pixels"/> square, for WinForms menus.</summary>
        public static System.Windows.Media.Imaging.BitmapSource Render(GlyphKind kind, int pixels, Color foreground, Color inner)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var scale = pixels / GlyphData.Grid;
                dc.PushTransform(new ScaleTransform(scale, scale));
                var fg = new SolidColorBrush(foreground);
                var pen = RoundPen(fg);
                var innerPen = RoundPen(new SolidColorBrush(inner));
                foreach (var (mode, data) in Parts(kind))
                {
                    if (mode == 'f') dc.DrawGeometry(fg, null, data);
                    else dc.DrawGeometry(null, mode == 'k' ? innerPen : pen, data);
                }
                dc.Pop();
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        protected override Size MeasureOverride(Size availableSize) => new Size(0, 0);

        protected override void OnRender(DrawingContext dc)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            var scale = size / GlyphData.Grid;
            dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            Pen pen = null, inner = null;
            foreach (var (mode, data) in Parts(Kind))
            {
                if (mode == 'f')
                    dc.DrawGeometry(Foreground, null, data);
                else if (mode == 'k')
                    dc.DrawGeometry(null, inner ?? (inner = RoundPen(InnerBrush, StrokeWeight)), data);
                else
                    dc.DrawGeometry(null, pen ?? (pen = RoundPen(Foreground, StrokeWeight)), data);
            }
            dc.Pop();
            dc.Pop();
        }
    }
}
