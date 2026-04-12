using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
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
            var context = await _tiaPortalGateway.GetCurrentContextAsync(System.Threading.CancellationToken.None);
            UpdateExpectedProjectPathEditModeFromContext(context);
            if (!context.IsTiaRunning)
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is not running.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("INFO", context.DiagnosticCode, StatusMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath))
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is running, but no open project was detected through Openness.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("WARN", context.DiagnosticCode, StatusMessage);
                return;
            }

            StatusMessage = $"Project path synchronized from TIA: {ExpectedProjectPath} | runtime {BuildRuntimeLabel(context)}";
            ApplyRuntimeDiagnostics(context);
            AddHistory("INFO", "SyncOk", $"Synchronized project path from TIA: {ExpectedProjectPath} | runtime {BuildRuntimeLabel(context)}");
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, "TIA Portal project path synchronization failed unexpectedly");
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
            var context = await _tiaPortalGateway.GetCurrentContextAsync(System.Threading.CancellationToken.None);
            UpdateExpectedProjectPathEditModeFromContext(context);

            if (!context.IsTiaRunning)
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is NOT running. Start TIA Portal to enable communication.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("ERROR", context.DiagnosticCode, StatusMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath))
            {
                StatusMessage = context.DiagnosticMessage ?? $"TIA Portal is running (PID: {context.SessionId}) but Openness could not connect or no project is open.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("WARN", context.DiagnosticCode, $"{StatusMessage} | PID: {context.SessionId}");
                return;
            }

            var unsavedInfo = context.UnsavedStateDetectedReliably
                ? (context.HasUnsavedChanges == true ? "unsaved changes detected" : "no unsaved changes")
                : "unsaved state unknown";

            StatusMessage = $"Connected to TIA Portal. Project: {context.ProjectName ?? context.OpenProjectPath} | {unsavedInfo} | runtime {BuildRuntimeLabel(context)}";
            ApplyRuntimeDiagnostics(context);
            AddHistory("OK", "Connected", $"Project: {context.ProjectName ?? context.OpenProjectPath} | PID: {context.SessionId} | {unsavedInfo} | runtime {BuildRuntimeLabel(context)}");
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, "TIA Portal connection check failed unexpectedly");
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
}
