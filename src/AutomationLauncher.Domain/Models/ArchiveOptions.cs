namespace AutomationLauncher.Domain.Models;

public sealed class ArchiveOptions
{
    public string ExpectedProjectPath { get; set; } = string.Empty;
    public string ArchiveOutputDirectory { get; set; } = string.Empty;
    public ArchiveBackupFlow BackupFlow { get; set; } = ArchiveBackupFlow.TimestampedRetention;
    public int SuccessfulBackupRetentionCount { get; set; }
    public bool TryDetectUnsavedChanges { get; set; } = true;
    public bool ForceSaveWhenDetectionUnavailable { get; set; } = true;
    public int PlcComparisonTimeoutSeconds { get; set; } = 30;
    public int OnlineStateCheckTimeoutSeconds { get; set; } = 30;
    public int GoOfflineTimeoutSeconds { get; set; } = 60;
    public int SaveWaitSeconds { get; set; } = 60;
    public int SaveTimeoutSeconds { get; set; } = 90;
    public int ArchiveTimeoutSeconds { get; set; } = 300;
    public int RetryCount { get; set; } = 1;
    public int RetryDelayMilliseconds { get; set; } = 2000;
    public TiaPortalVersionSelectionMode TiaVersionSelectionMode { get; set; } = TiaPortalVersionSelectionMode.Auto;
    public string? PreferredTiaVersion { get; set; }
    public string? OpennessAssemblyPath { get; set; }
    public IList<TiaPortalRuntimeConfiguration> KnownVersions { get; set; } = new List<TiaPortalRuntimeConfiguration>();

    /// <summary>Pre-save was attempted from the countdown splash before the archive workflow started.</summary>
    public bool PreSaveAttempted { get; set; }
    /// <summary>Result of the pre-save. Null when not attempted.</summary>
    public bool? PreSaveSucceeded { get; set; }
    /// <summary>What triggered the pre-save: "UserSaveNow", "AutoSaveCountdown", or null.</summary>
    public string? PreSaveTriggerSource { get; set; }
}

public enum ArchiveBackupFlow
{
    TimestampedRetention = 0,
    StableFileWithOld = 1
}
