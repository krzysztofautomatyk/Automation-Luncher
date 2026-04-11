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
