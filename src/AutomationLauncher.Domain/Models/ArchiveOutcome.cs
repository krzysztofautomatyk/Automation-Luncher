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
    TiaConnectionFailed = 8,
    PlcComparisonUnavailable = 9,
    PlcComparisonMismatch = 10,
    GoOfflineFailed = 11,
    PlcNotOnline = 12
}
