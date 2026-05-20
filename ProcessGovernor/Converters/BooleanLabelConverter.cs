using System.Globalization;
using System.Windows.Data;

namespace ProcessGovernor.Converters;

public sealed class BooleanLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var labels = (parameter as string)?.Split('/', 2, StringSplitOptions.TrimEntries);
        var trueLabel = labels is { Length: > 0 } ? labels[0] : "On";
        var falseLabel = labels is { Length: > 1 } ? labels[1] : "Off";
        return value is true ? trueLabel : falseLabel;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
