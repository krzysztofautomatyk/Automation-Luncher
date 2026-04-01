namespace AutomationLauncher.Domain.Models;

public sealed class ArchiveOptions
{
    public string ExpectedProjectPath { get; set; } = string.Empty;
    public string ArchiveOutputDirectory { get; set; } = string.Empty;
    public ArchiveBackupFlow BackupFlow { get; set; } = ArchiveBackupFlow.TimestampedRetention;
    public int SuccessfulBackupRetentionCount { get; set; }
    public bool TryDetectUnsavedChanges { get; set; } = true;
    public bool ForceSaveWhenDetectionUnavailable { get; set; } = true;
    public int SaveTimeoutSeconds { get; set; } = 90;
    public int ArchiveTimeoutSeconds { get; set; } = 300;
    public int RetryCount { get; set; } = 1;
    public int RetryDelayMilliseconds { get; set; } = 2000;
    public TiaPortalVersionSelectionMode TiaVersionSelectionMode { get; set; } = TiaPortalVersionSelectionMode.Auto;
    public string? PreferredTiaVersion { get; set; }
    public string? OpennessAssemblyPath { get; set; }
    public IList<TiaPortalRuntimeConfiguration> KnownVersions { get; set; } = new List<TiaPortalRuntimeConfiguration>();
}

public enum ArchiveBackupFlow
{
    TimestampedRetention = 0,
    StableFileWithOld = 1
}
