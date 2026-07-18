using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ProcessGovernor.Converters;

public sealed class HexColorBrushConverter : IValueConverter
{
    private static readonly Brush InvalidBrush = CreateBrush(Color.FromRgb(0x34, 0x3A, 0x40));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value as string) is Color color)
            {
                return CreateBrush(color);
            }
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
        }

        return InvalidBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
