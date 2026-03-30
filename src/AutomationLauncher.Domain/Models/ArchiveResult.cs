namespace AutomationLauncher.Domain.Models;

public sealed class ArchiveResult
{
    public ArchiveResult(
        ArchiveOutcome outcome,
        string message,
        string? archivePath = null,
        TimeSpan? duration = null,
        TiaProjectContext? runtimeContext = null)
    {
        Outcome = outcome;
        Message = message;
        ArchivePath = archivePath;
        Duration = duration;
        RuntimeContext = runtimeContext;
    }

    public ArchiveOutcome Outcome { get; }

    public string Message { get; }

    public string? ArchivePath { get; }

    public TimeSpan? Duration { get; }

    public TiaProjectContext? RuntimeContext { get; }
}
