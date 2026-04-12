namespace AutomationLauncher.App;

public sealed class AboutWindowViewModel
{
    public AboutWindowViewModel(IProtectedApplicationSettingsStore settingsStore, AutomationLauncherSettings settings)
    {
        Version = AppVersionInfo.DisplayVersion;
        VersionLabel = $"Version {Version}";
        HostName = Environment.MachineName;
        ProtectedSettingsPath = settingsStore.SettingsFilePath;
        LogDirectory = LogPathHelper.ResolveDirectory(settings.Logging.DirectoryPath);
    }

    public string Version { get; }

    public string VersionLabel { get; }

    public string HostName { get; }

    public string ProtectedSettingsPath { get; }

    public string LogDirectory { get; }
}