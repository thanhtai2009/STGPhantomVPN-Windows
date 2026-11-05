using System.Windows;
using System.Windows.Controls;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Views
{
    /// <summary>The server list shows one card per row on the compact window and two or three when maximized.</summary>
    public partial class ServersView : UserControl
    {
        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            nameof(Columns), typeof(int), typeof(ServersView), new PropertyMetadata(1));

        public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

        const double SingleColumnMaxWidth = 760, MultiColumnMaxWidth = 1600;

        public ServersView()
        {
            InitializeComponent();
            SizeChanged += (s, e) =>
            {
                var columns = ServerPicker.ColumnsFor(e.NewSize.Width - 48);
                Layout.MaxWidth = columns == 1 ? SingleColumnMaxWidth : MultiColumnMaxWidth;
                if (Columns != columns) Columns = columns;
            };
        }
    }
}
