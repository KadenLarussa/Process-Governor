using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly AppPaths _paths;
    private readonly IJsonFileStore _store;

    public SettingsService(AppPaths paths, IJsonFileStore store)
    {
        _paths = paths;
        _store = store;
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Current = await _store.LoadAsync(_paths.GetConfigPath("settings.json"), static () => new AppSettings(), cancellationToken).ConfigureAwait(false);
        var migrated = MigrateAppearanceDefaults(Current);
        if (migrated)
        {
            await _store.SaveAsync(_paths.GetConfigPath("settings.json"), Current, cancellationToken).ConfigureAwait(false);
        }

        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Current = settings;
        await _store.SaveAsync(_paths.GetConfigPath("settings.json"), settings, cancellationToken).ConfigureAwait(false);
        SettingsChanged?.Invoke(this, Current);
    }

    private static bool MigrateAppearanceDefaults(AppSettings settings)
    {
        var changed = false;
        if (string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(settings.Theme, "Midnight Gold", StringComparison.OrdinalIgnoreCase))
        {
            settings.Theme = "Soft Graphite";
            changed = true;
        }

        if (string.Equals(settings.AccentColor, "#4CC2FF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(settings.AccentColor, "#D7B45A", StringComparison.OrdinalIgnoreCase))
        {
            settings.AccentColor = "#70B7FF";
            changed = true;
        }

        return changed;
    }
}
