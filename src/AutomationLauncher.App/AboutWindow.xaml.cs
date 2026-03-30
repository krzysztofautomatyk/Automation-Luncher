using System.Windows;

namespace AutomationLauncher.App;

public partial class AboutWindow : Window
{
    public AboutWindow(IProtectedApplicationSettingsStore settingsStore, AutomationLauncherSettings settings)
    {
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
        DataContext = new AboutWindowViewModel(settingsStore, settings);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}