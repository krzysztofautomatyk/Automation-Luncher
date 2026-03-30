namespace AutomationLauncher.Domain.Models;

public sealed class ArchiveResult
{
    public ArchiveResult(
        ArchiveOutcome outcome,
        string message,
        string? archivePath = null,
        TimeSpan? duration = null)
    {
        Outcome = outcome;
        Message = message;
        ArchivePath = archivePath;
        Duration = duration;
    }

    public ArchiveOutcome Outcome { get; }

    public string Message { get; }

    public string? ArchivePath { get; }

    public TimeSpan? Duration { get; }
}
