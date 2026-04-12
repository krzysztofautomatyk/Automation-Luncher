using System.Windows;
using System.Windows.Forms;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private void InitializeTrayIcon()
    {
        if (_host is null)
        {
            return;
        }

        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => ExecuteOnUiThread(ShowMainWindow));
        _settingsMenuItem = new ToolStripMenuItem("Settings", null, (_, _) => ExecuteOnUiThread(ShowSettingsDialog));
        menu.Items.Add(_settingsMenuItem);
        menu.Items.Add("About", null, (_, _) => ExecuteOnUiThread(ShowAboutDialog));
        _checkTiaConnectionMenuItem = new ToolStripMenuItem("Check TIA connection", null, (_, _) => ExecuteOnUiThread(() => viewModel.CheckTiaConnectionCommand.Execute(null)));
        menu.Items.Add(_checkTiaConnectionMenuItem);
        _archiveNowMenuItem = new ToolStripMenuItem("Create archive now", null, async (_, _) => await ExecuteOnUiThreadAsync(RunArchiveNowFromMenuAsync));
        menu.Items.Add(_archiveNowMenuItem);
        _runStartupAutomationMenuItem = new ToolStripMenuItem("Run startup automation now", null, async (_, _) => await ExecuteOnUiThreadAsync(RunStartupSequenceManuallyAsync));
        menu.Items.Add(_runStartupAutomationMenuItem);
        _runManagedApplicationsMenuItem = new ToolStripMenuItem("Run managed applications", null, async (_, _) => await ExecuteOnUiThreadAsync(RunManagedApplicationsFromMenuAsync));
        menu.Items.Add(_runManagedApplicationsMenuItem);
        _stopManagedApplicationsMenuItem = new ToolStripMenuItem("Stop managed applications", null, async (_, _) => await ExecuteOnUiThreadAsync(StopManagedApplicationsFromMenuAsync));
        menu.Items.Add(_stopManagedApplicationsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        _openAutostartFolderMenuItem = new ToolStripMenuItem("Open autostart folder", null, (_, _) => ExecuteOnUiThread(() => viewModel.OpenStartupFolderCommand.Execute(null)));
        menu.Items.Add(_openAutostartFolderMenuItem);
        _openControlFilesFolderMenuItem = new ToolStripMenuItem("Open control files folder", null, (_, _) => ExecuteOnUiThread(() => viewModel.OpenControlFilesFolderCommand.Execute(null)));
        menu.Items.Add(_openControlFilesFolderMenuItem);
        _openLogFolderMenuItem = new ToolStripMenuItem("Open log folder", null, (_, _) => ExecuteOnUiThread(() => viewModel.OpenLogDirectoryCommand.Execute(null)));
        menu.Items.Add(_openLogFolderMenuItem);
        _deleteErrorMenuItem = new ToolStripMenuItem("Delete error", null, (_, _) => ExecuteOnUiThread(DeleteErrorMarkerFile));
        menu.Items.Add(_deleteErrorMenuItem);
        _loginMenuItem = new ToolStripMenuItem("Log in", null, (_, _) => ExecuteOnUiThread(LoginSession));
        menu.Items.Add(_loginMenuItem);
        _logoutMenuItem = new ToolStripMenuItem("Log out", null, (_, _) => ExecuteOnUiThread(() => LogoutSession("Session locked by user.", false)));
        menu.Items.Add(_logoutMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExecuteOnUiThread(ExitFromTray));

        _notifyIcon = new NotifyIcon
        {
            Text = $"Automation Launcher {AppVersionInfo.DisplayVersion}",
            Icon = AppIconFactory.GetTrayIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => ExecuteOnUiThread(ShowMainWindow);
        UpdateTrayMenuState();
    }

    private void ShowMainWindow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ShowMainWindow);
            return;
        }

        _mainWindow ??= _host?.Services.GetRequiredService<MainWindow>();
        _mainWindow?.ShowDashboard();
    }

    private void ShowSettingsDialog()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ShowSettingsDialog);
            return;
        }

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

    private bool ConfirmApplicationExit()
    {
        var message = _isStartupSequenceRunning
            ? "A startup automation sequence is currently running. Do you want to cancel it and exit Automation Launcher?"
            : "Do you want to exit Automation Launcher?";

        return System.Windows.MessageBox.Show(
                message,
                "AutomationLauncher",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question)
            == System.Windows.MessageBoxResult.Yes;
    }

    private void UpdateTrayMenuState()
    {
        var isAuthenticated = _sessionCoordinator?.IsAuthenticated == true;

        if (_settingsMenuItem is not null)
            _settingsMenuItem.Enabled = true;

        if (_checkTiaConnectionMenuItem is not null)
            _checkTiaConnectionMenuItem.Enabled = isAuthenticated;

        if (_archiveNowMenuItem is not null)
            _archiveNowMenuItem.Enabled = isAuthenticated && _hostControlState != HostControlState.Stopping && !_isStartupSequenceRunning;

        if (_runStartupAutomationMenuItem is not null)
            _runStartupAutomationMenuItem.Enabled = isAuthenticated && !_isStartupSequenceRunning;

        if (_runManagedApplicationsMenuItem is not null)
            _runManagedApplicationsMenuItem.Enabled = isAuthenticated
                && !_isStartupSequenceRunning
                && _hostControlState != HostControlState.Running
                && _hostControlState != HostControlState.Stopping;

        if (_stopManagedApplicationsMenuItem is not null)
            _stopManagedApplicationsMenuItem.Enabled = isAuthenticated
                && (_hostControlState == HostControlState.Running || _isStartupSequenceRunning);

        if (_openAutostartFolderMenuItem is not null)
            _openAutostartFolderMenuItem.Enabled = isAuthenticated;

        if (_openControlFilesFolderMenuItem is not null)
            _openControlFilesFolderMenuItem.Enabled = isAuthenticated;

        if (_openLogFolderMenuItem is not null)
            _openLogFolderMenuItem.Enabled = isAuthenticated;

        if (_loginMenuItem is not null)
            _loginMenuItem.Enabled = !isAuthenticated;

        if (_logoutMenuItem is not null)
            _logoutMenuItem.Enabled = isAuthenticated;

        if (_deleteErrorMenuItem is not null)
            _deleteErrorMenuItem.Enabled = _hasErrorControlFile;
    }

    private void ReportFatalError(string message)
    {
        try
        {
            System.Windows.MessageBox.Show(message, "AutomationLauncher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                Current?.Dispatcher.BeginInvoke(new System.Action(() => Shutdown(-1)));
            }
            catch
            {
                System.Environment.Exit(-1);
            }
        }
    }
}
