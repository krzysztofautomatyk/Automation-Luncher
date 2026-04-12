using System.Reflection;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Infrastructure.Tia;

public interface IOpennessVersionProvider
{
    bool CanHandle(TiaPortalRuntimeInfo runtime);

    TiaProjectContext TryReadOpenProject(Assembly assembly, int processId, TiaPortalRuntimeInfo runtime);

    bool TrySaveProject(Assembly assembly, string sessionId, TiaPortalRuntimeInfo runtime);

    bool TryArchiveProject(Assembly assembly, string sessionId, string destinationArchivePath, TiaPortalRuntimeInfo runtime);
}
