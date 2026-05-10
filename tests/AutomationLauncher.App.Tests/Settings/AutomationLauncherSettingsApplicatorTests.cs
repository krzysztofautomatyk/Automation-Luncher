using Xunit;

namespace AutomationLauncher.App.Tests.Settings;

public sealed class AutomationLauncherSettingsApplicatorTests
{
    [Fact]
    public void ApplyLoadedSettings_CopiesConfiguredValues()
    {
        var target = new AutomationLauncherSettings();
        var source = new AutomationLauncherSettings();
        source.Logging.DirectoryPath = @"C:\Logs\NewPath";
        source.Ui.ControlFilesDirectory = @"C:\ControlFiles";

        AutomationLauncherSettingsApplicator.ApplyLoadedSettings(target, source);

        Assert.Equal(@"C:\Logs\NewPath", target.Logging.DirectoryPath);
        Assert.Equal(@"C:\ControlFiles", target.Ui.ControlFilesDirectory);
    }

    [Fact]
    public void ApplyLoadedSettings_ReplacesNullSectionsWithDefaults()
    {
        var target = new AutomationLauncherSettings();
        var source = new AutomationLauncherSettings
        {
            Archive = null!,
            Project = null!,
            ControlFiles = null!,
            Startup = null!,
            Logging = null!,
            Ui = null!
        };

        AutomationLauncherSettingsApplicator.ApplyLoadedSettings(target, source);

        Assert.NotNull(target.Archive);
        Assert.NotNull(target.Project);
        Assert.NotNull(target.ControlFiles);
        Assert.NotNull(target.Startup);
        Assert.NotNull(target.Logging);
        Assert.NotNull(target.Ui);
    }
}
