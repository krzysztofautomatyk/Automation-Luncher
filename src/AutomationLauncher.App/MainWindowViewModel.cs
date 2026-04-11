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
using Media = System.Windows.Media;
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

public sealed class LogLineEntry
{
    private static readonly Media.Brush SearchMatchBackground = CreateFrozenBrush(255, 247, 204);
    private static readonly Media.Brush ErrorBackground = CreateFrozenBrush(255, 232, 232);
    private static readonly Media.Brush WarningBackground = CreateFrozenBrush(255, 242, 224);
    private static readonly Media.Brush InfoBackground = CreateFrozenBrush(236, 247, 255);
    private static readonly Media.Brush VerboseBackground = CreateFrozenBrush(245, 245, 245);
    private static readonly Media.Brush DefaultBackground = CreateFrozenBrush(255, 255, 255);
    private static readonly Media.Brush ErrorForeground = CreateFrozenBrush(137, 27, 27);
    private static readonly Media.Brush WarningForeground = CreateFrozenBrush(166, 101, 0);
    private static readonly Media.Brush DebugForeground = CreateFrozenBrush(48, 84, 120);
    private static readonly Media.Brush VerboseForeground = CreateFrozenBrush(88, 88, 88);
    private static readonly Media.Brush InfoForeground = CreateFrozenBrush(26, 72, 116);
    private static readonly Media.Brush DefaultForeground = CreateFrozenBrush(34, 34, 34);

    public LogLineEntry(string message, string level, bool isSearchMatch)
    {
        Message = message;
        Level = level;
        Foreground = GetForeground(level);
        Background = isSearchMatch ? SearchMatchBackground : GetBackground(level);
    }

    public string Message { get; }

    public string Level { get; }

    public Media.Brush Foreground { get; }

    public Media.Brush Background { get; }

    private static Media.Brush GetForeground(string level)
    {
        return level switch
        {
            "ERR" or "FTL" => ErrorForeground,
            "WRN" => WarningForeground,
            "DBG" => DebugForeground,
            "VRB" => VerboseForeground,
            "INF" => InfoForeground,
            _ => DefaultForeground
        };
    }

    private static Media.Brush GetBackground(string level)
    {
        return level switch
        {
            "ERR" or "FTL" => ErrorBackground,
            "WRN" => WarningBackground,
            "INF" => InfoBackground,
            "DBG" or "VRB" => VerboseBackground,
            _ => DefaultBackground
        };
    }

    private static Media.Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

internal sealed class LogRefreshResult
{
    public LogRefreshResult(string loadedLogFilePath, string snapshotKey, IReadOnlyList<string> logLines, string? errorMessage)
    {
        LoadedLogFilePath = loadedLogFilePath;
        SnapshotKey = snapshotKey;
        LogLines = logLines;
        ErrorMessage = errorMessage;
    }

    public string LoadedLogFilePath { get; }

    public string SnapshotKey { get; }

    public IReadOnlyList<string> LogLines { get; }

    public string? ErrorMessage { get; }

    public static LogRefreshResult Empty(string loadedLogFilePath)
    {
        return new LogRefreshResult(loadedLogFilePath, string.Empty, Array.Empty<string>(), null);
    }
}

internal sealed class OpennessAccessSnapshot
{
    public OpennessAccessSnapshot(
        string currentWindowsUser,
        bool isCurrentUserAdministrator,
        bool isOpennessGroupAvailable,
        bool isCurrentUserInOpennessGroup,
        string opennessGroupStatus,
        string scopeSummary,
        string resolvedAccountSummary,
        string discoverySummary,
        IReadOnlyList<string> relatedGroups,
        string resolvedGroupName,
        string? historyCode,
        string? historyMessage,
        string historyLevel)
    {
        CurrentWindowsUser = currentWindowsUser;
        IsCurrentUserAdministrator = isCurrentUserAdministrator;
        IsOpennessGroupAvailable = isOpennessGroupAvailable;
        IsCurrentUserInOpennessGroup = isCurrentUserInOpennessGroup;
        OpennessGroupStatus = opennessGroupStatus;
        ScopeSummary = scopeSummary;
        ResolvedAccountSummary = resolvedAccountSummary;
        DiscoverySummary = discoverySummary;
        RelatedGroups = relatedGroups;
        ResolvedGroupName = resolvedGroupName;
        HistoryCode = historyCode;
        HistoryMessage = historyMessage;
        HistoryLevel = historyLevel;
    }

    public string CurrentWindowsUser { get; }

    public bool IsCurrentUserAdministrator { get; }

    public bool IsOpennessGroupAvailable { get; }

    public bool IsCurrentUserInOpennessGroup { get; }

    public string OpennessGroupStatus { get; }

    public string ScopeSummary { get; }

    public string ResolvedAccountSummary { get; }

    public string DiscoverySummary { get; }

    public IReadOnlyList<string> RelatedGroups { get; }

    public string ResolvedGroupName { get; }

    public string? HistoryCode { get; }

    public string? HistoryMessage { get; }

    public string HistoryLevel { get; }
}

public partial class MainWindowViewModel : ObservableObject
{
    private const int MaxDisplayedLogLines = 5000;
    private const string OpennessSecurityGroupName = "Siemens TIA Portal Openness";
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
    private readonly List<string> _allFileLogLines = new();
    private readonly SemaphoreSlim _fileLogRefreshSemaphore = new(1, 1);
    private string _lastLogSnapshotKey = string.Empty;
    private bool _isInitializing = true;
    private bool _pendingFileLogRefresh;

    private readonly ILogger _log;
    private readonly ILogger _opennessLog;
    private readonly ILogger _historyLog;

    [ObservableProperty]
    private string expectedProjectPath = string.Empty;

    [ObservableProperty]
    private string archiveOutputDirectory = string.Empty;

    [ObservableProperty]
    private string selectedArchiveBackupFlow = ArchiveBackupFlow.TimestampedRetention.ToString();

    [ObservableProperty]
    private int successfulBackupRetentionCount;

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
    private string currentWindowsUser = "n/a";

    [ObservableProperty]
    private bool isCurrentUserAdministrator;

    [ObservableProperty]
    private bool isOpennessGroupAvailable;

    [ObservableProperty]
    private bool? isCurrentUserInOpennessGroup;

    [ObservableProperty]
    private string opennessGroupStatus = "Openness group access has not been checked yet.";

    [ObservableProperty]
    private string opennessAccessActionMessage = "Use Check group access to validate current Windows permissions for TIA Openness.";

    [ObservableProperty]
    private string lastOpennessAccessCheck = "Not checked yet.";

    [ObservableProperty]
    private bool isCheckingOpennessAccess;

    [ObservableProperty]
    private string opennessCheckScope = "Local machine scope will be shown after the first check.";

    [ObservableProperty]
    private string resolvedWindowsAccount = "Resolved Windows account details will appear after the first check.";

    [ObservableProperty]
    private string opennessGroupDiscoverySummary = "Related local group discovery has not run yet.";

    [ObservableProperty]
    private string resolvedOpennessGroupName = "Not checked yet.";

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
    private bool hasErrorControlFile;

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
    private string logSearchText = string.Empty;

    [ObservableProperty]
    private bool showErrorsAndWarningsOnly;

    [ObservableProperty]
    private bool isLogAutoScrollEnabled = true;

    [ObservableProperty]
    private string loadedLogFilePath = "No log file loaded.";

    [ObservableProperty]
    private string sessionTimeRemaining = "05:00";

    [ObservableProperty]
    private StartupSequenceEntry? selectedStartupSequenceEntry;

    public ObservableCollection<string> History { get; } = new();
    public RangeObservableCollection<LogLineEntry> FileLogs { get; } = new();
    public RangeObservableCollection<string> OpennessRelatedLocalGroups { get; } = new();

    public int VisibleLogCount => FileLogs.Count;

    public bool HasLogSearchText => !string.IsNullOrWhiteSpace(LogSearchText);

    public bool IsTimestampedBackupFlowSelected => string.Equals(SelectedArchiveBackupFlow, ArchiveBackupFlow.TimestampedRetention.ToString(), StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<string> TiaRuntimeSelectionModes { get; } = new()
    {
        TiaPortalVersionSelectionMode.Auto.ToString(),
        TiaPortalVersionSelectionMode.Manual.ToString()
    };

    public ObservableCollection<TiaPortalRuntimeInfo> AvailableTiaRuntimes { get; } = new();

    public ObservableCollection<string> ArchiveBackupFlows { get; } = new()
    {
        ArchiveBackupFlow.TimestampedRetention.ToString(),
        ArchiveBackupFlow.StableFileWithOld.ToString()
    };

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

        _log = Log.ForContext<MainWindowViewModel>();
        _opennessLog = Log.ForContext("SourceContext", "OpennessAccess");
        _historyLog = Log.ForContext("SourceContext", "ActivityHistory");

        ExpectedProjectPath = _settings.Archive.ExpectedProjectPath;
        ArchiveOutputDirectory = _settings.Archive.ArchiveOutputDirectory;
        SelectedArchiveBackupFlow = _settings.Archive.BackupFlow.ToString();
        SuccessfulBackupRetentionCount = _settings.Archive.SuccessfulBackupRetentionCount;
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
        FileLogs.CollectionChanged += HandleFileLogsCollectionChanged;

        _ = RefreshFileLogsAsync(forceRefresh: true);
        OpennessAccessActionMessage = "Loading current Openness access state...";
        _ = RefreshOpennessAccessStatusAsync(addHistory: false, completionMessage: "Current Openness access state loaded.");
        UpdateSessionCountdown();
    }

    public bool CanUseProtectedActions => IsSessionAuthenticated && !IsBusy;

    public bool CanUseProtectedUtilities => IsSessionAuthenticated;

    public bool CanLoginSession => !IsSessionAuthenticated;

    public string OpennessGroupName => OpennessSecurityGroupName;

    public bool CanCheckOpennessGroupAccess => !IsCheckingOpennessAccess;

    public bool CanRepairOpennessGroupMembership => !IsBusy
        && !IsCheckingOpennessAccess
        && IsOpennessGroupAvailable
        && IsCurrentUserInOpennessGroup == false;

    public bool CanRunStartupAutomationManually => IsSessionAuthenticated
        && !IsStartupAutomationRunning
        && CurrentHostControlState != HostControlState.Running
        && CurrentHostControlState != HostControlState.Stopping;

    public bool CanStopManagedApplications => IsSessionAuthenticated
        && (CurrentHostControlState == HostControlState.Running || IsStartupAutomationRunning);

    public string HostControlStateDisplay => CurrentHostControlState.ToString();

    public string HostRunFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.run");

    public string HostReadyFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.ready");

    public string HostErrorFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.error");

    public string HostStartFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.start");

    public string HostStopFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.stop");

    public string HostMarchFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.march");

    public string HostArchOkFilePath => Path.Combine(ControlFilesFolderPath, $"{HostName}.archok");

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

    [RelayCommand(CanExecute = nameof(CanCheckOpennessGroupAccess))]
    private async Task CheckOpennessGroupAccess()
    {
        IsCheckingOpennessAccess = true;
        LastOpennessAccessCheck = "Check in progress...";
        OpennessGroupStatus = "Checking local machine group membership and related Siemens/Openness groups...";
        OpennessAccessActionMessage = "Checking local Windows account, administrator rights, and Siemens TIA Portal Openness group membership...";

        _opennessLog.Information("Openness access check started by user action");

        try
        {
            var snapshot = await Task.Run(GetOpennessAccessSnapshot);
            ApplyOpennessAccessSnapshot(snapshot, addHistory: true);
            LastOpennessAccessCheck = $"Last check: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            OpennessAccessActionMessage = "Group access check completed.";
        }
        catch (Exception ex)
        {
            OpennessGroupStatus = $"Openness group check failed: {ex.Message}";
            OpennessAccessActionMessage = "Group access check failed.";
            LastOpennessAccessCheck = $"Last check failed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            _opennessLog.Error(ex, "Openness access check failed with an unhandled exception");
            AddHistory("ERROR", "OpennessCheckFailed", ex.Message);
        }
        finally
        {
            IsCheckingOpennessAccess = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRepairOpennessGroupMembership))]
    private async Task RepairOpennessGroupMembership()
    {
        IsCheckingOpennessAccess = true;
        LastOpennessAccessCheck = "Repair in progress...";
        OpennessGroupStatus = "Starting elevated repair for the local Siemens Openness group...";
        OpennessAccessActionMessage = "Starting elevated repair. Confirm the Windows UAC prompt to continue.";

        if (IsCurrentUserInOpennessGroup == true)
        {
            OpennessGroupStatus = "Current user already belongs to Siemens TIA Portal Openness.";
            OpennessAccessActionMessage = "Repair skipped because user already has access.";
            _opennessLog.Information(
                "Group membership repair skipped — user {User} is already a member of {Group}",
                CurrentWindowsUser, ResolvedOpennessGroupName);
            IsCheckingOpennessAccess = false;
            return;
        }

        if (!IsOpennessGroupAvailable || string.IsNullOrWhiteSpace(ResolvedOpennessGroupName))
        {
            OpennessGroupStatus = "No Openness group was found on this machine. Confirm TIA Openness installation.";
            OpennessAccessActionMessage = "Repair unavailable because no Openness Windows group was found.";
            _opennessLog.Warning(
                "Group membership repair aborted — no Openness group exists on this machine. TIA Openness may not be installed");
            IsCheckingOpennessAccess = false;
            return;
        }

        var targetGroupName = ResolvedOpennessGroupName;
        var userIdentity = string.IsNullOrWhiteSpace(CurrentWindowsUser)
            ? Environment.UserName
            : CurrentWindowsUser;

        _opennessLog.Information(
            "Starting group membership repair — User={User}, TargetGroup={Group}. UAC elevation required",
            userIdentity, targetGroupName);

        var processInfo = new ProcessStartInfo
        {
            FileName = "net",
            Arguments = $"localgroup \"{targetGroupName}\" \"{userIdentity}\" /add",
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            var process = Process.Start(processInfo);
            if (process is null)
            {
                OpennessGroupStatus = "Unable to start elevated membership repair process.";
                OpennessAccessActionMessage = "Repair could not start.";
                _opennessLog.Error(
                    "Failed to start elevated net.exe process for group membership repair — User={User}, Group={Group}",
                    userIdentity, targetGroupName);
                AddHistory("ERROR", "OpennessRepairFailed", OpennessGroupStatus);
                return;
            }

            OpennessAccessActionMessage = "Waiting for elevated repair process to finish...";

            var exitCode = await Task.Run(() =>
            {
                process.WaitForExit();
                return process.ExitCode;
            });

            if (exitCode == 0)
            {
                OpennessGroupStatus = $"User was added to '{targetGroupName}'. Sign out and sign in (or restart) to refresh Windows security token.";
                OpennessAccessActionMessage = "Repair finished successfully. Windows sign-out/sign-in is still required.";
                _opennessLog.Information(
                    "Group membership repair succeeded — User={User} added to group {Group}. Windows sign-out/sign-in required to refresh access token",
                    userIdentity, targetGroupName);
                AddHistory("OK", "OpennessRepairExecuted", OpennessGroupStatus);
            }
            else
            {
                OpennessGroupStatus = $"Membership repair finished with exit code {exitCode}.";
                OpennessAccessActionMessage = "Repair process finished, but Windows reported a non-zero exit code.";
                _opennessLog.Warning(
                    "Group membership repair completed with non-zero exit code {ExitCode} — User={User}, Group={Group}. User may already be a member or the command was rejected",
                    exitCode, userIdentity, targetGroupName);
                AddHistory("WARN", "OpennessRepairExitCode", OpennessGroupStatus);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            OpennessGroupStatus = "UAC elevation was cancelled. Membership was not changed.";
            OpennessAccessActionMessage = "Repair was cancelled in the UAC prompt.";
            _opennessLog.Warning(
                "Group membership repair cancelled — UAC prompt was dismissed by the user. No membership change was made for {User}",
                userIdentity);
            AddHistory("WARN", "OpennessRepairCancelled", OpennessGroupStatus);
        }
        catch (Exception ex)
        {
            OpennessGroupStatus = $"Failed to add user to Openness group: {ex.Message}";
            OpennessAccessActionMessage = "Repair failed.";
            _opennessLog.Error(ex,
                "Group membership repair failed with an unexpected exception — User={User}, Group={Group}",
                userIdentity, targetGroupName);
            AddHistory("ERROR", "OpennessRepairFailed", ex.Message);
        }
        finally
        {
            await RefreshOpennessAccessStatusAsync(addHistory: true, completionMessage: "Openness access state refreshed after repair.");
            IsCheckingOpennessAccess = false;
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
                BackupFlow = ParseArchiveBackupFlow(),
                SuccessfulBackupRetentionCount = Math.Max(0, SuccessfulBackupRetentionCount),
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
    private void ExportSettings()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        SyncSettingsModel();

        var dialog = new SaveFileDialog
        {
            Title = "Export settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"automation-launcher-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(_settings, BuildSettingsSerializerOptions());
            File.WriteAllText(dialog.FileName, json);
            SettingsStatusMessage = $"Settings exported to {dialog.FileName}";
            AddHistory("OK", "SettingsExported", SettingsStatusMessage);
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Settings export failed: {ex.Message}";
            AddHistory("ERROR", "SettingsExportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ImportSettings()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_sessionState.SettingsPassword))
        {
            return;
        }

        var password = _sessionState.SettingsPassword!;

        var dialog = new FileDialog
        {
            Title = "Import settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var importedSettings = JsonSerializer.Deserialize<AutomationLauncherSettings>(json, BuildSettingsSerializerOptions());
            if (importedSettings is null)
            {
                throw new InvalidOperationException("Imported settings file is empty or invalid.");
            }

            ApplyLoadedSettings(_settings, importedSettings);
            _protectedSettingsStore.Save(_settings, password);
            ReloadFromSettings();
            _ = RefreshFileLogsAsync(forceRefresh: true);
            SettingsStatusMessage = $"Settings imported from {dialog.FileName}";
            AddHistory("OK", "SettingsImported", SettingsStatusMessage);
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Settings import failed: {ex.Message}";
            AddHistory("ERROR", "SettingsImportFailed", ex.Message);
        }
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
        var snapshot = await Task.Run(GetOpennessAccessSnapshot);
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
                "Access check passed — User={User}, IsAdmin={IsAdmin}, Group={Group}, Member={IsMember}",
                snapshot.CurrentWindowsUser,
                snapshot.IsCurrentUserAdministrator,
                resolvedGroup,
                snapshot.IsCurrentUserInOpennessGroup);
        }
        else if (snapshot.IsOpennessGroupAvailable)
        {
            _opennessLog.Warning(
                "Access check: user is NOT a member — User={User}, IsAdmin={IsAdmin}, Group={Group}, Member={IsMember}",
                snapshot.CurrentWindowsUser,
                snapshot.IsCurrentUserAdministrator,
                resolvedGroup,
                snapshot.IsCurrentUserInOpennessGroup);
        }
        else
        {
            _opennessLog.Warning(
                "Access check: no Openness group found on this machine — User={User}, IsAdmin={IsAdmin}, Group={Group}",
                snapshot.CurrentWindowsUser,
                snapshot.IsCurrentUserAdministrator,
                resolvedGroup);
        }

        if (addHistory && !string.IsNullOrWhiteSpace(snapshot.HistoryCode) && !string.IsNullOrWhiteSpace(snapshot.HistoryMessage))
        {
            AddHistory(snapshot.HistoryLevel, snapshot.HistoryCode, snapshot.HistoryMessage!);
        }
    }

    private static OpennessAccessSnapshot GetOpennessAccessSnapshot()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var identityName = identity?.Name;
            var currentUser = !string.IsNullOrWhiteSpace(identityName)
                ? identityName!
                : Environment.UserName;

            var principal = identity is null ? null : new WindowsPrincipal(identity);
            var isAdministrator = principal?.IsInRole(WindowsBuiltInRole.Administrator) == true;
            var scopeSummary = BuildOpennessScopeSummary(currentUser);

            using var machineContext = new PrincipalContext(ContextType.Machine);
            var relatedGroups = DiscoverRelatedLocalGroups(machineContext);
            var (group, resolvedGroupName) = ResolveOpennessGroup(machineContext, relatedGroups);
            var discoverySummary = BuildLocalGroupDiscoverySummary(relatedGroups, group is not null, resolvedGroupName);

            var user = ResolveCurrentUserPrincipal(machineContext, currentUser, identity);
            var resolvedAccountSummary = BuildResolvedAccountSummary(currentUser, user, identity);

            if (group is null)
            {
                var missingMsg = $"No Openness group found. Searched for '{OpennessSecurityGroupName}' and any group containing 'Openness'. Confirm TIA Openness installation.";
                return new OpennessAccessSnapshot(
                    currentUser,
                    isAdministrator,
                    isOpennessGroupAvailable: false,
                    isCurrentUserInOpennessGroup: false,
                    opennessGroupStatus: missingMsg,
                    scopeSummary,
                    resolvedAccountSummary,
                    discoverySummary,
                    relatedGroups,
                    resolvedGroupName: string.Empty,
                    historyCode: "OpennessGroupMissing",
                    historyMessage: missingMsg,
                    historyLevel: "WARN");
            }

            if (user is null)
            {
                return new OpennessAccessSnapshot(
                    currentUser,
                    isAdministrator,
                    isOpennessGroupAvailable: true,
                    isCurrentUserInOpennessGroup: false,
                    opennessGroupStatus: "Could not resolve current Windows account in local principal context.",
                    scopeSummary,
                    resolvedAccountSummary,
                    discoverySummary,
                    relatedGroups,
                    resolvedGroupName,
                    historyCode: "OpennessUserResolveFailed",
                    historyMessage: "Could not resolve current Windows account in local principal context.",
                    historyLevel: "WARN");
            }

            var isMember = user.IsMemberOf(group);
            var status = isMember
                ? $"Access OK: current user belongs to '{resolvedGroupName}'."
                : $"Access missing: current user is not in '{resolvedGroupName}'.";

            return new OpennessAccessSnapshot(
                currentUser,
                isAdministrator,
                isOpennessGroupAvailable: true,
                isCurrentUserInOpennessGroup: isMember,
                opennessGroupStatus: status,
                scopeSummary,
                resolvedAccountSummary,
                discoverySummary,
                relatedGroups,
                resolvedGroupName,
                historyCode: isMember ? "OpennessAccessOk" : "OpennessAccessMissing",
                historyMessage: status,
                historyLevel: isMember ? "OK" : "WARN");
        }
        catch (PrincipalServerDownException ex)
        {
            return new OpennessAccessSnapshot(
                Environment.UserName,
                isCurrentUserAdministrator: false,
                isOpennessGroupAvailable: false,
                isCurrentUserInOpennessGroup: false,
                opennessGroupStatus: $"Local security account manager is not available: {ex.Message}",
                scopeSummary: "Target group scope: local machine only. The local security account manager was unavailable during the check.",
                resolvedAccountSummary: "Windows account could not be fully resolved because the principal server was unavailable.",
                discoverySummary: "Related local group discovery did not complete.",
                relatedGroups: Array.Empty<string>(),
                resolvedGroupName: "Unavailable",
                historyCode: "OpennessPrincipalServerDown",
                historyMessage: ex.Message,
                historyLevel: "ERROR");
        }
        catch (Exception ex)
        {
            return new OpennessAccessSnapshot(
                Environment.UserName,
                isCurrentUserAdministrator: false,
                isOpennessGroupAvailable: false,
                isCurrentUserInOpennessGroup: false,
                opennessGroupStatus: $"Openness group check failed: {ex.Message}",
                scopeSummary: "Target group scope: local machine only.",
                resolvedAccountSummary: "Windows account could not be fully resolved because the access check failed.",
                discoverySummary: "Related local group discovery did not complete.",
                relatedGroups: Array.Empty<string>(),
                resolvedGroupName: "Unavailable",
                historyCode: "OpennessCheckFailed",
                historyMessage: ex.Message,
                historyLevel: "ERROR");
        }
    }

    private static (GroupPrincipal? group, string resolvedGroupName) ResolveOpennessGroup(
        PrincipalContext machineContext, IReadOnlyList<string> relatedGroups)
    {
        // 1. Try the canonical group name first
        var exact = GroupPrincipal.FindByIdentity(machineContext, IdentityType.Name, OpennessSecurityGroupName)
            ?? GroupPrincipal.FindByIdentity(machineContext, OpennessSecurityGroupName);
        if (exact is not null)
            return (exact, OpennessSecurityGroupName);

        // 2. Fallback: the first discovered group whose name contains "Openness"
        var fallbackName = relatedGroups.FirstOrDefault(name =>
            name.IndexOf("Openness", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            var fallback = GroupPrincipal.FindByIdentity(machineContext, IdentityType.Name, fallbackName);
            if (fallback is not null)
                return (fallback, fallbackName!);
        }

        return (null, string.Empty);
    }

    private static UserPrincipal? ResolveCurrentUserPrincipal(PrincipalContext machineContext, string? identityName, WindowsIdentity? identity)
    {
        if (!string.IsNullOrWhiteSpace(identityName))
        {
            var byName = UserPrincipal.FindByIdentity(machineContext, IdentityType.Name, identityName);
            if (byName is not null)
            {
                return byName;
            }

            var bySam = UserPrincipal.FindByIdentity(machineContext, IdentityType.SamAccountName, identityName);
            if (bySam is not null)
            {
                return bySam;
            }

            var normalizedIdentityName = identityName!;
            var shortName = normalizedIdentityName.Contains('\\')
                ? normalizedIdentityName.Split('\\').LastOrDefault()
                : normalizedIdentityName;
            if (!string.IsNullOrWhiteSpace(shortName))
            {
                var byShortSam = UserPrincipal.FindByIdentity(machineContext, IdentityType.SamAccountName, shortName);
                if (byShortSam is not null)
                {
                    return byShortSam;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(Environment.UserName))
        {
            var byEnvironment = UserPrincipal.FindByIdentity(machineContext, IdentityType.SamAccountName, Environment.UserName);
            if (byEnvironment is not null)
            {
                return byEnvironment;
            }
        }

        var sid = identity?.User?.Value;
        if (!string.IsNullOrWhiteSpace(sid))
        {
            return UserPrincipal.FindByIdentity(machineContext, IdentityType.Sid, sid);
        }

        return null;
    }

    private static string BuildOpennessScopeSummary(string currentUser)
    {
        var authority = currentUser.Contains('\\')
            ? currentUser.Split('\\')[0]
            : Environment.MachineName;
        var accountScope = string.Equals(authority, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? "local machine account"
            : $"external account authority '{authority}'";

        return $"Target group scope: local machine only. The signed-in account is evaluated as {currentUser} ({accountScope}). Domain or AzureAD identities can still be members of the local machine group.";
    }

    private static string BuildResolvedAccountSummary(string currentUser, UserPrincipal? user, WindowsIdentity? identity)
    {
        var sid = identity?.User?.Value ?? "n/a";
        var authenticationType = string.IsNullOrWhiteSpace(identity?.AuthenticationType)
            ? "n/a"
            : identity!.AuthenticationType;

        if (user is null)
        {
            return $"WindowsIdentity resolved as {currentUser}. SID: {sid}. Authentication: {authenticationType}. The account could not be resolved inside the local machine principal context.";
        }

        var resolvedName = !string.IsNullOrWhiteSpace(user.SamAccountName)
            ? user.SamAccountName
            : user.Name ?? currentUser;
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? resolvedName
            : user.DisplayName;

        return $"WindowsIdentity resolved as {currentUser}. Local principal match: {displayName} ({resolvedName}). SID: {sid}. Authentication: {authenticationType}.";
    }

    private static IReadOnlyList<string> DiscoverRelatedLocalGroups(PrincipalContext machineContext)
    {
        var groups = new List<string>();
        using var query = new GroupPrincipal(machineContext);
        using var searcher = new PrincipalSearcher(query);

        foreach (var principal in searcher.FindAll())
        {
            using (principal)
            {
                if (principal is not GroupPrincipal group || string.IsNullOrWhiteSpace(group.Name))
                {
                    continue;
                }

                if (group.Name.IndexOf("Openness", StringComparison.OrdinalIgnoreCase) >= 0
                    || group.Name.IndexOf("Siemens", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    groups.Add(group.Name);
                }
            }
        }

        return groups
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => string.Equals(name, OpennessSecurityGroupName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildLocalGroupDiscoverySummary(IReadOnlyList<string> relatedGroups, bool targetGroupExists, string resolvedGroupName)
    {
        if (relatedGroups.Count == 0)
        {
            return "No local groups containing 'Openness' or 'Siemens' were found on this machine.";
        }

        var targetMessage = targetGroupExists
            ? $"Group used for check: '{resolvedGroupName}'."
            : "The exact target group is missing."
            ;

        return $"Found {relatedGroups.Count} local group(s) containing 'Openness' or 'Siemens'. {targetMessage}";
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
        _ = RefreshFileLogsAsync();
    }

    private void HandleFileLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(VisibleLogCount));
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

    private async Task RefreshFileLogsAsync(bool forceRefresh = false)
    {
        if (!await _fileLogRefreshSemaphore.WaitAsync(0))
        {
            _pendingFileLogRefresh = true;
            return;
        }

        try
        {
            do
            {
                _pendingFileLogRefresh = false;

                var refreshResult = await Task.Run(() => ReadLatestLogSnapshot(forceRefresh));
                if (refreshResult is null)
                {
                    continue;
                }

                if (refreshResult.ErrorMessage is not null)
                {
                    AddHistory("WARN", "LogReadFailed", refreshResult.ErrorMessage);
                }

                LoadedLogFilePath = refreshResult.LoadedLogFilePath;
                _lastLogSnapshotKey = refreshResult.SnapshotKey;

                _allFileLogLines.Clear();
                _allFileLogLines.AddRange(refreshResult.LogLines);

                await ApplyLogFilterAsync();
                forceRefresh = false;
            }
            while (_pendingFileLogRefresh);
        }
        finally
        {
            _fileLogRefreshSemaphore.Release();
        }
    }

    private async Task ApplyLogFilterAsync()
    {
        var snapshot = _allFileLogLines.ToArray();
        var searchTerm = (LogSearchText ?? string.Empty).Trim();
        var showErrorsAndWarningsOnlySnapshot = ShowErrorsAndWarningsOnly;

        var filteredEntries = await Task.Run(() => BuildFilteredLogEntries(snapshot, searchTerm, showErrorsAndWarningsOnlySnapshot));
        FileLogs.ReplaceRange(filteredEntries);
    }

    private LogRefreshResult? ReadLatestLogSnapshot(bool forceRefresh)
    {
        var logDirectoryPath = ResolveEffectiveLogDirectory();
        if (!Directory.Exists(logDirectoryPath))
        {
            return LogRefreshResult.Empty("No log file loaded.");
        }

        var logFiles = Directory.GetFiles(logDirectoryPath, "automation-launcher-*.log");
        if (logFiles.Length == 0)
        {
            return LogRefreshResult.Empty("No log file loaded.");
        }

        var activeLogFilePath = GetNewestLogFilePath(logFiles);

        var snapshotKey = activeLogFilePath;
        try
        {
            var info = new FileInfo(activeLogFilePath);
            snapshotKey = $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            // Keep path-only snapshot key when metadata cannot be read.
        }

        if (!forceRefresh && string.Equals(snapshotKey, _lastLogSnapshotKey, StringComparison.Ordinal))
        {
            return null;
        }

        var logLines = new Queue<string>();
        string? errorMessage = null;
        try
        {
            foreach (var line in ReadSharedLogLines(activeLogFilePath))
            {
                logLines.Enqueue(line);
                while (logLines.Count > MaxDisplayedLogLines)
                {
                    _ = logLines.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Unable to read active log file: {ex.Message}";
        }

        return new LogRefreshResult(activeLogFilePath, snapshotKey, logLines.ToArray(), errorMessage);
    }

    private static IReadOnlyList<LogLineEntry> BuildFilteredLogEntries(IReadOnlyList<string> logLines, string searchTerm, bool showErrorsAndWarningsOnly)
    {
        var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
        var filteredEntries = new List<LogLineEntry>(logLines.Count);

        for (var index = logLines.Count - 1; index >= 0; index--)
        {
            var logLine = logLines[index];
            var level = ExtractLogLevel(logLine);
            if (showErrorsAndWarningsOnly && level is not ("ERR" or "FTL" or "WRN"))
            {
                continue;
            }

            if (hasSearchTerm && logLine.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            filteredEntries.Add(new LogLineEntry(logLine, level, hasSearchTerm));
        }

        return filteredEntries;
    }

    private static string ExtractLogLevel(string logLine)
    {
        var openBracketIndex = logLine.IndexOf('[');
        if (openBracketIndex < 0)
        {
            return "N/A";
        }

        var closeBracketIndex = logLine.IndexOf(']', openBracketIndex + 1);
        if (closeBracketIndex < 0)
        {
            return "N/A";
        }

        var tokenLength = closeBracketIndex - openBracketIndex - 1;
        if (tokenLength != 3)
        {
            return "N/A";
        }

        return logLine.Substring(openBracketIndex + 1, tokenLength).ToUpperInvariant();
    }

    private static string GetNewestLogFilePath(IEnumerable<string> logFiles)
    {
        string? newestPath = null;
        var newestTimestamp = DateTime.MinValue;

        foreach (var logFile in logFiles)
        {
            DateTime candidateTimestamp;
            try
            {
                candidateTimestamp = File.GetLastWriteTimeUtc(logFile);
            }
            catch
            {
                candidateTimestamp = DateTime.MinValue;
            }

            if (newestPath is null || candidateTimestamp > newestTimestamp)
            {
                newestPath = logFile;
                newestTimestamp = candidateTimestamp;
            }
        }

        return newestPath ?? string.Empty;
    }

    private static IEnumerable<string> ReadSharedLogLines(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is not null)
            {
                yield return line;
            }
        }
    }

    private static JsonSerializerOptions BuildSettingsSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    private static void ApplyLoadedSettings(AutomationLauncherSettings target, AutomationLauncherSettings source)
    {
        target.Archive = source.Archive ?? new ArchiveOptions();
        target.Startup = source.Startup ?? new StartupSettings();
        target.Logging = source.Logging ?? new LoggingSettings();
        target.Ui = source.Ui ?? new UiSettings();
    }

    private ArchiveBackupFlow ParseArchiveBackupFlow()
    {
        return Enum.TryParse<ArchiveBackupFlow>(SelectedArchiveBackupFlow, ignoreCase: true, out var flow)
            ? flow
            : ArchiveBackupFlow.TimestampedRetention;
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

    public void SetErrorControlFilePresent(bool isPresent)
    {
        HasErrorControlFile = isPresent;
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
