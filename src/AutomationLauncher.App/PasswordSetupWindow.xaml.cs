using System.Windows;

namespace AutomationLauncher.App;

public partial class PasswordSetupWindow : Window
{
    private readonly IProtectedApplicationSettingsStore _settingsStore;

    public PasswordSetupWindow(IProtectedApplicationSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string? Password { get; private set; }

    private void SavePassword_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (!_settingsStore.ValidatePasswordRequirements(password, out var validationMessage))
        {
            ValidationMessage.Text = validationMessage;
            PasswordBox.Focus();
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            ValidationMessage.Text = "Passwords do not match.";
            ConfirmPasswordBox.Focus();
            return;
        }

        Password = password;
        DialogResult = true;
    }
}