using System.IO;
using System.Text.Json;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand]
    private void ApplySettings()
    {
        if (!EnsureAuthenticated())
            return;

        PersistSettings("Settings applied manually.", loggingChangeRequiresRestart: true);
    }

    [RelayCommand]
    private void ExportSettings()
    {
        if (!EnsureAuthenticated())
            return;

        SyncSettingsModel();

        var dialog = new SaveFileDialog
        {
            Title = "Export settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"automation-launcher-settings-{System.DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_settings, BuildSettingsSerializerOptions());
            File.WriteAllText(dialog.FileName, json);
            SettingsStatusMessage = $"Settings exported to {dialog.FileName}";
            AddHistory("OK", "SettingsExported", SettingsStatusMessage);
        }
        catch (System.Exception ex)
        {
            SettingsStatusMessage = $"Settings export failed: {ex.Message}";
            AddHistory("ERROR", "SettingsExportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ImportSettings()
    {
        if (!EnsureAuthenticated())
            return;

        if (string.IsNullOrWhiteSpace(_sessionState.SettingsPassword))
            return;

        var password = _sessionState.SettingsPassword!;

        var dialog = new FileDialog
        {
            Title = "Import settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var importedSettings = JsonSerializer.Deserialize<AutomationLauncherSettings>(json, BuildSettingsSerializerOptions());
            if (importedSettings is null)
                throw new System.InvalidOperationException("Imported settings file is empty or invalid.");

            AutomationLauncherSettingsApplicator.ApplyLoadedSettings(_settings, importedSettings);
            _protectedSettingsStore.Save(_settings, password);
            ReloadFromSettings();
            _ = RefreshFileLogsAsync(forceRefresh: true);
            SettingsStatusMessage = $"Settings imported from {dialog.FileName}";
            AddHistory("OK", "SettingsImported", SettingsStatusMessage);
        }
        catch (System.Exception ex)
        {
            SettingsStatusMessage = $"Settings import failed: {ex.Message}";
            AddHistory("ERROR", "SettingsImportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ResetSessionTimer()
    {
        if (!EnsureAuthenticated())
            return;

        _sessionCoordinator.RegisterActivity();
        UpdateSessionCountdown();
        SettingsStatusMessage = "Session timer reset.";
        AddHistory("INFO", "SessionTimerReset", "Session timer reset.");
    }

    [RelayCommand]
    private void LogoutSession()
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            SettingsStatusMessage = "Session is already locked.";
            UpdateSessionCountdown();
            return;
        }

        _sessionCoordinator.Logout("Session locked by user.", false);
    }

    private static JsonSerializerOptions BuildSettingsSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    private ArchiveBackupFlow ParseArchiveBackupFlow()
    {
        return System.Enum.TryParse<ArchiveBackupFlow>(SelectedArchiveBackupFlow, ignoreCase: true, out var flow)
            ? flow
            : ArchiveBackupFlow.TimestampedRetention;
    }
}
