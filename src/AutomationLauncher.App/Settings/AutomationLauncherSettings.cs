using System.Collections.Generic;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutomationLauncher.App;

public sealed class AutomationLauncherSettings
{
    public ArchiveOptions Archive { get; set; } = new();

    public StartupSettings Startup { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public UiSettings Ui { get; set; } = new();
}

public sealed class StartupSettings
{
    public bool RunOnWindowsStartup { get; set; }

    public bool RunSequenceOnWindowsStartup { get; set; } = true;

    public string SplashBackgroundImagePath { get; set; } = string.Empty;

    public IList<StartupSequenceEntry> SequenceEntries { get; set; } = new List<StartupSequenceEntry>();
}

public sealed class LoggingSettings
{
    public string DirectoryPath { get; set; } = "logs";

    public string MinimumLevel { get; set; } = "Information";

    public int RetainedFileCountLimit { get; set; } = 30;
}

public sealed class UiSettings
{
    public bool StartHiddenToTray { get; set; } = true;

    public string ControlFilesDirectory { get; set; } = string.Empty;
}

public partial class StartupSequenceEntry : ObservableObject
{
    [ObservableProperty]
    private string alias = string.Empty;

    [ObservableProperty]
    private string executablePath = string.Empty;

    [ObservableProperty]
    private int delaySeconds;

    public StartupSequenceEntry Clone()
    {
        return new StartupSequenceEntry
        {
            Alias = Alias,
            ExecutablePath = ExecutablePath,
            DelaySeconds = DelaySeconds
        };
    }
}
