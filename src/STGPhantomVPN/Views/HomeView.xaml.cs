using System.Windows;
using System.Windows.Controls;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Views
{
    /// <summary>
    /// Home arranges the same cards in one column (compact window) or two columns (wide, short page) and
    /// lets the Viewbox scale the block to the page, never past <see cref="HomeLayout.MaxScale"/>.
    /// One column: hero with the shield beside the title. Two columns: a tall hero (shield above the title,
    /// full-width connect button) on the left, server, session and IP cards on the right.
    /// </summary>
    public partial class HomeView : UserControl
    {
        bool? _twoColumns;

        public HomeView()
        {
            InitializeComponent();
            SizeChanged += (s, e) => ApplyLayout();
            Loaded += (s, e) => ApplyLayout();
            Board.SizeChanged += (s, e) => CapScale(e.NewSize);
        }

        void ApplyLayout()
        {
            var height = ActualHeight - (Credits?.ActualHeight ?? 0);
            var two = HomeLayout.UseTwoColumns(ActualWidth, height);
            if (_twoColumns == two) return;
            _twoColumns = two;

            LeftColumn.Width = new GridLength(two ? HomeLayout.WideColumnWidth : HomeLayout.SingleColumnWidth);
            GapColumn.Width = new GridLength(two ? HomeLayout.ColumnGap : 0);
            RightColumn.Width = new GridLength(two ? HomeLayout.WideColumnWidth : 0);
            Grid.SetRow(Secondary, two ? 0 : 1);
            Grid.SetColumn(Secondary, two ? 2 : 0);
            Primary.VerticalAlignment = two ? VerticalAlignment.Center : VerticalAlignment.Top;

            // Hero: side by side when compact, stacked and centred when wide.
            Grid.SetRow(HeroText, two ? 1 : 0);
            Grid.SetColumn(HeroText, two ? 0 : 1);
            Grid.SetColumnSpan(ShieldBox, two ? 2 : 1);
            Grid.SetColumnSpan(HeroText, two ? 2 : 1);
            HeroText.Margin = two ? new Thickness(0, 16, 0, 0) : new Thickness(20, 0, 0, 0);
            var align = two ? TextAlignment.Center : TextAlignment.Left;
            HeroTitle.TextAlignment = HeroSubtitle.TextAlignment = align;
            ConnectButton.HorizontalAlignment = two ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            StatusCard.Padding = two ? new Thickness(24, 36, 24, 28) : new Thickness(24, 18, 24, 18);
            ShieldBox.Width = ShieldBox.Height = two ? 140 : 104;
            ShieldGlyph.Width = ShieldGlyph.Height = two ? 100 : 76;
            ConnectRing.Width = ConnectRing.Height = two ? 84 : 64;
        }

        void CapScale(Size natural)
        {
            if (natural.Width <= 0 || natural.Height <= 0) return;
            Fit.MaxWidth = natural.Width * HomeLayout.MaxScale;
            Fit.MaxHeight = natural.Height * HomeLayout.MaxScale;
        }
    }
}
