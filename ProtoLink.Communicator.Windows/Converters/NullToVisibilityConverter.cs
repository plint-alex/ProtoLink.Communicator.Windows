using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProtoLink.Communicator.Windows.Converters;

/// <summary>Visible when value is non-null; Inverse → visible when null.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value != null;
        if (parameter?.ToString() == "Inverse") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
