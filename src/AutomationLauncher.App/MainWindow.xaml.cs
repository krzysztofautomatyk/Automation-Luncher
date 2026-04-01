using System.ComponentModel;
using System.Windows;

namespace AutomationLauncher.App;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = AppIconFactory.GetWindowIcon();
        StateChanged += HandleStateChanged;
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    public void ShowDashboard()
    {
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
}
