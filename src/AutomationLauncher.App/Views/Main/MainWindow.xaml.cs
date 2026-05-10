using System.ComponentModel;
using System.Windows;
using System.Collections.Specialized;

namespace AutomationLauncher.App;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _pendingLogScroll;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = AppIconFactory.GetWindowIcon();
        StateChanged += HandleStateChanged;
        viewModel.FileLogs.CollectionChanged += HandleFileLogsCollectionChanged;
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    public void ShowDashboard()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ShowDashboard);
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void HandleStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.OpenSettingsFromDashboard();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.OpenAboutFromDashboard();
        }
    }

    private void ShowFlow_Click(object sender, RoutedEventArgs e)
    {
        const string flowDescription =
            "Automation Launcher - Full archive flow\n\n" +
            "1) Trigger\n" +
            "- Archive can start from dashboard, tray menu, or a configured HOST control file (default: .march).\n" +
            "- If another workflow is already running, archive command is ignored.\n\n" +
            "2) Countdown splash (60s)\n" +
            "- User can click 'Archive now' to skip waiting.\n" +
            "- User can cancel archive and provide a reason.\n\n" +
            "3) Pre-save phase (if needed)\n" +
            "- If unsaved changes are detected (or fallback policy requires it), 30s save countdown starts.\n" +
            "- 'Save now' forces immediate save.\n" +
            "- 'Skip save' disables pre-save for this run.\n\n" +
            "4) Runtime validation\n" +
            "- TIA process and open project are read via Openness.\n" +
            "- Open project path must match configured expected path.\n" +
            "- PLC online/offline compare gate must be verified and equal (1:1).\n" +
            "- If compare is unavailable or mismatched, archive is blocked.\n\n" +
            "5) Save + archive execution\n" +
            "- Save runs according to policy (dirty-state aware).\n" +
            "- Archive is created with configured backup mode and retry policy.\n\n" +
            "6) Results and control files\n" +
            "- Success creates .archok and clears .error.\n" +
            "- Failure creates .error and keeps host state in Error.\n" +
            "- Structured logs are written to app logs and archive metrics log (*.archive.log).";

        System.Windows.MessageBox.Show(
            flowDescription,
            "Automation Launcher - Flow",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ArchiveNow_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            await app.RunArchiveNowFromDashboardAsync();
        }
    }

    private async void RunStartupAutomation_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            await app.RunStartupAutomationFromDashboardAsync();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ExitFromDashboard();
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.LoginFromDashboard();
        }
    }

    private void DeleteError_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.DeleteErrorFromDashboard();
        }
    }

    private async void RunManagedApplications_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            await app.RunManagedApplicationsFromMenuAsync();
        }
    }

    private async void StopManagedApplications_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            await app.StopManagedApplicationsFromMenuAsync();
        }
    }

    private void HandleFileLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsLogAutoScrollEnabled)
        {
            return;
        }

        if (LogsListBox.Items.Count == 0)
        {
            return;
        }

        if (_pendingLogScroll)
        {
            return;
        }

        _pendingLogScroll = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _pendingLogScroll = false;
            if (LogsListBox.Items.Count == 0)
            {
                return;
            }

            LogsListBox.ScrollIntoView(LogsListBox.Items[0]);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}
