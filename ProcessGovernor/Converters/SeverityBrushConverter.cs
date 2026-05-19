using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ProcessGovernor.Models;

namespace ProcessGovernor.Converters;

public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            LogSeverity.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107)),
            LogSeverity.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 199, 95)),
            LogSeverity.Information => new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 194, 255)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 164, 178))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
