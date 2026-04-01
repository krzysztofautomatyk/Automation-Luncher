using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveProjectUseCase _archiveProjectUseCase;
    private readonly ITiaPortalGateway _tiaPortalGateway;
    private readonly ITiaPortalRuntimeCatalog _runtimeCatalog;
    private readonly IProtectedApplicationSettingsStore _protectedSettingsStore;
    private readonly AppSessionState _sessionState;
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IAutostartService _autostartService;
    private readonly AutomationLauncherSettings _settings;
    private readonly DispatcherTimer _sessionCountdownTimer;
    private readonly DispatcherTimer _fileLogRefreshTimer;
    private string _lastLogSnapshotKey = string.Empty;
    private bool _isInitializing = true;

    [ObservableProperty]
    private string expectedProjectPath = string.Empty;

    [ObservableProperty]
    private string archiveOutputDirectory = string.Empty;

    [ObservableProperty]
    private bool tryDetectUnsavedChanges;

    [ObservableProperty]
    private bool forceSaveWhenDetectionUnavailable;

    [ObservableProperty]
    private string tiaRuntimeSelectionMode = TiaPortalVersionSelectionMode.Auto.ToString();

    [ObservableProperty]
    private TiaPortalRuntimeInfo? selectedTiaRuntime;

    [ObservableProperty]
    private string runtimeCatalogStatus = "Scanning installed TIA Portal runtimes...";

    [ObservableProperty]
    private string runtimeDecisionMessage = "Runtime provider diagnostics will appear here after connection or synchronization.";

    [ObservableProperty]
    private string detectedProcessVersion = "n/a";

    [ObservableProperty]
    private string selectedRuntimeVersion = "n/a";

    [ObservableProperty]
    private string selectedProviderName = "n/a";

    [ObservableProperty]
    private string selectedAssemblyPath = "n/a";

    [ObservableProperty]
    private string runtimeSelectionReason = "n/a";

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isSessionAuthenticated;

    [ObservableProperty]
    private bool isStartupAutomationRunning;

    [ObservableProperty]
    private HostControlState currentHostControlState = HostControlState.Ready;

    [ObservableProperty]
    private string hostName = Environment.MachineName;

    [ObservableProperty]
    private string appVersion = AppVersionInfo.DisplayVersion;

    [ObservableProperty]
    private string settingsFilePath = string.Empty;

    [ObservableProperty]
    private string settingsStatusMessage = "Protected settings are locked. Open Settings to unlock and edit configuration.";

    [ObservableProperty]
    private bool launchOnWindowsStartup;

    [ObservableProperty]
    private bool runStartupSequenceOnWindowsStartup = true;

    [ObservableProperty]
    private string startupFolderPath = string.Empty;

    [ObservableProperty]
    private string controlFilesFolderPath = AppContext.BaseDirectory;

    [ObservableProperty]
    private string startupSplashBackgroundImagePath = string.Empty;

    [ObservableProperty]
    private string logDirectory = "logs";

    [ObservableProperty]
    private string logMinimumLevel = "Information";

    [ObservableProperty]
    private int logRetentionFileCount = 30;

    [ObservableProperty]
    private string sessionTimeRemaining = "05:00";

    [ObservableProperty]
    private StartupSequenceEntry? selectedStartupSequenceEntry;

    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<string> FileLogs { get; } = new();

    public ObservableCollection<string> TiaRuntimeSelectionModes { get; } = new()
    {
        TiaPortalVersionSelectionMode.Auto.ToString(),
        TiaPortalVersionSelectionMode.Manual.ToString()
    };

    public ObservableCollection<TiaPortalRuntimeInfo> AvailableTiaRuntimes { get; } = new();

    public ObservableCollection<StartupSequenceEntry> StartupSequenceEntries { get; } = new();

    public ObservableCollection<string> LogLevels { get; } = new()
    {
        "Verbose",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Fatal"
    };

    public event EventHandler<ArchiveWorkflowStateChangedEventArgs>? ArchiveWorkflowStateChanged;

    public MainWindowViewModel(
        ArchiveProjectUseCase archiveProjectUseCase,
        ITiaPortalGateway tiaPortalGateway,
        ITiaPortalRuntimeCatalog runtimeCatalog,
        IProtectedApplicationSettingsStore protectedSettingsStore,
        AppSessionState sessionState,
        ISessionCoordinator sessionCoordinator,
        IAutostartService autostartService,
        AutomationLauncherSettings settings)
    {
        _archiveProjectUseCase = archiveProjectUseCase;
        _tiaPortalGateway = tiaPortalGateway;
        _runtimeCatalog = runtimeCatalog;
        _protectedSettingsStore = protectedSettingsStore;
        _sessionState = sessionState;
        _sessionCoordinator = sessionCoordinator;
        _autostartService = autostartService;
        _settings = settings;

        ExpectedProjectPath = _settings.Archive.ExpectedProjectPath;
        ArchiveOutputDirectory = _settings.Archive.ArchiveOutputDirectory;
        TryDetectUnsavedChanges = _settings.Archive.TryDetectUnsavedChanges;
        ForceSaveWhenDetectionUnavailable = _settings.Archive.ForceSaveWhenDetectionUnavailable;
        TiaRuntimeSelectionMode = _settings.Archive.TiaVersionSelectionMode.ToString();
        LaunchOnWindowsStartup = _settings.Startup.RunOnWindowsStartup;
        RunStartupSequenceOnWindowsStartup = _settings.Startup.RunSequenceOnWindowsStartup;
        StartupFolderPath = _autostartService.GetStartupFolderPath();
        ControlFilesFolderPath = AppContext.BaseDirectory;
        StartupSplashBackgroundImagePath = _settings.Startup.SplashBackgroundImagePath;
        LogDirectory = _settings.Logging.DirectoryPath;
        LogMinimumLevel = _settings.Logging.MinimumLevel;
        LogRetentionFileCount = _settings.Logging.RetainedFileCountLimit;
        SettingsFilePath = _protectedSettingsStore.SettingsFilePath;
        IsSessionAuthenticated = _sessionCoordinator.IsAuthenticated;

        LoadRuntimeCatalog();
        LoadStartupSequenceEntries();
        EnsureAutostartMatchesSettings();
        _isInitializing = false;
        _sessionCoordinator.SessionStateChanged += HandleSessionStateChanged;
        _sessionCountdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sessionCountdownTimer.Tick += HandleSessionCountdownTick;
        _sessionCountdownTimer.Start();

        _fileLogRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _fileLogRefreshTimer.Tick += HandleFileLogRefreshTick;
        _fileLogRefreshTimer.Start();

        RefreshFileLogs(forceRefresh: true);
        UpdateSessionCountdown();
    }

    public bool CanUseProtectedActions => IsSessionAuthenticated && !IsBusy;

    public bool CanUseProtectedUtilities => IsSessionAuthenticated;

    public bool CanLoginSession => !IsSessionAuthenticated;

    public bool CanRunStartupAutomationManually => IsSessionAuthenticated
        && !IsStartupAutomationRunning
        && CurrentHostControlState != HostControlState.Running
        && CurrentHostControlState != HostControlState.Stopping;

    public bool CanStopManagedApplications => IsSessionAuthenticated
        && (CurrentHostControlState == HostControlState.Running || IsStartupAutomationRunning);

    public string HostControlStateDisplay => CurrentHostControlState.ToString();

    public string HostRunFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.run");

    public string HostReadyFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.ready");

    public string HostStoppingFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.stopping");

    public string HostErrorFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.error");

    public string HostStartFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.start");

    public string HostStopFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.stop");

    public string HostMakeArchiveFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.makearchive");

    public string HostArchiveCreatedFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.archivecreated");

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

            ExpectedProjectPath = context.OpenProjectPath!;
            StatusMessage = $"Project path synchronized from TIA: {ExpectedProjectPath} | runtime {BuildRuntimeLabel(context)}";
            ApplyRuntimeDiagnostics(context);
            AddHistory("INFO", "SyncOk", $"Synchronized project path from TIA: {ExpectedProjectPath} | runtime {BuildRuntimeLabel(context)}");
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
        await RunArchiveWorkflowAsync();
    }

    private async Task<bool> RunArchiveWorkflowAsync()
    {
        ArchiveWorkflowStateChanged?.Invoke(this, new ArchiveWorkflowStateChangedEventArgs(true));
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
                TiaVersionSelectionMode = _settings.Archive.TiaVersionSelectionMode,
                PreferredTiaVersion = _settings.Archive.PreferredTiaVersion,
                OpennessAssemblyPath = _settings.Archive.OpennessAssemblyPath,
                KnownVersions = _settings.Archive.KnownVersions
            };

            var result = await _archiveProjectUseCase.ExecuteAsync(options, CancellationToken.None);
            var summary = $"{result.Outcome} | {result.Message}";

            if (result.RuntimeContext is not null)
            {
                ApplyRuntimeDiagnostics(result.RuntimeContext);
            }

            if (!string.IsNullOrWhiteSpace(result.ArchivePath))
            {
                summary += $" | {result.ArchivePath}";
            }

            AddHistory(result.Outcome == ArchiveOutcome.Success ? "OK" : "WARN", result.Outcome.ToString(), summary);
            StatusMessage = result.Message;
            return result.Outcome == ArchiveOutcome.Success;
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Archive command failed");
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

    [RelayCommand]
    private void BrowseProjectFile()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var dialog = new FileDialog
        {
            Title = "Select TIA project file",
            Filter = "TIA projects (*.ap*;*.zap*)|*.ap*;*.zap*|All files (*.*)|*.*",
            FileName = ExpectedProjectPath
        };

        if (dialog.ShowDialog() == true)
        {
            ExpectedProjectPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseArchiveDirectory()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var selectedPath = SelectFolder("Select archive output directory", ArchiveOutputDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            ArchiveOutputDirectory = selectedPath!;
        }
    }

    [RelayCommand]
    private void BrowseLogDirectory()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var selectedPath = SelectFolder("Select log directory", LogDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            LogDirectory = selectedPath!;
        }
    }

    [RelayCommand]
    private void OpenStartupFolder()
    {
        OpenPath(StartupFolderPath);
    }

    [RelayCommand]
    private void OpenControlFilesFolder()
    {
        OpenPath(ControlFilesFolderPath);
    }

    [RelayCommand]
    private void AddStartupSequenceEntry()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var dialog = new FileDialog
        {
            Title = "Select application for Windows startup sequence",
            Filter = "Applications (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var entry = new StartupSequenceEntry
        {
            Alias = Path.GetFileNameWithoutExtension(dialog.FileName) ?? string.Empty,
            ExecutablePath = dialog.FileName,
            DelaySeconds = 0
        };

        StartupSequenceEntries.Add(entry);
        SelectedStartupSequenceEntry = entry;
        PersistSettings("Startup sequence updated.");
    }

    [RelayCommand]
    private void RemoveSelectedStartupSequenceEntry()
    {
        if (!EnsureAuthenticated() || SelectedStartupSequenceEntry is null)
        {
            return;
        }

        var entryToRemove = SelectedStartupSequenceEntry;
        StartupSequenceEntries.Remove(entryToRemove);
        SelectedStartupSequenceEntry = StartupSequenceEntries.FirstOrDefault();
        PersistSettings("Startup sequence updated.");
    }

    [RelayCommand]
    private void MoveStartupSequenceEntryUp()
    {
        if (!EnsureAuthenticated() || SelectedStartupSequenceEntry is null)
        {
            return;
        }

        var currentIndex = StartupSequenceEntries.IndexOf(SelectedStartupSequenceEntry);
        if (currentIndex <= 0)
        {
            return;
        }

        StartupSequenceEntries.Move(currentIndex, currentIndex - 1);
        PersistSettings("Startup sequence order updated.");
    }

    [RelayCommand]
    private void MoveStartupSequenceEntryDown()
    {
        if (!EnsureAuthenticated() || SelectedStartupSequenceEntry is null)
        {
            return;
        }

        var currentIndex = StartupSequenceEntries.IndexOf(SelectedStartupSequenceEntry);
        if (currentIndex < 0 || currentIndex >= StartupSequenceEntries.Count - 1)
        {
            return;
        }

        StartupSequenceEntries.Move(currentIndex, currentIndex + 1);
        PersistSettings("Startup sequence order updated.");
    }

    [RelayCommand]
    private void BrowseStartupSplashBackground()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var dialog = new FileDialog
        {
            Title = "Select splash screen background image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            StartupSplashBackgroundImagePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void ClearStartupSplashBackground()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        StartupSplashBackgroundImagePath = string.Empty;
        PersistSettings("Startup splash background cleared.");
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var path = LogPathHelper.ResolveDirectory(LogDirectory);
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    [RelayCommand]
    private void ApplySettings()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        PersistSettings("Settings applied manually.", loggingChangeRequiresRestart: true);
    }

    [RelayCommand]
    private void ResetSessionTimer()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

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

    private bool CanArchive()
    {
        return !IsBusy && IsSessionAuthenticated;
    }

    partial void OnIsBusyChanged(bool value)
    {
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(HostStoppingFilePath));
        OnPropertyChanged(nameof(HostErrorFilePath));
        OnPropertyChanged(nameof(HostStartFilePath));
        OnPropertyChanged(nameof(HostStopFilePath));
        OnPropertyChanged(nameof(HostMakeArchiveFilePath));
        OnPropertyChanged(nameof(HostArchiveCreatedFilePath));
    }

    partial void OnControlFilesFolderPathChanged(string value)
    {
        OnPropertyChanged(nameof(HostRunFilePath));
        OnPropertyChanged(nameof(HostReadyFilePath));
        OnPropertyChanged(nameof(HostStoppingFilePath));
        OnPropertyChanged(nameof(HostErrorFilePath));
        OnPropertyChanged(nameof(HostStartFilePath));
        OnPropertyChanged(nameof(HostStopFilePath));
        OnPropertyChanged(nameof(HostMakeArchiveFilePath));
        OnPropertyChanged(nameof(HostArchiveCreatedFilePath));
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
        RefreshFileLogs(forceRefresh: true);
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
            SelectedTiaRuntime = AvailableTiaRuntimes.FirstOrDefault(runtime => string.Equals(runtime.Version, _settings.Archive.PreferredTiaVersion, StringComparison.OrdinalIgnoreCase));
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
    }

    private void SyncSettingsModel()
    {
        _settings.Archive.ExpectedProjectPath = ExpectedProjectPath?.Trim() ?? string.Empty;
        _settings.Archive.ArchiveOutputDirectory = ArchiveOutputDirectory?.Trim() ?? string.Empty;
        _settings.Archive.TryDetectUnsavedChanges = TryDetectUnsavedChanges;
        _settings.Archive.ForceSaveWhenDetectionUnavailable = ForceSaveWhenDetectionUnavailable;
        _settings.Archive.TiaVersionSelectionMode = string.Equals(TiaRuntimeSelectionMode, TiaPortalVersionSelectionMode.Manual.ToString(), StringComparison.OrdinalIgnoreCase)
            ? TiaPortalVersionSelectionMode.Manual
            : TiaPortalVersionSelectionMode.Auto;
        _settings.Archive.PreferredTiaVersion = SelectedTiaRuntime?.Version;
        _settings.Startup.RunOnWindowsStartup = LaunchOnWindowsStartup;
        _settings.Startup.RunSequenceOnWindowsStartup = RunStartupSequenceOnWindowsStartup;
        _settings.Startup.SplashBackgroundImagePath = StartupSplashBackgroundImagePath?.Trim() ?? string.Empty;
        _settings.Startup.SequenceEntries = StartupSequenceEntries
            .Select(entry => entry.Clone())
            .ToList();
        _settings.Logging.DirectoryPath = string.IsNullOrWhiteSpace(LogDirectory) ? "logs" : LogDirectory.Trim();
        _settings.Logging.MinimumLevel = string.IsNullOrWhiteSpace(LogMinimumLevel) ? "Information" : LogMinimumLevel.Trim();
        _settings.Logging.RetainedFileCountLimit = Math.Max(1, LogRetentionFileCount);
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

    private static string? SelectFolder(string description, string? initialPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            SelectedPath = string.IsNullOrWhiteSpace(initialPath) ? string.Empty : initialPath,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
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

    private void ReloadFromSettings()
    {
        _isInitializing = true;
        ExpectedProjectPath = _settings.Archive.ExpectedProjectPath;
        ArchiveOutputDirectory = _settings.Archive.ArchiveOutputDirectory;
        TryDetectUnsavedChanges = _settings.Archive.TryDetectUnsavedChanges;
        ForceSaveWhenDetectionUnavailable = _settings.Archive.ForceSaveWhenDetectionUnavailable;
        TiaRuntimeSelectionMode = _settings.Archive.TiaVersionSelectionMode.ToString();
        LaunchOnWindowsStartup = _settings.Startup.RunOnWindowsStartup;
        RunStartupSequenceOnWindowsStartup = _settings.Startup.RunSequenceOnWindowsStartup;
        StartupSplashBackgroundImagePath = _settings.Startup.SplashBackgroundImagePath;
        LogDirectory = _settings.Logging.DirectoryPath;
        LogMinimumLevel = _settings.Logging.MinimumLevel;
        LogRetentionFileCount = _settings.Logging.RetainedFileCountLimit;
        LoadRuntimeCatalog();
        LoadStartupSequenceEntries();
        _isInitializing = false;
        UpdateSessionCountdown();
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

    private void HandleSessionCountdownTick(object? sender, EventArgs e)
    {
        UpdateSessionCountdown();
    }

    private void HandleFileLogRefreshTick(object? sender, EventArgs e)
    {
        RefreshFileLogs();
    }

    private void UpdateSessionCountdown()
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            SessionTimeRemaining = "Locked";
            return;
        }

        var remaining = _sessionCoordinator.GetRemainingInactivity();
        SessionTimeRemaining = $"{Math.Max(0, (int)remaining.TotalMinutes):00}:{remaining.Seconds:00}";
    }

    private void RefreshFileLogs(bool forceRefresh = false)
    {
        var logDirectoryPath = ResolveEffectiveLogDirectory();
        if (!Directory.Exists(logDirectoryPath))
        {
            if (FileLogs.Count > 0)
            {
                FileLogs.Clear();
            }

            _lastLogSnapshotKey = string.Empty;
            return;
        }

        var logFiles = Directory.GetFiles(logDirectoryPath, "automation-launcher-*.log");
        Array.Sort(logFiles, StringComparer.OrdinalIgnoreCase);

        var snapshotParts = new List<string>(logFiles.Length);
        foreach (var logFile in logFiles)
        {
            try
            {
                var info = new FileInfo(logFile);
                snapshotParts.Add($"{info.Name}:{info.Length}:{info.LastWriteTimeUtc.Ticks}");
            }
            catch
            {
                snapshotParts.Add(logFile);
            }
        }

        var snapshotKey = string.Join("|", snapshotParts);
        if (!forceRefresh && string.Equals(snapshotKey, _lastLogSnapshotKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastLogSnapshotKey = snapshotKey;

        var logLines = new List<string>();
        foreach (var logFile in logFiles)
        {
            try
            {
                logLines.AddRange(File.ReadLines(logFile));
            }
            catch
            {
                // Ignore transient file-read issues and keep already collected log lines.
            }
        }

        FileLogs.Clear();
        foreach (var logLine in logLines)
        {
            FileLogs.Add(logLine);
        }
    }

    private string ResolveEffectiveLogDirectory()
    {
        var configuredPath = string.IsNullOrWhiteSpace(LogDirectory)
            ? "logs"
            : LogDirectory;

        var preferredDirectory = LogPathHelper.ResolveDirectory(configuredPath);
        try
        {
            Directory.CreateDirectory(preferredDirectory);
            return preferredDirectory;
        }
        catch
        {
            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutomationLauncher",
                "logs");
            Directory.CreateDirectory(fallbackDirectory);
            return fallbackDirectory;
        }
    }

    public void SetStartupAutomationRunning(bool isRunning)
    {
        IsStartupAutomationRunning = isRunning;
    }

    public void SetHostControlState(HostControlState state)
    {
        CurrentHostControlState = state;
    }

    public async Task RunArchiveFromControlFileAsync()
    {
        if (IsBusy)
        {
            AddHistory("INFO", "ArchiveCommandIgnored", "Archive command ignored because the launcher is already busy.");
            return;
        }

        await RunArchiveWorkflowAsync();
    }

    public async Task<bool> RunArchiveFromControlFileWithResultAsync()
    {
        if (IsBusy)
        {
            AddHistory("INFO", "ArchiveCommandIgnored", "Archive command ignored because the launcher is already busy.");
            return false;
        }

        return await RunArchiveWorkflowAsync();
    }
}
