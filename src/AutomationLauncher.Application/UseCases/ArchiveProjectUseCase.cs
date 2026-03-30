using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;

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

            var shouldSave = DetermineShouldSave(context, options, out var saveReason);
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

            var archivePath = _pathService.BuildArchiveFilePath(
                openProjectPath,
                options.ArchiveOutputDirectory,
                DateTimeOffset.Now);

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
                    var duration = DateTimeOffset.UtcNow - startedAt;
                    _logger.ArchiveCompleted(correlationId, true, archivePath, duration);
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
}
