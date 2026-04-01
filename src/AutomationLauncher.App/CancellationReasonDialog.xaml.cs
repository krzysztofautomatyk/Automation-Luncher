using System.Windows;

namespace AutomationLauncher.App;

public partial class CancellationReasonDialog : Window
{
    public CancellationReasonDialog(string prompt)
    {
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
        PromptText.Text = string.IsNullOrWhiteSpace(prompt)
            ? "Provide cancellation reason:"
            : prompt;
    }

    public string Reason { get; private set; } = string.Empty;

    private void ReasonTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(ReasonTextBox.Text);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        var trimmedReason = ReasonTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
        {
            return;
        }

        Reason = trimmedReason;
        DialogResult = true;
        Close();
    }
}
