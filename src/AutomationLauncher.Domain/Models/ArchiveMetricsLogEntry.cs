namespace AutomationLauncher.Domain.Models;

public sealed class ArchiveMetricsLogEntry
{
    public ArchiveMetricsLogEntry(
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
        ArchivePath = archivePath;
        CorrelationId = correlationId;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        ProjectPath = projectPath;
        ProjectSizeBytes = projectSizeBytes;
        ArchiveSizeBytes = archiveSizeBytes;
        Duration = duration;
        PreSaveAttempted = preSaveAttempted;
        PreSaveSucceeded = preSaveSucceeded;
        PreSaveTriggerSource = preSaveTriggerSource;
    }

    public string ArchivePath { get; }

    public string CorrelationId { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset FinishedAt { get; }

    public string ProjectPath { get; }

    public long? ProjectSizeBytes { get; }

    public long? ArchiveSizeBytes { get; }

    public TimeSpan Duration { get; }

    public bool PreSaveAttempted { get; }

    public bool? PreSaveSucceeded { get; }

    public string? PreSaveTriggerSource { get; }
}
