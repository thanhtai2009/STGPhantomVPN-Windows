using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace STGPhantomVPN.Converters
{
    /// <summary>true → Visible; pass ConverterParameter="invert" for the opposite.</summary>
    public sealed class BoolToVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = value is bool v && v;
            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Non-empty string → Visible, null or empty → Collapsed.</summary>
    public sealed class NonEmptyToVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public sealed class InverseBool : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }

    /// <summary>Binds a RadioButton to an enum or string value: IsChecked = (value == parameter).</summary>
    public sealed class EqualsParameter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool b) || !b || parameter == null) return Binding.DoNothing;
            if (targetType.IsEnum) return Enum.Parse(targetType, parameter.ToString(), true);
            return parameter.ToString();
        }
    }

    /// <summary>MultiBinding: true (or Visible) when both bound servers have the same host and IP.</summary>
    public sealed class SameServer : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var same = values.Length == 2 && values[0] is Core.VpnServer a && values[1] is Core.VpnServer b && a.Key == b.Key;
            if (targetType == typeof(Visibility)) return same ? Visibility.Visible : Visibility.Collapsed;
            return same;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }

    /// <summary>(server list, columns) to rows of server cards for the multi-column server list.</summary>
    public sealed class ServerRows : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var list = values.Length > 0 ? values[0] as System.Collections.Generic.IReadOnlyList<Core.VpnServer> : null;
            var columns = values.Length > 1 && values[1] is int c ? c : 1;
            return Core.ServerPicker.Rows(list, columns);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }
}

namespace STGPhantomVPN.Converters
{
    /// <summary>
    /// Navigation pane: true / Visible when the selected tab index equals the parameter; checking a pane item
    /// selects its tab.
    /// </summary>
    public sealed class TabIs : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var same = value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p;
            if (targetType == typeof(System.Windows.Visibility)) return same ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            return same;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool b && b && int.TryParse(parameter?.ToString(), out var p) ? (object)p : System.Windows.Data.Binding.DoNothing;
    }
}
