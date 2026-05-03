using System.Windows;

namespace AutomationLauncher.App;

public partial class ScriptInstructionsWindow : Window
{
    public ScriptInstructionsWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}