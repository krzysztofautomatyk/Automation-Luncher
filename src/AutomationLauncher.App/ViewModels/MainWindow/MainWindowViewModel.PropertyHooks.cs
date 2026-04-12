using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Forms = System.Windows.Forms;
using FileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomationLauncher.App;
public partial class MainWindowViewModel : ObservableObject
{
    partial void OnIsBusyChanged(bool value)
    {
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        RepairOpennessGroupMembershipCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUseProtectedActions));
        OnPropertyChanged(nameof(CanRunStartupAutomationManually));
    }

    partial void OnIsSessionAuthenticatedChanged(bool value)
    {
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUseProtectedActions));
        OnPropertyChanged(nameof(CanUseProtectedUtilities));
        OnPropertyChanged(nameof(CanLoginSession));
        OnPropertyChanged(nameof(CanRunStartupAutomationManually));
        OnPropertyChanged(nameof(CanStopManagedApplications));
    }

    partial void OnIsStartupAutomationRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunStartupAutomationManually));
        OnPropertyChanged(nameof(CanStopManagedApplications));
    }

    partial void OnCurrentHostControlStateChanged(HostControlState value)
    {
        OnPropertyChanged(nameof(CanRunStartupAutomationManually));
        OnPropertyChanged(nameof(CanStopManagedApplications));
        OnPropertyChanged(nameof(HostControlStateDisplay));
    }

    partial void OnHostNameChanged(string value)
    {
        OnPropertyChanged(nameof(HostRunFilePath));
        OnPropertyChanged(nameof(HostReadyFilePath));
        OnPropertyChanged(nameof(HostErrorFilePath));
        OnPropertyChanged(nameof(HostStartFilePath));
        OnPropertyChanged(nameof(HostStopFilePath));
        OnPropertyChanged(nameof(HostMarchFilePath));
        OnPropertyChanged(nameof(HostArchOkFilePath));
    }

    partial void OnControlFilesFolderPathChanged(string value)
    {
        OnPropertyChanged(nameof(HostRunFilePath));
        OnPropertyChanged(nameof(HostReadyFilePath));
        OnPropertyChanged(nameof(HostErrorFilePath));
        OnPropertyChanged(nameof(HostStartFilePath));
        OnPropertyChanged(nameof(HostStopFilePath));
        OnPropertyChanged(nameof(HostMarchFilePath));
        OnPropertyChanged(nameof(HostArchOkFilePath));

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Control files folder updated.");
    }

    partial void OnExpectedProjectPathChanged(string value)
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Project path updated.");
    }

    partial void OnArchiveOutputDirectoryChanged(string value)
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Archive directory updated.");
    }

    partial void OnSelectedArchiveBackupFlowChanged(string value)
    {
        OnPropertyChanged(nameof(IsTimestampedBackupFlowSelected));
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Archive backup flow updated.");
    }

    partial void OnSuccessfulBackupRetentionCountChanged(int value)
    {
        if (value < 0)
        {
            SuccessfulBackupRetentionCount = 0;
            return;
        }

        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Archive retention updated.");
    }

    partial void OnTryDetectUnsavedChangesChanged(bool value)
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Unsaved changes detection policy updated.");
    }

    partial void OnForceSaveWhenDetectionUnavailableChanged(bool value)
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Fallback save policy updated.");
    }

    partial void OnTiaRuntimeSelectionModeChanged(string value)
    {
        _settings.Archive.TiaVersionSelectionMode = string.Equals(value, TiaPortalVersionSelectionMode.Manual.ToString(), StringComparison.OrdinalIgnoreCase)
            ? TiaPortalVersionSelectionMode.Manual
            : TiaPortalVersionSelectionMode.Auto;

        UpdateRuntimeCatalogStatus();
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("TIA runtime selection mode updated.");
    }

    partial void OnSelectedTiaRuntimeChanged(TiaPortalRuntimeInfo? value)
    {
        _settings.Archive.PreferredTiaVersion = value?.Version;
        UpdateRuntimeCatalogStatus();
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Preferred TIA runtime updated.");
    }

    partial void OnIsOpennessGroupAvailableChanged(bool value)
    {
        RepairOpennessGroupMembershipCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCurrentUserInOpennessGroupChanged(bool? value)
    {
        RepairOpennessGroupMembershipCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCheckingOpennessAccessChanged(bool value)
    {
        CheckOpennessGroupAccessCommand.NotifyCanExecuteChanged();
        RepairOpennessGroupMembershipCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCheckOpennessGroupAccess));
    }

    partial void OnLaunchOnWindowsStartupChanged(bool value)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        try
        {
            _autostartService.SetEnabled(value);
            PersistSettings(value
                ? "Application startup entry enabled."
                : "Application startup entry disabled.");
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Autostart update failed: {ex.Message}";
            AddHistory("ERROR", "AutostartUpdateFailed", ex.Message);
            _isInitializing = true;
            LaunchOnWindowsStartup = _autostartService.IsEnabled();
            _isInitializing = false;
        }
    }

    partial void OnRunStartupSequenceOnWindowsStartupChanged(bool value)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings(value
            ? "Windows startup sequence enabled."
            : "Windows startup sequence disabled.");
    }

    partial void OnStartupSplashBackgroundImagePathChanged(string value)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings(string.IsNullOrWhiteSpace(value)
            ? "Startup splash background cleared."
            : "Startup splash background updated.");
    }

    partial void OnLogDirectoryChanged(string value)
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Log directory updated.", loggingChangeRequiresRestart: true);
        _ = RefreshFileLogsAsync(forceRefresh: true);
    }

    partial void OnLogMinimumLevelChanged(string value)
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Log level updated.", loggingChangeRequiresRestart: true);
    }

    partial void OnLogRetentionFileCountChanged(int value)
    {
        if (value < 1)
        {
            LogRetentionFileCount = 1;
            return;
        }

        if (!_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Log retention updated.", loggingChangeRequiresRestart: true);
    }

    partial void OnLogSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasLogSearchText));
        _ = ApplyLogFilterAsync();
    }

    partial void OnShowErrorsAndWarningsOnlyChanged(bool value)
    {
        _ = ApplyLogFilterAsync();
    }
}

