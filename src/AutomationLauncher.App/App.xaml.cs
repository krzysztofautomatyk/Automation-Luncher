using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private enum TrayIndicatorMode
    {
        None,
        Startup,
        StopPending,
        Archiving
    }

    private static Mutex? _singleInstanceMutex;
    private IHost? _host;
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _settingsMenuItem;
    private ToolStripMenuItem? _checkTiaConnectionMenuItem;
    private ToolStripMenuItem? _archiveNowMenuItem;
    private ToolStripMenuItem? _runStartupAutomationMenuItem;
    private ToolStripMenuItem? _runManagedApplicationsMenuItem;
    private ToolStripMenuItem? _stopManagedApplicationsMenuItem;
    private ToolStripMenuItem? _openAutostartFolderMenuItem;
    private ToolStripMenuItem? _openControlFilesFolderMenuItem;
    private ToolStripMenuItem? _openLogFolderMenuItem;
    private ToolStripMenuItem? _loginMenuItem;
    private ToolStripMenuItem? _logoutMenuItem;
    private MainWindow? _mainWindow;
    private readonly AppSessionState _sessionState = new();
    private readonly object _startupProcessesSyncRoot = new();
    private readonly List<Process> _startupLaunchedProcesses = new();
    private DispatcherTimer? _sessionTimer;
    private DispatcherTimer? _startupIndicatorTimer;
    private DispatcherTimer? _startupControlFileTimer;
    private ISessionCoordinator? _sessionCoordinator;
    private CancellationTokenSource? _startupSequenceCancellationSource;
    private bool _isStartupSequenceRunning;
    private bool _startupIndicatorUsesWarningIcon;
    private HostControlState _hostControlState = HostControlState.Ready;
    private TrayIndicatorMode _trayIndicatorMode;
    private bool _launchedFromWindowsStartup;
    private bool _isHandlingControlSignal;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Logger.Error(args.Exception, "Unhandled UI exception");
            ReportFatalError($"Critical error: {args.Exception.Message}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Logger.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");
            ReportFatalError("A fatal application error occurred. Automation Launcher will close.");
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        _launchedFromWindowsStartup = e.Args.Any(arg => string.Equals(arg, AppLaunchArguments.StartupLaunch, StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(true, "Global\\AutomationLauncher.Singleton", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("AutomationLauncher is already running.", "AutomationLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var configuration = BuildConfiguration();
        var settings = configuration.Get<AutomationLauncherSettings>() ?? new AutomationLauncherSettings();
        var settingsStore = new ProtectedApplicationSettingsStore(GetProtectedSettingsFilePath());

        if (settingsStore.TryLoadCachedSettings(out var cachedSettings, out _))
        {
            ApplyLoadedSettings(settings, cachedSettings!);
        }

        DeleteLegacyUserSettingsFile(settingsStore);
        Log.Logger = BuildLogger(settings);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(configuration);
                services.AddSingleton(settings);
                services.AddSingleton(_sessionState);
                services.AddSingleton<IProtectedApplicationSettingsStore>(settingsStore);
                services.AddSingleton<ISessionCoordinator, SessionCoordinator>();
                services.AddSingleton<IAutostartService, StartupScriptAutostartService>();
                services.AddSingleton<IStartupSequenceRunner, StartupSequenceRunner>();
                services.AddInfrastructure(settings.Archive, Log.Logger);
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<AboutWindow>();
                services.AddTransient<SettingsWindow>();
                services.AddTransient<StartupSequenceSplashWindow>();
            })
            .Build();

        await _host.StartAsync();

        _mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = _mainWindow;
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        viewModel.ArchiveWorkflowStateChanged += HandleArchiveWorkflowStateChanged;
        _sessionCoordinator = _host.Services.GetRequiredService<ISessionCoordinator>();
        _sessionCoordinator.RegisterActivity();
        _sessionCoordinator.SessionStateChanged += HandleSessionStateChanged;
        StartSessionTimer();
        InputManager.Current.PreProcessInput += HandlePreProcessInput;
        InitializeTrayIcon();

        InitializeHostControlFlow();

        if (!settings.Ui.StartHiddenToTray)
        {
            _mainWindow.ShowDashboard();
        }

        await RunStartupSequenceIfRequiredAsync(e.Args, settings);

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_sessionTimer is not null)
        {
            _sessionTimer.Stop();
            _sessionTimer.Tick -= HandleSessionTimerTick;
        }

        if (_startupControlFileTimer is not null)
        {
            _startupControlFileTimer.Stop();
            _startupControlFileTimer.Tick -= HandleStartupControlFileTimerTick;
        }

        InputManager.Current.PreProcessInput -= HandlePreProcessInput;

        if (_sessionCoordinator is not null)
        {
            _sessionCoordinator.SessionStateChanged -= HandleSessionStateChanged;
        }

        if (_host?.Services.GetService<MainWindowViewModel>() is MainWindowViewModel viewModel)
        {
            viewModel.ArchiveWorkflowStateChanged -= HandleArchiveWorkflowStateChanged;
        }

        _startupSequenceCancellationSource?.Cancel();
        _startupSequenceCancellationSource?.Dispose();
        DisposeTrackedStartupProcesses();
        ClearAllHostControlFiles();

        _mainWindow?.PrepareForExit();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(GetLegacyUserSettingsFilePath(), optional: true, reloadOnChange: true)
            .Build();
    }

    private static string GetLegacyUserSettingsFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutomationLauncher",
            "user-settings.json");
    }

    private static string GetProtectedSettingsFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutomationLauncher",
            "protected-settings.json");
    }

    private static void ApplyLoadedSettings(AutomationLauncherSettings target, AutomationLauncherSettings source)
    {
        target.Archive = source.Archive ?? new ArchiveOptions();
        target.Startup = source.Startup ?? new StartupSettings();
        target.Logging = source.Logging ?? new LoggingSettings();
        target.Ui = source.Ui ?? new UiSettings();
    }

    private static void DeleteLegacyUserSettingsFile(IProtectedApplicationSettingsStore settingsStore)
    {
        var legacyPath = GetLegacyUserSettingsFilePath();
        if (!File.Exists(legacyPath))
        {
            return;
        }

        if (!settingsStore.HasProtectedSettings() && !File.Exists(settingsStore.CachedSettingsFilePath))
        {
            return;
        }

        try
        {
            File.Delete(legacyPath);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Unable to delete legacy user settings file");
        }
    }

    private static ILogger BuildLogger(AutomationLauncherSettings settings)
    {
        var logDirectory = ResolveWritableLogDirectory(settings.Logging.DirectoryPath);

        var minimumLevel = ParseLogLevel(settings.Logging.MinimumLevel);

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "AutomationLauncher")
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .WriteTo.Console(restrictedToMinimumLevel: minimumLevel)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "automation-launcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, settings.Logging.RetainedFileCountLimit),
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static string ResolveWritableLogDirectory(string configuredDirectory)
    {
        var preferredDirectory = LogPathHelper.ResolveDirectory(configuredDirectory);
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

    private static LogEventLevel ParseLogLevel(string configuredLevel)
    {
        foreach (var level in Enum.GetValues(typeof(LogEventLevel)).Cast<LogEventLevel>())
        {
            if (string.Equals(level.ToString(), configuredLevel, StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        return LogEventLevel.Information;
    }

    private void InitializeTrayIcon()
    {
        if (_host is null)
        {
            return;
        }

        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => ShowMainWindow());
        _settingsMenuItem = new ToolStripMenuItem("Settings", null, (_, _) => ShowSettingsDialog());
        menu.Items.Add(_settingsMenuItem);
        menu.Items.Add("About", null, (_, _) => ShowAboutDialog());
        _checkTiaConnectionMenuItem = new ToolStripMenuItem("Check TIA connection", null, (_, _) => viewModel.CheckTiaConnectionCommand.Execute(null));
        menu.Items.Add(_checkTiaConnectionMenuItem);
        _archiveNowMenuItem = new ToolStripMenuItem("Create archive now", null, (_, _) => viewModel.ArchiveCommand.Execute(null));
        menu.Items.Add(_archiveNowMenuItem);
        _runStartupAutomationMenuItem = new ToolStripMenuItem("Run startup automation now", null, async (_, _) => await RunStartupSequenceManuallyAsync());
        menu.Items.Add(_runStartupAutomationMenuItem);
        _runManagedApplicationsMenuItem = new ToolStripMenuItem("Run managed applications", null, async (_, _) => await RunManagedApplicationsFromMenuAsync());
        menu.Items.Add(_runManagedApplicationsMenuItem);
        _stopManagedApplicationsMenuItem = new ToolStripMenuItem("Stop managed applications", null, async (_, _) => await StopManagedApplicationsFromMenuAsync());
        menu.Items.Add(_stopManagedApplicationsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        _openAutostartFolderMenuItem = new ToolStripMenuItem("Open autostart folder", null, (_, _) => viewModel.OpenStartupFolderCommand.Execute(null));
        menu.Items.Add(_openAutostartFolderMenuItem);
        _openControlFilesFolderMenuItem = new ToolStripMenuItem("Open control files folder", null, (_, _) => viewModel.OpenControlFilesFolderCommand.Execute(null));
        menu.Items.Add(_openControlFilesFolderMenuItem);
        _openLogFolderMenuItem = new ToolStripMenuItem("Open log folder", null, (_, _) => viewModel.OpenLogDirectoryCommand.Execute(null));
        menu.Items.Add(_openLogFolderMenuItem);
        _loginMenuItem = new ToolStripMenuItem("Log in", null, (_, _) => LoginSession());
        menu.Items.Add(_loginMenuItem);
        _logoutMenuItem = new ToolStripMenuItem("Log out", null, (_, _) => LogoutSession("Session locked by user.", false));
        menu.Items.Add(_logoutMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        _notifyIcon = new NotifyIcon
        {
            Text = $"Automation Launcher {AppVersionInfo.DisplayVersion}",
            Icon = AppIconFactory.GetTrayIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayMenuState();
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= _host?.Services.GetRequiredService<MainWindow>();
        _mainWindow?.ShowDashboard();
    }

    private void ShowSettingsDialog()
    {
        if (_host is null)
        {
            return;
        }

        if (_sessionCoordinator is not null && !_sessionCoordinator.EnsureAuthenticated(_mainWindow))
        {
            return;
        }

        var window = _host.Services.GetRequiredService<SettingsWindow>();
        if (_mainWindow is not null && _mainWindow.IsLoaded && _mainWindow.IsVisible)
        {
            window.Owner = _mainWindow;
        }

        window.ShowDialog();
    }

    public void OpenSettingsFromDashboard()
    {
        ShowSettingsDialog();
    }

    public void OpenAboutFromDashboard()
    {
        ShowAboutDialog();
    }

    public async Task RunStartupAutomationFromDashboardAsync()
    {
        await RunStartupSequenceManuallyAsync();
    }

    public async Task RunManagedApplicationsFromMenuAsync()
    {
        if (_sessionCoordinator?.IsAuthenticated != true)
        {
            return;
        }

        await HandleStartControlFileDetectedAsync();
    }

    public async Task StopManagedApplicationsFromMenuAsync()
    {
        if (_sessionCoordinator?.IsAuthenticated != true)
        {
            return;
        }

        await HandleStopControlFileDetectedAsync();
    }

    public void ExitFromDashboard()
    {
        ExitFromTray();
    }

    public void LoginFromDashboard()
    {
        LoginSession();
    }

    private void ShowAboutDialog()
    {
        if (_host is null)
        {
            return;
        }

        var window = _host.Services.GetRequiredService<AboutWindow>();
        if (_mainWindow is not null && _mainWindow.IsLoaded && _mainWindow.IsVisible)
        {
            window.Owner = _mainWindow;
        }

        window.ShowDialog();
    }

    private void ExitFromTray()
    {
        if (!ConfirmApplicationExit())
        {
            return;
        }

        _startupSequenceCancellationSource?.Cancel();
        _mainWindow?.PrepareForExit();
        Shutdown();
    }

    private async Task RunStartupSequenceIfRequiredAsync(string[] startupArgs, AutomationLauncherSettings settings)
    {
        if (!_launchedFromWindowsStartup || !settings.Startup.RunOnWindowsStartup || !settings.Startup.RunSequenceOnWindowsStartup)
        {
            return;
        }

        await RunStartupSequenceAsync(settings, "Preparing startup automation...");
    }

    private async Task RunStartupSequenceManuallyAsync()
    {
        if (_host is null)
        {
            return;
        }

        if (_sessionCoordinator?.IsAuthenticated != true)
        {
            return;
        }

        var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
        await RunStartupSequenceAsync(settings, "Preparing manual startup automation...");
    }

    private async Task RunStartupSequenceAsync(AutomationLauncherSettings settings, string initialStatus)
    {
        if (_host is null)
        {
            return;
        }

        if (_isStartupSequenceRunning)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Startup automation is already running.", ToolTipIcon.Info);
            return;
        }

        if (_hostControlState == HostControlState.Running || _hostControlState == HostControlState.Stopping)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Managed applications are already active. Stop them before starting a new sequence.", ToolTipIcon.Info);
            return;
        }

        var entries = settings.Startup.SequenceEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ExecutablePath))
            .Select(entry => entry.Clone())
            .ToList();

        if (entries.Count == 0)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "No startup automation items are configured.", ToolTipIcon.Warning);
            return;
        }

        var splashWindow = _host.Services.GetRequiredService<StartupSequenceSplashWindow>();
        var runner = _host.Services.GetRequiredService<IStartupSequenceRunner>();
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        var completedSuccessfully = false;
        _startupSequenceCancellationSource = new CancellationTokenSource();
        _isStartupSequenceRunning = true;
        viewModel.SetStartupAutomationRunning(true);
        TransitionHostControlState(HostControlState.Running, "Startup automation sequence started.");
        StartStartupIndicator();
        UpdateTrayMenuState();

        splashWindow.SetApplicationTitle("Automation Launcher");
        splashWindow.SetBackgroundImage(settings.Startup.SplashBackgroundImagePath);
        splashWindow.ConfigureActions(showConfirmAction: true, confirmButtonText: "Start now", cancelButtonText: "Cancel startup");
        splashWindow.ConfigureConfirmDialog("Start startup automation immediately?");
        splashWindow.ConfigureCancelDialog(
            "Cancel startup automation",
            "Provide a reason for cancelling the startup automation:");
        var startImmediatelyRequested = false;
        var isStartupCancellationDialogOpen = false;

        void HandleStartupCancellationDialogOpened(object? sender, EventArgs e)
        {
            isStartupCancellationDialogOpen = true;
        }

        void HandleStartupCancellationDialogClosed(object? sender, EventArgs e)
        {
            isStartupCancellationDialogOpen = false;
        }

        void HandleStartupSplashConfirmRequested(object? sender, EventArgs e)
        {
            startImmediatelyRequested = true;
        }

        splashWindow.CancelRequested += HandleStartupSplashCancelRequested;
        splashWindow.ConfirmRequested += HandleStartupSplashConfirmRequested;
        splashWindow.CancellationDialogOpened += HandleStartupCancellationDialogOpened;
        splashWindow.CancellationDialogClosed += HandleStartupCancellationDialogClosed;
        splashWindow.Show();

        try
        {
            for (var remainingSeconds = 10; remainingSeconds > 0; remainingSeconds--)
            {
                splashWindow.SetStatus($"Startup automation begins in {remainingSeconds}s. Click Start now to run immediately.");
                var elapsedMilliseconds = 0;

                while (elapsedMilliseconds < 1000)
                {
                    if (startImmediatelyRequested)
                    {
                        break;
                    }

                    if (_startupSequenceCancellationSource.Token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(_startupSequenceCancellationSource.Token);
                    }

                    await Task.Delay(100);

                    if (isStartupCancellationDialogOpen)
                    {
                        continue;
                    }

                    elapsedMilliseconds += 100;
                }

                if (startImmediatelyRequested)
                {
                    break;
                }
            }

            if (_startupSequenceCancellationSource.Token.IsCancellationRequested)
            {
                throw new OperationCanceledException(_startupSequenceCancellationSource.Token);
            }

            splashWindow.ConfigureActions(showConfirmAction: false, confirmButtonText: null, cancelButtonText: "Cancel startup");
            splashWindow.SetStatus(initialStatus);

            var result = await runner.RunAsync(
                entries,
                splashWindow,
                _startupSequenceCancellationSource.Token,
                TrackStartupProcess);
            completedSuccessfully = true;
            splashWindow.SetStatus(result.Message);
            await Task.Delay(900);

            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", result.Message, ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            await StopTrackedStartupProcessesAsync();
            TransitionHostControlState(HostControlState.Ready, "Startup automation was cancelled.");
            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", "Startup automation was cancelled.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            await StopTrackedStartupProcessesAsync();
            TransitionHostControlState(HostControlState.Error, $"Startup automation failed: {ex.Message}");
            Log.Logger.Error(ex, "Startup automation failed");
            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", $"Startup automation failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            splashWindow.CancelRequested -= HandleStartupSplashCancelRequested;
            splashWindow.ConfirmRequested -= HandleStartupSplashConfirmRequested;
            splashWindow.CancellationDialogOpened -= HandleStartupCancellationDialogOpened;
            splashWindow.CancellationDialogClosed -= HandleStartupCancellationDialogClosed;
            splashWindow.Close();
            _startupSequenceCancellationSource?.Dispose();
            _startupSequenceCancellationSource = null;
            _isStartupSequenceRunning = false;
            viewModel.SetStartupAutomationRunning(false);
            StopStartupIndicator();

            if (completedSuccessfully && _hostControlState != HostControlState.Running)
            {
                TransitionHostControlState(HostControlState.Running, "Startup automation completed successfully.");
            }

            UpdateTrayMenuState();
        }
    }

    private void UpdateTrayMenuState()
    {
        var isAuthenticated = _sessionCoordinator?.IsAuthenticated == true;

        if (_settingsMenuItem is not null)
        {
            _settingsMenuItem.Enabled = true;
        }

        if (_checkTiaConnectionMenuItem is not null)
        {
            _checkTiaConnectionMenuItem.Enabled = isAuthenticated;
        }

        if (_archiveNowMenuItem is not null)
        {
            _archiveNowMenuItem.Enabled = isAuthenticated && _hostControlState != HostControlState.Stopping && !_isStartupSequenceRunning;
        }

        if (_runStartupAutomationMenuItem is not null)
        {
            _runStartupAutomationMenuItem.Enabled = isAuthenticated && !_isStartupSequenceRunning;
        }

        if (_runManagedApplicationsMenuItem is not null)
        {
            _runManagedApplicationsMenuItem.Enabled = isAuthenticated
                && !_isStartupSequenceRunning
                && _hostControlState != HostControlState.Running
                && _hostControlState != HostControlState.Stopping;
        }

        if (_stopManagedApplicationsMenuItem is not null)
        {
            _stopManagedApplicationsMenuItem.Enabled = isAuthenticated
                && (_hostControlState == HostControlState.Running || _isStartupSequenceRunning);
        }

        if (_openAutostartFolderMenuItem is not null)
        {
            _openAutostartFolderMenuItem.Enabled = isAuthenticated;
        }

        if (_openControlFilesFolderMenuItem is not null)
        {
            _openControlFilesFolderMenuItem.Enabled = isAuthenticated;
        }

        if (_openLogFolderMenuItem is not null)
        {
            _openLogFolderMenuItem.Enabled = isAuthenticated;
        }

        if (_loginMenuItem is not null)
        {
            _loginMenuItem.Enabled = !isAuthenticated;
        }

        if (_logoutMenuItem is not null)
        {
            _logoutMenuItem.Enabled = isAuthenticated;
        }
    }

    private void StartSessionTimer()
    {
        _sessionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _sessionTimer.Tick += HandleSessionTimerTick;
        _sessionTimer.Start();
    }

    private void HandleSessionTimerTick(object? sender, EventArgs e)
    {
        if (_sessionCoordinator?.HasTimedOut() == true)
        {
            LogoutSession("Session locked after 5 minutes of inactivity.", true);
        }
    }

    private void HandlePreProcessInput(object? sender, PreProcessInputEventArgs e)
    {
        if (_sessionCoordinator is null || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        if (e.StagingItem.Input is System.Windows.Input.MouseEventArgs or System.Windows.Input.KeyboardEventArgs)
        {
            _sessionCoordinator.RegisterActivity();
        }
    }

    private void LogoutSession(string reason, bool isAutomatic)
    {
        _sessionCoordinator?.Logout(reason, isAutomatic);
    }

    private void LoginSession()
    {
        if (_sessionCoordinator is null || _sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        _sessionCoordinator.EnsureAuthenticated(_mainWindow);
        UpdateTrayMenuState();
    }

    private void HandleSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (!e.IsAuthenticated)
        {
            foreach (var settingsWindow in Windows.OfType<Window>().Where(window => window is SettingsWindow).ToList())
            {
                settingsWindow.Close();
            }

            if (_notifyIcon is not null)
            {
                _notifyIcon.ShowBalloonTip(3000, "Automation Launcher", e.Message, ToolTipIcon.Info);
            }

            UpdateTrayMenuState();

            return;
        }

        _sessionCoordinator?.RegisterActivity();
        UpdateTrayMenuState();
    }

    private void HandleStartupSplashCancelRequested(object? sender, StartupSplashCancelRequestedEventArgs e)
    {
        Log.Logger.Warning("Startup automation cancellation requested by user. Reason: {CancellationReason}", e.Reason);
        _startupSequenceCancellationSource?.Cancel();
    }

    private bool ConfirmApplicationExit()
    {
        var message = _isStartupSequenceRunning
            ? "A startup automation sequence is currently running. Do you want to cancel it and exit Automation Launcher?"
            : "Do you want to exit Automation Launcher?";

        return System.Windows.MessageBox.Show(
                message,
                "AutomationLauncher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            == MessageBoxResult.Yes;
    }

    private void ReportFatalError(string message)
    {
        try
        {
            System.Windows.MessageBox.Show(message, "AutomationLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                Current?.Dispatcher.BeginInvoke(new Action(() => Shutdown(-1)));
            }
            catch
            {
                Environment.Exit(-1);
            }
        }
    }

    private void StartStartupIndicator()
    {
        SetTrayIndicatorMode(TrayIndicatorMode.Startup);
    }

    private void SetTrayIndicatorMode(TrayIndicatorMode mode)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _trayIndicatorMode = mode;

        if (mode == TrayIndicatorMode.None)
        {
            if (_startupIndicatorTimer is not null)
            {
                _startupIndicatorTimer.Stop();
                _startupIndicatorTimer.Tick -= HandleStartupIndicatorTick;
            }

            _startupIndicatorUsesWarningIcon = false;
            _notifyIcon.Icon = AppIconFactory.GetTrayIcon();
            return;
        }

        _startupIndicatorTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _startupIndicatorTimer.Tick -= HandleStartupIndicatorTick;
        _startupIndicatorTimer.Tick += HandleStartupIndicatorTick;
        _startupIndicatorUsesWarningIcon = true;
        _notifyIcon.Icon = GetIndicatorIconForCurrentMode();
        _startupIndicatorTimer.Start();
    }

    private void StopStartupIndicator()
    {
        if (_trayIndicatorMode == TrayIndicatorMode.Startup)
        {
            SetTrayIndicatorMode(TrayIndicatorMode.None);
        }
    }

    private void HandleStartupIndicatorTick(object? sender, EventArgs e)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Icon = _startupIndicatorUsesWarningIcon
            ? AppIconFactory.GetTrayIcon()
            : GetIndicatorIconForCurrentMode();
        _startupIndicatorUsesWarningIcon = !_startupIndicatorUsesWarningIcon;
    }

    private Icon GetIndicatorIconForCurrentMode()
    {
        return _trayIndicatorMode switch
        {
            TrayIndicatorMode.Archiving => AppIconFactory.GetArchiveTrayIcon(),
            TrayIndicatorMode.StopPending => AppIconFactory.GetStopTrayIcon(),
            _ => AppIconFactory.GetStartupTrayIcon()
        };
    }

    private void HandleArchiveWorkflowStateChanged(object? sender, ArchiveWorkflowStateChangedEventArgs e)
    {
        SetTrayIndicatorMode(e.IsRunning ? TrayIndicatorMode.Archiving : GetPreferredTrayIndicatorMode());
        UpdateTrayMenuState();
    }

    private TrayIndicatorMode GetPreferredTrayIndicatorMode()
    {
        if (_hostControlState == HostControlState.Stopping)
        {
            return TrayIndicatorMode.StopPending;
        }

        if (_isStartupSequenceRunning)
        {
            return TrayIndicatorMode.Startup;
        }

        return TrayIndicatorMode.None;
    }

    private void InitializeHostControlFlow()
    {
        DeleteControlCommandFiles();
        NormalizeHostControlState();
        StartStartupControlFileMonitor();
    }

    private void StartStartupControlFileMonitor()
    {
        _startupControlFileTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };

        _startupControlFileTimer.Tick -= HandleStartupControlFileTimerTick;
        _startupControlFileTimer.Tick += HandleStartupControlFileTimerTick;
        _startupControlFileTimer.Start();
    }

    private async void HandleStartupControlFileTimerTick(object? sender, EventArgs e)
    {
        if (_isHandlingControlSignal)
        {
            return;
        }

        var startFilePath = GetControlFilePath("start");
        if (File.Exists(startFilePath))
        {
            Log.Logger.Information("Detected start control file at {ControlFilePath}", startFilePath);

            _isHandlingControlSignal = true;
            try
            {
                DeleteControlFile(startFilePath);
                await HandleStartControlFileDetectedAsync();
            }
            finally
            {
                _isHandlingControlSignal = false;
            }

            return;
        }

        var stopFilePath = GetControlFilePath("stop");
        if (File.Exists(stopFilePath))
        {
            Log.Logger.Information("Detected stop control file at {ControlFilePath}", stopFilePath);

            _isHandlingControlSignal = true;
            try
            {
                DeleteControlFile(stopFilePath);
                await HandleStopControlFileDetectedAsync();
            }
            finally
            {
                _isHandlingControlSignal = false;
            }

            return;
        }

        var makeArchiveFilePath = GetControlFilePath("makearchive");
        if (!File.Exists(makeArchiveFilePath))
        {
            return;
        }

        Log.Logger.Information("Detected makearchive control file at {ControlFilePath}", makeArchiveFilePath);

        _isHandlingControlSignal = true;
        try
        {
            DeleteControlFile(makeArchiveFilePath);
            await HandleMakeArchiveControlFileDetectedAsync();
        }
        finally
        {
            _isHandlingControlSignal = false;
        }
    }

    private async Task HandleStartControlFileDetectedAsync()
    {
        if (_host is null)
        {
            return;
        }

        if (_isStartupSequenceRunning || _hostControlState == HostControlState.Running || _hostControlState == HostControlState.Stopping)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Start command detected, but startup automation is already running.", ToolTipIcon.Info);
            return;
        }

        var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Start command detected. Running startup automation.", ToolTipIcon.Info);
        await RunStartupSequenceAsync(settings, "Preparing startup automation from control file...");
    }

    private async Task HandleStopControlFileDetectedAsync()
    {
        if (_hostControlState != HostControlState.Running && !_isStartupSequenceRunning)
        {
            Log.Logger.Information("Stop command ignored because the launcher is not in the running state.");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command ignored because no managed runtime is currently active.", ToolTipIcon.Info);
            return;
        }

        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command detected. Waiting 60 seconds before stopping startup applications.", ToolTipIcon.Info);
        SetTrayIndicatorMode(TrayIndicatorMode.StopPending);
        TransitionHostControlState(HostControlState.Stopping, "Stop command accepted.");

        if (!await ConfirmStopSequenceAsync())
        {
            TransitionHostControlState(HostControlState.Running, "Stop command cancelled by user.");
            SetTrayIndicatorMode(_isStartupSequenceRunning ? TrayIndicatorMode.Startup : TrayIndicatorMode.None);
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command was cancelled. Startup applications continue running.", ToolTipIcon.Info);
            return;
        }

        if (_isStartupSequenceRunning)
        {
            _startupSequenceCancellationSource?.Cancel();
            await WaitForStartupSequenceToStopAsync();
        }

        await StopTrackedStartupProcessesAsync();
        TransitionHostControlState(HostControlState.Ready, "Managed applications were stopped.");
        SetTrayIndicatorMode(TrayIndicatorMode.None);
        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Startup applications were stopped. Ready marker created.", ToolTipIcon.Info);
    }

    private async Task HandleMakeArchiveControlFileDetectedAsync()
    {
        if (_host is null)
        {
            return;
        }

        if (_host.Services.GetRequiredService<MainWindowViewModel>() is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsBusy)
        {
            Log.Logger.Information("Makearchive command ignored because the launcher is already busy.");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive command ignored because another operation is already running.", ToolTipIcon.Info);
            return;
        }

        DeleteControlFile(GetControlFilePath("archivecreated"));
        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Makearchive command detected. Starting archive workflow.", ToolTipIcon.Info);
        var archiveCreated = await viewModel.RunArchiveFromControlFileWithResultAsync();

        if (archiveCreated)
        {
            WriteControlFile(GetControlFilePath("archivecreated"));
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive created successfully. Archive marker file written.", ToolTipIcon.Info);
        }
    }

    private async Task<bool> ConfirmStopSequenceAsync()
    {
        if (_host is null)
        {
            return true;
        }

        var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
        var splashWindow = _host.Services.GetRequiredService<StartupSequenceSplashWindow>();
        var requestedImmediateStop = false;
        var requestedKeepRunning = false;
        var isCancellationDialogOpen = false;

        void HandleCancellationDialogOpened(object? sender, EventArgs e)
        {
            isCancellationDialogOpen = true;
        }

        void HandleCancellationDialogClosed(object? sender, EventArgs e)
        {
            isCancellationDialogOpen = false;
        }

        void HandleCancelRequested(object? sender, StartupSplashCancelRequestedEventArgs e)
        {
            requestedKeepRunning = true;
            Log.Logger.Information("Stop sequence request was cancelled by user. Reason: {CancellationReason}", e.Reason);
        }

        void HandleConfirmRequested(object? sender, EventArgs e)
        {
            requestedImmediateStop = true;
        }

        splashWindow.SetApplicationTitle("Automation Launcher");
        splashWindow.SetBackgroundImage(settings.Startup.SplashBackgroundImagePath);
        splashWindow.ConfigureActions(showConfirmAction: true, confirmButtonText: "Stop now", cancelButtonText: "Keep running");
        splashWindow.ConfigureConfirmDialog("Stop startup applications now without waiting for the countdown?");
        splashWindow.ConfigureCancelDialog(
            "Keep startup applications running",
            "Provide a reason why startup applications should keep running:");
        splashWindow.CancelRequested += HandleCancelRequested;
        splashWindow.ConfirmRequested += HandleConfirmRequested;
        splashWindow.CancellationDialogOpened += HandleCancellationDialogOpened;
        splashWindow.CancellationDialogClosed += HandleCancellationDialogClosed;
        splashWindow.Show();

        try
        {
            for (var remainingSeconds = 60; remainingSeconds > 0; remainingSeconds--)
            {
                splashWindow.SetStatus($"Stop requested. Startup applications will be stopped in {remainingSeconds}s. Click Cancel to keep them running.");
                var elapsedMilliseconds = 0;

                while (elapsedMilliseconds < 1000)
                {
                    if (requestedImmediateStop)
                    {
                        splashWindow.SetStatus("Stopping startup applications immediately...");
                        await Task.Delay(250);
                        return true;
                    }

                    if (requestedKeepRunning)
                    {
                        return false;
                    }

                    await Task.Delay(100);

                    if (isCancellationDialogOpen)
                    {
                        continue;
                    }

                    elapsedMilliseconds += 100;
                }
            }

            splashWindow.SetStatus("Stopping startup applications...");
            await Task.Delay(500);
            return true;
        }
        finally
        {
            splashWindow.CancelRequested -= HandleCancelRequested;
            splashWindow.ConfirmRequested -= HandleConfirmRequested;
            splashWindow.CancellationDialogOpened -= HandleCancellationDialogOpened;
            splashWindow.CancellationDialogClosed -= HandleCancellationDialogClosed;
            splashWindow.Close();
        }
    }

    private async Task WaitForStartupSequenceToStopAsync()
    {
        var attemptsRemaining = 25;
        while (_isStartupSequenceRunning && attemptsRemaining-- > 0)
        {
            await Task.Delay(200);
        }
    }

    private void TrackStartupProcess(Process process)
    {
        lock (_startupProcessesSyncRoot)
        {
            _startupLaunchedProcesses.RemoveAll(startedProcess => startedProcess.HasExited);
            _startupLaunchedProcesses.Add(process);
        }
    }

    private async Task StopTrackedStartupProcessesAsync()
    {
        List<Process> trackedProcesses;
        lock (_startupProcessesSyncRoot)
        {
            trackedProcesses = _startupLaunchedProcesses
                .GroupBy(process => process.Id)
                .Select(group => group.Last())
                .ToList();
            _startupLaunchedProcesses.Clear();
        }

        foreach (var process in trackedProcesses)
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                var exitedGracefully = false;
                try
                {
                    if (process.CloseMainWindow())
                    {
                        exitedGracefully = await WaitForProcessExitAsync(process, 5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    exitedGracefully = true;
                }

                if (!exitedGracefully && !process.HasExited)
                {
                    process.Kill();
                    await WaitForProcessExitAsync(process, 3000);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to stop startup-launched process {ProcessId}", process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void DisposeTrackedStartupProcesses()
    {
        lock (_startupProcessesSyncRoot)
        {
            foreach (var process in _startupLaunchedProcesses)
            {
                process.Dispose();
            }

            _startupLaunchedProcesses.Clear();
        }
    }

    private static Task<bool> WaitForProcessExitAsync(Process process, int timeoutMilliseconds)
    {
        return Task.Run(() =>
        {
            try
            {
                return process.WaitForExit(timeoutMilliseconds);
            }
            catch
            {
                return true;
            }
        });
    }

    private static string GetControlFilePath(string state)
    {
        return Path.Combine(AppContext.BaseDirectory, $"{Environment.MachineName}.{state}");
    }

    private static void WriteControlFile(string path)
    {
        try
        {
            File.WriteAllText(path, $"{Environment.MachineName} {DateTimeOffset.Now:O}");
            Log.Logger.Information("Created control file {ControlFileName} at {ControlFilePath}", Path.GetFileName(path), path);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to write control file {ControlFilePath}", path);
        }
    }

    private static void DeleteControlFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
            Log.Logger.Information("Deleted control file {ControlFileName} at {ControlFilePath}", Path.GetFileName(path), path);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to delete control file {ControlFilePath}", path);
        }
    }

    private void DeleteControlCommandFiles()
    {
        DeleteControlFile(GetControlFilePath("start"));
        DeleteControlFile(GetControlFilePath("stop"));
        DeleteControlFile(GetControlFilePath("makearchive"));
    }

    private void NormalizeHostControlState()
    {
        var previousStateFiles = GetHostStateFilePaths().Where(File.Exists).ToList();
        if (previousStateFiles.Count > 0)
        {
            Log.Logger.Information("Normalizing host control state on startup. Removing existing state markers: {StateMarkers}", string.Join(", ", previousStateFiles.Select(Path.GetFileName)));
        }

        TransitionHostControlState(HostControlState.Ready, "Application startup normalization.");
    }

    private void TransitionHostControlState(HostControlState newState, string reason)
    {
        var previousState = _hostControlState;
        ClearHostStateFiles();

        var stateFilePath = GetControlFilePath(GetStateFileSuffix(newState));
        WriteControlFile(stateFilePath);
        _hostControlState = newState;
        NotifyHostControlStateChanged(newState);
        Log.Logger.Information("Host control state changed from {PreviousState} to {NewState}. Reason: {Reason}", previousState, newState, reason);
    }

    private void NotifyHostControlStateChanged(HostControlState state)
    {
        if (_host?.Services.GetService<MainWindowViewModel>() is MainWindowViewModel viewModel)
        {
            viewModel.SetHostControlState(state);
        }
    }

    private void ClearAllHostControlFiles()
    {
        DeleteControlCommandFiles();
        ClearHostStateFiles();
        DeleteControlFile(GetControlFilePath("archivecreated"));
    }

    private void ClearHostStateFiles()
    {
        foreach (var stateFilePath in GetHostStateFilePaths())
        {
            DeleteControlFile(stateFilePath);
        }
    }

    private IEnumerable<string> GetHostStateFilePaths()
    {
        yield return GetControlFilePath("ready");
        yield return GetControlFilePath("run");
        yield return GetControlFilePath("stopping");
        yield return GetControlFilePath("error");
    }

    private static string GetStateFileSuffix(HostControlState state)
    {
        return state switch
        {
            HostControlState.Ready => "ready",
            HostControlState.Running => "run",
            HostControlState.Stopping => "stopping",
            HostControlState.Error => "error",
            _ => "ready"
        };
    }
}
