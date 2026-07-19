using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Colors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ProcessGovernor.Services;

public sealed class ThemeService : IThemeService, IDisposable
{
    private static readonly Color DefaultAccent = Color.FromRgb(0x70, 0xB7, 0xFF);
    private readonly ISettingsService _settingsService;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public void Apply(AppSettings settings)
    {
        var accent = TryParseColor(settings.AccentColor, out var parsed) ? parsed : DefaultAccent;
        UiDispatch.Invoke(() =>
        {
            var resources = System.Windows.Application.Current?.Resources;
            if (resources is null)
            {
                return;
            }

            resources["AccentColor"] = accent;
            resources["AccentBrush"] = CreateBrush(accent);
            resources["AccentHoverBrush"] = CreateBrush(Mix(accent, Colors.White, 0.18));
            resources["AccentSoftBrush"] = CreateBrush(Mix(accent, Color.FromRgb(0x18, 0x1B, 0x1E), 0.76));
            resources["AccentSubtleBrush"] = CreateBrush(Mix(accent, Color.FromRgb(0x10, 0x12, 0x14), 0.88));
            resources["AccentTextBrush"] = CreateBrush(Color.FromRgb(0x08, 0x13, 0x1E));
            resources[System.Windows.SystemColors.HighlightBrushKey] = CreateBrush(Mix(accent, Color.FromRgb(0x18, 0x1B, 0x1E), 0.68));
        });
    }

    public void Dispose() => _settingsService.SettingsChanged -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, AppSettings settings) => Apply(settings);

    private static bool TryParseColor(string? value, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
        }

        color = DefaultAccent;
        return false;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Mix(Color foreground, Color background, double backgroundWeight)
    {
        var weight = Math.Clamp(backgroundWeight, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * (1 - weight) + background.R * weight),
            (byte)Math.Round(foreground.G * (1 - weight) + background.G * weight),
            (byte)Math.Round(foreground.B * (1 - weight) + background.B * weight));
    }
}
