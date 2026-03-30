namespace AutomationLauncher.Domain.Models;

public sealed class ArchiveOptions
{
    public string ExpectedProjectPath { get; set; } = string.Empty;
    public string ArchiveOutputDirectory { get; set; } = string.Empty;
    public bool TryDetectUnsavedChanges { get; set; } = true;
    public bool ForceSaveWhenDetectionUnavailable { get; set; } = true;
    public int SaveTimeoutSeconds { get; set; } = 90;
    public int ArchiveTimeoutSeconds { get; set; } = 300;
    public int RetryCount { get; set; } = 1;
    public int RetryDelayMilliseconds { get; set; } = 2000;
    public string? OpennessAssemblyPath { get; set; }
}
