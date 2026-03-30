using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public sealed class AutomationLauncherSettings
{
    public ArchiveOptions Archive { get; set; } = new();
}
