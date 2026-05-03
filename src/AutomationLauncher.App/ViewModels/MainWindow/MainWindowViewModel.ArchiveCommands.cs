using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task ArchiveAsync()
    {
        await RunArchiveWorkflowAsync();
    }

    /// <summary>Set by App.Archive.cs before calling RunArchiveWorkflowAsync so pre-save info lands in the metrics log.</summary>
    internal (bool Attempted, bool? Succeeded, string? TriggerSource) PendingPreSave { get; set; }

    internal async Task<bool> RunArchiveWorkflowAsync()
    {
        ArchiveWorkflowStateChanged?.Invoke(this, new ArchiveWorkflowStateChangedEventArgs(true));
        IsBusy = true;
        StatusMessage = "Running archive workflow...";
        ArchiveCommand.NotifyCanExecuteChanged();

        var pendingPreSave = PendingPreSave;
        PendingPreSave = default;

        try
        {
            var options = new ArchiveOptions
            {
                ExpectedProjectPath = ExpectedProjectPath,
                ArchiveOutputDirectory = ArchiveOutputDirectory,
                BackupFlow = ParseArchiveBackupFlow(),
                SuccessfulBackupRetentionCount = System.Math.Max(0, SuccessfulBackupRetentionCount),
                TryDetectUnsavedChanges = TryDetectUnsavedChanges,
                ForceSaveWhenDetectionUnavailable = ForceSaveWhenDetectionUnavailable,
                SaveTimeoutSeconds = _settings.Archive.SaveTimeoutSeconds,
                ArchiveTimeoutSeconds = _settings.Archive.ArchiveTimeoutSeconds,
                RetryCount = _settings.Archive.RetryCount,
                RetryDelayMilliseconds = _settings.Archive.RetryDelayMilliseconds,
                TiaVersionSelectionMode = _settings.Archive.TiaVersionSelectionMode,
                PreferredTiaVersion = _settings.Archive.PreferredTiaVersion,
                OpennessAssemblyPath = _settings.Archive.OpennessAssemblyPath,
                KnownVersions = _settings.Archive.KnownVersions,
                PreSaveAttempted = pendingPreSave.Attempted,
                PreSaveSucceeded = pendingPreSave.Succeeded,
                PreSaveTriggerSource = pendingPreSave.TriggerSource
            };

            var result = await _archiveProjectUseCase.ExecuteAsync(options, System.Threading.CancellationToken.None);
            var summary = $"{result.Outcome} | {result.Message}";

            if (result.RuntimeContext is not null)
            {
                ApplyRuntimeDiagnostics(result.RuntimeContext);
                UpdateExpectedProjectPathEditModeFromContext(result.RuntimeContext);
            }

            if (!string.IsNullOrWhiteSpace(result.ArchivePath))
                summary += $" | {result.ArchivePath}";

            AddHistory(result.Outcome == ArchiveOutcome.Success ? "OK" : "WARN", result.Outcome.ToString(), summary);
            StatusMessage = result.Message;
            return result.Outcome == ArchiveOutcome.Success;
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, "Archive workflow failed unexpectedly");
            StatusMessage = ex.Message;
            AddHistory("ERROR", "ArchiveFailed", ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
            ArchiveWorkflowStateChanged?.Invoke(this, new ArchiveWorkflowStateChangedEventArgs(false));
        }
    }

    private bool CanArchive()
    {
        return !IsBusy && IsSessionAuthenticated;
    }
}
