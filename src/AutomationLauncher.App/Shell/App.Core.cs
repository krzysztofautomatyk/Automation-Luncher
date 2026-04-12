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
        Archiving,
        Error
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
    private ToolStripMenuItem? _deleteErrorMenuItem;
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
    private bool _hasErrorControlFile;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Logger.Error(args.Exception, "Unhandled UI exception");
            MarkErrorControlFile("Unhandled UI exception.");
            ReportFatalError($"Critical error: {args.Exception.Message}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Logger.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");
            MarkErrorControlFile("Unhandled domain exception.");
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
            .WriteTo.Console(
                restrictedToMinimumLevel: minimumLevel,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDirectory, "automation-launcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, settings.Logging.RetainedFileCountLimit),
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}")
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
}
