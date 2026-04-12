using System.Windows;

namespace AutomationLauncher.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = AppIconFactory.GetWindowIcon();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}