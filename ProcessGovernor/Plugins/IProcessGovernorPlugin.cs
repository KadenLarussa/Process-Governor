namespace ProcessGovernor.Plugins;

public interface IProcessGovernorPlugin
{
    PluginManifest Manifest { get; }

    ValueTask InitializeAsync(PluginContext context, CancellationToken cancellationToken);

    IReadOnlyCollection<PluginActionDescriptor> GetActions();
}
