using System.Windows;
using System.Windows.Input;

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
        PasswordTextBox.Clear();
        FocusActivePasswordControl();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        var enteredPassword = GetEnteredPassword();
        if (string.IsNullOrWhiteSpace(enteredPassword))
        {
            ValidationMessage.Text = "Password is required.";
            FocusActivePasswordControl();
            return;
        }

        Password = enteredPassword;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private string GetEnteredPassword()
    {
        return ShowPasswordToggle.IsChecked == true
            ? PasswordTextBox.Text
            : PasswordBox.Password;
    }

    private void FocusActivePasswordControl()
    {
        if (ShowPasswordToggle.IsChecked == true)
        {
            PasswordTextBox.Focus();
            PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            return;
        }

        PasswordBox.Focus();
    }

    private void PasswordInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        Unlock_Click(sender, new RoutedEventArgs());
    }

    private void ShowPasswordToggle_Checked(object sender, RoutedEventArgs e)
    {
        PasswordTextBox.Text = PasswordBox.Password;
        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordTextBox.Visibility = Visibility.Visible;
        FocusActivePasswordControl();
    }

    private void ShowPasswordToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        PasswordBox.Password = PasswordTextBox.Text;
        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        FocusActivePasswordControl();
    }
}