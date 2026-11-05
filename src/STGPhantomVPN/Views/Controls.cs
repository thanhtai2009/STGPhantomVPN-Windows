using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using STGPhantomVPN.Core;
using STGPhantomVPN.Services;
using STGPhantomVPN.ViewModels;

namespace STGPhantomVPN.Views
{
    /// <summary>
    /// Country flag in a tonal circle, from the bundled PNGs (<see cref="CountryFlags"/>). Unknown codes show
    /// a globe. The two-letter code stays available as the tooltip.
    /// </summary>
    public sealed class CountryBadge : Border
    {
        public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
            nameof(Code), typeof(string), typeof(CountryBadge), new PropertyMetadata("", (d, e) => ((CountryBadge)d).Update(e.NewValue as string)));

        const double FlagWidth = 28, FlagHeight = 21, FlagRadius = 3;
        static readonly Dictionary<string, BitmapImage> Cache = new Dictionary<string, BitmapImage>(StringComparer.Ordinal);

        readonly Image _flag = new Image
        {
            Width = FlagWidth,
            Height = FlagHeight,
            Stretch = Stretch.UniformToFill,
            Clip = new RectangleGeometry(new Rect(0, 0, FlagWidth, FlagHeight), FlagRadius, FlagRadius),
        };
        readonly Border _flagFrame;
        readonly Glyph _globe = new Glyph
        {
            Kind = GlyphKind.Globe,
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

        public CountryBadge()
        {
            Width = 40;
            Height = 40;
            CornerRadius = new CornerRadius(8);
            VerticalAlignment = VerticalAlignment.Center;
            SetResourceReference(BackgroundProperty, "Fluent.AccentSubtle");
            _globe.SetResourceReference(Glyph.ForegroundProperty, "Fluent.AccentText");
            RenderOptions.SetBitmapScalingMode(_flag, BitmapScalingMode.HighQuality);

            // A hairline keeps white flags (Japan, Korea) distinct from a light background.
            _flagFrame = new Border
            {
                Width = FlagWidth,
                Height = FlagHeight,
                CornerRadius = new CornerRadius(FlagRadius),
                BorderThickness = new Thickness(0.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            var grid = new Grid();
            grid.Children.Add(_flag);
            grid.Children.Add(_flagFrame);
            grid.Children.Add(_globe);
            Child = grid;
            Update(Code);
        }

        void Update(string code)
        {
            var image = Load(CountryFlags.ResourcePath(code));
            _flag.Source = image;
            var hasFlag = image != null;
            _flag.Visibility = _flagFrame.Visibility = hasFlag ? Visibility.Visible : Visibility.Collapsed;
            _globe.Visibility = hasFlag ? Visibility.Collapsed : Visibility.Visible;
            var normalized = CountryFlags.Normalize(code);
            ToolTip = normalized?.ToUpperInvariant();
        }

        internal static BitmapImage Load(string path)
        {
            if (path == null) return null;
            if (Cache.TryGetValue(path, out var cached)) return cached;
            BitmapImage image = null;
            try
            {
                image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri("pack://application:,,,/STGPhantomVPN;component/" + path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException)
            {
                Log.Warn("Flag image " + path + " could not be loaded: " + ex.Message);
                image = null;
            }
            Cache[path] = image;
            return image;
        }
    }

    /// <summary>Small inline flag (20 x 15) for a country code; collapsed when the code is unknown.</summary>
    public sealed class SmallFlag : Border
    {
        public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
            nameof(Code), typeof(string), typeof(SmallFlag), new PropertyMetadata("", (d, e) => ((SmallFlag)d).Update(e.NewValue as string)));

        readonly Image _image = new Image { Width = 20, Height = 15, Stretch = Stretch.UniformToFill };

        public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

        public SmallFlag()
        {
            CornerRadius = new CornerRadius(2);
            // 1px neutral outline so white-field flags (Japan, ...) stay visible on light cards.
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x80, 0x80, 0x80));
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(0, 0, 6, 0);
            _image.Clip = new RectangleGeometry(new Rect(0, 0, 20, 15), 2, 2);
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            Child = _image;
            Update(Code);
        }

        void Update(string code)
        {
            var image = CountryBadge.Load(CountryFlags.ResourcePath(code));
            _image.Source = image;
            Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = CountryFlags.Normalize(code)?.ToUpperInvariant();
        }
    }

    /// <summary>Label above a large value, as in the Android stat cells.</summary>
    public sealed class StatCell : StackPanel
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(StatCell), new PropertyMetadata("", (d, e) => ((StatCell)d)._label.Text = e.NewValue as string));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(string), typeof(StatCell), new PropertyMetadata("", (d, e) => ((StatCell)d)._value.Text = e.NewValue as string));

        readonly TextBlock _label = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _value = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

        public StatCell()
        {
            Margin = new Thickness(16, 0, 16, 0);
            VerticalAlignment = VerticalAlignment.Center;
            _label.SetResourceReference(TextBlock.ForegroundProperty, "Fluent.TextSecondary");
            Children.Add(_label);
            Children.Add(_value);
        }
    }

    /// <summary>"Designed by TaiNT13@PNC STG" link that opens a feedback e-mail, like the Android footer.</summary>
    public sealed class DesignerCredits : TextBlock
    {
        public DesignerCredits()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            TextAlignment = TextAlignment.Center;
            FontSize = 12;
            Cursor = Cursors.Hand;
            SetResourceReference(ForegroundProperty, "Fluent.AccentText");
            Text = Loc.T("credits");
            ToolTip = Loc.T("feedback_hint");
            Loc.Instance.PropertyChanged += (s, e) => ToolTip = Loc.T("feedback_hint");
            MouseLeftButtonUp += (s, e) =>
            {
                if (DataContext is MainViewModel vm && vm.FeedbackCommand.CanExecute(null)) vm.FeedbackCommand.Execute(null);
            };
        }
    }
}
