namespace ProcessGovernor.Core;

public sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProcessGovernor");
        ConfigDirectory = Path.Combine(Root, "config");
        LogsDirectory = Path.Combine(Root, "logs");
    }

    public string Root { get; }

    public string ConfigDirectory { get; }

    public string LogsDirectory { get; }

    public string GetConfigPath(string fileName) => Path.Combine(ConfigDirectory, fileName);

    public string GetLogPath(string fileName) => Path.Combine(LogsDirectory, fileName);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
