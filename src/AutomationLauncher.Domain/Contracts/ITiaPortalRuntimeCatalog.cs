using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Domain.Contracts;

public interface ITiaPortalRuntimeCatalog
{
    IReadOnlyList<TiaPortalRuntimeInfo> GetAvailableRuntimes();
}