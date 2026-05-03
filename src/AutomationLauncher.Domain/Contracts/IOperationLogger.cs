using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Domain.Contracts;

public interface IOperationLogger
{
    void ArchiveStarted(string correlationId, string expectedProjectPath, string outputDirectory);
    void TiaContextRead(string correlationId, TiaProjectContext context);
    void TiaDiagnostic(string correlationId, string diagnosticCode, string message);
    void OnlineStateCheckAttempted(string correlationId, string sessionId);
    void OnlineStateCheckCompleted(string correlationId, OnlineStateResult result);
    void PlcComparisonAttempted(string correlationId, string sessionId);
    void PlcComparisonCompleted(string correlationId, PlcOnlineOfflineComparisonResult result);
    void GoOfflineAttempted(string correlationId, string sessionId);
    void GoOfflineCompleted(string correlationId, GoOfflineResult result);
    void SaveAttempted(string correlationId, bool shouldSave, string reason);
    void SaveCompleted(string correlationId, bool success);
    void ArchiveAttempted(string correlationId, string archivePath, int attempt);
    void ArchiveCompleted(string correlationId, bool success, string? archivePath, TimeSpan duration);
    void Failed(string correlationId, string stage, string message, Exception? exception = null);
}
