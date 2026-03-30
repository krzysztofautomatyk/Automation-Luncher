using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public interface IRuntimeSelectionSettingsStore
{
    void SaveRuntimeSelection(ArchiveOptions options);
}