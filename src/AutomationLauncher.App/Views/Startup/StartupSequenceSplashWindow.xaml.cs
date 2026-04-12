using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutomationLauncher.App;

public partial class StartupSequenceSplashWindow : Window
{
    private string _confirmDialogMessage = "Do you want to continue?";
    private string _cancelReasonDialogTitle = "Cancel operation";
    private string _cancelReasonDialogPrompt = "Provide the reason for cancellation:";

    public StartupSequenceSplashWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
    }

    public event EventHandler<StartupSplashCancelRequestedEventArgs>? CancelRequested;
    public event EventHandler? ConfirmRequested;
    public event EventHandler? CancellationDialogOpened;
    public event EventHandler? CancellationDialogClosed;

    public void SetApplicationTitle(string title)
    {
        ApplicationTitleText.Text = title;
    }

    public void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    public void SetBackgroundImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
        {
            BackgroundImage.Source = null;
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        BackgroundImage.Source = bitmap;
    }

    public void ConfigureActions(bool showConfirmAction, string? confirmButtonText, string? cancelButtonText, bool showCancelAction = true)
    {
        ConfirmActionButton.Visibility = showConfirmAction ? Visibility.Visible : Visibility.Collapsed;
        CancelActionButton.Visibility = showCancelAction ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(confirmButtonText))
        {
            ConfirmActionButton.Content = confirmButtonText;
        }

        if (!string.IsNullOrWhiteSpace(cancelButtonText))
        {
            CancelActionButton.Content = cancelButtonText;
        }
    }

    public void ConfigureConfirmDialog(string confirmationMessage)
    {
        _confirmDialogMessage = string.IsNullOrWhiteSpace(confirmationMessage)
            ? "Do you want to continue?"
            : confirmationMessage;
    }

    public void ConfigureCancelDialog(string dialogTitle, string dialogPrompt)
    {
        _cancelReasonDialogTitle = string.IsNullOrWhiteSpace(dialogTitle)
            ? "Cancel operation"
            : dialogTitle;

        _cancelReasonDialogPrompt = string.IsNullOrWhiteSpace(dialogPrompt)
            ? "Provide the reason for cancellation:"
            : dialogPrompt;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                _confirmDialogMessage,
                "Automation Launcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ConfirmRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancellationDialogOpened?.Invoke(this, EventArgs.Empty);

        var reasonDialog = new CancellationReasonDialog(_cancelReasonDialogPrompt)
        {
            Owner = this,
            Title = _cancelReasonDialogTitle
        };

        try
        {
            if (reasonDialog.ShowDialog() != true)
            {
                return;
            }

            CancelRequested?.Invoke(this, new StartupSplashCancelRequestedEventArgs(reasonDialog.Reason));
        }
        finally
        {
            CancellationDialogClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsClickInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DragMove();
    }

    private static bool IsClickInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}

public sealed class StartupSplashCancelRequestedEventArgs : EventArgs
{
    public StartupSplashCancelRequestedEventArgs(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}