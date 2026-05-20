namespace ProcessGovernor.Plugins;

public sealed record PluginManifest(string Id, string Name, Version Version)
{
    public string Author { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Version MinimumAppVersion { get; init; } = new(0, 1, 0);
}
