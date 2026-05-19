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
        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Current = settings;
        await _store.SaveAsync(_paths.GetConfigPath("settings.json"), settings, cancellationToken).ConfigureAwait(false);
        SettingsChanged?.Invoke(this, Current);
    }
}
