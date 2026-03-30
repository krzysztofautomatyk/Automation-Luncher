using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AutomationLauncher.App;

public partial class StartupSequenceSplashWindow : Window
{
    public StartupSequenceSplashWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.GetWindowIcon();
    }

    public event EventHandler? CancelRequested;

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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}