using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using Serilog;

namespace AutomationLauncher.Infrastructure.Logging;

public sealed class SerilogOperationLogger : IOperationLogger
{
    private readonly ILogger _logger;

    public SerilogOperationLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void ArchiveStarted(string correlationId, string expectedProjectPath, string outputDirectory)
    {
        _logger.Information("ArchiveStarted CorrelationId={CorrelationId} ExpectedProjectPath={ExpectedProjectPath} OutputDirectory={OutputDirectory}",
            correlationId, expectedProjectPath, outputDirectory);
    }

    public void TiaContextRead(string correlationId, TiaProjectContext context)
    {
        _logger.Information(
            "TiaContextRead CorrelationId={CorrelationId} IsTiaRunning={IsTiaRunning} OpenProjectPath={OpenProjectPath} SessionId={SessionId} HasUnsavedChanges={HasUnsavedChanges} UnsavedStateDetectedReliably={UnsavedStateDetectedReliably} DiagnosticCode={DiagnosticCode} DiagnosticMessage={DiagnosticMessage}",
            correlationId,
            context.IsTiaRunning,
            context.OpenProjectPath,
            context.SessionId,
            context.HasUnsavedChanges,
            context.UnsavedStateDetectedReliably,
            context.DiagnosticCode,
            context.DiagnosticMessage);
    }

    public void TiaDiagnostic(string correlationId, string diagnosticCode, string message)
    {
        _logger.Warning("TiaDiagnostic CorrelationId={CorrelationId} DiagnosticCode={DiagnosticCode} Message={Message}",
            correlationId, diagnosticCode, message);
    }

    public void OnlineStateCheckAttempted(string correlationId, string sessionId)
    {
        _logger.Information("OnlineStateCheckAttempted CorrelationId={CorrelationId} SessionId={SessionId}", correlationId, sessionId);
    }

    public void OnlineStateCheckCompleted(string correlationId, OnlineStateResult result)
    {
        _logger.Information(
            "OnlineStateCheckCompleted CorrelationId={CorrelationId} Checked={Checked} HasOnlineDevices={HasOnlineDevices} OnlineDeviceCount={OnlineDeviceCount} DiagnosticCode={DiagnosticCode} DiagnosticMessage={DiagnosticMessage}",
            correlationId,
            result.Checked,
            result.HasOnlineDevices,
            result.OnlineDeviceCount,
            result.DiagnosticCode,
            result.DiagnosticMessage);
    }

    public void PlcComparisonAttempted(string correlationId, string sessionId)
    {
        _logger.Information("PlcComparisonAttempted CorrelationId={CorrelationId} SessionId={SessionId}", correlationId, sessionId);
    }

    public void PlcComparisonCompleted(string correlationId, PlcOnlineOfflineComparisonResult result)
    {
        _logger.Information(
            "PlcComparisonCompleted CorrelationId={CorrelationId} Verified={Verified} IsEqual={IsEqual} DiagnosticCode={DiagnosticCode} DiagnosticMessage={DiagnosticMessage}",
            correlationId,
            result.Verified,
            result.IsEqual,
            result.DiagnosticCode,
            result.DiagnosticMessage);
    }

    public void GoOfflineAttempted(string correlationId, string sessionId)
    {
        _logger.Information("GoOfflineAttempted CorrelationId={CorrelationId} SessionId={SessionId}", correlationId, sessionId);
    }

    public void GoOfflineCompleted(string correlationId, GoOfflineResult result)
    {
        _logger.Information(
            "GoOfflineCompleted CorrelationId={CorrelationId} Success={Success} DevicesProcessed={DevicesProcessed} DevicesSetOffline={DevicesSetOffline} DiagnosticCode={DiagnosticCode} DiagnosticMessage={DiagnosticMessage}",
            correlationId,
            result.Success,
            result.DevicesProcessed,
            result.DevicesSetOffline,
            result.DiagnosticCode,
            result.DiagnosticMessage);
    }

    public void SaveAttempted(string correlationId, bool shouldSave, string reason)
    {
        _logger.Information("SaveAttempted CorrelationId={CorrelationId} ShouldSave={ShouldSave} Reason={Reason}",
            correlationId, shouldSave, reason);
    }

    public void SaveCompleted(string correlationId, bool success)
    {
        _logger.Information("SaveCompleted CorrelationId={CorrelationId} Success={Success}", correlationId, success);
    }

    public void ArchiveAttempted(string correlationId, string archivePath, int attempt)
    {
        _logger.Information("ArchiveAttempted CorrelationId={CorrelationId} ArchivePath={ArchivePath} Attempt={Attempt}",
            correlationId, archivePath, attempt);
    }

    public void ArchiveCompleted(string correlationId, bool success, string? archivePath, TimeSpan duration)
    {
        _logger.Information("ArchiveCompleted CorrelationId={CorrelationId} Success={Success} ArchivePath={ArchivePath} DurationMs={DurationMs}",
            correlationId, success, archivePath, duration.TotalMilliseconds);
    }

    public void Failed(string correlationId, string stage, string message, Exception? exception = null)
    {
        _logger.Error(exception, "ArchiveFailed CorrelationId={CorrelationId} Stage={Stage} Message={Message}",
            correlationId, stage, message);
    }
}
