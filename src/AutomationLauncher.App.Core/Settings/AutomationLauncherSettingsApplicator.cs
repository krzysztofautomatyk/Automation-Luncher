using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public static class AutomationLauncherSettingsApplicator
{
    public static void ApplyLoadedSettings(AutomationLauncherSettings target, AutomationLauncherSettings source)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        target.Archive = source.Archive ?? new ArchiveOptions();
        target.Project = source.Project ?? new ProjectSettings();
        target.ControlFiles = source.ControlFiles ?? new ControlFilesSettings();
        target.Startup = source.Startup ?? new StartupSettings();
        target.Logging = source.Logging ?? new LoggingSettings();
        target.Ui = source.Ui ?? new UiSettings();
    }
}
