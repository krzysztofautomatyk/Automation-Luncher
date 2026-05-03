using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using System.Globalization;
using System.Text;
using System.Linq;

namespace AutomationLauncher.Application.UseCases;

public sealed class ArchiveProjectUseCase
{
    private readonly ITiaPortalGateway _tiaPortalGateway;
    private readonly IPathService _pathService;
    private readonly IOperationLogger _logger;

    public ArchiveProjectUseCase(
        ITiaPortalGateway tiaPortalGateway,
        IPathService pathService,
        IOperationLogger logger)
    {
        _tiaPortalGateway = tiaPortalGateway;
        _pathService = pathService;
        _logger = logger;
    }

    public async Task<ArchiveResult> ExecuteAsync(ArchiveOptions options, CancellationToken cancellationToken)
    {
        var startedAtLocal = DateTimeOffset.Now;
        var startedAt = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            if (string.IsNullOrWhiteSpace(options.ExpectedProjectPath) || string.IsNullOrWhiteSpace(options.ArchiveOutputDirectory))
            {
                return new ArchiveResult(ArchiveOutcome.ConfigurationError, "Missing ExpectedProjectPath or ArchiveOutputDirectory.");
            }

            _pathService.EnsureDirectoryExists(options.ArchiveOutputDirectory);
            var expectedProject = _pathService.NormalizePath(options.ExpectedProjectPath);
            _logger.ArchiveStarted(correlationId, expectedProject, options.ArchiveOutputDirectory);

            var context = await _tiaPortalGateway.GetCurrentContextAsync(cancellationToken);
            _logger.TiaContextRead(correlationId, context);
            if (!string.IsNullOrWhiteSpace(context.DiagnosticCode) && !string.IsNullOrWhiteSpace(context.DiagnosticMessage))
            {
                _logger.TiaDiagnostic(correlationId, context.DiagnosticCode!, context.DiagnosticMessage!);
            }

            if (!context.IsTiaRunning)
            {
                return new ArchiveResult(ArchiveOutcome.TiaNotRunning, context.DiagnosticMessage ?? "TIA Portal is not running.", runtimeContext: context);
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath) || string.IsNullOrWhiteSpace(context.SessionId))
            {
                var outcome = string.Equals(context.DiagnosticCode, "NoProjectOpen", StringComparison.Ordinal)
                    ? ArchiveOutcome.NoProjectOpen
                    : ArchiveOutcome.TiaConnectionFailed;

                return new ArchiveResult(outcome, context.DiagnosticMessage ?? "TIA Portal is running, but no project is open.", runtimeContext: context);
            }

            var openProjectPath = context.OpenProjectPath!;
            var sessionId = context.SessionId!;
            var actualProject = _pathService.NormalizePath(openProjectPath);
            if (!ProjectPathsMatch(expectedProject, actualProject))
            {
                return new ArchiveResult(
                    ArchiveOutcome.WrongProjectOpen,
                    $"Different project is open. Expected: {expectedProject}, Actual: {actualProject}",
                    runtimeContext: context);
            }

            // ── Step 1: Verify PLC is ONLINE ──────────────────────────────
            _logger.OnlineStateCheckAttempted(correlationId, sessionId);
            var onlineState = await _tiaPortalGateway.CheckOnlineStateAsync(
                sessionId,
                TimeSpan.FromSeconds(options.OnlineStateCheckTimeoutSeconds),
                cancellationToken);
            _logger.OnlineStateCheckCompleted(correlationId, onlineState);

            if (!onlineState.Checked)
            {
                var diagMsg = string.IsNullOrWhiteSpace(onlineState.DiagnosticMessage)
                    ? "Online state check could not be completed. Proceeding with archive."
                    : $"Online state check could not be completed. {onlineState.DiagnosticMessage} Proceeding with archive.";

                _logger.TiaDiagnostic(correlationId, "OnlineStateCheckSkipped", diagMsg);
            }
            else if (!onlineState.HasOnlineDevices)
            {
                _logger.TiaDiagnostic(correlationId, "PlcNotOnlineSkipped",
                    $"No online PLC devices detected (count={onlineState.OnlineDeviceCount}). " +
                    "Online state may not be reliably detectable for this TIA version. Proceeding with archive.");
            }

            // ── Step 2: Compare online vs offline 1:1 ─────────────────────
            _logger.PlcComparisonAttempted(correlationId, sessionId);
            var plcComparison = await _tiaPortalGateway.CompareOnlineOfflineAsync(
                sessionId,
                TimeSpan.FromSeconds(options.PlcComparisonTimeoutSeconds),
                cancellationToken);
            _logger.PlcComparisonCompleted(correlationId, plcComparison);

            if (!plcComparison.Verified)
            {
                var message = string.IsNullOrWhiteSpace(plcComparison.DiagnosticMessage)
                    ? "PLC online/offline comparison could not be verified. Proceeding with archive."
                    : $"PLC online/offline comparison could not be verified. {plcComparison.DiagnosticMessage} Proceeding with archive.";

                _logger.TiaDiagnostic(correlationId, "PlcComparisonSkipped", message);
            }
            else if (!plcComparison.IsEqual)
            {
                return new ArchiveResult(
                    ArchiveOutcome.PlcComparisonMismatch,
                    "PLC online/offline comparison detected differences. Online and offline versions are not 1:1. Archive was blocked.",
                    runtimeContext: context);
            }

            // ── Step 3: Go offline ────────────────────────────────────────
            _logger.GoOfflineAttempted(correlationId, sessionId);
            var goOffline = await _tiaPortalGateway.GoOfflineAsync(
                sessionId,
                TimeSpan.FromSeconds(options.GoOfflineTimeoutSeconds),
                cancellationToken);
            _logger.GoOfflineCompleted(correlationId, goOffline);

            if (!goOffline.Success)
            {
                var offlineMessage = string.IsNullOrWhiteSpace(goOffline.DiagnosticMessage)
                    ? "Failed to switch PLC devices to offline mode."
                    : $"Failed to switch PLC devices to offline mode. {goOffline.DiagnosticMessage}";

                if (goOffline.DevicesSetOffline == 0 && goOffline.DevicesProcessed > 0)
                {
                    return new ArchiveResult(ArchiveOutcome.GoOfflineFailed, offlineMessage, runtimeContext: context);
                }

                _logger.TiaDiagnostic(correlationId, "GoOfflinePartial", offlineMessage);
            }

            // ── Step 4: Save project ──────────────────────────────────────
            // Re-read context after going offline to get fresh unsaved state
            var refreshedContext = await _tiaPortalGateway.GetCurrentContextAsync(cancellationToken);

            var projectSizeBytes = TryGetPathSizeBytes(actualProject);

            var shouldSave = DetermineShouldSave(refreshedContext, options, out var saveReason);
            _logger.SaveAttempted(correlationId, shouldSave, saveReason);
            if (shouldSave)
            {
                var saveOk = await _tiaPortalGateway.SaveProjectAsync(
                    sessionId,
                    TimeSpan.FromSeconds(options.SaveTimeoutSeconds),
                    cancellationToken);

                _logger.SaveCompleted(correlationId, saveOk);
                if (!saveOk)
                {
                    return new ArchiveResult(ArchiveOutcome.SaveFailed, "Unable to save project before archive.", runtimeContext: context);
                }
            }

            var archiveIdentity = BuildArchiveIdentity();
            var archivePath = BuildArchiveDestinationPath(options, openProjectPath, archiveIdentity, DateTimeOffset.Now);
            var oldArchivePath = options.BackupFlow == ArchiveBackupFlow.StableFileWithOld
                ? _pathService.BuildArchiveFilePath(openProjectPath, options.ArchiveOutputDirectory, archiveIdentity + "_old")
                : null;

            if (options.BackupFlow == ArchiveBackupFlow.StableFileWithOld && oldArchivePath is not null)
            {
                PrepareStableBackupTarget(archivePath, oldArchivePath);
            }

            var attempt = 0;
            while (true)
            {
                attempt++;
                _logger.ArchiveAttempted(correlationId, archivePath, attempt);

                var archiveOk = await _tiaPortalGateway.ArchiveProjectAsync(
                    sessionId,
                    archivePath,
                    TimeSpan.FromSeconds(options.ArchiveTimeoutSeconds),
                    cancellationToken);

                if (archiveOk)
                {
                    var finishedAtLocal = DateTimeOffset.Now;
                    var duration = DateTimeOffset.UtcNow - startedAt;
                    FinalizeSuccessfulBackup(options, archivePath, oldArchivePath, archiveIdentity);
                    _logger.ArchiveCompleted(correlationId, true, archivePath, duration);
                    TryWriteArchiveMetricsLog(
                        archivePath,
                        correlationId,
                        startedAtLocal,
                        finishedAtLocal,
                        actualProject,
                        projectSizeBytes,
                        TryGetPathSizeBytes(archivePath),
                        duration,
                        options.PreSaveAttempted,
                        options.PreSaveSucceeded,
                        options.PreSaveTriggerSource);
                    return new ArchiveResult(ArchiveOutcome.Success, "Archive completed successfully.", archivePath, duration, context);
                }

                if (attempt > options.RetryCount)
                {
                    var duration = DateTimeOffset.UtcNow - startedAt;
                    _logger.ArchiveCompleted(correlationId, false, archivePath, duration);
                    return new ArchiveResult(ArchiveOutcome.ArchiveFailed, "Archive failed after retries.", null, duration, context);
                }

                await Task.Delay(options.RetryDelayMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Failed(correlationId, "Execute", "Unexpected archive error", ex);
            return new ArchiveResult(ArchiveOutcome.UnexpectedError, ex.Message);
        }
    }

    private static bool DetermineShouldSave(TiaProjectContext context, ArchiveOptions options, out string reason)
    {
        if (!options.TryDetectUnsavedChanges)
        {
            reason = "Unsaved change detection disabled in configuration.";
            return options.ForceSaveWhenDetectionUnavailable;
        }

        if (context.UnsavedStateDetectedReliably && context.HasUnsavedChanges.HasValue)
        {
            reason = context.HasUnsavedChanges.Value
                ? "Reliable dirty-state detection indicates unsaved changes."
                : "Reliable dirty-state detection indicates no unsaved changes.";
            return context.HasUnsavedChanges.Value;
        }

        reason = "Dirty-state detection unavailable or unreliable.";
        return options.ForceSaveWhenDetectionUnavailable;
    }

    private static bool ProjectPathsMatch(string expectedProject, string actualProject)
    {
        if (string.Equals(expectedProject, actualProject, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var expectedNoExt = Path.ChangeExtension(expectedProject, null)?.TrimEnd('.');
        var actualNoExt = Path.ChangeExtension(actualProject, null)?.TrimEnd('.');

        return !string.IsNullOrWhiteSpace(expectedNoExt)
            && !string.IsNullOrWhiteSpace(actualNoExt)
            && string.Equals(expectedNoExt, actualNoExt, StringComparison.OrdinalIgnoreCase);
    }

    private static long? TryGetPathSizeBytes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(filePath =>
                {
                    try
                    {
                        return new FileInfo(filePath).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteArchiveMetricsLog(
        string archivePath,
        string correlationId,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string projectPath,
        long? projectSizeBytes,
        long? archiveSizeBytes,
        TimeSpan duration,
        bool preSaveAttempted = false,
        bool? preSaveSucceeded = null,
        string? preSaveTriggerSource = null)
    {
        try
        {
            var outputDirectory = Path.GetDirectoryName(archivePath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            var archiveBaseName = Path.GetFileNameWithoutExtension(archivePath);
            var metricsLogPath = Path.Combine(outputDirectory, $"{archiveBaseName}.archive.log");
            var sb = new StringBuilder()
                .AppendLine("Archive Metrics")
                .AppendLine($"CorrelationId={correlationId}")
                .AppendLine($"StartedAt={startedAt:O}")
                .AppendLine($"ProjectPath={projectPath}")
                .AppendLine($"ProjectSizeBytes={FormatBytesValue(projectSizeBytes)}")
                .AppendLine($"ProjectSizeMB={FormatMegabytesValue(projectSizeBytes)}");

            if (preSaveAttempted)
            {
                sb.AppendLine($"PreSaveAttempted=true")
                  .AppendLine($"PreSaveTriggerSource={preSaveTriggerSource ?? "Unknown"}")
                  .AppendLine($"PreSaveSucceeded={preSaveSucceeded?.ToString() ?? "N/A"}");
            }
            else
            {
                sb.AppendLine("PreSaveAttempted=false");
            }

            sb.AppendLine($"FinishedAt={finishedAt:O}")
              .AppendLine($"ArchivePath={archivePath}")
              .AppendLine($"ArchiveSizeBytes={FormatBytesValue(archiveSizeBytes)}")
              .AppendLine($"ArchiveSizeMB={FormatMegabytesValue(archiveSizeBytes)}")
              .AppendLine($"DurationMs={duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}");

            File.WriteAllText(metricsLogPath, sb.ToString());
        }
        catch
        {
            // Metrics log generation must not fail the archive workflow.
        }
    }

    private static string FormatBytesValue(long? bytes)
    {
        return bytes.HasValue
            ? bytes.Value.ToString(CultureInfo.InvariantCulture)
            : "N/A";
    }

    private static string FormatMegabytesValue(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "N/A";
        }

        var megabytes = bytes.Value / (1024d * 1024d);
        return megabytes.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string BuildArchiveIdentity()
    {
        return Environment.MachineName + "_automaticBackup";
    }

    private string BuildArchiveDestinationPath(ArchiveOptions options, string projectPath, string archiveIdentity, DateTimeOffset timestamp)
    {
        return options.BackupFlow == ArchiveBackupFlow.StableFileWithOld
            ? _pathService.BuildArchiveFilePath(projectPath, options.ArchiveOutputDirectory, archiveIdentity)
            : _pathService.BuildArchiveFilePath(projectPath, options.ArchiveOutputDirectory, $"{archiveIdentity}_{timestamp:yyyyMMdd_HHmmss}");
    }

    private static void PrepareStableBackupTarget(string archivePath, string oldArchivePath)
    {
        if (File.Exists(oldArchivePath))
        {
            File.Delete(oldArchivePath);
        }

        if (File.Exists(archivePath))
        {
            File.Move(archivePath, oldArchivePath);
        }
    }

    private void FinalizeSuccessfulBackup(ArchiveOptions options, string archivePath, string? oldArchivePath, string archiveIdentity)
    {
        if (options.BackupFlow == ArchiveBackupFlow.StableFileWithOld)
        {
            if (!string.IsNullOrWhiteSpace(oldArchivePath) && File.Exists(oldArchivePath))
            {
                try
                {
                    File.Delete(oldArchivePath);
                }
                catch
                {
                    // Old backup cleanup should not fail a successful archive.
                }
            }

            return;
        }

        CleanupOldSuccessfulBackups(archivePath, archiveIdentity, options.SuccessfulBackupRetentionCount);
    }

    private static void CleanupOldSuccessfulBackups(string currentArchivePath, string archiveIdentity, int retentionCount)
    {
        if (retentionCount <= 0)
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(currentArchivePath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        var matchingFiles = Directory
            .EnumerateFiles(directoryPath, archiveIdentity + "_*.zap*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var obsoleteFile in matchingFiles.Skip(retentionCount))
        {
            try
            {
                File.Delete(obsoleteFile);
            }
            catch
            {
                // Retention cleanup should not fail a successful archive.
            }
        }
    }
}
