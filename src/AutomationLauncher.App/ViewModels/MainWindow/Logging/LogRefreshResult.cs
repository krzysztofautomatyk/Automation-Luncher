namespace AutomationLauncher.App;

internal sealed class LogRefreshResult
{
    public LogRefreshResult(string loadedLogFilePath, string snapshotKey, IReadOnlyList<string> logLines, string? errorMessage)
    {
        LoadedLogFilePath = loadedLogFilePath;
        SnapshotKey = snapshotKey;
        LogLines = logLines;
        ErrorMessage = errorMessage;
    }

    public string LoadedLogFilePath { get; }

    public string SnapshotKey { get; }

    public IReadOnlyList<string> LogLines { get; }

    public string? ErrorMessage { get; }

    public static LogRefreshResult Empty(string loadedLogFilePath)
    {
        return new LogRefreshResult(loadedLogFilePath, string.Empty, Array.Empty<string>(), null);
    }
}
