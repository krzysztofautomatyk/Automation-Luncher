namespace AutomationLauncher.Domain.Models;

public enum ArchiveOutcome
{
    Success = 0,
    TiaNotRunning = 1,
    NoProjectOpen = 2,
    WrongProjectOpen = 3,
    SaveFailed = 4,
    ArchiveFailed = 5,
    ConfigurationError = 6,
    UnexpectedError = 7,
    TiaConnectionFailed = 8
}
