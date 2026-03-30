using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Domain.Contracts;

public interface ITiaPortalGateway
{
    Task<TiaProjectContext> GetCurrentContextAsync(CancellationToken cancellationToken);
    Task<bool> SaveProjectAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> ArchiveProjectAsync(string sessionId, string destinationArchivePath, TimeSpan timeout, CancellationToken cancellationToken);
}
