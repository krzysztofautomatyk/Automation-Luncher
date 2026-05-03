using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand]
    private void ClearLogSearch()
    {
        if (string.IsNullOrEmpty(LogSearchText))
        {
            return;
        }

        LogSearchText = string.Empty;
    }

    private void LoadRuntimeCatalog()
    {
        AvailableTiaRuntimes.Clear();

        var runtimes = _runtimeCatalog.GetAvailableRuntimes();
        foreach (var runtime in runtimes)
        {
            AvailableTiaRuntimes.Add(runtime);
        }

        if (!string.IsNullOrWhiteSpace(_settings.Archive.PreferredTiaVersion))
        {
            SelectedTiaRuntime = AvailableTiaRuntimes.FirstOrDefault(runtime =>
                string.Equals(runtime.Version, _settings.Archive.PreferredTiaVersion, StringComparison.OrdinalIgnoreCase));
        }

        SelectedTiaRuntime ??= AvailableTiaRuntimes.FirstOrDefault();
        UpdateRuntimeCatalogStatus();
    }

    private void UpdateRuntimeCatalogStatus()
    {
        if (AvailableTiaRuntimes.Count == 0)
        {
            RuntimeCatalogStatus = "No TIA Portal PublicAPI runtimes were detected. Add version overrides or install PublicAPI for the target version.";
            return;
        }

        var selectedRuntimeLabel = SelectedTiaRuntime is null
            ? "none"
            : $"{SelectedTiaRuntime.DisplayName} ({SelectedTiaRuntime.Source})";

        RuntimeCatalogStatus = $"Detected {AvailableTiaRuntimes.Count} runtime(s). Selection mode: {TiaRuntimeSelectionMode}. Selected runtime: {selectedRuntimeLabel}.";
    }

    private void PersistSettings(string successMessage, bool loggingChangeRequiresRestart = false)
    {
        if (_isInitializing)
        {
            return;
        }

        if (!_sessionState.HasUnlockedSettings || string.IsNullOrWhiteSpace(_sessionState.SettingsPassword))
        {
            SettingsStatusMessage = "Protected settings are not unlocked in the current session.";
            return;
        }

        try
        {
            _sessionCoordinator.RegisterActivity();
            SyncSettingsModel();
            _protectedSettingsStore.Save(_settings, _sessionState.SettingsPassword!);
            SettingsStatusMessage = loggingChangeRequiresRestart
                ? successMessage + " Restart the application to apply logging changes."
                : successMessage;
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Protected settings save failed: {ex.Message}";
            AddHistory("WARN", "ProtectedSettingsSaveFailed", ex.Message);
        }
    }

    private void ApplyRuntimeDiagnostics(TiaProjectContext context)
    {
        DetectedProcessVersion = context.DetectedProcessVersion ?? "n/a";
        SelectedRuntimeVersion = context.TiaVersion ?? SelectedTiaRuntime?.Version ?? "n/a";
        SelectedProviderName = context.ProviderName ?? "n/a";
        SelectedAssemblyPath = context.OpennessAssemblyPath ?? SelectedTiaRuntime?.OpennessAssemblyPath ?? "n/a";
        RuntimeSelectionReason = context.RuntimeSelectionReason ?? "No runtime selection reason available.";
        RuntimeDecisionMessage = BuildDecisionMessage(context);
    }

    private void UpdateExpectedProjectPathEditModeFromContext(TiaProjectContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.OpenProjectPath))
        {
            var normalizedPath = context.OpenProjectPath!.Trim();
            if (!string.Equals(ExpectedProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                ExpectedProjectPath = normalizedPath;
            }

            IsExpectedProjectPathManualEditEnabled = false;
            return;
        }

        IsExpectedProjectPathManualEditEnabled = true;
    }

    private string BuildRuntimeLabel(TiaProjectContext context)
    {
        var runtimeVersion = context.TiaVersion ?? SelectedTiaRuntime?.Version ?? "auto";
        var providerLabel = string.IsNullOrWhiteSpace(context.ProviderName) ? string.Empty : $" | provider {context.ProviderName}";
        return string.IsNullOrWhiteSpace(context.OpennessAssemblyPath)
            ? runtimeVersion + providerLabel
            : $"{runtimeVersion} | {context.OpennessAssemblyPath}{providerLabel}";
    }

    private static string BuildDecisionMessage(TiaProjectContext context)
    {
        var provider = string.IsNullOrWhiteSpace(context.ProviderName) ? "not selected" : context.ProviderName;
        var reason = string.IsNullOrWhiteSpace(context.RuntimeSelectionReason) ? "No runtime selection reason available." : context.RuntimeSelectionReason;
        return $"Provider: {provider}. Reason: {reason}";
    }

    private void AddHistory(string level, string? code, string message)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? "General" : code;
        History.Insert(0, $"{DateTime.Now:HH:mm:ss} | {level} | {normalizedCode} | {message}");
        switch (level)
        {
            case "OK":
            case "INFO":
                _historyLog.Information("[{EventCode}] {HistoryMessage}", normalizedCode, message);
                break;
            case "WARN":
                _historyLog.Warning("[{EventCode}] {HistoryMessage}", normalizedCode, message);
                break;
            case "ERROR":
                _historyLog.Error("[{EventCode}] {HistoryMessage}", normalizedCode, message);
                break;
            default:
                _historyLog.Information("[{EventCode}] {HistoryMessage}", normalizedCode, message);
                break;
        }
    }

    private void SyncSettingsModel()
    {
        _settings.Archive.ExpectedProjectPath = ExpectedProjectPath?.Trim() ?? string.Empty;
        _settings.Archive.ArchiveOutputDirectory = ArchiveOutputDirectory?.Trim() ?? string.Empty;
        _settings.Archive.BackupFlow = ParseArchiveBackupFlow();
        _settings.Archive.SuccessfulBackupRetentionCount = Math.Max(0, SuccessfulBackupRetentionCount);
        _settings.Archive.TryDetectUnsavedChanges = TryDetectUnsavedChanges;
        _settings.Archive.ForceSaveWhenDetectionUnavailable = ForceSaveWhenDetectionUnavailable;
        _settings.Archive.TiaVersionSelectionMode = string.Equals(TiaRuntimeSelectionMode, TiaPortalVersionSelectionMode.Manual.ToString(), StringComparison.OrdinalIgnoreCase)
            ? TiaPortalVersionSelectionMode.Manual
            : TiaPortalVersionSelectionMode.Auto;
        _settings.Archive.PreferredTiaVersion = SelectedTiaRuntime?.Version;
        _settings.Project.PowerShellScripts = ProjectScriptEntries
            .Select(entry => entry.Clone())
            .ToList();
        _settings.ControlFiles.Bindings = ControlFileScriptBindings
            .Select(binding => binding.Clone())
            .ToList();
        _settings.Startup.RunOnWindowsStartup = LaunchOnWindowsStartup;
        _settings.Startup.RunSequenceOnWindowsStartup = RunStartupSequenceOnWindowsStartup;
        _settings.Startup.SplashBackgroundImagePath = StartupSplashBackgroundImagePath?.Trim() ?? string.Empty;
        _settings.Startup.SequenceEntries = StartupSequenceEntries
            .Select(entry => entry.Clone())
            .ToList();
        _settings.Logging.DirectoryPath = string.IsNullOrWhiteSpace(LogDirectory) ? "logs" : LogDirectory.Trim();
        _settings.Logging.MinimumLevel = string.IsNullOrWhiteSpace(LogMinimumLevel) ? "Information" : LogMinimumLevel.Trim();
        _settings.Logging.RetainedFileCountLimit = Math.Max(1, LogRetentionFileCount);
        _settings.Ui.ControlFilesDirectory = string.Equals(ControlFilesFolderPath, AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : (ControlFilesFolderPath?.Trim() ?? string.Empty);
    }

    private void EnsureAutostartMatchesSettings()
    {
        try
        {
            _autostartService.SetEnabled(_settings.Startup.RunOnWindowsStartup);
            LaunchOnWindowsStartup = _autostartService.IsEnabled();
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Unable to synchronize autostart: {ex.Message}";
            AddHistory("WARN", "AutostartSyncFailed", ex.Message);
        }
    }

    private bool EnsureAuthenticated()
    {
        return _sessionCoordinator.EnsureAuthenticated(System.Windows.Application.Current?.MainWindow);
    }

    private void HandleSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (e.IsAuthenticated)
        {
            IsSessionAuthenticated = true;
            ReloadFromSettings();
            OpennessAccessActionMessage = "Refreshing Openness access state for the unlocked session...";
            _ = RefreshOpennessAccessStatusAsync(addHistory: false, completionMessage: "Current Openness access state loaded.");
            SettingsStatusMessage = e.Message;
            StatusMessage = "Ready";
            UpdateSessionCountdown();
            return;
        }

        IsSessionAuthenticated = false;
        SettingsStatusMessage = e.Message;
        RuntimeDecisionMessage = "Settings are locked. Open Settings to unlock and edit configuration.";
        UpdateSessionCountdown();
        AddHistory("INFO", e.IsAutomatic ? "SessionTimedOut" : "SessionLocked", e.Message);
    }

    private async Task RefreshOpennessAccessStatusAsync(bool addHistory, string completionMessage)
    {
        var snapshot = await Task.Run(OpennessAccessChecker.GetSnapshot);
        ApplyOpennessAccessSnapshot(snapshot, addHistory);
        LastOpennessAccessCheck = $"Last check: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        OpennessAccessActionMessage = completionMessage;
    }

    private void ApplyOpennessAccessSnapshot(OpennessAccessSnapshot snapshot, bool addHistory)
    {
        CurrentWindowsUser = snapshot.CurrentWindowsUser;
        IsCurrentUserAdministrator = snapshot.IsCurrentUserAdministrator;
        IsOpennessGroupAvailable = snapshot.IsOpennessGroupAvailable;
        IsCurrentUserInOpennessGroup = snapshot.IsCurrentUserInOpennessGroup;
        OpennessGroupStatus = snapshot.OpennessGroupStatus;
        OpennessCheckScope = snapshot.ScopeSummary;
        ResolvedWindowsAccount = snapshot.ResolvedAccountSummary;
        OpennessGroupDiscoverySummary = snapshot.DiscoverySummary;
        OpennessRelatedLocalGroups.ReplaceRange(snapshot.RelatedGroups);
        ResolvedOpennessGroupName = snapshot.ResolvedGroupName;

        var resolvedGroup = string.IsNullOrEmpty(snapshot.ResolvedGroupName) ? "(none found)" : snapshot.ResolvedGroupName;
        if (snapshot.IsCurrentUserInOpennessGroup)
        {
            _opennessLog.Information(
                "Access check passed - User={User}, IsAdmin={IsAdmin}, Group={Group}, Member={IsMember}",
                snapshot.CurrentWindowsUser,
                snapshot.IsCurrentUserAdministrator,
                resolvedGroup,
                snapshot.IsCurrentUserInOpennessGroup);
        }
        else if (snapshot.IsOpennessGroupAvailable)
        {
            _opennessLog.Warning(
                "Access check: user is NOT a member - User={User}, IsAdmin={IsAdmin}, Group={Group}, Member={IsMember}",
                snapshot.CurrentWindowsUser,
                snapshot.IsCurrentUserAdministrator,
                resolvedGroup,
                snapshot.IsCurrentUserInOpennessGroup);
        }
        else
        {
            _opennessLog.Warning(
                "Access check: no Openness group found on this machine - User={User}, IsAdmin={IsAdmin}, Group={Group}",
                snapshot.CurrentWindowsUser,
                snapshot.IsCurrentUserAdministrator,
                resolvedGroup);
        }

        if (addHistory && !string.IsNullOrWhiteSpace(snapshot.HistoryCode) && !string.IsNullOrWhiteSpace(snapshot.HistoryMessage))
        {
            AddHistory(snapshot.HistoryLevel, snapshot.HistoryCode, snapshot.HistoryMessage!);
        }
    }

    private void ReloadFromSettings()
    {
        _isInitializing = true;

        ExpectedProjectPath = _settings.Archive.ExpectedProjectPath;
        ArchiveOutputDirectory = _settings.Archive.ArchiveOutputDirectory;
        SelectedArchiveBackupFlow = _settings.Archive.BackupFlow.ToString();
        SuccessfulBackupRetentionCount = _settings.Archive.SuccessfulBackupRetentionCount;
        TryDetectUnsavedChanges = _settings.Archive.TryDetectUnsavedChanges;
        ForceSaveWhenDetectionUnavailable = _settings.Archive.ForceSaveWhenDetectionUnavailable;
        TiaRuntimeSelectionMode = _settings.Archive.TiaVersionSelectionMode.ToString();
        LaunchOnWindowsStartup = _settings.Startup.RunOnWindowsStartup;
        RunStartupSequenceOnWindowsStartup = _settings.Startup.RunSequenceOnWindowsStartup;
        ControlFilesFolderPath = ResolveControlFilesDirectory(_settings.Ui.ControlFilesDirectory);
        StartupSplashBackgroundImagePath = _settings.Startup.SplashBackgroundImagePath;
        LogDirectory = _settings.Logging.DirectoryPath;
        LogMinimumLevel = _settings.Logging.MinimumLevel;
        LogRetentionFileCount = _settings.Logging.RetainedFileCountLimit;
        LoadRuntimeCatalog();
        LoadProjectScriptEntries();
        LoadControlFileScriptBindings();
        LoadStartupSequenceEntries();
        _isInitializing = false;
        UpdateSessionCountdown();
    }

    private void LoadProjectScriptEntries()
    {
        ProjectScriptEntries.CollectionChanged -= HandleProjectScriptEntriesChanged;

        foreach (var existingEntry in ProjectScriptEntries)
        {
            DetachProjectScriptEntry(existingEntry);
        }

        ProjectScriptEntries.Clear();

        foreach (var entry in _settings.Project.PowerShellScripts)
        {
            var clone = entry.Clone();
            AttachProjectScriptEntry(clone);
            ProjectScriptEntries.Add(clone);
        }

        ProjectScriptEntries.CollectionChanged += HandleProjectScriptEntriesChanged;
        SelectedProjectScriptEntry = ProjectScriptEntries.FirstOrDefault();
    }

    private void AttachProjectScriptEntry(ProjectScriptEntry entry)
    {
        entry.PropertyChanged += HandleProjectScriptEntryPropertyChanged;
        entry.Parameters.CollectionChanged += HandleProjectScriptParametersChanged;

        foreach (var parameter in entry.Parameters)
        {
            parameter.PropertyChanged += HandleProjectScriptParameterPropertyChanged;
        }
    }

    private void DetachProjectScriptEntry(ProjectScriptEntry entry)
    {
        entry.PropertyChanged -= HandleProjectScriptEntryPropertyChanged;
        entry.Parameters.CollectionChanged -= HandleProjectScriptParametersChanged;

        foreach (var parameter in entry.Parameters)
        {
            parameter.PropertyChanged -= HandleProjectScriptParameterPropertyChanged;
        }
    }

    private void LoadControlFileScriptBindings()
    {
        ControlFileScriptBindings.CollectionChanged -= HandleControlFileScriptBindingsChanged;

        foreach (var existingBinding in ControlFileScriptBindings)
        {
            DetachControlFileBinding(existingBinding);
        }

        ControlFileScriptBindings.Clear();

        foreach (var binding in _settings.ControlFiles.Bindings)
        {
            var clone = binding.Clone();
            AttachControlFileBinding(clone);
            ControlFileScriptBindings.Add(clone);
        }

        ControlFileScriptBindings.CollectionChanged += HandleControlFileScriptBindingsChanged;
        SelectedControlFileScriptBinding = ControlFileScriptBindings.FirstOrDefault();
    }

    private void AttachControlFileBinding(ControlFileScriptBinding binding)
    {
        binding.PreExecutionSteps.CollectionChanged += HandleControlFileBindingStepsChanged;
        binding.PostExecutionSteps.CollectionChanged += HandleControlFileBindingStepsChanged;

        foreach (var step in binding.PreExecutionSteps)
        {
            AttachControlFileStep(step);
        }

        foreach (var step in binding.PostExecutionSteps)
        {
            AttachControlFileStep(step);
        }
    }

    private void DetachControlFileBinding(ControlFileScriptBinding binding)
    {
        binding.PreExecutionSteps.CollectionChanged -= HandleControlFileBindingStepsChanged;
        binding.PostExecutionSteps.CollectionChanged -= HandleControlFileBindingStepsChanged;

        foreach (var step in binding.PreExecutionSteps)
        {
            DetachControlFileStep(step);
        }

        foreach (var step in binding.PostExecutionSteps)
        {
            DetachControlFileStep(step);
        }
    }

    private void AttachControlFileStep(ControlFileScriptSequenceStep step)
    {
        step.PropertyChanged += HandleControlFileScriptStepPropertyChanged;
        step.ParameterOverrides.CollectionChanged += HandleControlFileStepParameterOverridesChanged;

        foreach (var overrideEntry in step.ParameterOverrides)
        {
            overrideEntry.PropertyChanged += HandleControlFileStepParameterOverridePropertyChanged;
        }
    }

    private void DetachControlFileStep(ControlFileScriptSequenceStep step)
    {
        step.PropertyChanged -= HandleControlFileScriptStepPropertyChanged;
        step.ParameterOverrides.CollectionChanged -= HandleControlFileStepParameterOverridesChanged;

        foreach (var overrideEntry in step.ParameterOverrides)
        {
            overrideEntry.PropertyChanged -= HandleControlFileStepParameterOverridePropertyChanged;
        }
    }

    private void HandleControlFileScriptBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ControlFileScriptBinding binding in e.OldItems)
            {
                DetachControlFileBinding(binding);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ControlFileScriptBinding binding in e.NewItems)
            {
                AttachControlFileBinding(binding);
            }
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Control file script automation updated.");
    }

    private void HandleControlFileBindingStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ControlFileScriptSequenceStep step in e.OldItems)
            {
                DetachControlFileStep(step);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ControlFileScriptSequenceStep step in e.NewItems)
            {
                AttachControlFileStep(step);
            }
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Control file script automation updated.");
    }

    private void HandleControlFileScriptStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Control file script automation updated.");
        RefreshControlFileStepPreview();
    }

    private void HandleControlFileStepParameterOverridesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ControlFileScriptParameterOverrideEntry overrideEntry in e.OldItems)
            {
                overrideEntry.PropertyChanged -= HandleControlFileStepParameterOverridePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ControlFileScriptParameterOverrideEntry overrideEntry in e.NewItems)
            {
                overrideEntry.PropertyChanged += HandleControlFileStepParameterOverridePropertyChanged;
            }
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Control file script parameter overrides updated.");
        RefreshControlFileStepPreview();
    }

    private void HandleControlFileStepParameterOverridePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Control file script parameter overrides updated.");
        RefreshControlFileStepPreview();
    }

    private void HandleProjectScriptEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ProjectScriptEntry entry in e.OldItems)
            {
                DetachProjectScriptEntry(entry);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ProjectScriptEntry entry in e.NewItems)
            {
                AttachProjectScriptEntry(entry);
            }
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Project script library updated.");
        RefreshProjectScriptPreview();
    }

    private void HandleProjectScriptParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ProjectScriptParameterEntry parameter in e.OldItems)
            {
                parameter.PropertyChanged -= HandleProjectScriptParameterPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ProjectScriptParameterEntry parameter in e.NewItems)
            {
                parameter.PropertyChanged += HandleProjectScriptParameterPropertyChanged;
            }
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Script parameters updated.");
        RefreshProjectScriptPreview();
    }

    private void HandleProjectScriptParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Script parameters updated.");
        RefreshProjectScriptPreview();
    }

    private void HandleProjectScriptEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectScriptEntry.IsRunning)
            || e.PropertyName == nameof(ProjectScriptEntry.LastRunStatus)
            || e.PropertyName == nameof(ProjectScriptEntry.LastRunFinishedAt)
            || e.PropertyName == nameof(ProjectScriptEntry.LastExitCode)
            || e.PropertyName == nameof(ProjectScriptEntry.LastOutput))
        {
            return;
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Project script library updated.");
    }

    private void LoadStartupSequenceEntries()
    {
        StartupSequenceEntries.CollectionChanged -= HandleStartupSequenceEntriesChanged;

        foreach (var existingEntry in StartupSequenceEntries)
        {
            existingEntry.PropertyChanged -= HandleStartupSequenceEntryPropertyChanged;
        }

        StartupSequenceEntries.Clear();

        foreach (var entry in _settings.Startup.SequenceEntries)
        {
            var clone = entry.Clone();
            clone.PropertyChanged += HandleStartupSequenceEntryPropertyChanged;
            StartupSequenceEntries.Add(clone);
        }

        StartupSequenceEntries.CollectionChanged += HandleStartupSequenceEntriesChanged;
        SelectedStartupSequenceEntry = StartupSequenceEntries.FirstOrDefault();
    }

    private void HandleStartupSequenceEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (StartupSequenceEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= HandleStartupSequenceEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (StartupSequenceEntry entry in e.NewItems)
            {
                entry.PropertyChanged += HandleStartupSequenceEntryPropertyChanged;
            }
        }

        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Startup sequence updated.");
    }

    private void HandleStartupSequenceEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializing || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        PersistSettings("Startup sequence updated.");
    }
}
