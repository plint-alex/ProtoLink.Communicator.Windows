using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ProtoLink.Communicator.Windows.Converters;

public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value as string;
        if (string.IsNullOrEmpty(status)) return System.Windows.Media.Brushes.Black;
        if (status.Contains("successfully", StringComparison.OrdinalIgnoreCase) || status.Contains("Success"))
            return System.Windows.Media.Brushes.Green;
        if (status.Contains("error", StringComparison.OrdinalIgnoreCase) || status.Contains("Failed") || status.Contains("Invalid"))
            return System.Windows.Media.Brushes.Red;
        return System.Windows.Media.Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
