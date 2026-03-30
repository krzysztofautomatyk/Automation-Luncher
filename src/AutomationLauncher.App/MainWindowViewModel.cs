using System.Collections.ObjectModel;
using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveProjectUseCase _archiveProjectUseCase;
    private readonly ITiaPortalGateway _tiaPortalGateway;
    private readonly AutomationLauncherSettings _settings;

    [ObservableProperty]
    private string expectedProjectPath = string.Empty;

    [ObservableProperty]
    private string archiveOutputDirectory = string.Empty;

    [ObservableProperty]
    private bool tryDetectUnsavedChanges;

    [ObservableProperty]
    private bool forceSaveWhenDetectionUnavailable;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<string> History { get; } = new();

    public MainWindowViewModel(
        ArchiveProjectUseCase archiveProjectUseCase,
        ITiaPortalGateway tiaPortalGateway,
        AutomationLauncherSettings settings)
    {
        _archiveProjectUseCase = archiveProjectUseCase;
        _tiaPortalGateway = tiaPortalGateway;
        _settings = settings;

        ExpectedProjectPath = _settings.Archive.ExpectedProjectPath;
        ArchiveOutputDirectory = _settings.Archive.ArchiveOutputDirectory;
        TryDetectUnsavedChanges = _settings.Archive.TryDetectUnsavedChanges;
        ForceSaveWhenDetectionUnavailable = _settings.Archive.ForceSaveWhenDetectionUnavailable;
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task SyncProjectFromTiaAsync()
    {
        IsBusy = true;
        StatusMessage = "Reading project from TIA Portal...";
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();

        try
        {
            var context = await _tiaPortalGateway.GetCurrentContextAsync(CancellationToken.None);
            if (!context.IsTiaRunning)
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is not running.";
                AddHistory("INFO", context.DiagnosticCode, StatusMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath))
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is running, but no open project was detected through Openness.";
                AddHistory("WARN", context.DiagnosticCode, StatusMessage);
                return;
            }

            ExpectedProjectPath = context.OpenProjectPath!;
            StatusMessage = $"Project path synchronized from TIA: {ExpectedProjectPath}";
            AddHistory("INFO", "SyncOk", $"Synchronized project path from TIA: {ExpectedProjectPath}");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "SyncProjectFromTia failed");
            StatusMessage = ex.Message;
            AddHistory("ERROR", "SyncFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task CheckTiaConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking TIA Portal connection...";
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();

        try
        {
            var context = await _tiaPortalGateway.GetCurrentContextAsync(CancellationToken.None);

            if (!context.IsTiaRunning)
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is NOT running. Start TIA Portal to enable communication.";
                AddHistory("ERROR", context.DiagnosticCode, StatusMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath))
            {
                StatusMessage = context.DiagnosticMessage ?? $"TIA Portal is running (PID: {context.SessionId}) but Openness could not connect or no project is open.";
                AddHistory("WARN", context.DiagnosticCode, $"{StatusMessage} | PID: {context.SessionId}");
                return;
            }

            var unsavedInfo = context.UnsavedStateDetectedReliably
                ? (context.HasUnsavedChanges == true ? "unsaved changes detected" : "no unsaved changes")
                : "unsaved state unknown";

            StatusMessage = $"Connected to TIA Portal. Project: {context.ProjectName ?? context.OpenProjectPath} | {unsavedInfo}";
            AddHistory("OK", "Connected", $"Project: {context.ProjectName ?? context.OpenProjectPath} | PID: {context.SessionId} | {unsavedInfo}");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "CheckTiaConnection failed");
            StatusMessage = $"Connection check failed: {ex.Message}";
            AddHistory("ERROR", "ConnectionCheckFailed", $"Connection check failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task ArchiveAsync()
    {
        IsBusy = true;
        StatusMessage = "Running archive workflow...";
        ArchiveCommand.NotifyCanExecuteChanged();

        try
        {
            var options = new ArchiveOptions
            {
                ExpectedProjectPath = ExpectedProjectPath,
                ArchiveOutputDirectory = ArchiveOutputDirectory,
                TryDetectUnsavedChanges = TryDetectUnsavedChanges,
                ForceSaveWhenDetectionUnavailable = ForceSaveWhenDetectionUnavailable,
                SaveTimeoutSeconds = _settings.Archive.SaveTimeoutSeconds,
                ArchiveTimeoutSeconds = _settings.Archive.ArchiveTimeoutSeconds,
                RetryCount = _settings.Archive.RetryCount,
                RetryDelayMilliseconds = _settings.Archive.RetryDelayMilliseconds,
                OpennessAssemblyPath = _settings.Archive.OpennessAssemblyPath
            };

            var result = await _archiveProjectUseCase.ExecuteAsync(options, CancellationToken.None);
            var summary = $"{result.Outcome} | {result.Message}";

            if (!string.IsNullOrWhiteSpace(result.ArchivePath))
            {
                summary += $" | {result.ArchivePath}";
            }

            AddHistory(result.Outcome == ArchiveOutcome.Success ? "OK" : "WARN", result.Outcome.ToString(), summary);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Archive command failed");
            StatusMessage = ex.Message;
            AddHistory("ERROR", "ArchiveFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanArchive()
    {
        return !IsBusy;
    }

    private void AddHistory(string level, string? code, string message)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? "General" : code;
        History.Insert(0, $"{DateTime.Now:HH:mm:ss} | {level} | {normalizedCode} | {message}");
    }
}
