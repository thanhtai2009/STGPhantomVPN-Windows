using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Views
{
    /// <summary>Attached options for the Fluent control styles in Themes/Fluent.xaml.</summary>
    public static class FluentAssist
    {
        /// <summary>Placeholder text of a Fluent TextBox / ComboBox.</summary>
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
            "Placeholder", typeof(string), typeof(FluentAssist), new FrameworkPropertyMetadata(""));
        public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
        public static void SetPlaceholder(DependencyObject d, string v) => d.SetValue(PlaceholderProperty, v);

        /// <summary>Shows a small ProgressRing in front of a button's content while work runs.</summary>
        public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
            "IsBusy", typeof(bool), typeof(FluentAssist), new FrameworkPropertyMetadata(false));
        public static bool GetIsBusy(DependencyObject d) => (bool)d.GetValue(IsBusyProperty);
        public static void SetIsBusy(DependencyObject d, bool v) => d.SetValue(IsBusyProperty, v);

        /// <summary>Corner radius of buttons and inputs: 4 (WinUI ControlCornerRadius) unless set.</summary>
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
            "CornerRadius", typeof(CornerRadius), typeof(FluentAssist), new FrameworkPropertyMetadata(new CornerRadius(4)));
        public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
        public static void SetCornerRadius(DependencyObject d, CornerRadius v) => d.SetValue(CornerRadiusProperty, v);
    }

    /// <summary>WinUI ProgressRing: a rotating accent arc over a faint track, drawn as vectors so it is sharp at any DPI.</summary>
    public sealed class ProgressRing : FrameworkElement
    {
        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(ProgressRing), new FrameworkPropertyMetadata(true, (d, e) => ((ProgressRing)d).UpdateAnimation()));
        public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(
            typeof(ProgressRing), new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ShowTrackProperty = DependencyProperty.Register(
            nameof(ShowTrack), typeof(bool), typeof(ProgressRing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        /// <summary>Stroke width; 0 picks about a tenth of the diameter (WinUI uses 4 at 32 px).</summary>
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public bool ShowTrack { get => (bool)GetValue(ShowTrackProperty); set => SetValue(ShowTrackProperty, value); }

        readonly RotateTransform _spin = new RotateTransform();

        public ProgressRing()
        {
            Width = Height = 32;
            RenderTransform = _spin;
            RenderTransformOrigin = new Point(0.5, 0.5);
            IsVisibleChanged += (s, e) => UpdateAnimation();
            IsHitTestVisible = false;
        }

        void UpdateAnimation()
        {
            if (IsActive && IsVisible)
                _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
            else
                _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) => new Size(0, 0);

        protected override void OnRender(DrawingContext dc)
        {
            if (!IsActive) return;
            var size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            var t = Thickness > 0 ? Thickness : Math.Max(1.5, size / 9);
            var r = (size - t) / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            if (ShowTrack)
            {
                var track = Foreground.Clone();
                track.Opacity = 0.18;
                dc.DrawEllipse(null, new Pen(track, t), c, r, r);
            }
            // A 100 degree arc, round caps, as the indeterminate WinUI ring looks mid-spin.
            var start = new Point(c.X, c.Y - r);
            var a = 100 * Math.PI / 180;
            var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(Foreground, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }
    }

    public enum InfoBarSeverity { Informational, Success, Warning, Error }

    /// <summary>WinUI InfoBar: tinted bar with a severity icon, bold title, message and an optional action (Content).</summary>
    public sealed class InfoBar : ContentControl
    {
        public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
            nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar), new FrameworkPropertyMetadata(InfoBarSeverity.Informational));
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(InfoBar), new FrameworkPropertyMetadata(""));
        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message), typeof(string), typeof(InfoBar), new FrameworkPropertyMetadata(""));


        public InfoBarSeverity Severity { get => (InfoBarSeverity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    }

    /// <summary>WinUI ContentDialog host: the page, plus a smoke layer and a centred dialog card while IsOpen.</summary>
    public sealed class DialogHost : ContentControl
    {
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            nameof(IsOpen), typeof(bool), typeof(DialogHost), new FrameworkPropertyMetadata(false));
        public static readonly DependencyProperty DialogContentProperty = DependencyProperty.Register(
            nameof(DialogContent), typeof(object), typeof(DialogHost), new FrameworkPropertyMetadata(null));


        public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public object DialogContent { get => GetValue(DialogContentProperty); set => SetValue(DialogContentProperty, value); }
    }

    /// <summary>
    /// Windows Community Toolkit SettingsCard: icon, header and description on the left, the control (Content)
    /// on the right. With IsClickEnabled it acts as a button row with a chevron (Command).
    /// </summary>
    public sealed class SettingsCard : ContentControl
    {
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(object), typeof(SettingsCard), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
            nameof(Description), typeof(object), typeof(SettingsCard), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(GlyphKind?), typeof(SettingsCard), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
            nameof(Footer), typeof(object), typeof(SettingsCard), new FrameworkPropertyMetadata(null));


        public object Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
        public object Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
        public GlyphKind? Icon { get => (GlyphKind?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        /// <summary>Extra content under the row (e.g. custom DNS fields), inside the same card.</summary>
        public object Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    }

    /// <summary>
    /// Windows Community Toolkit SettingsExpander: a SettingsCard-like header row (Header, Description, Icon,
    /// HeaderContent on the right) and a body (Content) under a divider, folded with the chevron.
    /// </summary>
    public sealed class SettingsExpander : ContentControl
    {
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(object), typeof(SettingsExpander), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
            nameof(Description), typeof(object), typeof(SettingsExpander), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(GlyphKind?), typeof(SettingsExpander), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
            nameof(HeaderContent), typeof(object), typeof(SettingsExpander), new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
            nameof(IsExpanded), typeof(bool), typeof(SettingsExpander), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));


        public object Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
        public object Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
        public GlyphKind? Icon { get => (GlyphKind?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public object HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
        public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    }
}
