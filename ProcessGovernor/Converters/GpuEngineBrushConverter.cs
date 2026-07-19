using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace ProcessGovernor.Converters;

public sealed class GpuEngineBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var engine = value as string;
        var resourceKey = engine switch
        {
            "Decode" => "DecodeBrush",
            "Encode" => "EncodeBrush",
            "Compute" => "WarningBrush",
            "Video" => "DecodeBrush",
            "3D" => "AccentBrush",
            _ => "MutedTextBrush"
        };

        return System.Windows.Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
