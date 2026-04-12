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
    private const int MaxDisplayedLogLines = 5000;
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
    private bool isExpectedProjectPathManualEditEnabled = true;

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
        ControlFilesFolderPath = ResolveControlFilesDirectory(_settings.Ui.ControlFilesDirectory);
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

    public string OpennessGroupName => OpennessAccessChecker.GroupName;

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

    private static string ResolveControlFilesDirectory(string? configuredDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredDirectory)
            ? AppContext.BaseDirectory
            : configuredDirectory.Trim();

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }
}
