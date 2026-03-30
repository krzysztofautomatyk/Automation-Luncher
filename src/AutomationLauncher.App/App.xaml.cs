using System.IO;
using System.Linq;
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
    private static Mutex? _singleInstanceMutex;
    private IHost? _host;
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _runStartupAutomationMenuItem;
    private MainWindow? _mainWindow;
    private readonly AppSessionState _sessionState = new();
    private DispatcherTimer? _sessionTimer;
    private ISessionCoordinator? _sessionCoordinator;
    private CancellationTokenSource? _startupSequenceCancellationSource;
    private bool _isStartupSequenceRunning;

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
    _sessionCoordinator = _host.Services.GetRequiredService<ISessionCoordinator>();
    _sessionCoordinator.RegisterActivity();
    _sessionCoordinator.SessionStateChanged += HandleSessionStateChanged;
    StartSessionTimer();
    InputManager.Current.PreProcessInput += HandlePreProcessInput;
        InitializeTrayIcon();

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

        InputManager.Current.PreProcessInput -= HandlePreProcessInput;

        if (_sessionCoordinator is not null)
        {
            _sessionCoordinator.SessionStateChanged -= HandleSessionStateChanged;
        }

        _startupSequenceCancellationSource?.Cancel();
        _startupSequenceCancellationSource?.Dispose();

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
        menu.Items.Add("Settings", null, (_, _) => ShowSettingsDialog());
        menu.Items.Add("About", null, (_, _) => ShowAboutDialog());
        menu.Items.Add("Check TIA connection", null, (_, _) => viewModel.CheckTiaConnectionCommand.Execute(null));
        menu.Items.Add("Archive now", null, (_, _) => viewModel.ArchiveCommand.Execute(null));
        _runStartupAutomationMenuItem = new ToolStripMenuItem("Run startup automation now", null, async (_, _) => await RunStartupSequenceManuallyAsync());
        menu.Items.Add(_runStartupAutomationMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open autostart folder", null, (_, _) => viewModel.OpenStartupFolderCommand.Execute(null));
        menu.Items.Add("Open log folder", null, (_, _) => viewModel.OpenLogDirectoryCommand.Execute(null));
        menu.Items.Add("Log out", null, (_, _) => LogoutSession("Session locked by user.", false));
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
        var launchedFromWindowsStartup = startupArgs.Any(arg => string.Equals(arg, AppLaunchArguments.StartupLaunch, StringComparison.OrdinalIgnoreCase));
        if (!launchedFromWindowsStartup || !settings.Startup.RunOnWindowsStartup || !settings.Startup.RunSequenceOnWindowsStartup)
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
        _startupSequenceCancellationSource = new CancellationTokenSource();
        _isStartupSequenceRunning = true;
        SetStartupAutomationMenuState(false);

        splashWindow.SetApplicationTitle("Automation Launcher");
        splashWindow.SetBackgroundImage(settings.Startup.SplashBackgroundImagePath);
        splashWindow.SetStatus(initialStatus);
        splashWindow.CancelRequested += HandleStartupSplashCancelRequested;
        splashWindow.Show();

        try
        {
            var result = await runner.RunAsync(entries, splashWindow, _startupSequenceCancellationSource.Token);
            splashWindow.SetStatus(result.Message);
            await Task.Delay(900);

            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", result.Message, ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", "Startup automation was cancelled.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Startup automation failed");
            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", $"Startup automation failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            splashWindow.CancelRequested -= HandleStartupSplashCancelRequested;
            splashWindow.Close();
            _startupSequenceCancellationSource?.Dispose();
            _startupSequenceCancellationSource = null;
            _isStartupSequenceRunning = false;
            SetStartupAutomationMenuState(true);
        }
    }

    private void SetStartupAutomationMenuState(bool isEnabled)
    {
        if (_runStartupAutomationMenuItem is not null)
        {
            _runStartupAutomationMenuItem.Enabled = isEnabled;
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

            return;
        }

        _sessionCoordinator?.RegisterActivity();
    }

    private void HandleStartupSplashCancelRequested(object? sender, EventArgs e)
    {
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
}
