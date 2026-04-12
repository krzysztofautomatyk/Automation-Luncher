namespace AutomationLauncher.App;

public sealed class StartupSequenceRunResult
{
    public bool WasCancelled { get; set; }

    public int StartedCount { get; set; }

    public int FailedCount { get; set; }

    public string Message { get; set; } = string.Empty;
}