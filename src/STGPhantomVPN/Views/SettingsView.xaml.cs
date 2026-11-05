using System.Windows;
using System.Windows.Controls;

namespace STGPhantomVPN.Views
{
    public partial class SettingsView : UserControl
    {
        /// <summary>Below this width the About and Credits cards move under the other settings.</summary>
        const double TwoColumnMinWidth = 820;

        public SettingsView()
        {
            InitializeComponent();
            SizeChanged += (s, e) => ApplyLayout(e.NewSize.Width >= TwoColumnMinWidth);
        }

        void ApplyLayout(bool twoColumns)
        {
            GapColumn.Width = new GridLength(twoColumns ? 16 : 0);
            RightColumn.Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(RightPanel, twoColumns ? 1 : 2);
            Grid.SetColumn(RightPanel, twoColumns ? 2 : 0);
            Grid.SetColumnSpan(LeftPanel, twoColumns ? 1 : 3);
            Grid.SetColumnSpan(RightPanel, twoColumns ? 1 : 3);
        }
    }
}
