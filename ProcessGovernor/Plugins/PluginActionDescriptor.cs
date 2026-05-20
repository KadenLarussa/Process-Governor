namespace ProcessGovernor.Plugins;

public sealed record PluginActionDescriptor(string Id, string DisplayName, string Description)
{
    public bool RequiresElevation { get; init; }

    public bool SupportsRollback { get; init; }
}
