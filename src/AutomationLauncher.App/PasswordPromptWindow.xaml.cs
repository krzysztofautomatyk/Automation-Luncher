using System.Windows;

namespace AutomationLauncher.App;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string? Password { get; private set; }

    public void ShowValidation(string message)
    {
        ValidationMessage.Text = message;
        PasswordBox.Clear();
        PasswordBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            ValidationMessage.Text = "Password is required.";
            PasswordBox.Focus();
            return;
        }

        Password = PasswordBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}