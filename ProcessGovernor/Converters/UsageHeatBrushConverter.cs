using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace ProcessGovernor.Converters;

public sealed class UsageHeatBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryGetValue(value, out var numericValue))
        {
            return FindBrush("MutedTextBrush", Brushes.Gray);
        }

        var mode = parameter as string ?? "Percent";
        var resourceKey = mode switch
        {
            "Memory" => SelectLevel(numericValue, 256 * 1024 * 1024d, 1024 * 1024 * 1024d, 2 * 1024 * 1024 * 1024d),
            "Throughput" => SelectLevel(numericValue, 1024d, 1024 * 1024d, 50 * 1024 * 1024d),
            _ => SelectLevel(numericValue, 0.1, 50, 80)
        };

        return FindBrush(resourceKey, Brushes.DodgerBlue);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;

    private static string SelectLevel(double value, double idle, double medium, double high)
    {
        if (value <= idle)
        {
            return "MutedTextBrush";
        }

        if (value < medium)
        {
            return "AccentBrush";
        }

        return value < high ? "WarningBrush" : "DangerBrush";
    }

    private static bool TryGetValue(object? value, out double numericValue)
    {
        if (value is null)
        {
            numericValue = 0;
            return false;
        }

        try
        {
            numericValue = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(numericValue) && !double.IsInfinity(numericValue);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            numericValue = 0;
            return false;
        }
    }

    private static Brush FindBrush(string key, Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
